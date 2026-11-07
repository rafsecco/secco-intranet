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
   instalação do SecureGate está em "SecureGate: três clients e o `migrate`", abaixo.
6. **Mais de uma réplica exige a pasta de chaves compartilhada.** As chaves do DataProtection
   ficam em `Intranet__ProtecaoDeDados__PastaDasChaves`, e cada réplica precisa enxergar a mesma
   pasta, ou o cookie de sessão emitido por uma não vale na outra. Fora de escopo deste guia.

## Topologia

```text
Internet
   │  HTTPS
   ▼
Reverse proxy (TLS termination — Caddy, nginx, Traefik; fora do docker-compose deste
   │            repo, ou um serviço a mais no mesmo compose)
   │  HTTP interno, rede que só o proxy alcança
   ▼
intranet-web (container, esta imagem)
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

## SecureGate: três clients e o `migrate`

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

**2. Declare os dois clients de plataforma da Intranet em `SecureGate:PlatformClients`.** Clients sem tenant
só nascem dessa lista, e o seed de referência (que roda no `migrate`) **remove** todo client de
plataforma que não estiver declarado nela, inclusive os inseridos à mão no banco. Se o client
da Intranet ficar de fora, o `migrate` o apaga, e a Intranet perde o login e a administração ao
mesmo tempo.

Os dois primeiros são **de plataforma** (sem tenant) porque o SecureGate dá um único grant a cada
client de plataforma: o login do navegador é authorization code, e as chamadas de serviço são client
credentials. O terceiro é **de produto**, vinculado ao tenant da Intranet: é com ele que a tela de
acesso administra pessoas e perfis (ADR-0052 da plataforma).

| Client | Tipo | Escopos | Usado por |
|---|---|---|---|
| Login (ex.: `intranet-login`) | `AuthorizationCode` | `profile`, `roles` | login interativo (`Intranet:Login`) |
| Máquina de plataforma (ex.: `intranet`) | `ClientCredentials` | `authorization:read`, `permissions:intranet`, `logstream`, `notificationhub` | permissões e revogação de sessão, publicação do catálogo de permissões no `migrate`, LogStream e NotificationHub (`Secco:SecureGate`) |
| Gestão do tenant (ex.: `intranet-gestao`) | `ClientCredentials`, **de produto**, criado pelo operador no tenant da Intranet | `securegate:tenant-manage`, `directory:read` | client administrativo (setores, `/acesso`): ler o tenant, listar e ler pessoas, atribuir e retirar perfil, nome de exibição, desativar e reativar, encerrar sessões, criar, listar, ler, alterar permissões e excluir perfil (`Secco:SecureGate:TenantManagement`) |

O client de máquina de plataforma **não carrega mais `securegate:admin`**: o secret dele deixou de
alcançar a instalação inteira. O client do navegador também não o carrega. Sem `permissions:intranet`, o `migrate` da
Intranet falha com `403` ao publicar o catálogo de permissões (ADR-0039 da plataforma), e o SecureGate
passa a recusar as permissões novas dela. O catálogo publicado leva as famílias por instância
`setor-{id}:read`/`write` e `biblioteca-{id}:read`/`write` (o `{id}` é o slug), e as de biblioteca
entram pelo mesmo `migrate`, sem passo extra. `logstream` e `notificationhub` só são
necessários se a Intranet usar esses serviços.

O client de gestão **não** entra em `SecureGate:PlatformClients`: quem o cria é o operador da
instalação, pela API do SecureGate, no tenant da Intranet (`POST /api/v1/tenants/{id}/clients`, com os
escopos `securegate:tenant-manage` e `directory:read`). O secret volta só nessa resposta; guarde-o na
hora e configure-o em `Secco__SecureGate__TenantManagement__ClientId`/`ClientSecret`. Cada escrita
desse client é registrada pelo próprio SecureGate, no banco dele, antes de acontecer (ADR-0054 da
plataforma): não depende do LogStream nem de `SecureGate:Audit`. Se a gravação falhar, a escrita recebe
`503` e não acontece. Quem lê essa trilha é o operador, no AdminPortal (seção "Trilha" do tenant).

Na configuração **do SecureGate** (não da Intranet), com os secrets por variável de ambiente
(`SecureGate__PlatformClients__0__ClientSecret`, `SecureGate__PlatformClients__1__ClientSecret`):

```json
{
  "SecureGate": {
    "PlatformClients": [
      {
        "ClientId": "intranet-login",
        "Type": "AuthorizationCode",
        "Scopes": ["profile", "roles"],
        "RedirectUris": ["https://intranet.suaempresa.com/signin-oidc"],
        "PostLogoutRedirectUris": ["https://intranet.suaempresa.com/signout-callback-oidc"]
      },
      {
        "ClientId": "intranet",
        "Type": "ClientCredentials",
        "Scopes": ["authorization:read", "permissions:intranet", "logstream", "notificationhub"]
      }
    ],
    "PermissionCatalog": {
      "Products": ["intranet"]
    }
  }
}
```

O `PermissionCatalog:Products` registra o escopo `permissions:intranet`, que não é de nenhum produto
da plataforma ([secco-platform#49](https://github.com/rafsecco/secco-platform/issues/49)). Sem ele, o
SecureGate recusa subir com o client acima ("escopo não registrado"). Depois de acrescentá-lo, rode o
`migrate` do SecureGate antes de subir, porque é o seed de referência que registra o escopo.

**3. Dê ao SecureGate duas credenciais de banco.** Desde o isolamento de tenant com Row-Level
Security (ADR-0041 da plataforma), o `migrate` usa uma credencial **dona** do banco
(`SecureGate__Database__MigrationConnectionString`), e a API usa outra **sem posse**, só com leitura
e escrita (`SecureGate__Database__ConnectionString`). Fora de Development, o SecureGate **recusa
subir** se a credencial de runtime for dona do banco. Em SQL Server, ao atualizar:

```sql
CREATE LOGIN secco_securegate_migrator WITH PASSWORD = N'<senha>';
USE secco_securegate;
CREATE USER secco_securegate_migrator FOR LOGIN secco_securegate_migrator;
ALTER ROLE db_owner ADD MEMBER secco_securegate_migrator;
ALTER ROLE db_owner DROP MEMBER secco_securegate_app;      -- a credencial de runtime atual
ALTER ROLE db_datareader ADD MEMBER secco_securegate_app;
ALTER ROLE db_datawriter ADD MEMBER secco_securegate_app;
```

Em PostgreSQL, o papel do `migrate` é dono das tabelas, e o de runtime tem `SELECT, INSERT,
UPDATE, DELETE` nas tabelas e `USAGE` nas sequences, sem `BYPASSRLS` e sem ser superusuário. Com o
RLS ligado, uma ferramenta SQL não enxerga nada sem contexto de sessão, nem com `sa`: antes de
consultar, rode `EXEC sp_set_session_context N'secco_mode', N'platform'` (SQL Server) ou
`SELECT set_config('secco.mode', 'platform', false)` (PostgreSQL). Detalhes na seção 7 do
`docs/getting-started.md` da plataforma.

## Construir a imagem

O `Dockerfile` está em `src/Secco.Intranet.Web/Dockerfile`. Build a partir da **raiz do
repositório** (o Central Package Management exige os `.props`/`nuget.config` da raiz no contexto),
com BuildKit:

```bash
export NUGET_PAT=ghp_xxx   # PAT com escopo read:packages (ou: export NUGET_PAT=$(gh auth token))

