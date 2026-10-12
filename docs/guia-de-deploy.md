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
3. **Migrations não rodam no startup fora de Development.** Quem as aplica é o verbo
   `migrate` do próprio binário, rodado pelo deploy antes de subir a versão nova (ver "Banco de
   dados"). A exceção é o tenant cadastrado depois do deploy: o banco dele é migrado no primeiro
   uso, sem reiniciar nada.
4. **O cache de permissão do Diretório organizacional é em memória, por processo
   (`IMemoryCache`, TTL de 60s).** Com uma única réplica (o que este guia assume), isso
   não é problema. Se um dia você rodar mais de uma réplica do container atrás do mesmo
   proxy, revogações de permissão podem demorar até 60s a mais para valer numa réplica que
   não recebeu a requisição que invalidou o cache — não é uma falha de segurança (a
   policy de autorização em si é sempre reavaliada por requisição), só uma latência de
   propagação que hoje não é distribuída. Fora de escopo deste guia; citado aqui para não
   ser descoberto em produção.
5. **SecureGate, LogStream e NotificationHub são serviços externos.** Este guia não cobre
   como instalá-los — só como apontar a Intranet para eles. O que a Intranet exige da
   instalação do SecureGate está em "SecureGate: dois clients e o `migrate`", abaixo.
6. **Ligar LogStream ou NotificationHub a um tenant administrado exige um alvo de provisionamento
   no SecureGate** (`SecureGate:Provisioning:Targets`). A Intranet não deixa escolher servidor pela
   tela; sem um alvo declarado na plataforma, "Ligar" recusa com uma mensagem que diz isso.

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

## SecureGate: dois clients e o `migrate`

Vale para o SecureGate a partir da versão que acompanha o `Secco.SecureGate.Client` 0.15.0
(ADR-0037 e ADR-0038 da plataforma). Os detalhes completos estão na seção "7. Implantar" do
`docs/getting-started.md` e no `CHANGELOG.md` do `secco-platform`.

**1. Rode o `migrate` antes de subir o SecureGate.** Fora de Development, o SecureGate não migra
o próprio banco nem roda o seed de referência no startup. Com migration pendente, **ele se
recusa a subir**. No primeiro deploy dessa versão e em cada atualização dele:

```bash
dotnet Secco.SecureGate.Api.dll migrate   # mesma imagem e mesma configuração da API
echo $?                                    # 0 = aplicado; 1 = falhou (o log diz onde)
```

**2. Declare os dois clients da Intranet em `SecureGate:PlatformClients`.** Clients sem tenant
só nascem dessa lista, e o seed de referência (que roda no `migrate`) **remove** todo client de
plataforma que não estiver declarado nela, inclusive os inseridos à mão no banco. Se o client
da Intranet ficar de fora, o `migrate` o apaga, e a Intranet perde o login e a administração ao
mesmo tempo.

São **dois** clients porque o SecureGate dá um único grant a cada client de plataforma: o login
do navegador é authorization code, e as chamadas de serviço são client credentials.

| Client | Tipo | Escopos | Usado por |
|---|---|---|---|
| Login (ex.: `secco-intranet-login`) | `AuthorizationCode` | `profile`, `roles` | login interativo (`Intranet:Login`) |
| Máquina (ex.: `secco-intranet`) | `ClientCredentials` | `securegate:admin`, `authorization:read`, `logstream`, `notificationhub` | client administrativo (setores, `/acesso`, tenants), permissões e revogação de sessão, LogStream e NotificationHub (`Secco:SecureGate`) |

O client do navegador não carrega `securegate:admin`. `logstream` e `notificationhub` só são
necessários se a Intranet usar esses serviços.

Na configuração **do SecureGate** (não da Intranet), com os secrets por variável de ambiente
(`SecureGate__PlatformClients__0__ClientSecret`, `SecureGate__PlatformClients__1__ClientSecret`):

```json
{
  "SecureGate": {
    "PlatformClients": [
      {
        "ClientId": "secco-intranet-login",
        "Type": "AuthorizationCode",
        "Scopes": ["profile", "roles"],
        "RedirectUris": ["https://intranet.suaempresa.com/signin-oidc"],
        "PostLogoutRedirectUris": ["https://intranet.suaempresa.com/signout-callback-oidc"]
      },
      {
        "ClientId": "secco-intranet",
        "Type": "ClientCredentials",
        "Scopes": ["securegate:admin", "authorization:read", "logstream", "notificationhub"]
      }
    ]
  }
}
```

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
3. **Registre a connection string de cada tenant** na configuração da aplicação (variável
   de ambiente, ver tabela abaixo) — não existe UI para cadastrar tenant hoje; é
   configuração de deploy.
4. **Rode o `migrate`** com a mesma imagem e a mesma configuração do container da aplicação.
   Ele aplica as migrations de todos os tenants do catálogo e sai, sem subir o servidor:

   ```bash
   docker compose run --rm secco-intranet-web migrate
   echo $?   # 0 = tudo aplicado; 1 = algum tenant falhou (o log diz qual, pelo id)
   ```

   O argumento vira `dotnet Secco.Intranet.Web.dll migrate`, pelo `ENTRYPOINT` da imagem. Um
   tenant que falha não impede os outros. Rode de novo a cada deploy, **antes** de subir o
   container novo; sem migration pendente, ele não muda nada.

   Um tenant acrescentado ao catálogo depois do deploy não precisa do `migrate`: o banco dele
   é migrado na primeira vez que a Intranet o abre. Se esse banco estiver fora do ar, só esse
   tenant recebe `503` com `Retry-After`, e a próxima tentativa acontece depois de 15 segundos.
   Criar o banco e o login (passo 2) continua sendo do DBA.

## Variáveis de ambiente

Convenção ASP.NET Core: `__` no nome da variável vira `:` na chave de configuração.

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Sim | `Production`. Nunca `Development` num host exposto — liga HSTS, exige HTTPS no cookie e no OIDC, desliga migration/seed automáticos. |
| `Secco__SecureGate__Authority` | Sim | URL do SecureGate que emite os tokens. Sem ela, a aplicação sobe no "modo aberto" (sem autenticação) — nunca em produção (ADR-0020). |
| `Secco__SecureGate__BaseUrl` | Sim | URL da API do SecureGate, usada pelas chamadas de serviço (client administrativo, permissões, revogação de sessão). Em geral, a mesma da `Authority`. |
| `Secco__SecureGate__ClientId` / `Secco__SecureGate__ClientSecret` | Sim | Client de **máquina** (`ClientCredentials`): client administrativo, permissões, revogação de sessão, LogStream e NotificationHub. Não serve para o login — ver "SecureGate: dois clients e o `migrate`". |
| `Intranet__Login__ClientId` / `Intranet__Login__ClientSecret` | Sim | Client do **login interativo** (`AuthorizationCode`), separado do de máquina. Sem o `ClientId`, a aplicação não sobe em Production. |
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
      Secco__SecureGate__BaseUrl: "${SECURE_GATE_AUTHORITY}"
      Secco__SecureGate__ClientId: "${SECURE_GATE_CLIENT_ID}"
      Secco__SecureGate__ClientSecret: "${SECURE_GATE_CLIENT_SECRET}"
      Intranet__Login__ClientId: "${INTRANET_LOGIN_CLIENT_ID}"
      Intranet__Login__ClientSecret: "${INTRANET_LOGIN_CLIENT_SECRET}"
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
2. `docker compose run --rm secco-intranet-web migrate` com a imagem nova, **antes** do passo
   3 (ver "Banco de dados" acima). Saída diferente de `0` interrompe o deploy.
3. `docker compose up -d secco-intranet-web` — recria só o container da aplicação; o proxy
   e o banco não são afetados.
4. Confirme `GET /health/ready` → `200` antes de considerar o deploy concluído.

Não há mecanismo de rollback automático de migration — se precisar reverter, é
`dotnet ef database update <migration-anterior>` manual, contra a connection string de cada
tenant (`--project src/Secco.Intranet.Migrations.SqlServer` ou `.Postgres`).

## Checklist antes de ir ao ar

- [ ] Imagem builda com o PAT do feed privado (mecanismo do secret ainda a adicionar ao
      Dockerfile, ver "Construir a imagem").
- [ ] `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] `Secco:SecureGate:Authority/BaseUrl/ClientId/ClientSecret` configurados contra o
      SecureGate real (nunca o modo aberto).
- [ ] `Intranet:Login:ClientId/ClientSecret` configurados com o client de login.
- [ ] Os dois clients da Intranet declarados em `SecureGate:PlatformClients` e o `migrate` do
      SecureGate rodado antes de subir o SecureGate.
- [ ] Porta do container não publicada para fora do host — só o proxy alcança.
- [ ] `Secco:ReverseProxy:Habilitado=true` **só** com o ponto acima garantido.
- [ ] `migrate` da Intranet terminado com `0` antes do primeiro start.
- [ ] Role `intranet-admin` criada e atribuída a alguém antes do primeiro login.
- [ ] `GET /health/ready` → `200` depois de subir.
