# Guia de deploy em produção

> Caminho coberto: **Docker/docker-compose em host único**, atrás de um reverse proxy
> com TLS. É o caminho mais simples de operar sozinho, e o que este guia assume do início
> ao fim. Outras topologias (Kubernetes, múltiplos hosts) não estão cobertas — nada no
> código impede, mas nada aqui foi pensado para isso.

Este guia é para quem vai **rodar a própria instância** da Intranet (ADR-0002: é um
produto que a empresa baixa e opera, não um serviço hospedado pela Secco). Ele assume que
você já tem, ou vai provisionar separadamente, um `Secco.SecureGate` acessível — a
Intranet não roda sem ele (é a única dependência dura; LogStream e NotificationHub são
opcionais, ver abaixo).

## Limitações conhecidas — leia antes de começar

Nenhuma delas impede o deploy, mas todas mudam como você vai operar. Ignorá-las custa
caro depois.

1. **Construir a imagem exige credencial do feed privado.** Os pacotes `Secco.*`
   (SDK, clients) vêm do GitHub Packages, que exige autenticação mesmo para pacotes
   públicos do pacote NuGet em si — sem uma credencial válida, `dotnet restore` falha
   dentro do build da imagem. Não existe mirror público hoje. Ver "Construir a imagem"
   abaixo para o mecanismo (`--secret`, PAT com escopo `read:packages`).
2. **Nenhum vault de segredos.** Toda credencial (connection string, client secret do
   SecureGate, senha do banco) é variável de ambiente. Se você tem Vault/Key Vault/Secrets
   Manager na sua infraestrutura, injete essas variáveis a partir de lá — a aplicação não
   fala com nenhum deles diretamente.
3. **Migrations não são automáticas fora de Development.** Em produção, ninguém aplica
   migration como efeito colateral do startup (evita duas réplicas competindo, e evita
   aplicar migration destrutiva sem intenção explícita). É um passo manual, coberto abaixo.
4. **O cache de permissão do Diretório organizacional é em memória, por processo
   (`IMemoryCache`, TTL de 60s).** Com uma única réplica (o que este guia assume), isso
   não é problema. Se um dia você rodar mais de uma réplica do container atrás do mesmo
   proxy, revogações de permissão podem demorar até 60s a mais para valer numa réplica que
   não recebeu a requisição que invalidou o cache — não é uma falha de segurança (a
   policy de autorização em si é sempre reavaliada por requisição), só uma latência de
   propagação que hoje não é distribuída. Fora de escopo deste guia; citado aqui para não
   ser descoberto em produção.
5. **SecureGate, LogStream e NotificationHub são serviços externos.** Este guia não cobre
   como instalá-los — só como apontar a Intranet para eles.

## Topologia

```text
Internet
   │  HTTPS
   ▼
Reverse proxy (TLS termination — Caddy, nginx, Traefik; fora do docker-compose deste
   │            repo, ou um serviço a mais no mesmo compose)
   │  HTTP interno, rede que só o proxy alcança
   ▼
secco-intranet-web (container, esta imagem)
   │
   ├──► SQL Server ou PostgreSQL — um banco por tenant (ADR-0005/ADR-0018)
   │
   └──► Secco.SecureGate (externo) ── Secco.LogStream (externo, opcional)
                                   └── Secco.NotificationHub (externo, opcional)
```

A postura de rede que sustenta `Secco:ReverseProxy:Habilitado` (ver abaixo): a porta do
container **não pode ser alcançável diretamente** de fora — só o proxy a alcança. Num
`docker-compose` isso é automático (a porta do serviço só é exposta ao host se você
publicar `ports:`; não publique, deixe só o proxy publicar 443).

## Construir a imagem

O `Dockerfile` já existe em `src/Secco.Intranet.Web/Dockerfile` e já documenta o bloqueio
do feed privado no próprio arquivo. Build a partir da **raiz do repositório** (o
Central Package Management exige os `.props`/`nuget.config` da raiz no contexto):

```bash
export NUGET_PAT=ghp_xxx   # PAT com escopo read:packages, de uma conta com acesso ao feed "secco"

docker build \
  -f src/Secco.Intranet.Web/Dockerfile \
  -t secco-intranet-web:latest \
  --secret id=nuget_pat,env=NUGET_PAT \
  .
```

