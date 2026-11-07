# Docker e dados da instalação — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `docker compose up` sobe a Intranet em DEV com os dados em `.dados/`, e o guia de deploy traz o compose de HML/PRD em bind mounts e o backup e restauração.

**Architecture:** O compose ganha a aplicação e guarda tudo em `.dados/`. A Web persiste as chaves do DataProtection em pasta e resolve caminhos relativos a partir do content root, para `dotnet run`, F5 e container usarem as mesmas pastas. HML/PRD ficam como modelo no guia, sem arquivo versionado.

**Tech Stack:** Docker Compose 2.24+, BuildKit, ASP.NET Core 10 DataProtection, xUnit, AwesomeAssertions.

**Spec:** [docs/specs/2026-11-06-docker-e-dados-design.md](../specs/2026-11-06-docker-e-dados-design.md)

## Global Constraints

- Branch `feat/docker-e-dados`; commits nela; push só quando o usuário pedir. Todo commit termina com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- `.env` real **nunca** é editado pelo implementador. Antes da Task 4, o usuário faz duas mudanças nele: acrescenta `NUGET_PAT` e troca `secco_intranet_alfa`/`secco_intranet_alfa_app` por `intranet_alfa`/`intranet_alfa_app` na connection string.
- Formato de `.env.example`: `VAR=valor`, aspas simples em segredo, vazio como `VAR=`, comentário em linha própria.
- Nomes exatos: serviço e imagem `intranet-web`; `ServiceName` `intranet`; `SetApplicationName("intranet")`; `NUGET_USER` `intranet`; banco e login `intranet_alfa` e `intranet_alfa_app`; imagem em `/var/lib/intranet/{documentos,chaves}`; host em `${INTRANET_DADOS:-/srv/intranet}`; DEV em `.dados/{documentos,chaves,sql/{data,log,secrets}}`.
- Chave de configuração das chaves: `Intranet:ProtecaoDeDados:PastaDasChaves`.
- Nada versionado cita fork ou empresa adotante pelo nome.
- C#: tabs; YAML/JSON/scripts: espaços conforme `.editorconfig`.
- Limpar containers e pastas de teste criados fora de `.dados/`. O volume `secco-intranet_mssql-data` do usuário não é apagado.

## Review Focus

1. `.dados/sql` copiada com um `.env` de outro `MSSQL_SA_PASSWORD`: o `sqlserver-init` termina com erro em cerca de 60s, sem travar (Task 4).
2. `MSSQL_APP_PASSWORD` com `#`, `$` ou espaço: a connection string chega literal à aplicação (Task 4).
3. `NUGET_PAT` vazio: o build falha de forma compreensível, e `docker compose up -d sqlserver-init` continua subindo só o SQL (Task 4).
4. Segundo `docker compose up` com `.dados/` já cheia, dentro da janela do erro 904: a aplicação só inicia com o banco legível (Task 4).
5. `dotnet run` a partir da raiz, de `scripts/` e de `src/Secco.Intranet.Web`: a mesma `.dados/` na raiz (Task 5).

---

### Task 1: Caminhos relativos e chaves do DataProtection na Web

**Files:**
- Create: `src/Secco.Intranet.Web/Hosting/IntranetDadosExtensions.cs`
- Modify: `src/Secco.Intranet.Web/Program.cs:74-78`, `src/Secco.Intranet.Web/appsettings.Development.json`, `.gitignore`, `.dockerignore`
- Test: `tests/Secco.Intranet.Tests/Unit/CaminhosDeDadosTests.cs`, `tests/Secco.Intranet.Tests/Unit/ProtecaoDeDadosTests.cs`

**Interfaces:**
- Produces, em `Secco.Intranet.Web.Hosting.IntranetDadosExtensions`:
  - `public const string ChavePastaDasChaves = "Intranet:ProtecaoDeDados:PastaDasChaves";`
  - `public static void ResolverCaminhosDeDados(this ConfigurationManager configuration, string contentRootPath)`
  - `public static IServiceCollection AddIntranetProtecaoDeDados(this IServiceCollection services, IConfiguration configuration)`

- [ ] **Step 1: Escrever os testes que falham**

`CaminhosDeDadosTests`, com `contentRoot = Path.Combine(Path.GetTempPath(), "app", "src", "Web")`, para as duas chaves (`Intranet:Documentos:Armazenamento:Raiz` e `ChavePastaDasChaves`):