docker build \
  -f src/Secco.Intranet.Web/Dockerfile \
  -t intranet-web:<versao> \
  --secret id=nuget_pat,env=NUGET_PAT \
  .
```

O PAT entra como BuildKit secret e só existe no ambiente do comando de restore: não vai para
arquivo, camada nem `docker history`. O usuário do feed é `intranet` por padrão (o GitHub
Packages aceita qualquer usuário não vazio com PAT); `--build-arg NUGET_USER=<usuario>` troca.

A imagem:

- roda com o usuário não-root `app` (uid `1654`) da imagem base;
- guarda os arquivos em `/var/lib/intranet/documentos` (documentos, fotos e imagens do Mural) e as
  chaves do DataProtection em `/var/lib/intranet/chaves`, as duas declaradas como `VOLUME`. O bind
  mount de cada uma precisa pertencer ao uid `1654`;
- não traz `curl` nem `wget`: o healthcheck do exemplo de compose abaixo usa o `bash` da imagem.

Todo projeto sob `src/` entra na imagem sem editar o `Dockerfile`, inclusive um tema ou uma RCL
sua. Projeto fora de `src/` precisa de `COPY` próprio.

Ao subir, a aplicação registra o aviso `No XML encryptor configured`. É esperado: as chaves ficam
sem cifra em repouso, e por isso a pasta delas é tratada como segredo (ver "Backup e restauração").

**A imagem precisa ser glibc, nunca Alpine.** O `Secco.SDK.Imaging` (foto do colaborador) traz um asset nativo compilado para glibc; numa base Alpine (musl) o processamento de imagem falha. A base de runtime atual, `mcr.microsoft.com/dotnet/aspnet:10.0`, é Debian e atende.

## Banco de dados

1. **Escolha o engine** — `Intranet:Database:Provider` (`SqlServer`, padrão, ou
   `PostgreSql`). Todos os tenants de uma instalação usam o mesmo engine.
2. **Crie um banco e um login de aplicação por tenant**, com permissão só naquele banco
   (nunca `sa`/superusuário — mesmo princípio do `docker/init/provision-tenant-db.sql`
   usado em desenvolvimento, adapte para o seu SGBD gerenciado).
   **Porta do banco:** em HML pode ficar aberta para a equipe consultar; em PRD, fechada, alcançável
   só pela aplicação e pelo DBA.
3. **Registre a connection string de cada tenant** na configuração da aplicação (variável
   de ambiente, ver tabela abaixo) — não existe UI para cadastrar tenant hoje; é
   configuração de deploy.
4. **Rode o `migrate`** com a mesma imagem e a mesma configuração do container da aplicação.
   Ele aplica as migrations de todos os tenants do catálogo e sai, sem subir o servidor:

   ```bash
   docker compose run --rm intranet-web migrate
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
| `ASPNETCORE_ENVIRONMENT` | Sim | `Production`. `Development` é o **único** ambiente sem as travas de segurança: qualquer outro valor exige HTTPS no cookie e no OIDC, recusa subir sem as credenciais e a chave mestra abaixo e desliga migration/seed automáticos. Nunca `Development` num host exposto. |
| `Secco__SecureGate__Authority` | Sim | URL do SecureGate que emite os tokens. Fora de `Development`, sem ela a aplicação recusa subir; só em `Development` há o "modo aberto" (sem autenticação) (ADR-0020). |
| `Secco__SecureGate__BaseUrl` | Sim | URL da API do SecureGate, usada pelas chamadas de serviço (client administrativo, permissões, revogação de sessão). Em geral, a mesma da `Authority`. |
| `Secco__SecureGate__ClientId` / `Secco__SecureGate__ClientSecret` | Sim | Client de **máquina de plataforma** (`ClientCredentials`): permissões, revogação de sessão, catálogo de permissões, LogStream e NotificationHub. Não serve para o login nem para a gestão do tenant — ver "SecureGate: três clients e o `migrate`". |
| `Secco__SecureGate__TenantManagement__ClientId` / `Secco__SecureGate__TenantManagement__ClientSecret` | Sim | Client **de produto** da gestão do tenant (`securegate:tenant-manage`), criado pelo operador no tenant da Intranet. A `BaseUrl` vem de `Secco__SecureGate__BaseUrl`. Sem o `ClientId` e o `ClientSecret`, a aplicação não sobe fora de Development; sem o `ClientId`, a tela de acesso responde "não configurado". |
| `Intranet__Login__ClientId` / `Intranet__Login__ClientSecret` | Sim | Client do **login interativo** (`AuthorizationCode`), separado do de máquina. Sem o `ClientId`, a aplicação não sobe fora de Development. |
| `Secco__Tenancy__Tenants__{tenantId}__ConnectionString` | Sim, uma por tenant | Connection string do banco daquele tenant. `{tenantId}` é o GUID do tenant, hífens inclusos. |
| `Intranet__Database__Provider` | Não (padrão `SqlServer`) | `SqlServer` ou `PostgreSql`. |
| `Secco__ReverseProxy__Habilitado` | Sim, se atrás de proxy | `true` para confiar em `X-Forwarded-For`/`X-Forwarded-Proto`. Só ligue se a porta do container for inalcançável por fora do proxy — ver "Topologia". |
| `Intranet__Auditoria__LogStreamUrl` | Não, exceto para exportar | URL do `Secco.LogStream`. Vazia = trilha de auditoria não é gravada (modo silencioso, não é erro). **Exceção: a exportação Excel/PDF.** A trilha dela é fail-closed (ADR-0013), e fora de `Development` uma exportação sem a URL é recusada. |
| `Intranet__Exportacao__MaxXlsxRows` | Não (padrão `100000`) | Maior número de linhas de uma planilha exportada. Teto do Excel: 1.048.575. Acima do limite, a exportação pede para restringir o filtro. |
| `Intranet__Exportacao__MaxPdfRows` | Não (padrão `10000`) | Maior número de linhas de um PDF exportado (perto de 300 páginas). |
| `Intranet__Exportacao__MaxConcurrentExports` | Não (padrão: metade dos processadores) | Quantas exportações geram ao mesmo tempo na instância; as demais esperam vaga. |
| `Intranet__Exportacao__TempDirectory` | Não (padrão: temporário do sistema) | Raiz do diretório temporário onde o arquivo é gerado antes do envio. O SDK usa um subdiretório próprio, `0700` no Linux, e apaga o arquivo ao terminar. |
| `Intranet__Exportacao__PorPessoaPorMinuto` | Não (padrão `10`) | Exportações por pessoa por minuto; acima disso, `429`. O contador é da instância: com N réplicas, o teto efetivo é N vezes este valor. |
| `Intranet__Notificacao__HubUrl` | Não | URL do `Secco.NotificationHub`. Vazia = sino/e-mail de notificação não são enviados. |
| `Intranet__Notificacao__UrlBase` | Recomendada se `HubUrl` estiver definida | URL pública da Intranet (ex.: `https://intranet.suaempresa.com`, sem barra final) — sem ela, o link da notificação por e-mail fica relativo e não abre fora do navegador já logado. |
| `Intranet__Documentos__Chave__ChaveAtiva` | Sim, fora de Development | Chave mestra que embrulha as chaves dos documentos cifrados: base64 de 32 bytes, por exemplo `openssl rand -base64 32`. Sem ela a aplicação não sobe. **Perder a chave é perder todos os documentos**: guarde-a fora do host. A chave de desenvolvimento embutida só vale em `Development`. |
| `Intranet__Documentos__Chave__ChavesAposentadas__0` (e `__1`, ...) | Não | Chaves mestras antigas (base64, 32 bytes), usadas só para decifrar durante a rotação. Mantenha-as até a chave ativa ter reembrulhado todos os documentos. |
| `Intranet__Documentos__Armazenamento__Raiz` | Não (a imagem define `/var/lib/intranet/documentos`) | Pasta onde ficam os arquivos cifrados: documentos, fotos e imagens do Mural. Faça o bind mount dela: sem isso, recriar o container apaga os arquivos. |
| `Intranet__ProtecaoDeDados__PastaDasChaves` | Não (a imagem define `/var/lib/intranet/chaves`) | Pasta das chaves do DataProtection, que protegem cookie de sessão, antiforgery e TempData. Faça o bind mount dela: sem isso, recriar o container desconecta todo mundo. Vazia, vale o padrão do framework. ADR-0014. |
| `Intranet__Documentos__TamanhoMaximoBytes` | Não (padrão 25 MB, `26214400`) | Tamanho máximo aceito por documento enviado. |
| `Intranet__Imaging__MaxInputBytes` | Não (padrão 10 MB, `10485760`) | Tamanho máximo do arquivo de imagem aceito no envio da foto do colaborador (seção `Intranet:Imaging`, `Secco.SDK.Imaging`). O envio também é barrado em ~10 MB na própria rota (limite de requisição) e no texto da tela, então subir este valor acima disso não tem efeito. |
| `Intranet__Imaging__MaxPixels` | Não (padrão 50 milhões) | Máximo de pixels da imagem de entrada; acima disso o envio é recusado. Cada decodificação pode usar ~4 bytes por pixel (50 milhões ≈ 200 MB), multiplicado por `MaxConcurrentOperations`: em host com pouca memória, baixe este valor (ex.: 25 milhões) ou o de `MaxConcurrentOperations`. |
| `Intranet__Imaging__MaxConcurrentOperations` | Não (padrão = número de processadores) | Quantas imagens são processadas ao mesmo tempo. |
| `Intranet__Csp__Modo` | Não (padrão `Enforce`) | Modo da Content-Security-Policy (ADR-0057 da plataforma): `Enforce` bloqueia; `ReportOnly` só relata violações no console do navegador (ou no coletor abaixo), útil para investigar um bloqueio novo sem tirar a tela do ar. Valor desconhecido impede a subida. Os demais cabeçalhos (`nosniff`, `Referrer-Policy`, `Permissions-Policy`, `X-Frame-Options`, HSTS fora de Development) valem nos dois modos. |
| `Intranet__Csp__ReportUri` | Não | Coletor externo, HTTPS, dos relatórios de violação da CSP. A Intranet não hospeda coletor. |
| `Secco__LogStream__*` | Não | Seção do sink de log geral (`ILogger` → LogStream), separada da auditoria acima. Vazia = log só local (stdout do container). Ver `Secco.SDK.Logging` para as chaves da seção. |

