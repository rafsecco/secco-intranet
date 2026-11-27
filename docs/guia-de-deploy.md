# Guia de deploy em produção

> Caminho coberto: **Docker/docker-compose em host único**, atrás de um reverse proxy
> com TLS. É o caminho mais simples de operar sozinho, e o que este guia assume do início
> ao fim. Outras topologias (Kubernetes, múltiplos hosts) não estão cobertas — nada no
> código impede, mas nada aqui foi pensado para isso.

Este guia é para quem vai **rodar a própria instância** da Intranet (ADR-0002: é um
produto que a empresa baixa e opera, não um serviço hospedado pela Secco).

A Intranet depende do `Secco.SecureGate` e não roda sem ele. O `Secco.LogStream` e o
`Secco.NotificationHub` são opcionais (ver abaixo). Os três, mais o `Secco.AdminPortal` (o console
de operação), são distribuídos pela plataforma como **imagens Docker públicas** no GHCR. Eles são
implantados pelo [guia de implantação da plataforma](https://github.com/rafsecco/secco-platform/blob/main/docs/guia-de-implantacao.md), sem compilar nada. Este guia
cobre a Intranet e o encaixe dela nessa instalação, de preferência no mesmo host.

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
5. **SecureGate, LogStream e NotificationHub vêm das imagens da plataforma.** Quem os instala é o
   [guia de implantação da plataforma](https://github.com/rafsecco/secco-platform/blob/main/docs/guia-de-implantacao.md), com o compose de host único dela
   (`deploy/` do `secco-platform`). Este guia não repete aquele: traz só o que a Intranet exige da
   instalação ("SecureGate: quatro clients e o `migrate`") e como a Intranet entra no mesmo host
   ("`docker-compose` de HML e PRD").
6. **Mais de uma réplica exige a pasta de chaves compartilhada.** As chaves do DataProtection
   ficam em `Intranet__ProtecaoDeDados__PastaDasChaves`, e cada réplica precisa enxergar a mesma
   pasta, ou o cookie de sessão emitido por uma não vale na outra. Fora de escopo deste guia.

## Topologia

No mesmo host da plataforma, que é o caminho deste guia:

```text
Internet
   │  HTTPS 443 (e 80 para o ACME)
   ▼
proxy (Caddy do compose da plataforma: um só por host)
   │  login.exemplo.com.br        → securegate        ┐
   │  logs.exemplo.com.br         → logstream         │ imagens da plataforma
   │  notificacoes.exemplo.com.br → notificationhub   │ (deploy/ do secco-platform)
   │  admin.exemplo.com.br        → adminportal       ┘
   │  intranet.exemplo.com.br     → intranet-web      ← este repositório
   │
   └── rede "secco" (a da plataforma; a Intranet entra nela como rede externa)

intranet-web ──► SQL Server ou PostgreSQL — um banco por tenant (ADR-0005/ADR-0018)
             └─► SecureGate, LogStream e NotificationHub pelos nomes públicos, que
                 dentro da rede resolvem para o proxy
```

Com a plataforma em outro host, a Intranet tem o próprio proxy e chama os serviços pelos nomes
públicos, pela internet ou pela rede interna da empresa. O compose de exemplo mostra as duas formas.

A postura de rede que sustenta `Secco:ReverseProxy:Habilitado` (ver abaixo): a porta do
container **não pode ser alcançável diretamente** de fora — só o proxy a alcança. Num
`docker-compose` isso é automático (a porta do serviço só é exposta ao host se você
publicar `ports:`; não publique, deixe só o proxy publicar 443).

## SecureGate: quatro clients e o `migrate`

**Versão das imagens.** Use a versão de imagem da plataforma cuja linha na tabela de
compatibilidade (no guia de implantação e no `CHANGELOG.md` do `secco-platform`) traz os clients que
esta versão da Intranet usa. A Intranet 0.2.0 usa `Secco.SecureGate.Client` 0.26.0,
`Secco.LogStream.Client` 0.5.0 e `Secco.NotificationHub.Client` 0.6.0. Os clients batem com a linha
0.1.0 da tabela, mas é a partir das **imagens 0.2.0** que o NotificationHub lê tenants e permissões do
SecureGate, e só com isso a integração de notificação funciona (ver o client de integração abaixo).
Use as **imagens 0.2.0** ou mais novas.

**1. Rode o `migrate` do SecureGate antes de subir.** Fora de Development, o SecureGate não migra o
próprio banco nem roda o seed de referência no startup. Com migration pendente, **ele se recusa a
subir**. No compose da plataforma, o `migrate` é um serviço próprio, e é ele que aplica os clients
abaixo:

```bash
cd <pasta deploy/ da plataforma>
docker compose run --rm securegate-migrate
echo $?   # 0 = aplicado; 1 = falhou (o log diz onde)
```

**2. Declare os dois clients de plataforma da Intranet em `SecureGate:PlatformClients`.** Clients sem tenant
só nascem dessa lista, e o seed de referência (que roda no `migrate`) **remove** todo client de
plataforma que não estiver declarado nela, inclusive os inseridos à mão no banco. Se o client
da Intranet ficar de fora, o `migrate` o apaga, e a Intranet perde o login e a administração ao
mesmo tempo.

Os dois primeiros são **de plataforma** (sem tenant) porque o SecureGate dá um único grant a cada
client de plataforma: o login do navegador é authorization code, e as chamadas de serviço são client
credentials. Os outros dois são **de produto**, vinculados ao tenant da Intranet, e o token deles sai
com `tenant_id`:
- **gestão:** é com ele que a tela de acesso administra pessoas e perfis (ADR-0052 da plataforma);
- **integração:** é com ele que a Intranet grava a trilha e o log no LogStream e notifica pelo
  NotificationHub. Um client de plataforma não serve para isso, porque o token dele não tem tenant, e
  os dois serviços não saberiam onde gravar.

| Client | Tipo | Escopos | Usado por |
|---|---|---|---|
| Login (ex.: `intranet-login`) | `AuthorizationCode` | `profile`, `roles` | login interativo (`Intranet:Login`) |
| Máquina de plataforma (ex.: `intranet`) | `ClientCredentials` | `authorization:read`, `permissions:intranet` | permissões e revogação de sessão, publicação do catálogo de permissões no `migrate` (`Secco:SecureGate`) |
| Integração (ex.: `cli_…`, nome "Intranet") | `ClientCredentials`, **de produto**, criado pelo operador no tenant da Intranet, com o perfil `intranet-integracao` | `logstream`, `notificationhub` | trilha de auditoria e log no LogStream, notificações e caixa in-app no NotificationHub (`Secco:LogStream` e `Secco:NotificationHub`) |
| Gestão do tenant (ex.: `intranet-gestao`) | `ClientCredentials`, **de produto**, criado pelo operador no tenant da Intranet | `securegate:tenant-manage`, `directory:read` | client administrativo (setores, `/acesso`): ler o tenant, listar e ler pessoas, atribuir e retirar perfil, nome de exibição, desativar e reativar, encerrar sessões, criar, listar, ler, alterar permissões e excluir perfil (`Secco:SecureGate:TenantManagement`) |

O client de máquina de plataforma **não carrega mais `securegate:admin`**: o secret dele deixou de
alcançar a instalação inteira. O client do navegador também não o carrega. Sem `permissions:intranet`, o `migrate` da
Intranet falha com `403` ao publicar o catálogo de permissões (ADR-0039 da plataforma), e o SecureGate
passa a recusar as permissões novas dela. O catálogo publicado leva as famílias por instância
`setor-{id}:read`/`write` e `biblioteca-{id}:read`/`write` (o `{id}` é o slug), e as de biblioteca
entram pelo mesmo `migrate`, sem passo extra.

**O client de integração** só é preciso se a Intranet usar o LogStream ou o NotificationHub. Quem cria
é o operador, no tenant da Intranet, pelo AdminPortal ou pela API do SecureGate. Primeiro, o perfil
com as permissões das chamadas que a Intranet faz:

```bash
# Perfil com as permissões (todas já estão nos catálogos publicados pelos dois serviços)
curl -fsS -X POST "https://login.exemplo.com.br/api/v1/tenants/$TENANT/roles" \
  -H "Authorization: Bearer $TOKEN_OPERADOR" -H "Content-Type: application/json" \
  -d '{"name":"intranet-integracao"}'
curl -fsS -X PUT "https://login.exemplo.com.br/api/v1/tenants/$TENANT/roles/intranet-integracao/permissions" \
  -H "Authorization: Bearer $TOKEN_OPERADOR" -H "Content-Type: application/json" \
  -d '{"permissions":["audit-entries:write","log-entries:write","notifications:write","notifications:read","in-app-notifications:read","in-app-notifications:write"]}'

# O client de produto; o clientSecret volta só nesta resposta
curl -fsS -X POST "https://login.exemplo.com.br/api/v1/tenants/$TENANT/clients" \
  -H "Authorization: Bearer $TOKEN_OPERADOR" -H "Content-Type: application/json" \
  -d '{"name":"Intranet","scopes":["logstream","notificationhub"],"roles":["intranet-integracao"]}'
```

| Permissão | Serviço | Para quê |
|---|---|---|
| `audit-entries:write` | LogStream | trilha de auditoria (`Intranet__Auditoria__LogStreamUrl`), inclusive a obrigatória da exportação |
| `log-entries:write` | LogStream | log geral (`Secco__LogStream__BaseUrl`, sink do `Secco.SDK.Logging`) |
| `notifications:write` | NotificationHub | enviar as notificações e os avisos de vencimento |
| `notifications:read` | NotificationHub | resumo de entrega de uma publicação |
| `in-app-notifications:read` e `in-app-notifications:write` | NotificationHub | sino: ler a caixa e marcar como lida |

Na Intranet, o `clientId` e o `clientSecret` vão em `Secco__LogStream__ClientId`/`ClientSecret` e em
`Secco__NotificationHub__ClientId`/`ClientSecret` (o mesmo client nos dois). A URL do SecureGate vem
de `Secco__SecureGate__BaseUrl`. Revogar uma permissão no perfil vale nos dois serviços em até um TTL
do cache de permissões. O secret só alcança o tenant da Intranet: um `X-Tenant-Id` de outro tenant é
recusado com `400`.

O client de gestão **não** entra em `SecureGate:PlatformClients`: quem o cria é o operador da
instalação, pela API do SecureGate, no tenant da Intranet (`POST /api/v1/tenants/{id}/clients`, com os
escopos `securegate:tenant-manage` e `directory:read`). O secret volta só nessa resposta; guarde-o na
hora e configure-o em `Secco__SecureGate__TenantManagement__ClientId`/`ClientSecret`. Cada escrita
desse client é registrada pelo próprio SecureGate, no banco dele, antes de acontecer (ADR-0054 da
plataforma): não depende do LogStream nem de `SecureGate:Audit`. Se a gravação falhar, a escrita recebe
`503` e não acontece. Quem lê essa trilha é o operador, no AdminPortal (seção "Trilha" do tenant).

**O `directory:read` desse client alimenta o Diretório e o Organograma (ADR-0015).** A Intranet pede um
token só com esse escopo e lê as pessoas pela API de diretório.

- **Sem o escopo:** o Diretório continua funcionando pela leitura de antes, sem os atributos do Entra, e
  o log avisa ("Leitura do diretório do SecureGate falhou … usando a gestão de acesso").
- **Para o Organograma se montar a partir do Entra,** o SecureGate precisa de três coisas:
  - a versão com a ADR-0059 da plataforma;
  - o grupo de provisionamento marcado;
  - o `User.Read.All` concedido no Entra (ver o README do SecureGate).
- **Campos do Entra:** cargo, setor e gestor que vêm de lá ficam só leitura no Diretório.
- **Setor:** casa quando o nome do setor na Intranet é igual ao departamento do Entra. Maiúsculas e
  acentos não contam. Os departamentos que não casam aparecem em `/diretorio/importar`.

Na configuração **do SecureGate** (não da Intranet). No compose da plataforma, os clients ficam na
âncora `x-securegate-clients` da sua cópia do `deploy/docker-compose.yml`, que só o
`securegate-migrate` recebe. Use os próximos índices livres (os quatro da plataforma ocupam de 0 a 3)
e os secrets vindos do `.env` da plataforma. O SecureGate em execução lê os clients do banco, então
não precisa ser reiniciado depois do `migrate`.

```yaml
x-securegate-clients: &securegate-clients
  # ... os clients da plataforma, índices 0 a 3 ...
  SecureGate__PlatformClients__4__ClientId: "intranet-login"
  SecureGate__PlatformClients__4__Type: "AuthorizationCode"
  SecureGate__PlatformClients__4__ClientSecret: "${INTRANET_LOGIN_CLIENT_SECRET:?}"
  SecureGate__PlatformClients__4__Scopes__0: "profile"
  SecureGate__PlatformClients__4__Scopes__1: "roles"
  SecureGate__PlatformClients__4__RedirectUris__0: "https://intranet.exemplo.com.br/signin-oidc"
  SecureGate__PlatformClients__4__PostLogoutRedirectUris__0: "https://intranet.exemplo.com.br/signout-callback-oidc"
  SecureGate__PlatformClients__5__ClientId: "intranet"
  SecureGate__PlatformClients__5__Type: "ClientCredentials"
  SecureGate__PlatformClients__5__ClientSecret: "${INTRANET_CLIENT_SECRET:?}"
  SecureGate__PlatformClients__5__Scopes__0: "authorization:read"
  SecureGate__PlatformClients__5__Scopes__1: "permissions:intranet"
  SecureGate__PermissionCatalog__Products__0: "intranet"
```

Os secrets precisam de ao menos 32 caracteres (`openssl rand -hex 32`). Os mesmos valores vão para a
configuração da Intranet (`Intranet__Login__ClientSecret` e `Secco__SecureGate__ClientSecret`).

O `PermissionCatalog:Products` registra o escopo `permissions:intranet`, que não é de nenhum produto
da plataforma ([secco-platform#49](https://github.com/rafsecco/secco-platform/issues/49)). Sem ele, o
SecureGate recusa subir com o client acima ("escopo não registrado"). Depois de acrescentá-lo, rode o
`migrate` do SecureGate antes de subir, porque é o seed de referência que registra o escopo.

**3. Dê ao SecureGate duas credenciais de banco.** Desde o isolamento de tenant com Row-Level
Security (ADR-0041 da plataforma), o `migrate` usa uma credencial **dona** do banco
(`SecureGate__Database__MigrationConnectionString`), e a API usa outra **sem posse**, só com leitura
e escrita (`SecureGate__Database__ConnectionString`). Fora de Development, o SecureGate **recusa
subir** se a credencial de runtime for dona do banco. Numa instalação nova, o script do guia da
plataforma (`deploy/sql/sqlserver-plataforma.sql`) já cria as duas. Numa instalação antiga, que
ainda usa uma credencial só, em SQL Server:

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
| `Secco__SecureGate__ClientId` / `Secco__SecureGate__ClientSecret` | Sim | Client de **máquina de plataforma** (`ClientCredentials`): permissões, revogação de sessão e catálogo de permissões. Não serve para o login, para a gestão do tenant nem para LogStream e NotificationHub — ver "SecureGate: quatro clients e o `migrate`". |
| `Secco__LogStream__ClientId` / `Secco__LogStream__ClientSecret` | Sim, se usar o LogStream | Client **de integração** (de produto, do tenant da Intranet). Sem eles, as chamadas ao LogStream caem no client de plataforma, que não tem tenant, e são recusadas. |
| `Secco__NotificationHub__ClientId` / `Secco__NotificationHub__ClientSecret` | Sim, se usar o NotificationHub | O mesmo client de integração. Exige as imagens 0.2.0 ou mais novas da plataforma. |
| `Secco__SecureGate__TenantManagement__ClientId` / `Secco__SecureGate__TenantManagement__ClientSecret` | Sim | Client **de produto** da gestão do tenant (`securegate:tenant-manage`), criado pelo operador no tenant da Intranet. A `BaseUrl` vem de `Secco__SecureGate__BaseUrl`. Sem o `ClientId` e o `ClientSecret`, a aplicação não sobe fora de Development; sem o `ClientId`, a tela de acesso responde "não configurado". |
| `Intranet__Login__ClientId` / `Intranet__Login__ClientSecret` | Sim | Client do **login interativo** (`AuthorizationCode`), separado do de máquina. Sem o `ClientId`, a aplicação não sobe fora de Development. |
| `Secco__Tenancy__Tenants__{tenantId}__ConnectionString` | Sim, uma por tenant | Connection string do banco daquele tenant. `{tenantId}` é o GUID do tenant, hífens inclusos. |
| `Intranet__Database__Provider` | Não (padrão `SqlServer`) | `SqlServer` ou `PostgreSql`. |
| `Secco__ReverseProxy__Habilitado` | Sim, se atrás de proxy | `true` para confiar em `X-Forwarded-For`/`X-Forwarded-Proto`. Só ligue se a porta do container for inalcançável por fora do proxy — ver "Topologia". |
| `AllowedHosts` | Recomendado | Domínio da instalação, por exemplo `intranet.exemplo.com.br` (vários separados por `;`). O padrão `*` aceita qualquer cabeçalho `Host`; com o domínio fixo, requisição com outro `Host` recebe 400, e o endereço de retorno do login nunca é montado com um host forjado. Se um healthcheck chama a aplicação por `localhost`, inclua `localhost` na lista. |
| `Intranet__Auditoria__LogStreamUrl` | Não, exceto para exportar | URL do `Secco.LogStream`. Vazia = trilha de auditoria não é gravada (modo silencioso, não é erro). **Exceção: a exportação Excel/PDF.** A trilha dela é fail-closed (ADR-0013), e fora de `Development` uma exportação sem a URL é recusada. |
| `Intranet__Exportacao__MaxXlsxRows` | Não (padrão `100000`) | Maior número de linhas de uma planilha exportada. Teto do Excel: 1.048.575. Acima do limite, a exportação pede para restringir o filtro. |
| `Intranet__Exportacao__MaxPdfRows` | Não (padrão `10000`) | Maior número de linhas de um PDF exportado (perto de 300 páginas). |
| `Intranet__Exportacao__MaxConcurrentExports` | Não (padrão: metade dos processadores) | Quantas exportações geram ao mesmo tempo na instância; as demais esperam vaga. |
| `Intranet__Exportacao__TempDirectory` | Não (padrão: temporário do sistema) | Raiz do diretório temporário onde o arquivo é gerado antes do envio. O SDK usa um subdiretório próprio, `0700` no Linux, e apaga o arquivo ao terminar. |
| `Intranet__Exportacao__PorPessoaPorMinuto` | Não (padrão `10`) | Exportações por pessoa por minuto; acima disso, `429`. O contador é da instância: com N réplicas, o teto efetivo é N vezes este valor. |
| `Intranet__Notificacao__HubUrl` | Não | URL do `Secco.NotificationHub`. Vazia = sino/e-mail de notificação não são enviados. |
| `Intranet__Notificacao__UrlBase` | Recomendada se `HubUrl` estiver definida | URL pública da Intranet (ex.: `https://intranet.suaempresa.com`, sem barra final) — sem ela, o link da notificação por e-mail fica relativo e não abre fora do navegador já logado. |
| `Intranet__Documentos__Chave__ChaveAtiva` | Sim, fora de Development | Chave mestra que embrulha as chaves dos documentos cifrados: base64 de 32 bytes, por exemplo `openssl rand -base64 32`. Sem ela a aplicação não sobe. **Perder a chave é perder todos os documentos**: guarde-a fora do host. A chave de desenvolvimento embutida só vale em `Development`. |
| `Intranet__Documentos__Chave__ChavesAposentadas__0` (e `__1`, ...) | Não | Chaves mestras antigas (base64, 32 bytes), usadas só para decifrar durante a rotação. O `migrate` reembrulha as chaves dos arquivos para a ativa, e a aposentada sai quando nada mais depende dela (ver "Troca da chave mestra"). |
| `Intranet__Documentos__Armazenamento__Raiz` | Não (a imagem define `/var/lib/intranet/documentos`) | Pasta onde ficam os arquivos cifrados: documentos, fotos e imagens do Mural. Faça o bind mount dela: sem isso, recriar o container apaga os arquivos. |
| `Intranet__ProtecaoDeDados__PastaDasChaves` | Não (a imagem define `/var/lib/intranet/chaves`) | Pasta das chaves do DataProtection, que protegem cookie de sessão, antiforgery e TempData. Faça o bind mount dela: sem isso, recriar o container desconecta todo mundo. Vazia, vale o padrão do framework. ADR-0014. |
| `Intranet__Documentos__TamanhoMaximoBytes` | Não (padrão 25 MB, `26214400`) | Tamanho máximo aceito por documento enviado. |
| `Intranet__Imaging__MaxInputBytes` | Não (padrão 10 MB, `10485760`) | Tamanho máximo do arquivo de imagem aceito no envio da foto do colaborador (seção `Intranet:Imaging`, `Secco.SDK.Imaging`). O envio também é barrado em ~10 MB na própria rota (limite de requisição) e no texto da tela, então subir este valor acima disso não tem efeito. |
| `Intranet__Imaging__MaxPixels` | Não (padrão 50 milhões) | Máximo de pixels da imagem de entrada; acima disso o envio é recusado. Cada decodificação pode usar ~4 bytes por pixel (50 milhões ≈ 200 MB), multiplicado por `MaxConcurrentOperations`: em host com pouca memória, baixe este valor (ex.: 25 milhões) ou o de `MaxConcurrentOperations`. |
| `Intranet__Imaging__MaxConcurrentOperations` | Não (padrão = número de processadores) | Quantas imagens são processadas ao mesmo tempo. |
| `Intranet__Csp__Modo` | Não (padrão `Enforce`) | Modo da Content-Security-Policy (ADR-0057 da plataforma): `Enforce` bloqueia; `ReportOnly` só relata violações no console do navegador (ou no coletor abaixo), útil para investigar um bloqueio novo sem tirar a tela do ar. Valor desconhecido impede a subida. Os demais cabeçalhos (`nosniff`, `Referrer-Policy`, `Permissions-Policy`, `X-Frame-Options`, HSTS fora de Development) valem nos dois modos. |
| `Intranet__Chat__Provedor` | Não (padrão vazio) | Liga o botão de chat no Organograma, na página da pessoa e na lista do Diretório. Vazio = sem botão. Hoje só existe `Teams`: o botão abre a conversa com a pessoa em outra aba. Valor desconhecido impede a subida. A Intranet não chama o Microsoft, só monta o endereço a partir do e-mail; por isso o e-mail do Diretório precisa ser o mesmo com que a pessoa entra no Teams. |
| `Intranet__Chat__TenantId` | Não | Identificador (GUID) da organização no Microsoft, acrescentado ao link do Teams. Só vale com o provedor `Teams`. Valor que não seja GUID impede a subida. |
| `Intranet__Salas__FusoHorario` | Não (padrão `America/Sao_Paulo`) | Fuso em que os horários das reservas de sala são entendidos: o que a pessoa digita e o que a grade mostra. Id IANA ou do Windows. Id inválido impede a subida. |
| `Intranet__Formatos__Cultura` | Não (padrão `pt-BR`) | Cultura dos campos de data: como as telas de salas, vencimentos e avisos mostram e leem datas (`pt-BR` = dd/mm/aaaa e hora de 24 horas). Os campos são texto nesse padrão, não o seletor do navegador, que segue o idioma do navegador. O formato aaaa-mm-dd é sempre aceito. Cultura inexistente impede a subida. As datas apenas exibidas nas demais telas ainda usam dd/mm/aaaa fixo; elas passam a seguir esta chave com a troca de idioma. |
| `Intranet__Agenda__Provedor` | Não (padrão vazio) | Liga o espelhamento das reservas de sala numa agenda externa. Vazio = desligado, nada sai da Intranet. Hoje só existe `Microsoft`. Valor desconhecido impede a subida. Ver "Espelhar as reservas no Microsoft 365". |
| `Intranet__Agenda__IntervaloEmSegundos` | Não (padrão `30`) | De quanto em quanto tempo o processo em segundo plano envia as pendências à agenda. Mínimo `5`; valor menor impede a subida. |
| `Intranet__Agenda__Microsoft__TenantId` | Sim, com o provedor `Microsoft` | Identificador (GUID) da organização no Microsoft 365. |
| `Intranet__Agenda__Microsoft__ClientId` | Sim, com o provedor `Microsoft` | Identificador (GUID) do aplicativo registrado. |
| `Intranet__Agenda__Microsoft__ClientSecret` | Sim, com o provedor `Microsoft` | Segredo do aplicativo. Só por variável de ambiente ou cofre, nunca em arquivo versionado. |
| `Intranet__Agenda__Microsoft__CaixaOrganizadora` | Sim, com o provedor `Microsoft` | E-mail da caixa de serviço que organiza os eventos. |
| `Intranet__Csp__ReportUri` | Não | Coletor externo, HTTPS, dos relatórios de violação da CSP. A Intranet não hospeda coletor. |
| `Secco__LogStream__*` | Não | Seção do sink de log geral (`ILogger` → LogStream), separada da auditoria acima. Vazia = log só local (stdout do container). Ver `Secco.SDK.Logging` para as chaves da seção. |

Credenciais de `Secco.LogStream`/`Secco.NotificationHub`: os SDKs leem `ClientId` e `ClientSecret` da
seção do próprio serviço (`Secco:LogStream`, `Secco:NotificationHub`), e o que faltar cai em
`Secco:SecureGate` (secco-platform#25). Por isso o client de integração entra nas duas seções: sem ele,
as chamadas sairiam com o client de plataforma, sem tenant.

## Aviso de vencimento (`Intranet:Vencimentos`)

O recurso Vencimentos envia os avisos por um worker em processo, um por tenant, que acorda a cada intervalo, decide quais marcos venceram e manda cada aviso na hora pelo `Secco.NotificationHub`. Sem `Intranet__Notificacao__HubUrl` o worker nem é registrado e não há aviso de vencimento (as telas continuam funcionando).

| Variável | Obrigatória | Descrição |
|---|---|---|
| `Intranet__Vencimentos__Habilitado` | Não (padrão `true`) | `false` desliga o worker. |
| `Intranet__Vencimentos__IntervaloEmMinutos` | Não (padrão `60`) | De quanto em quanto tempo o worker confere os itens. |
| `Intranet__Vencimentos__FusoHorario` | Não (padrão `America/Sao_Paulo`) | Fuso que define "hoje" para os marcos de aviso. Id IANA (ex.: `America/Sao_Paulo`). Id inválido: o worker registra um erro no log e não roda. |

## Espelhar as reservas no Microsoft 365

O espelhamento é opcional e vem desligado. A Intranet grava a reserva primeiro e decide sozinha os conflitos de horário; um processo em segundo plano envia depois cada reserva à agenda do Microsoft 365: cria o evento, atualiza quando a reserva muda e apaga quando é cancelada. O Microsoft manda os convites, as atualizações e os cancelamentos. A via é de mão única: nada que acontece na agenda volta para a Intranet.

Como funciona: os eventos são criados na agenda de uma **caixa de serviço** (por exemplo "Reserva de Salas"), que convida quem reservou, os convidados que têm e-mail no Diretório e a sala (como recurso, quando a sala tem e-mail cadastrado). O convite chega como enviado pela caixa de serviço, com o nome de quem reservou no texto. O aplicativo só precisa de acesso a essa caixa.

**Reservas feitas antes de ligar o espelhamento não são enviadas.** Só as criadas, alteradas ou canceladas depois de ligar entram na fila.

Os comandos abaixo foram conferidos na documentação da Microsoft em 2026-11-25 ([RBAC for Applications no Exchange Online](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac) e [Set-CalendarProcessing](https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/set-calendarprocessing)). A Microsoft muda esses comandos de tempos em tempos: se algum não funcionar, confira as páginas.

### 1. Registrar o aplicativo (Microsoft Entra)

1. No centro de administração do Microsoft Entra, registre um aplicativo (só deste diretório) e anote o **ID do aplicativo (cliente)** e o **ID do diretório (locatário)**.
2. Crie um segredo do cliente e guarde o valor num cofre.
3. **Não conceda** a permissão `Calendars.ReadWrite` no Entra ID. Ela vale para todas as caixas da organização e soma-se à concessão do passo 3 abaixo, desfazendo o escopo. A permissão vem só pelo RBAC do Exchange Online.

### 2. Criar a caixa de serviço

Crie uma caixa de correio compartilhada ou de usuário (por exemplo `reserva-de-salas@suaempresa.com`). Ela é o valor de `Intranet__Agenda__Microsoft__CaixaOrganizadora`. Sem login interativo: só o aplicativo a usa.

### 3. Conceder o papel com escopo só nessa caixa (Exchange Online)

Quem executa precisa estar no grupo de funções Organization Management do Exchange Online. Depois do `Connect-ExchangeOnline`:

```powershell
# IDs da página "Aplicativos empresariais" do Entra (não os de "Registros de aplicativo").
New-ServicePrincipal -AppId <ID do aplicativo> -ObjectId <ID de objeto do principal de serviço> -DisplayName "Intranet - reserva de salas"

# Escopo: só a caixa de serviço.
New-ManagementScope -Name "Caixa de reserva de salas" -RecipientRestrictionFilter "Alias -eq 'reserva-de-salas'"

New-ManagementRoleAssignment -App <ID de objeto do principal de serviço> -Role "Application Calendars.ReadWrite" -CustomResourceScope "Caixa de reserva de salas"

# Confere o que o aplicativo pode fazer na caixa.
Test-ServicePrincipalAuthorization -Identity <ID de objeto do principal de serviço> -Resource reserva-de-salas@suaempresa.com
```

O filtro do escopo segue o formato do exemplo da documentação (`CustomAttribute1 -eq '...'`), aqui com o alias da caixa (a parte do e-mail antes do `@`); a lista de propriedades filtráveis está em [Filterable properties for the -RecipientFilter parameter](https://learn.microsoft.com/en-us/powershell/exchange/recipientfilter-properties). Mudanças de permissão levam de 30 minutos a 2 horas para valer; o `Test-ServicePrincipalAuthorization` não passa por esse atraso.

### 4. Configurar cada sala para aceitar pedidos só da caixa de serviço

Em cada caixa de sala do Exchange, deixe só a caixa de serviço reservar, para que a reserva direta pela agenda deixe de funcionar:

```powershell
Set-CalendarProcessing -Identity "<sala>" -AutomateProcessing AutoAccept -BookInPolicy "reserva-de-salas@suaempresa.com" -AllBookInPolicy $false
```

`AllRequestInPolicy` e `AllRequestOutOfPolicy` já vêm `$false`, então ninguém mais consegue pedir a sala. Depois, no cadastro da sala na Intranet (`/salas/gerenciar`), preencha o **e-mail na agenda** com o endereço da caixa da sala: sem ele, a sala não entra como recurso no convite.

### 5. Ligar na Intranet

Defina `Intranet__Agenda__Provedor=Microsoft` e as quatro chaves `Intranet__Agenda__Microsoft__*` da tabela de variáveis, com o segredo vindo do cofre. Falta ou formato inválido em qualquer uma impede a subida. Reinicie a aplicação.

### 6. Conferir à mão

A chamada real ao Microsoft não faz parte dos testes automáticos. Depois de ligar:

1. Reserve uma sala com um convidado do Diretório. Em até o intervalo configurado, o convite chega ao convidado e à caixa da sala, e o detalhe da reserva mostra "espelhada".
2. Altere o horário na Intranet: o convite é atualizado.
3. Cancele a reserva: o cancelamento chega, e o evento some da agenda da caixa de serviço.
4. Tente reservar a sala direto pela agenda de uma pessoa: o pedido tem de ser recusado.
5. Se uma reserva ficar em "falhou", o detalhe mostra o motivo e o `intranet-admin` tem o botão para tentar de novo. A reserva continua valendo na Intranet.

## `docker-compose` de HML e PRD

Exemplo mínimo, para o **mesmo host da plataforma**: a Intranet entra na rede dela e usa o proxy dela.
Um host tem uma porta 443 só, então não suba um segundo proxy. O banco fica fora do compose,
gerenciado à parte. Os dados ficam em pastas do host, para o backup ser cópia de pasta (ver "Backup e
restauração").

Suba a plataforma antes, pelo guia dela, até os serviços ficarem `healthy` e com os clients da
Intranet aplicados pelo `securegate-migrate` (seção "SecureGate: quatro clients e o `migrate`"). O
`migrate` da Intranet publica o catálogo de permissões no SecureGate e precisa dele no ar.

Prepare o host uma vez. As duas pastas da aplicação precisam pertencer ao uid da imagem:

```bash
export INTRANET_DADOS=/srv/intranet
sudo install -d -o 1654 -g 1654 -m 0700 "$INTRANET_DADOS/documentos" "$INTRANET_DADOS/chaves"
sudo install -d -m 0700 "$INTRANET_DADOS/caddy"   # só com a plataforma em outro host (proxy próprio)
```

Pasta com dono errado aparece no log como erro de permissão, com o caminho. A correção é
`sudo chown -R 1654:1654 <pasta>`.

```yaml
services:
  intranet-web:
    image: intranet-web:${INTRANET_VERSAO}   # a imagem que você construiu, com tag de versão
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
      # Client de integração (de produto, do tenant): LogStream e NotificationHub
      - Secco__LogStream__ClientId=${INTEGRACAO_CLIENT_ID:-}
      - Secco__LogStream__ClientSecret=${INTEGRACAO_CLIENT_SECRET:-}
      - Secco__NotificationHub__ClientId=${INTEGRACAO_CLIENT_ID:-}
      - Secco__NotificationHub__ClientSecret=${INTEGRACAO_CLIENT_SECRET:-}
      - Intranet__Login__ClientId=${INTRANET_LOGIN_CLIENT_ID}
      - Intranet__Login__ClientSecret=${INTRANET_LOGIN_CLIENT_SECRET}
      - Intranet__Documentos__Chave__ChaveAtiva=${INTRANET_DOCUMENTOS_CHAVE}
      - Secco__ReverseProxy__Habilitado=true
      - Secco__Tenancy__Tenants__${TENANT_ID}__ConnectionString=${TENANT_CONNECTION_STRING}
      # Serviços opcionais da plataforma, pelos nomes públicos (dentro da rede, resolvem para o proxy)
      - Intranet__Auditoria__LogStreamUrl=${LOGSTREAM_URL:-}
      - Intranet__Notificacao__HubUrl=${NOTIFICATIONHUB_URL:-}
      - Intranet__Notificacao__UrlBase=${INTRANET_URL:-}
    # Nenhuma porta publicada para o host — só o proxy alcança esta rede.
    networks:
      - secco
    volumes:
      - ${INTRANET_DADOS:-/srv/intranet}/documentos:/var/lib/intranet/documentos
      - ${INTRANET_DADOS:-/srv/intranet}/chaves:/var/lib/intranet/chaves
    healthcheck:
      # A imagem não tem curl/wget: o bash abre a conexão e confere o status 200.
      test: ["CMD", "bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/live HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]
      interval: 30s
      timeout: 5s
      retries: 3

networks:
  secco:
    # A rede do compose da plataforma (SECCO_NETWORK_NAME no .env dela; padrão "secco")
    external: true
    name: secco
```

Com `SECURE_GATE_AUTHORITY=https://login.exemplo.com.br`, `LOGSTREAM_URL=https://logs.exemplo.com.br`,
`NOTIFICATIONHUB_URL=https://notificacoes.exemplo.com.br` e `INTRANET_URL=https://intranet.exemplo.com.br`
no `.env` da Intranet. Os nomes públicos funcionam de dentro da rede porque o proxy da plataforma os
tem como alias.

**O site da Intranet vai no `Caddyfile` da plataforma** (`deploy/Caddyfile`), ao lado dos outros:

```caddyfile
intranet.exemplo.com.br {
    reverse_proxy intranet-web:8080
}
```

Acrescente também o nome nos aliases de rede do serviço `proxy` da plataforma e rode
`docker compose up -d proxy` na pasta dela. O certificado sai do Let's Encrypt, e só exige o DNS
apontando para o host. A seção "Produto adotante no mesmo host" do guia da plataforma descreve os três
pontos do encaixe.

**Plataforma em outro host:** a Intranet tem o próprio proxy e não entra na rede `secco`. Troque
`networks` por uma rede local e acrescente o serviço do proxy:

```yaml
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

Nesse caso, o `Caddyfile` acima fica neste host, e os nomes públicos da plataforma precisam ser
alcançáveis daqui.

O mesmo arquivo serve a HML e PRD, com um `.env` por ambiente ao lado dele. Em HML, use
`ASPNETCORE_ENVIRONMENT=Staging` e uma **chave mestra própria**, nunca a de PRD: quem administra HML
não pode conseguir decifrar os arquivos de produção. Os serviços da plataforma, esses sim, rodam com
`Production` também na HML, como manda o guia dela.

Isto é um ponto de partida deliberadamente enxuto, não um arquivo entregue neste repositório. O
compose da plataforma, ao contrário, é versionado e testado: está em `deploy/` do `secco-platform`.

## Primeiro `intranet-admin`

Já documentado no [`README.md`](../README.md#primeiro-intranet-admin) — crie a Role
`intranet-admin` no tenant e atribua ao primeiro usuário pela API do SecureGate ou pelo AdminPortal
(imagem `secco-adminportal`, no compose da plataforma) antes do primeiro login. O primeiro operador
da instalação, sem o qual ninguém entra no AdminPortal, tem passo próprio no guia da plataforma
("Primeiro operador"). Sem isso, ninguém acessa a Área
administrativa (`/Acesso`, `/Setores`) para conceder o resto.

## Health checks

- `GET /health/live` — o processo está de pé. Use como liveness probe.
- `GET /health/ready` — checagem de prontidão registrada por `AddSeccoHealthChecks()`
  (`Secco.SDK.AspNetCore`). Use como readiness probe antes de rotear tráfego. Não consulta os
  bancos dos tenants: responde `200` mesmo com o banco fora do ar. Para confirmar um deploy ou uma
  restauração, abra também uma página da Intranet.

Nenhum dos dois exige autenticação.

## Atualização (rollout)

Sem orquestrador com rolling update (é o caso de um host único), o corte é direto.

**A plataforma vem antes.** Se a versão nova da Intranet pede clients mais novos, atualize antes as
imagens da plataforma para a versão da tabela de compatibilidade, pelo guia dela (troca da tag,
`migrate` de cada serviço, `up -d`). Depois, a Intranet:

1. Build da imagem nova (tag por versão, não `latest` puro — facilita rollback).
2. `docker compose run --rm intranet-web migrate` com a imagem nova, **antes** do passo
   3 (ver "Banco de dados" acima). Saída diferente de `0` interrompe o deploy.
3. `docker compose up -d intranet-web` — recria só o container da aplicação; o proxy
   e o banco não são afetados.
4. Confirme `GET /health/ready` → `200` antes de considerar o deploy concluído.

Não há mecanismo de rollback automático de migration — se precisar reverter, é
`dotnet ef database update <migration-anterior>` manual, contra a connection string de cada
tenant (`--project src/Secco.Intranet.Migrations.SqlServer` ou `.Postgres`).

## Troca da chave mestra

A chave de cada arquivo é embrulhada pela chave mestra. Trocar a mestra não regrava nenhum arquivo:
o `migrate` reembrulha, para a chave ativa, só a coluna da chave de cada documento, imagem de
destaque, imagem no texto e foto, em todos os tenants. Os arquivos continuam como estão na pasta.

1. Gere a chave nova (base64, 32 bytes). Ponha a atual em
   `Intranet__Documentos__Chave__ChavesAposentadas__0` (as anteriores seguem em `__1`, ...) e a
   nova em `Intranet__Documentos__Chave__ChaveAtiva`.
2. Faça o deploy normal, com o `migrate` antes de subir a aplicação (ver "Atualização (rollout)").
   O `migrate` reembrulha o que encontrar. Ele precisa da mesma configuração de chaves da aplicação
   (`Intranet__Documentos__Chave__ChaveAtiva` e `ChavesAposentadas`): fora de Development, sem a
   chave ativa ele para antes de tocar em qualquer tenant.
3. Durante o deploy, o container antigo ainda grava arquivos com a chave antiga. Depois que todos os
   containers estiverem na configuração nova, rode o `migrate` de novo.
4. Quando um `migrate` terminar com código 0 e sem nenhuma chave reembrulhada em nenhum tenant, a
   aposentada pode sair da configuração.
5. Guarde a chave retirada no cofre enquanto existir backup que dependa dela (ver "Backup e
   restauração").

No log do `migrate`, por tenant, aparecem a tabela, quantas chaves foram reembrulhadas e quantas
ficaram "sem chave conhecida". Tenant sem nada a fazer não gera linha. O log não traz chave,
connection string nem caminho de arquivo.

**Código de saída 1 e "sem chave conhecida":** a chave de um arquivo que nenhuma chave configurada
abre faz o tenant falhar. O `migrate` termina com código 1 e o seed de referência não roda. Costuma
ser uma aposentada retirada cedo demais: recoloque-a em `ChavesAposentadas` e rode de novo. As outras
chaves do tenant são reembrulhadas mesmo assim, e os outros tenants não são afetados.

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
`Intranet__Documentos__Chave__ChavesAposentadas__*`. O `migrate` só reembrulha o banco em uso, não
os backups (ver "Troca da chave mestra").

### Restaurar

Nesta ordem:

1. **Configure as chaves mestras**: `Intranet__Documentos__Chave__ChaveAtiva` e, se o backup for de
   antes de uma rotação, a chave da época em `ChavesAposentadas`. O próximo `migrate` reembrulha as
   chaves dos arquivos para a ativa (ver "Troca da chave mestra").
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
- [ ] Plataforma implantada pelas imagens 0.2.0 ou mais novas, com os quatro serviços `healthy`.
- [ ] Usando LogStream ou NotificationHub: client de integração criado no tenant da Intranet, com o
      perfil `intranet-integracao` e as seis permissões, configurado em `Secco__LogStream__*` e
      `Secco__NotificationHub__*`.
- [ ] Os dois clients de plataforma da Intranet e o `PermissionCatalog__Products` na âncora
      `x-securegate-clients` do compose da plataforma, e o `securegate-migrate` rodado depois.
- [ ] SecureGate com duas credenciais de banco: a de migração (dona) e a de runtime (sem posse).
- [ ] Mesmo host: `intranet-web` na rede `secco`, site da Intranet no `Caddyfile` da plataforma e
      nenhum segundo proxy.
- [ ] Porta do container não publicada para fora do host — só o proxy alcança.
- [ ] Em PRD, porta do banco fechada: só a aplicação e o DBA a alcançam.
- [ ] `Secco:ReverseProxy:Habilitado=true` **só** com o ponto acima garantido.
- [ ] `migrate` da Intranet terminado com `0` antes do primeiro start.
- [ ] Role `intranet-admin` criada e atribuída a alguém antes do primeiro login.
- [ ] `GET /health/ready` → `200` depois de subir, e uma página da Intranet abrindo.
- [ ] Backup configurado e **restauração testada** (ver "Backup e restauração"), com a chave mestra
      guardada à parte.