O `Dockerfile` de hoje **não** injeta esse secret no `dotnet nuget update source` antes do
restore — isso ainda precisa ser adicionado a ele (um passo de RUN com
`--mount=type=secret,id=nuget_pat` lendo o PAT e chamando
`dotnet nuget update source secco --username <user> --password $(cat /run/secrets/nuget_pat) --store-password-in-clear-text`
antes do `dotnet restore`). Sem isso, o comando acima builda mas o restore falha do mesmo
jeito. Trate isso como pré-requisito deste guia, não como algo já resolvido — é o próximo
passo antes de um CI/CD real existir para este projeto.

## Banco de dados

1. **Escolha o engine** — `Intranet:Database:Provider` (`SqlServer`, padrão, ou
   `PostgreSql`). Todos os tenants de uma instalação usam o mesmo engine.
2. **Crie um banco e um login de aplicação por tenant**, com permissão só naquele banco
   (nunca `sa`/superusuário — mesmo princípio do `docker/init/provision-tenant-db.sql`
   usado em desenvolvimento, adapte para o seu SGBD gerenciado).
3. **Aplique as migrations** contra a connection string real de cada tenant — não existe
   hoje um comando que faça isso para todos os tenants do catálogo de uma vez fora de
   Development; repita por tenant:

   ```bash
   dotnet ef database update \
     --project src/Secco.Intranet.Migrations.SqlServer \
     --connection "Server=...;Database=...;User Id=...;Password=...;Encrypt=true"
   ```

   (troque o `--project` para `Secco.Intranet.Migrations.Postgres` se o engine for
   PostgreSQL). Rode de novo a cada deploy que trouxer migration nova, **antes** de subir
   os containers com a imagem nova — nunca deixe o container antigo e o schema novo
   coexistirem por engano, mas também não corra pra aplicar migration com o container novo
   já de pé.
4. **Registre a connection string de cada tenant** na configuração da aplicação (variável
   de ambiente, ver tabela abaixo) — não existe UI para cadastrar tenant hoje; é
   configuração de deploy.

## Variáveis de ambiente

Convenção ASP.NET Core: `__` no nome da variável vira `:` na chave de configuração.

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Sim | `Production`. Nunca `Development` num host exposto — liga HSTS, exige HTTPS no cookie e no OIDC, desliga migration/seed automáticos. |
| `Secco__SecureGate__Authority` | Sim | URL do SecureGate que emite os tokens. Sem ela, a aplicação sobe no "modo aberto" (sem autenticação) — nunca em produção (ADR-0020). |
| `Secco__SecureGate__ClientId` / `Secco__SecureGate__ClientSecret` | Sim | Credenciais do client OAuth desta instalação (login interativo **e** o client administrativo que provisiona Roles de setor — é o mesmo client). |
| `Secco__Tenancy__Tenants__{tenantId}__ConnectionString` | Sim, uma por tenant | Connection string do banco daquele tenant. `{tenantId}` é o GUID do tenant, hífens inclusos. |
| `Intranet__Database__Provider` | Não (padrão `SqlServer`) | `SqlServer` ou `PostgreSql`. |
| `Secco__ReverseProxy__Habilitado` | Sim, se atrás de proxy | `true` para confiar em `X-Forwarded-For`/`X-Forwarded-Proto`. Só ligue se a porta do container for inalcançável por fora do proxy — ver "Topologia". |
| `Intranet__Auditoria__LogStreamUrl` | Não | URL do `Secco.LogStream`. Vazia = trilha de auditoria não é gravada (modo silencioso, não é erro). |
| `Intranet__Notificacao__HubUrl` | Não | URL do `Secco.NotificationHub`. Vazia = sino/e-mail de notificação não são enviados. |
| `Intranet__Notificacao__UrlBase` | Recomendada se `HubUrl` estiver definida | URL pública da Intranet (ex.: `https://intranet.suaempresa.com`, sem barra final) — sem ela, o link da notificação por e-mail fica relativo e não abre fora do navegador já logado. |
| `Secco__LogStream__*` | Não | Seção do sink de log geral (`ILogger` → LogStream), separada da auditoria acima. Vazia = log só local (stdout do container). Ver `Secco.SDK.Logging` para as chaves da seção. |