Credenciais de `Secco.LogStream`/`Secco.NotificationHub` (quando as URLs acima estiverem
preenchidas) reaproveitam `Secco__SecureGate__ClientId`/`ClientSecret` — os SDKs montam
client credentials a partir dessa mesma seção (secco-platform#25); não há uma credencial
separada para configurar.

## Aviso de vencimento (`Intranet:Vencimentos`)

O recurso Vencimentos envia os avisos por um worker em processo, um por tenant, que acorda a cada intervalo, decide quais marcos venceram e manda cada aviso na hora pelo `Secco.NotificationHub`. Sem `Intranet__Notificacao__HubUrl` o worker nem é registrado e não há aviso de vencimento (as telas continuam funcionando).

| Variável | Obrigatória | Descrição |
|---|---|---|
| `Intranet__Vencimentos__Habilitado` | Não (padrão `true`) | `false` desliga o worker. |
| `Intranet__Vencimentos__IntervaloEmMinutos` | Não (padrão `60`) | De quanto em quanto tempo o worker confere os itens. |
| `Intranet__Vencimentos__FusoHorario` | Não (padrão `America/Sao_Paulo`) | Fuso que define "hoje" para os marcos de aviso. Id IANA (ex.: `America/Sao_Paulo`). Id inválido: o worker registra um erro no log e não roda. |

## `docker-compose` de HML e PRD

Exemplo mínimo — adapte para o seu proxy. O banco fica fora do compose, gerenciado à parte. Os dados
ficam em pastas do host, para o backup ser cópia de pasta (ver "Backup e restauração").

Prepare o host uma vez. As duas pastas da aplicação precisam pertencer ao uid da imagem:

```bash
export INTRANET_DADOS=/srv/intranet
sudo install -d -o 1654 -g 1654 -m 0700 "$INTRANET_DADOS/documentos" "$INTRANET_DADOS/chaves"
sudo install -d -m 0700 "$INTRANET_DADOS/caddy"
```

Pasta com dono errado aparece no log como erro de permissão, com o caminho. A correção é
`sudo chown -R 1654:1654 <pasta>`.

```yaml
services:
  intranet-web:
    image: intranet-web:latest
    restart: unless-stopped
    # Em lista, não em mapa: o Compose não interpola chave de mapa, e o nome da connection string
    # carrega o id do tenant.
    environment:
      - ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Production}
      - Secco__SecureGate__Authority=${SECURE_GATE_AUTHORITY}
      - Secco__SecureGate__BaseUrl=${SECURE_GATE_AUTHORITY}
      - Secco__SecureGate__ClientId=${SECURE_GATE_CLIENT_ID}
      - Secco__SecureGate__ClientSecret=${SECURE_GATE_CLIENT_SECRET}
      - Secco__SecureGate__TenantManagement__ClientId=${SECURE_GATE_TENANT_MANAGEMENT_CLIENT_ID}
      - Secco__SecureGate__TenantManagement__ClientSecret=${SECURE_GATE_TENANT_MANAGEMENT_CLIENT_SECRET}
      - Intranet__Login__ClientId=${INTRANET_LOGIN_CLIENT_ID}
      - Intranet__Login__ClientSecret=${INTRANET_LOGIN_CLIENT_SECRET}
      - Intranet__Documentos__Chave__ChaveAtiva=${INTRANET_DOCUMENTOS_CHAVE}
      - Secco__ReverseProxy__Habilitado=true
      - Secco__Tenancy__Tenants__${TENANT_ID}__ConnectionString=${TENANT_CONNECTION_STRING}
    # Nenhuma porta publicada para o host — só o proxy alcança esta rede.
    networks:
      - interna
    volumes:
      - ${INTRANET_DADOS:-/srv/intranet}/documentos:/var/lib/intranet/documentos
      - ${INTRANET_DADOS:-/srv/intranet}/chaves:/var/lib/intranet/chaves
    healthcheck:
      # A imagem não tem curl/wget: o bash abre a conexão e confere o status 200.
      test: ["CMD", "bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/live HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]
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
      - ${INTRANET_DADOS:-/srv/intranet}/caddy:/data
    networks:
      - interna
    depends_on:
      - intranet-web

networks:
  interna:
```

O mesmo arquivo serve a HML e PRD, com um `.env` por ambiente ao lado dele. Em HML, use
`ASPNETCORE_ENVIRONMENT=Staging` e uma **chave mestra própria**, nunca a de PRD: quem administra HML
não pode conseguir decifrar os arquivos de produção.

`Caddyfile` mínimo (TLS automático via Let's Encrypt, só exige DNS apontando para o host):

```caddyfile
intranet.suaempresa.com {
    reverse_proxy intranet-web:8080
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
  (`Secco.SDK.AspNetCore`). Use como readiness probe antes de rotear tráfego. Não consulta os
  bancos dos tenants: responde `200` mesmo com o banco fora do ar. Para confirmar um deploy ou uma
  restauração, abra também uma página da Intranet.

Nenhum dos dois exige autenticação.

## Atualização (rollout)

Sem orquestrador com rolling update (é o caso de um host único), o corte é direto:

1. Build da imagem nova (tag por versão, não `latest` puro — facilita rollback).
2. `docker compose run --rm intranet-web migrate` com a imagem nova, **antes** do passo
   3 (ver "Banco de dados" acima). Saída diferente de `0` interrompe o deploy.
3. `docker compose up -d intranet-web` — recria só o container da aplicação; o proxy
   e o banco não são afetados.
4. Confirme `GET /health/ready` → `200` antes de considerar o deploy concluído.

Não há mecanismo de rollback automático de migration — se precisar reverter, é
`dotnet ef database update <migration-anterior>` manual, contra a connection string de cada
tenant (`--project src/Secco.Intranet.Migrations.SqlServer` ou `.Postgres`).

## Backup e restauração

Quatro peças, cada uma copiada do seu jeito:

| Peça | Como copiar | Se perder |
|---|---|---|
| Banco de cada tenant | `BACKUP DATABASE` ou `pg_dump`, nunca arquivo | Perde os dados e as chaves embrulhadas dos arquivos |
| Pasta `documentos` (documentos, fotos, imagens do Mural) | Cópia de pasta | Perde os arquivos |
| Chave mestra e chaves aposentadas | Cofre, **separado dos outros dois** | Perde todos os arquivos |
| Pasta `chaves` (DataProtection) | Opcional, cifrada | Todos entram de novo; nada mais se perde |

**Chave mestra junto do backup é a mesma coisa que sem cifra.** Quem tem as três primeiras peças lê
tudo. A chave vai para um cofre, com outro controle de acesso.

**A pasta `chaves` é segredo.** Quem a tem forja a sessão de qualquer pessoa, inclusive de um
`intranet-admin`. Como perdê-la só obriga todos a entrar de novo, deixá-la fora do backup é uma opção
legítima.

### Fazer o backup

A ordem é **banco primeiro, depois a pasta**. Assim, toda linha do banco copiado tem o arquivo na
cópia seguinte, e um arquivo sem linha é inofensivo. Como trocar uma foto remove o arquivo anterior,
a janela não é zero: um backup estrito pede a aplicação parada ou um horário sem uso.

1. **Banco**, um por tenant. Nunca copie o arquivo do banco.

   ```sql
   -- SQL Server
   BACKUP DATABASE [intranet_alfa]
     TO DISK = N'/var/opt/mssql/backup/intranet_alfa.bak'
     WITH CHECKSUM, COMPRESSION, INIT;
   ```

   ```bash
   # PostgreSQL
   pg_dump --format=custom --dbname=intranet_alfa --file=intranet_alfa.dump
   ```

2. **Pasta `documentos`**:

   ```bash
   sudo tar -C "$INTRANET_DADOS" -czf "documentos-$(date +%F).tar.gz" documentos
   ```

3. **Pasta `chaves`**, se decidir guardá-la, sempre cifrada:

   ```bash
   sudo tar -C "$INTRANET_DADOS" -cz chaves | gpg --symmetric --output "chaves-$(date +%F).tar.gz.gpg"
   ```

**Rotação da chave mestra:** um backup antigo pode estar embrulhado com uma chave já aposentada.
Guarde cada chave enquanto existir backup que dependa dela, mesmo depois de tirá-la de
`Intranet__Documentos__Chave__ChavesAposentadas__*`.

### Restaurar

Nesta ordem:

1. **Configure as chaves mestras**: `Intranet__Documentos__Chave__ChaveAtiva` e, se o backup for de
   antes de uma rotação, a chave da época em `ChavesAposentadas`.
2. **Restaure o banco.** Em outro servidor, o usuário do banco fica órfão e precisa ser religado ao
   login:

   ```sql
   -- SQL Server
   RESTORE DATABASE [intranet_alfa] FROM DISK = N'/var/opt/mssql/backup/intranet_alfa.bak';
   USE [intranet_alfa];
   ALTER USER [intranet_alfa_app] WITH LOGIN = [intranet_alfa_app];
   ```

   ```bash
   # PostgreSQL: crie o banco vazio antes; o dono passa a ser o usuário da conexão
   pg_restore --no-owner --dbname=intranet_alfa intranet_alfa.dump
   ```

3. **Restaure a pasta `documentos`**, com o dono certo:

   ```bash
   sudo tar -C "$INTRANET_DADOS" -xzf documentos-AAAA-MM-DD.tar.gz
   sudo chown -R 1654:1654 "$INTRANET_DADOS/documentos"
   ```

4. **Restaure a pasta `chaves`**, se a guardou, com o mesmo dono. Sem ela, todos entram de novo.
5. **Rode o `migrate`** se a imagem for mais nova que o backup.
6. **Suba a aplicação** e confira `GET /health/ready` → `200`, abrindo também uma página, porque essa
   checagem não consulta o banco.

**Um tenant só:** restaure o banco dele e a subpasta `{raiz}/{tenantId}`. Os outros não são afetados.

### Testar a restauração

Um backup que nunca foi restaurado é uma hipótese. Restaure em um ambiente isolado, com
`Intranet__Notificacao__HubUrl` vazia, para o worker de vencimentos não mandar aviso real.

Depois, abra um documento antigo, uma foto e uma imagem do Mural: só a abertura prova a decifragem.
Compare também a contagem de arquivos em `{raiz}/{tenantId}` com a de linhas nas tabelas que os
referenciam. `RESTORE VERIFYONLY` confirma o arquivo de backup, mas não substitui esse teste.

## Checklist antes de ir ao ar

- [ ] Imagem construída com o PAT do feed privado como BuildKit secret (ver "Construir a imagem").
- [ ] `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] `Secco:SecureGate:Authority/BaseUrl/ClientId/ClientSecret` configurados contra o
      SecureGate real (nunca o modo aberto).
- [ ] `Intranet:Login:ClientId/ClientSecret` configurados com o client de login.
- [ ] `Secco:SecureGate:TenantManagement:ClientId/ClientSecret` configurados com o client de produto
      criado pelo operador no tenant da Intranet (`securegate:tenant-manage` + `directory:read`).
- [ ] `Intranet:Documentos:Chave:ChaveAtiva` configurada (base64 de 32 bytes) e guardada fora do
      host: sem ela a aplicação não sobe, e perdê-la é perder os documentos.
- [ ] Bind mounts em `/var/lib/intranet/documentos` e `/var/lib/intranet/chaves`, com as pastas do
      host pertencendo ao uid `1654`: sem eles, recriar o container apaga os arquivos e desconecta
      todo mundo.
- [ ] Os dois clients de plataforma da Intranet declarados em `SecureGate:PlatformClients` e o
      `migrate` do SecureGate rodado antes de subir o SecureGate.
- [ ] SecureGate com duas credenciais de banco: a de migração (dona) e a de runtime (sem posse).
- [ ] Porta do container não publicada para fora do host — só o proxy alcança.
- [ ] Em PRD, porta do banco fechada: só a aplicação e o DBA a alcançam.
- [ ] `Secco:ReverseProxy:Habilitado=true` **só** com o ponto acima garantido.
- [ ] `migrate` da Intranet terminado com `0` antes do primeiro start.
- [ ] Role `intranet-admin` criada e atribuída a alguém antes do primeiro login.
- [ ] `GET /health/ready` → `200` depois de subir, e uma página da Intranet abrindo.
- [ ] Backup configurado e **restauração testada** (ver "Backup e restauração"), com a chave mestra
      guardada à parte.