- `CaminhoRelativo_ViraAbsolutoAPartirDoContentRoot`: `"../../.dados/documentos"` resulta em `Path.GetFullPath(Path.Combine(contentRoot, "../../.dados/documentos"))`.
- `CaminhoAbsoluto_FicaIntacto`: um caminho de `Path.GetTempPath()` não muda.
- `ChaveAusente_FicaAusente`: `configuration[chave]` continua `null`.
- `ChaveVazia_FicaVazia`: `configuration[chave]` continua vazio.

`ProtecaoDeDadosTests`, com pasta temporária por teste e `IDisposable` para apagar:

- `ComPasta_GravaAChaveNela`: depois de `CreateProtector("t").Protect("x")`, `Directory.GetFiles(pasta, "key-*.xml")` não é vazio.
- `AChaveNaoFicaPresaAMaquina`: o XML gravado não contém `encryptedSecret`, para a pasta poder trocar de máquina.
- `ContentRootsDiferentes_DeciframOQueOOutroCifrou`: dois `ServiceProvider`, cada um com `IHostEnvironment` de `ContentRootPath` diferente e a mesma pasta; o segundo decifra o que o primeiro cifrou.
- `SemPasta_ProtegeEDecifra`: sem a chave, ida e volta funciona.

- [ ] **Step 2: Rodar e confirmar que falham**

Run: `dotnet test --filter "FullyQualifiedName~CaminhosDeDadosTests|FullyQualifiedName~ProtecaoDeDadosTests"`
Expected: erro de compilação, `IntranetDadosExtensions` não existe.

- [ ] **Step 3: Implementar `IntranetDadosExtensions`**

`ResolverCaminhosDeDados`: para cada chave com valor não vazio e `!Path.IsPathRooted`, grava o absoluto com `configuration.AddInMemoryCollection`. `AddIntranetProtecaoDeDados`: `AddDataProtection().SetApplicationName("intranet")`, mais `PersistKeysToFileSystem(new DirectoryInfo(pasta))` quando a chave estiver preenchida. O XML doc diz por que é arquivo, não banco: a Intranet só tem bancos por tenant.

- [ ] **Step 4: Ligar no host**

No `Program.cs`, antes de `AddIntranetInfrastructure`: `builder.Configuration.ResolverCaminhosDeDados(builder.Environment.ContentRootPath);` e `builder.Services.AddIntranetProtecaoDeDados(builder.Configuration);`.

Em `appsettings.Development.json`, dentro de `Intranet`: `"Documentos": { "Armazenamento": { "Raiz": "../../.dados/documentos" } }` e `"ProtecaoDeDados": { "PastaDasChaves": "../../.dados/chaves" }`.

Acrescentar `.dados/` ao `.gitignore`, na seção de banco local, e ao `.dockerignore`.

- [ ] **Step 5: Rodar os testes**

Run: `dotnet test --filter "FullyQualifiedName~CaminhosDeDadosTests|FullyQualifiedName~ProtecaoDeDadosTests"`
Expected: PASS.

Run: `git check-ignore .dados/; echo $?`
Expected: `.dados/` e `0`.

- [ ] **Step 6: Commit**

```bash
git add src/Secco.Intranet.Web tests/Secco.Intranet.Tests/Unit .gitignore .dockerignore
git commit -m "feat(hosting): chaves do DataProtection em pasta e dados de DEV fora de bin"
```

---

### Task 2: Nomes neutros no código e no banco de DEV

**Files:**
- Modify: `src/Secco.Intranet.Web/Program.cs:63`, `docker/init/provision-tenant-db.sql`, `.env.example:21-24,41-44`, `.vscode/settings.json:105,110`, `docs/roteiro-fumaca-securegate.md`, `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs:136-137`
- Test: `tests/Secco.Intranet.Tests/Integration/EnvioDeLogTests.cs:63-64`

- [ ] **Step 1: Mudar o teste**