Credenciais de `Secco.LogStream`/`Secco.NotificationHub` (quando as URLs acima estiverem
preenchidas) reaproveitam `Secco__SecureGate__ClientId`/`ClientSecret` — os SDKs montam
client credentials a partir dessa mesma seção (secco-platform#25); não há uma credencial
separada para configurar.

## `docker-compose` de produção

Exemplo mínimo — adapte para o seu proxy e para onde o banco realmente mora (gerenciado
fora do compose é o normal; o serviço `sqlserver` abaixo é só se você optar por rodá-lo no
mesmo host):

```yaml
services:
  secco-intranet-web:
    image: secco-intranet-web:latest
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Secco__SecureGate__Authority: "${SECURE_GATE_AUTHORITY}"
      Secco__SecureGate__ClientId: "${SECURE_GATE_CLIENT_ID}"
      Secco__SecureGate__ClientSecret: "${SECURE_GATE_CLIENT_SECRET}"
      Secco__ReverseProxy__Habilitado: "true"
      Secco__Tenancy__Tenants__${TENANT_ID}__ConnectionString: "${TENANT_CONNECTION_STRING}"
    # Nenhuma porta publicada para o host — só o proxy alcança esta rede.
    networks:
      - interna
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/live"]
      interval: 30s
      timeout: 5s
      retries: 3

  proxy:
    image: caddy:2
    restart: unless-stopped
    ports:
      - "443:443"
      - "80:80"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy-data:/data
    networks:
      - interna
    depends_on:
      - secco-intranet-web

networks:
  interna:

volumes:
  caddy-data:
```

`Caddyfile` mínimo (TLS automático via Let's Encrypt, só exige DNS apontando para o host):

```caddyfile
intranet.suaempresa.com {
    reverse_proxy secco-intranet-web:8080
}
```

Isto é um ponto de partida deliberadamente enxuto — não um arquivo entregue neste
repositório, porque o proxy, o certificado e o banco variam demais entre instalações para
travar num exemplo versionado.

## Primeiro `intranet-admin`

Já documentado no [`README.md`](../README.md#primeiro-intranet-admin) — crie a Role
`intranet-admin` no tenant e atribua ao primeiro usuário pela API do SecureGate (ou pelo
AdminPortal, se você tiver um) antes do primeiro login. Sem isso, ninguém acessa a Área
administrativa (`/Acesso`, `/Setores`) para conceder o resto.

## Health checks

- `GET /health/live` — o processo está de pé. Use como liveness probe.
- `GET /health/ready` — checagem de prontidão registrada por `AddSeccoHealthChecks()`
  (`Secco.SDK.AspNetCore`). Use como readiness probe antes de rotear tráfego.

Nenhum dos dois exige autenticação.

## Atualização (rollout)

Sem orquestrador com rolling update (é o caso de um host único), o corte é direto:

1. Build da imagem nova (tag por versão, não `latest` puro — facilita rollback).
2. Se a versão trouxe migration nova, aplique contra cada tenant **antes** do passo 3 (ver
   "Banco de dados" acima).
3. `docker compose up -d secco-intranet-web` — recria só o container da aplicação; o proxy
   e o banco não são afetados.
4. Confirme `GET /health/ready` → `200` antes de considerar o deploy concluído.

Não há mecanismo de rollback automático de migration — se precisar reverter, é
`dotnet ef database update <migration-anterior>` manual, mesma mecânica do passo 2.

## Checklist antes de ir ao ar

- [ ] Imagem builda com o PAT do feed privado (mecanismo do secret ainda a adicionar ao
      Dockerfile, ver "Construir a imagem").
- [ ] `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] `Secco:SecureGate:Authority/ClientId/ClientSecret` configurados contra o SecureGate
      real (nunca o modo aberto).
- [ ] Porta do container não publicada para fora do host — só o proxy alcança.
- [ ] `Secco:ReverseProxy:Habilitado=true` **só** com o ponto acima garantido.
- [ ] Migrations aplicadas em cada tenant antes do primeiro start.
- [ ] Role `intranet-admin` criada e atribuída a alguém antes do primeiro login.
- [ ] `GET /health/ready` → `200` depois de subir.
