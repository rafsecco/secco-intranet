# Secco Intranet

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=flat-square)
![ASP.NET Core MVC](https://img.shields.io/badge/ASP.NET%20Core%20MVC-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![EF Core](https://img.shields.io/badge/EF%20Core-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![SQL Server](https://img.shields.io/badge/SQL%20Server-CC2927?style=flat-square)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?style=flat-square&logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-2496ED?style=flat-square&logo=docker&logoColor=white)

![Bootstrap](https://img.shields.io/badge/Bootstrap-5.3-7952B3?style=flat-square&logo=bootstrap&logoColor=white)
![Sass](https://img.shields.io/badge/Sass-CC6699?style=flat-square&logo=sass&logoColor=white)
![OIDC](https://img.shields.io/badge/OIDC-relying%20party-0B7285?style=flat-square)
![xUnit](https://img.shields.io/badge/xUnit-5E5E5E?style=flat-square)
![Testcontainers](https://img.shields.io/badge/Testcontainers-2496ED?style=flat-square)
![Licença MIT](https://img.shields.io/badge/licen%C3%A7a-MIT-green?style=flat-square)

Produto de intranet corporativa, open source, do ecossistema Secco Platform. Repositório
**standalone** (não faz parte do monorepo `secco-platform`) — consome `Secco.SecureGate`,
`Secco.LogStream` e `Secco.NotificationHub` como pacotes NuGet publicados pelo monorepo,
não por referência de projeto.

Nasce conforme as ADRs da plataforma: camadas com dependências para dentro (ADR-0002),
`AddSeccoPlatform()` (ADR-0004), multi-tenancy database-per-tenant (ADR-0005), nomenclatura
de banco por convention (ADR-0017), dois providers (ADR-0018) e testes (ADR-0012). Gerado a
partir do conteúdo do template `secco-service` do secco-platform, adaptado para repositório
próprio — **sem** os projetos Api e Client do template: pela decisão de monolito (ADR-0002
deste produto), não há Api HTTP separada nem, portanto, contrato OpenAPI/client NSwag.

Decisões arquiteturais específicas deste produto (Setor = Role, monolito, sistema de temas,
armazenamento de documentos) estão documentadas em
[`docs/adr/secco-intranet-adrs.md`](docs/adr/secco-intranet-adrs.md) — consultar antes de
mudanças estruturais. O plano de fases está em [`docs/roadmap.md`](docs/roadmap.md), e o guia
para escrever um tema em [`docs/temas.md`](docs/temas.md).

O que este produto espera da plataforma e ainda não existe — com o link da issue onde cada
lacuna é discutida — está em [`docs/plataforma.md`](docs/plataforma.md). Capacidade transversal
(logging, auditoria, provisionamento de banco) é pedida ao monorepo, nunca reimplementada aqui:
ver ADR-0006 e ADR-0007.

## Tecnologias

| Camada              | O que é usado                                                  | Por quê                                                                |
| ------------------- | -------------------------------------------------------------- | ---------------------------------------------------------------------- |
| Aplicação           | .NET 10, C#, ASP.NET Core MVC                                  | Monolito servindo HTML, sem Api HTTP separada (ADR-0002)               |
| Dados               | EF Core 10, SQL Server (padrão) e PostgreSQL                   | Dois providers com migrations em assemblies próprios (ADR-0018)        |
| Multi-tenancy       | `Secco.SDK.AspNetCore`                                         | Um banco por tenant, resolvido por requisição (ADR-0005 da plataforma) |
| Identidade          | OpenID Connect contra o `Secco.SecureGate`                     | Relying party com cookie de sessão; setor é Role (ADR-0001)            |
| Interface           | Razor Class Library por tema, Bootstrap 5.3 compilado por Sass | Tema é pacote independente; o core não impõe framework CSS (ADR-0003)  |
| Tipografia e ícones | Instrument Sans, Inter, JetBrains Mono, Bootstrap Icons        | Auto-hospedados: a intranet precisa renderizar sem internet            |
| Criptografia        | AES-256-GCM em envelope, `System.Security.Cryptography`        | Documento cifrado em repouso, sem dependência externa (ADR-0005)       |
| Testes              | xUnit, AwesomeAssertions, Testcontainers, `Secco.SDK.Testing`  | Integração contra SQL Server real, sobre o host de verdade (ADR-0012)  |
| Ambiente            | Docker Compose, Node apenas para compilar os assets do tema    | O CSS compilado é versionado: `dotnet run` funciona sem Node           |

## Pré-requisitos

- **.NET SDK 10.0** — confira com `dotnet --version`.
- **Docker** — o SQL Server de desenvolvimento sobe via `docker compose`, e os testes de
  integração criam o próprio container através do Testcontainers.
- **Autenticação no feed de pacotes** (abaixo) — sem ela o `dotnet restore` não passa.
- **Arquivo `.env`** (abaixo) — sem ele o `docker compose up` falha, de propósito.

### Variáveis de ambiente (`.env`)

Nenhum segredo é versionado. As credenciais de desenvolvimento — senha do `sa` e a
connection string do tenant de DEV — ficam num `.env` na raiz, ignorado pelo git. O
inventário das variáveis está em [`.env.example`](.env.example), o único versionado:

```bash
cp .env.example .env
# abra o .env e troque MSSQL_SA_PASSWORD — e a mesma senha dentro da connection string
```

Quem lê esse arquivo:

- **`docker compose`** lê o `.env` da raiz sozinho, para interpolar o
  `docker-compose.yml`. Sem `MSSQL_SA_PASSWORD` definida o `up` falha com mensagem
  explícita, em vez de subir um banco com senha que está no repositório.
- **VS Code (F5)** lê via `"envFile"` no `.vscode/launch.json`. É assim que a variável
  `Secco__Tenancy__Tenants__<id>__ConnectionString` chega na configuração da aplicação —
  o provider de variáveis de ambiente do ASP.NET Core troca `__` por `:`.

Para `dotnet run` direto no terminal, exporte as variáveis à mão antes. E note que
`set -a; . ./.env` **não** serve para a linha da connection string: o nome carrega os
hífens do GUID do tenant, que não formam nome de variável válido no shell.

### Autenticar no feed de pacotes

Os pacotes `Secco.*` são **públicos**, mas o registry NuGet do GitHub Packages **não serve
leitura anônima** — é limitação dele, não uma restrição deste projeto. Sem token, o
`dotnet restore` falha com `401 Unauthorized` e nenhum `Secco.*` resolve. Qualquer conta
GitHub serve: **não** é preciso ter acesso ao repositório `rafsecco/secco-platform`.

Basta um Personal Access Token com o escopo `read:packages`:

```bash
gh auth login --scopes read:packages   # ou, se já estiver logado: gh auth refresh -s read:packages

dotnet nuget add source "https://nuget.pkg.github.com/rafsecco/index.json" \
  --name secco --username SEU_USUARIO_GITHUB --password "$(gh auth token)"
```

A credencial vai para o `NuGet.Config` de **usuário** (`%APPDATA%\NuGet\NuGet.Config` no
Windows, `~/.nuget/NuGet/NuGet.Config` no Linux/macOS), que é o padrão do comando acima —
nunca para o `nuget.config` deste repositório, que é versionado e vazaria o token num
commit. Funciona porque o NuGet casa `packageSourceCredentials` pelo **nome** da fonte ao
mesclar os configs da hierarquia, então o `<clear />` do `nuget.config` do repo limpa só as
fontes herdadas, não as credenciais.

No Linux/macOS acrescente `--store-password-in-clear-text`: a criptografia do NuGet só
existe no Windows. Em CI, o `GITHUB_TOKEN` do próprio workflow já basta.

### Ambiente no VS Code

O repositório versiona `.vscode/` (settings, tasks, launch e extensões recomendadas) para
que o ambiente funcione igual em qualquer máquina, sem depender de um perfil pessoal do VS
Code. Ao abrir a pasta, aceite a notificação de extensões recomendadas
(`.vscode/extensions.json`) — inclui C# Dev Kit, editorconfig, mssql, pgsql, spell checker
(inglês + português) e afins.

`F5` roda o perfil **"Web: https (Development)"**, que lê porta e
`ASPNETCORE_ENVIRONMENT` do `launchSettings.json` (fonte única, para o F5 e o `dotnet run`
baterem) e carrega o `.env` da raiz via `"envFile"` — é de lá que vem a connection string
do tenant. Antes da primeira vez: `cp .env.example .env`, `dotnet dev-certs https --trust`
(uma vez só) e `docker compose up -d` (o `Program.cs` aplica migrations e faz seed dos
tenants em `Development` no start). `Ctrl+Shift+B` roda a task `build`, que builda a solução inteira; a
view **Testing** do C# Dev Kit descobre os testes automaticamente (sem precisar de task
separada). Para gerar migration, use a task `ef: nova migration (ambos os engines)` — ela
encadeia os dois providers em sequência, porque a ADR-0018 exige o par.

Quatro extensões estão em `unwantedRecommendations` de propósito:
`formulahendry.dotnet-test-explorer` duplica o Test Explorer do C# Dev Kit (dois
descobridores sobre os mesmos testes); `jmrog.vscode-nuget-package-manager` e
`aliasadidev.nugetpackagemanagergui` escrevem `Version=` direto no `.csproj`, o que quebra o
Central Package Management do `Directory.Packages.props`; `adrianwilczynski.namespace` é
redundante com o C# Dev Kit, que já gera namespace file-scoped conforme o `.editorconfig`.

### Rodar

```bash
cp .env.example .env   # uma vez por máquina — ver acima
docker compose up -d   # SQL Server de desenvolvimento na porta 1433
dotnet restore
dotnet build
dotnet test            # os testes de integração sobem container próprio (Testcontainers)
```

## Arquitetura

- **Monolito**: `Secco.Intranet.Web` (MVC) consome a Application layer diretamente —
  sem camada Api HTTP própria por enquanto. Controllers ficam finos (só orquestram o
  Mediator/handlers) e Views recebem DTO, nunca entidade — mantém aberta a porta para
  extrair uma Api de verdade no futuro (ex: se um front em Angular/React/Blazor WASM
  fizer sentido) sem reescrever regra de negócio.
- **Setor = Role tenant-scoped no SecureGate**: cada setor cria as Roles `{slug}-admin`
  e `{slug}-user`; usuário com acesso a múltiplos setores recebe múltiplas Roles.
- **Temas via Razor Class Library**: cada tema (`Secco.Intranet.Themes.*`) é um pacote
  independente; o core não impõe framework CSS — o tema padrão usa Bootstrap.

## Autenticação (SecureGate)

A Intranet é uma relying party OIDC do `Secco.SecureGate` (ADR-0023 da plataforma): cookie
de sessão + authorization code/PKCE, sem custódia de access token (a Intranet não chama
nenhuma API on-behalf-of do usuário hoje). Composição lazy por configuração
(`Secco.Intranet.Web.Authentication.IntranetAuthenticationExtensions`):

- **Chaves** (seção `Secco:SecureGate`): `Authority`, `ClientId`, `ClientSecret`.
- **Redirect URI**: `/signin-oidc` (padrão do middleware OpenIdConnect).
- **Scopes** do login interativo: `openid profile roles` — sem `securegate:admin` (esse
  scope é exclusivo do provisionamento automático de Roles via client credentials, ver
  abaixo) e sem `offline_access`/custódia de token.
- **Modo aberto**: sem `Secco:SecureGate:Authority` configurada, nenhum middleware de
  autenticação/autorização é registrado — modo válido apenas em DEV local (sem SecureGate
  disponível) e no ambiente `Testing`. ADR-0020: produção exige a seção; o modo aberto não
  deve chegar lá.

Separadamente, o cadastro de um setor (ADR-0001) provisiona automaticamente as Roles
`{slug}-admin`/`{slug}-user` no tenant atual via `Secco.SecureGate.Client`, autenticado por
client credentials com o scope administrativo (`securegate:admin`, via
`AddSecureGateAdminClient()`) — não confundir com os scopes do login interativo acima. Sem
a seção `Secco:SecureGate` configurada, esse provisionamento cai num adapter no-op
(`NullSetorAccessProvisioner`), o mesmo modo aberto de DEV/Testing.

## Pós-geração (checklist)

1. [feito] **Gerar as migrations iniciais** (uma por engine, ADR-0018) — `Initial` existe
   nos dois projetos de migration:
    ```bash
    dotnet ef migrations add Initial --project src/Secco.Intranet.Migrations.SqlServer --output-dir Migrations
    dotnet ef migrations add Initial --project src/Secco.Intranet.Migrations.Postgres --output-dir Migrations
    ```
2. [feito] **Montar a solution** — `Secco.Intranet.slnx`, no formato XML novo em vez do
   `.sln` clássico.
3. [feito] **Apontar o `nuget.config`** para o feed onde `Secco.SecureGate.Client`,
   `Secco.LogStream.Client`, `Secco.NotificationHub.Client` e `Secco.SharedKernel` são
   publicados pelo secco-platform — fonte `secco`, com `packageSourceMapping` prendendo o
   padrão `Secco.*` só a ela. Consumir exige token: ver [Pré-requisitos](#pré-requisitos).
4. **CI**: workflow próprio deste repositório (não compartilha o `ci.yml` do monorepo).
   **Pendente** — ainda não existe `.github/workflows/`.
5. [feito] **Projeto `Secco.Intranet.Web`** (MVC) — não vem do template original, é
   específico deste produto (ADR-0002). Os testes de integração (`tests/.../Integration`)
   foram reativados sobre o host real via `WebApplicationFactory<Program>`.

## O recurso Setor

Primeiro recurso real do domínio, substituindo o Sample de exemplo do template — é a base
organizacional da Intranet (documentos, processos e outros recursos futuros são habilitados
por setor).

- Entidade `BaseEntity` (Guid v7) com guarda de invariante e colunas por convention
  (`tb_setores`, `ds_nome`, `ds_slug`, `fl_fixo`, `fl_ativo`...).
- Handler com `Result<T>` (ADR-0004) e limites de entrada (ADR-0020) via options com bind lazy.
- Paginação com `PagedResult<T>`; slug único por tenant, validado antes de persistir.
- Testes unitários (fake da porta) e de integração com SQL Server real (Testcontainers)
  sobre o host `Secco.Intranet.Web` (ADR-0002).

### Próximos recursos do domínio (ainda não implementados)

- `Recurso` + `SetorRecurso`: catálogo de módulos habilitáveis por setor (documentos,
  processos, inventário...) com visibilidade público/privado.
- `ItemMenu`: tabela autorecursiva para o menu extensível por quem adotar o projeto.
- `ItemVencimento` + `AvisoAntecedencia`: avisos de vencimento (certificados, licenças...).
- `AvisoUsuario`: central de notificação in-app (toast + área de avisos), consumindo o
  canal in-app do `Secco.NotificationHub`.
- Motor de processos (Fase 2): `ProcessoDefinicao` → `Etapa[]` → `ProcessoInstancia`.

## Contribuindo

O roteiro está em [`CONTRIBUTING.md`](CONTRIBUTING.md): o que não se negocia, como uma
capacidade de plataforma é pedida em vez de reimplementada, e o que rodar antes de dizer que
terminou.

Vulnerabilidade **não** vai em issue pública — o canal privado e o escopo do que interessa
estão em [`SECURITY.md`](SECURITY.md).

## Licença

[MIT](LICENSE). Copyright (c) 2026 Rafael Secco.