Em `OProdutoSeIdentificaNoFluxo`, esperar `"intranet"`.

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test --filter "FullyQualifiedName~EnvioDeLogTests.OProdutoSeIdentificaNoFluxo"`
Expected: FAIL, esperado `intranet`, obtido `secco-intranet`.

- [ ] **Step 3: Renomear**

`ServiceName` para `intranet`. `secco_intranet_alfa` para `intranet_alfa`, `secco_intranet_alfa_app` para `intranet_alfa_app`, e nos testes e no roteiro `secco_intranet_<nome>` para `intranet_<nome>`. No `.env.example`, tirar o comentário que justifica o nome pelo padrão do template. `docs/roadmap.md` não muda: é histórico.

- [ ] **Step 4: Rodar a suíte inteira**

Run: `dotnet test Secco.Intranet.slnx`
Expected: PASS. Os testes de integração criam os bancos com o nome novo.

Run: `git grep -n "secco_intranet" -- ":!docs/specs" ":!docs/plans" ":!docs/roadmap.md"`
Expected: nenhuma linha.

- [ ] **Step 5: Commit**

```bash
git add -A src docker tests docs/roteiro-fumaca-securegate.md .env.example .vscode/settings.json
git commit -m "refactor: nomes de servico de log e banco de DEV sem o nome do produto"
```

---

### Task 3: Imagem com caminhos neutros e tema de fork

**Files:**
- Modify: `src/Secco.Intranet.Web/Dockerfile`, `docs/temas.md`, `docs/extensao.md`

**Interfaces:**
- Produces: imagem com `Intranet__Documentos__Armazenamento__Raiz=/var/lib/intranet/documentos` e `Intranet__ProtecaoDeDados__PastaDasChaves=/var/lib/intranet/chaves`, as duas pastas com dono `$APP_UID` e declaradas como `VOLUME`.

- [ ] **Step 1: Alterar o `Dockerfile`**

Trocar a lista de `COPY` de `.csproj` por `COPY --parents src/*/*.csproj ./`, e a lista de `COPY` de fontes por `COPY src/ src/`. `COPY --parents` funciona com o frontend `docker/dockerfile:1`, testado. Usar os caminhos neutros, `ARG NUGET_USER=intranet` e `-t intranet-web` no comentário de uso.

- [ ] **Step 2: Construir e inspecionar**

Run: `NUGET_PAT=$(gh auth token) docker build -f src/Secco.Intranet.Web/Dockerfile -t intranet-web --secret id=nuget_pat,env=NUGET_PAT .`
Expected: build conclui.

Run: `docker run --rm --entrypoint sh intranet-web -c 'ls -ld /var/lib/intranet/*; id -u; printenv | grep Intranet__'`
Expected: duas pastas com dono `1654`, uid `1654`, e as duas variáveis.

- [ ] **Step 3: Provar com um tema de fork de mentira**

Criar `src/Secco.Intranet.Themes.Teste/Secco.Intranet.Themes.Teste.csproj` (SDK `Microsoft.NET.Sdk.Razor`, `net10.0`, sem pacotes), com `ProjectReference` temporária na Web. Reconstruir sem mexer no `Dockerfile`.

Run: `docker run --rm --entrypoint sh intranet-web -c 'ls /app | grep Themes.Teste'`
Expected: `Secco.Intranet.Themes.Teste.dll`.

Desfazer: `git checkout -- src/Secco.Intranet.Web/Secco.Intranet.Web.csproj`, apagar a pasta, reconstruir.

- [ ] **Step 4: Documentar**

Em `docs/temas.md` e `docs/extensao.md`, uma frase: projeto sob `src/` entra na imagem sem editar o `Dockerfile`; projeto fora de `src/` precisa de `COPY` próprio.

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Web/Dockerfile docs/temas.md docs/extensao.md
git commit -m "feat(imagem): caminhos neutros, pasta das chaves e projetos de fork no build"
```

---

### Task 4: Compose de DEV com a aplicação e dados em `.dados/`

**Files:**
- Modify: `docker-compose.yml`, `.env.example`

**Interfaces:**
- Consumes: imagem da Task 3; `intranet_alfa` da Task 2.
- Produces: serviços `dados-init`, `sqlserver`, `sqlserver-init`, `intranet-web`; o comando `docker compose up -d sqlserver-init`, usado pela Task 5.

- [ ] **Step 1: Confirmar o `.env` do usuário**

Pedir ao usuário as duas mudanças de "Global Constraints" e aguardar.

- [ ] **Step 2: Escrever o `docker-compose.yml`**

**`dados-init`:** `busybox` com versão fixa, `restart: "no"`, mount `./.dados:/dados`. O comando, sem arquivo de script para não depender de fim de linha:

```sh
mkdir -p /dados/documentos /dados/chaves /dados/sql/data /dados/sql/log /dados/sql/secrets
chown -R 10001:0 /dados/sql || true
chown -R ${INTRANET_UID:-1654} /dados/documentos /dados/chaves || true
```

**`sqlserver`:** sai o volume `mssql-data` e a seção `volumes:`; entram `./.dados/sql/data`, `log` e `secrets` em `/var/opt/mssql/{data,log,secrets}`, com um comentário citando que o diretório inteiro derruba o SQL no Docker do Windows. `depends_on: dados-init` com `service_completed_successfully`. O comentário sobre começar do zero passa a ser "apague `.dados/sql`".

**`sqlserver-init`:** tentativas limitadas. `sqlcmd -b` nos dois comandos, e termina com erro se esgotar:

```sh
for tentativa in $$(seq 1 30); do
  sqlcmd ... -b -v AppPassword="$$MSSQL_APP_PASSWORD" -i /init/provision-tenant-db.sql \
    && sqlcmd ... -b -d intranet_alfa -Q "SELECT TOP 1 1 FROM sys.objects" \
    && exit 0
  sleep 2
done
exit 1
```

**`intranet-web`:**
- `build` com `context: .`, `dockerfile: src/Secco.Intranet.Web/Dockerfile`, `secrets: [nuget_pat]`; raiz `secrets.nuget_pat.environment: NUGET_PAT`.
- `user: "${INTRANET_UID:-1654}"`, `env_file: [.env]`, `ports: "${INTRANET_PORT:-8080}:8080"`.
- `environment`: `ASPNETCORE_ENVIRONMENT: Development`; `MSSQL_SA_PASSWORD`, `MSSQL_APP_PASSWORD` e `NUGET_PAT` com `""`; `Secco__Tenancy__Tenants__018f0000-0000-7000-8000-000000000001__ConnectionString` com `Server=sqlserver,1433;Database=intranet_alfa;User Id=intranet_alfa_app;Password=${MSSQL_APP_PASSWORD};TrustServerCertificate=true`.
- Mounts `./.dados/documentos:/var/lib/intranet/documentos` e `./.dados/chaves:/var/lib/intranet/chaves`.
- `depends_on`: `dados-init` e `sqlserver-init`, ambos `service_completed_successfully`.
- Healthcheck igual ao do guia, em `/health/live`.

O cabeçalho do arquivo troca o aviso de "será adicionado" pelos dois usos, tudo ou só o SQL.

- [ ] **Step 3: Atualizar `.env.example`**

Acrescentar `NUGET_PAT=` (PAT com `read:packages`), `INTRANET_PORT=8080` e, comentado, `INTRANET_UID` com a explicação de Linux. Atualizar "Quem lê este arquivo": o `.env` agora também chega ao container.

- [ ] **Step 4: Subir do zero**

Run: `docker compose up -d --build --wait intranet-web`, depois `curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/health/ready`
Expected: `200`.

Run: `docker compose exec intranet-web sh -c 'printenv MSSQL_SA_PASSWORD MSSQL_APP_PASSWORD NUGET_PAT | grep -c .'`
Expected: `0`.

Run: `git status --short`
Expected: nada de `.dados/`.

- [ ] **Step 5: Provar a persistência**

Pela tela: publicar um documento e deixar um formulário aberto. `docker compose down`, `docker compose up -d --wait intranet-web`. O documento abre, e o formulário aberto é aceito ao enviar.

- [ ] **Step 6: Provar a troca de máquina**

`docker compose down`, copiar a raiz para uma pasta temporária (`.dados/`, `.env`, `docker-compose.yml`, `docker/`, `src/` e os arquivos de build da raiz), subir de lá com outro `COMPOSE_PROJECT_NAME`. O documento abre. Derrubar e apagar a cópia.

- [ ] **Step 7: Review Focus 1, senha do `sa` diferente**

Run: `MSSQL_SA_PASSWORD='Outra#Senha2026' docker compose up sqlserver-init`, com `.dados/sql` já existente.
Expected: código de saída diferente de 0 em cerca de 60s, sem travar.

- [ ] **Step 8: Review Focus 2, senha com símbolos**

Em projeto de rascunho com `.env` contendo `MSSQL_APP_PASSWORD='a#b$c d'`:

Run: `docker compose config | grep ConnectionString`
Expected: `Password=a#b$$c d`, a forma escapada de `$` que o `config` imprime.

- [ ] **Step 9: Review Focus 3, sem PAT**

Em projeto de rascunho, com Dockerfile mínimo que lê `/run/secrets/nuget_pat` e falha se estiver vazio: confirmar que `NUGET_PAT` definido só no `.env` chega ao build. Depois, com o repositório real:

Run: `NUGET_PAT= docker compose build intranet-web`
Expected: falha com mensagem do restore ou do secret. Anotar o texto e colocá-lo no comentário de `NUGET_PAT` no `.env.example`.

Run: `NUGET_PAT= docker compose up -d sqlserver-init`
Expected: sobe só `dados-init`, `sqlserver` e `sqlserver-init`, sem build.

- [ ] **Step 10: Review Focus 4, segundo `up`**

Run: `docker compose down && docker compose up -d --wait intranet-web`, com `.dados/` cheia. Repetir duas vezes.
Expected: `sqlserver-init` termina com `0`, e `docker compose logs intranet-web` não mostra erro de migration nem Msg 904.

- [ ] **Step 11: Verificar em Linux**

Run: `wsl -l -q`. Havendo distro com Docker, repetir os Steps 4 e 5 de dentro dela, em pasta do sistema de arquivos Linux, e conferir o dono das pastas com `ls -ln .dados`. Não havendo, registrar na entrega que o item segue sem verificação em Linux.

- [ ] **Step 12: Commit**

```bash
git add docker-compose.yml .env.example
git commit -m "feat(dev): docker compose sobe a aplicacao com os dados em .dados"
```

---

### Task 5: Instância existente, scripts e documentação de DEV

**Files:**
- Create: `docker-compose.sql-existente.yml`
- Modify: `scripts/dev.ps1`, `scripts/dev.sh`, `.vscode/tasks.json`, `.env.example`, `README.md:57-168`

**Interfaces:**
- Consumes: serviços da Task 4.

- [ ] **Step 1: Escrever o override**

`sqlserver` e `sqlserver-init` com `profiles: [sql-container]`. `intranet-web` com `depends_on: !override` só em `dados-init`, a connection string removida com `!reset null` e `extra_hosts: ["host.docker.internal:host-gateway"]`.

- [ ] **Step 2: Verificar o override**

Em `.env` de rascunho com `COMPOSE_FILE=docker-compose.yml:docker-compose.sql-existente.yml` e `COMPOSE_PATH_SEPARATOR=:`:

Run: `docker compose config --services`
Expected: `dados-init` e `intranet-web`.

Run: `docker compose config | grep ConnectionString`
Expected: o valor do `.env`, sem `sqlserver`.

- [ ] **Step 3: Alterar os scripts**

`dev.ps1`: sai o `[ValidateSet]`; a ajuda de `.PARAMETER Tema` passa a dizer "qualquer tema registrado". `dev.sh`: muda o comentário de uso. Nos dois: `docker compose --project-directory ... up -d sqlserver-init`, e pular o Docker quando o `COMPOSE_FILE` lido do `.env` contiver `sql-existente`, com uma linha de aviso.

- [ ] **Step 4: Verificar os scripts**

Run: `./scripts/dev.ps1 -Tema Qualquer`, com o compose derrubado.
Expected: o script aceita o nome, sobem só os serviços de SQL, sem build, e a aplicação escuta em 5250. Parar o processo.

Run: `bash scripts/dev.sh --tema Qualquer`
Expected: mesmo resultado. Parar o processo.

- [ ] **Step 5: Review Focus 5, diretório de trabalho**

Com um documento publicado pelo Step 4, rodar a aplicação com `dotnet run --project` a partir da raiz, de `scripts/` e, sem `--project`, de `src/Secco.Intranet.Web`.
Expected: nos três, o documento abre, e nenhuma pasta `.dados` nova aparece fora da raiz (`git status --short --ignored | grep .dados` mostra uma só).

Depois: `dotnet clean Secco.Intranet.slnx`, subir de novo, e o documento abre.

- [ ] **Step 6: Task do VS Code, `.env.example` e README**

- `tasks.json`: nova task `docker: SQL de DEV` com `up -d sqlserver-init`; a `docker: compose up` continua subindo tudo.
- `.env.example`: bloco comentado com as duas linhas de `COMPOSE_FILE`, e as três notas da decisão 3 da spec (Kerberos, `host.docker.internal`, senhas de SQL obrigatórias).
- `README.md`: pré-requisito `NUGET_PAT`; as duas formas de rodar (tudo em container ou só o SQL com F5/`dotnet run`); seção curta de instância existente; seção "Trocar de máquina": `docker compose down`, copiar `.dados/` e `.env`, `docker compose up`. Corrigir os trechos que citam `docker compose up -d` como só SQL.

- [ ] **Step 7: Commit**

```bash
git add docker-compose.sql-existente.yml scripts .vscode/tasks.json .env.example README.md
git commit -m "feat(dev): instancia de SQL existente, scripts aceitam tema de fork"
```

---

### Task 6: Guia de deploy, backup e ADR

**Files:**
- Modify: `docs/guia-de-deploy.md`, `docs/adr/secco-intranet-adrs.md`, `docs/roadmap.md`

- [ ] **Step 1: Atualizar o guia, exceto backup**

- `secco-intranet-web` vira `intranet-web` em todo o guia, inclusive topologia, `Caddyfile` e comandos de deploy.
- Exemplos de client viram `intranet-login`, `intranet` e `intranet-gestao`.
- "Construir a imagem": caminhos neutros, as duas pastas, o preparo do host, a nota de que projeto sob `src/` entra sozinho, e o aviso de log `No XML encryptor configured` como esperado.
- Tabela de variáveis: `Intranet__Documentos__Armazenamento__Raiz` com o caminho novo; linha nova para `Intranet__ProtecaoDeDados__PastaDasChaves`.
- Compose de HML/PRD conforme a decisão 10 da spec: sintaxe de lista na connection string; bind mounts em `${INTRANET_DADOS:-/srv/intranet}`; sem `volumes:` nomeados; `install -d -o 1654 -g 1654 -m 0700`; sem a menção ao serviço `sqlserver`; parágrafo sobre um `.env` por ambiente, `Staging` e chave mestra própria em HML.
- Limitações conhecidas: várias réplicas exigem pasta de chaves compartilhada.
- Uma linha de diagnóstico: pasta com dono errado aparece como erro de permissão no log, com o caminho; a correção é o `chown` para `1654`.
- Checklist: bind mounts em vez de volume, e a pasta de chaves.

- [ ] **Step 2: Escrever "Backup e restauração"**

Nova seção antes de "Checklist", com o conteúdo inteiro da decisão 11 da spec: a tabela das quatro peças, a ordem do backup e por quê, os comandos de `BACKUP DATABASE` e `pg_dump`, a cópia de pasta, a rotação, a ordem da restauração, `ALTER USER ... WITH LOGIN`, restaurar um tenant só e o teste de restauração. O checklist ganha "restauração testada".

- [ ] **Step 3: Verificar o compose do guia**

Extrair o YAML do guia para o scratchpad, com um `.env` de exemplo (`TENANT_ID` com um GUID, `INTRANET_DADOS=/srv/teste`).

Run: `docker compose config | grep -E "ConnectionString|/srv/teste"`
Expected: variável com o GUID interpolado, e os três mounts em `/srv/teste`.

- [ ] **Step 4: ADR e roadmap**

ADR-0014, curta, no formato do arquivo: chaves do DataProtection em pasta, e não no banco, contra a regra da ADR-0035 da plataforma, porque a Intranet só tem bancos por tenant; consequências: backup opcional e pasta tratada como segredo. Marcar a entrega no `docs/roadmap.md`, na fase de operação, com link para o guia.

- [ ] **Step 5: Verificação final**

Run: `git grep -nE "secco-intranet-web|/var/lib/secco|/srv/secco|secco_intranet" -- ":!docs/specs" ":!docs/plans" ":!docs/roadmap.md"`
Expected: nenhuma linha.

Run: `dotnet test Secco.Intranet.slnx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add docs/guia-de-deploy.md docs/adr/secco-intranet-adrs.md docs/roadmap.md
git commit -m "docs(deploy): compose de HML/PRD em bind mounts, backup e restauracao"
```

- [ ] **Step 7: Registrar as quebras**

A lista "Quebras" da spec vai na descrição da entrega, junto com o que ficou sem verificação em Linux.
