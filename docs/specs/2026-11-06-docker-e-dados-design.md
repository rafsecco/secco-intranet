# Docker e dados da instalação — desenho

**Data:** 2026-11-06
**Estado:** aprovado

## Problema

Quem adota a Intranet monta ambientes de DEV, HML e PRD e precisa de um padrão para rodá-la em Docker e
guardar os dados de um jeito que o backup seja cópia de pasta. Hoje:

- O `docker-compose.yml` de DEV só tem o SQL Server. A aplicação roda por `dotnet run`, e o padrão de
  `ArquivoStoreOptions.Raiz` é `AppContext.BaseDirectory/documentos`, dentro de `bin/`: um `dotnet clean` ou
  `git clean` apaga os documentos, as fotos e as imagens do Mural.
- As chaves do ASP.NET Core DataProtection não são persistidas: não há `AddDataProtection` nem no repositório
  nem no `Secco.SDK.AspNetCore`. Num container recriado as chaves mudam, e cookies de sessão, antiforgery e
  TempData antigos deixam de valer.
- O `dev.ps1` só aceita os dois temas de fábrica, e o `Dockerfile` lista cada projeto de tema com `COPY`.
- O compose de produção do guia usa volumes nomeados. Além disso, a linha
  `Secco__Tenancy__Tenants__${TENANT_ID}__ConnectionString:` **não funciona**: o Compose não interpola chave de
  mapa, então a variável sai com o nome literal e o tenant nunca é configurado.

O caso que motiva: trocar de máquina de DEV copiando uma pasta e um arquivo, e operar HML/PRD com um backup que
qualquer operador entenda.

## Objetivos

1. `docker compose up` sobe a aplicação em DEV com SQL em container, e há um caminho documentado para usar uma
   instância de SQL existente.
2. Documentos, chaves e banco de DEV moram em `.dados/`, fora do versionamento. Migrar de máquina é copiar
   `.dados/` e `.env`.
3. `dotnet run` e F5 continuam funcionando e usam as mesmas pastas do container.
4. `dev.ps1`, `dev.sh` e o build da imagem aceitam tema de fork.
5. O guia de deploy traz o compose de HML/PRD em bind mounts e uma seção de backup e restauração.
6. Nenhum caminho de dados nem nome de operação (serviço, imagem, identificação no log) carrega `secco`: quem adota
   usa a pasta e os nomes dele.

## Fora de escopo

- **PostgreSQL no compose de DEV.** O compose segue só com SQL Server; o guia cobre o backup dos dois engines.
- **Várias réplicas.** O guia continua assumindo host único. A pasta de chaves só serve a mais de uma instância
  se for compartilhada, e isso fica citado como limitação.
- **Cifrar as chaves do DataProtection em repouso** (`ProtectKeysWith*`). O guia trata a pasta como segredo.
- **Script ou agendamento de backup.** O guia descreve o procedimento; a automação é de cada instalação.
- **Compose de produção como arquivo versionado.** Continua no guia, como já é a posição dele: proxy, certificado
  e banco variam demais entre instalações.
- **Compatibilidade com o arranjo atual.** A primeira instalação ainda está em testes, e quebrar é aceitável desde
  que registrado (ver "Quebras").
- **Identidade de código e configuração da plataforma.** Namespaces e assemblies `Secco.Intranet.*`, o
  `ENTRYPOINT` `Secco.Intranet.Web.dll` e as seções `Secco:*` (`Secco__SecureGate__*`, `Secco__Tenancy__*`) continuam:
  são o contrato com o SDK, e renomeá-los é outro projeto.
- **Nomes internos sem efeito em operação:** pasta temporária dos testes e `name` dos `package.json` dos temas.

## Decisões

### 1. Caminhos e nomes neutros

| Onde | Nome |
|---|---|
| DEV, na raiz do repositório | `.dados/documentos`, `.dados/chaves`, `.dados/sql/{data,log,secrets}` |
| Dentro da imagem | `/var/lib/intranet/documentos`, `/var/lib/intranet/chaves` |
| Host de HML/PRD | `${INTRANET_DADOS:-/srv/intranet}/documentos`, `.../chaves`, `.../caddy` |
| Serviço do Compose e imagem | `intranet-web` |
| `ServiceName` no LogStream | `intranet`, o mesmo nome de produto de `permissions:intranet` |
| DataProtection | `SetApplicationName("intranet")` |
| Usuário padrão do feed no build (`NUGET_USER`) | `intranet`; o GitHub Packages aceita qualquer valor não vazio |
| Exemplos de client no guia | `intranet-login`, `intranet`, `intranet-gestao` |
| Banco e login de DEV | `intranet_alfa`, `intranet_alfa_app` |
| Bancos dos testes e do roteiro de fumaça | Mesmo padrão: `intranet_<nome>` |

O `/var/lib/secco-intranet` e o `secco-intranet-web` atuais saem do `Dockerfile` e do guia. Nada depende desses nomes:
o código da plataforma não os cita, e os clients são nomeados pelo operador. O teste de `EnvioDeLogTests` que fixa o
`ServiceName` acompanha a mudança.

O banco de DEV muda em `docker/init/provision-tenant-db.sql`, `.env.example` e `.vscode/settings.json`. Isso diverge do
padrão `secco_<produto>_<tenant>` do template da plataforma, de propósito. O registro histórico no `docs/roadmap.md`
não muda. Em HML/PRD o nome do banco continua sendo escolha do DBA.

### 2. Compose de DEV

A aplicação entra no `docker-compose.yml` como serviço `intranet-web`, sem perfil. Assim `docker compose up` sobe
tudo.

- **Build:** pelo `Dockerfile` existente, com o PAT como BuildKit secret (`secrets.nuget_pat.environment: NUGET_PAT`).
  `NUGET_PAT` passa a ser variável do `.env`.
- **Ambiente:** `env_file: .env`, para que o que o adotante puser ali (tema, SecureGate) chegue à aplicação. O
  serviço **zera** `MSSQL_SA_PASSWORD`, `MSSQL_APP_PASSWORD` e `NUGET_PAT` em `environment:`. Sem isso, a senha do
  `sa` chegaria ao processo da aplicação, contra a ADR-0007, e o PAT ficaria visível no container em execução.
- **`ASPNETCORE_ENVIRONMENT=Development`:** migrations e seed no start, e modo aberto quando não há SecureGate.
- **Connection string:** a do `.env` usa `localhost`, que não alcança o SQL de dentro do container. O serviço a
  sobrescreve com `Server=sqlserver,1433` e `${MSSQL_APP_PASSWORD}`.
- **Porta:** `${INTRANET_PORT:-8080}:8080`, diferente dos 5250/7250 do `dotnet run`, para os dois não colidirem.
- **Dependência:** `depends_on: sqlserver-init` com `service_completed_successfully`.

**Quem quer só o SQL** (F5, `dotnet run`): `docker compose up -d sqlserver-init`. O `dev.ps1`, o `dev.sh`, o README e
a task do VS Code passam a usar esse comando.

### 3. Instância de SQL existente

Arquivo `docker-compose.sql-existente.yml`, ativado por uma linha no `.env`:

```dotenv
COMPOSE_FILE=docker-compose.yml:docker-compose.sql-existente.yml
COMPOSE_PATH_SEPARATOR=:
```

O override põe `sqlserver` e `sqlserver-init` num perfil que ninguém liga, deixa a aplicação dependendo só do
`dados-init` (decisão 5) e tira a sobrescrita da connection string, de modo que vale a do `.env`. Precisa de Compose
2.24 ou mais novo, por causa de `!reset`/`!override`.

O guia e o `.env.example` documentam:

- De um container Linux, autenticação integrada do Windows/AD não funciona sem Kerberos. Nesse caso, login SQL, ou
  seguir com `dotnet run`.
- SQL na própria máquina: `localhost` não alcança o host a partir do container. Use `host.docker.internal`; o override
  declara o `extra_hosts` com `host-gateway` para funcionar também em Linux.
- `MSSQL_SA_PASSWORD` e `MSSQL_APP_PASSWORD` continuam obrigatórias no `.env`, porque o Compose interpola o arquivo
  inteiro, mesmo com o serviço desligado. Com valor qualquer, sem uso.
- Com esse modo ativo, `dev.ps1` e `dev.sh` pulam o Docker sozinhos, sem precisar de `-SemDocker`: o nome do serviço
  na linha de comando ligaria o SQL mesmo fora do perfil.

### 4. Dados do SQL de DEV em subpastas

O banco vai junto em `.dados/`, porque os documentos sem as linhas do banco não abrem: a chave de cada arquivo fica
em `ds_chave_embrulhada`.

O mount é por **subpastas**, `data`, `log` e `secrets`. Evidência do teste descartável, no Docker Desktop com WSL2:

| Formato | Resultado |
|---|---|
| `/var/opt/mssql` inteiro em bind mount | O SQL Server trava ao subir (gera dump e sai) |
| Subpastas em bind mount | Dados sobrevivem a recriar o container, a copiar a pasta e a `docker kill` |
| Volume nomeado (referência) | Leitura disponível 1s depois de subir |

Dois custos do formato por subpastas, aceitáveis em DEV:

- Por 4 a 5s depois de o `SELECT 1` responder, ler o banco dá erro 904, mesmo com o status `ONLINE`. O healthcheck
  atual não enxerga isso, então o `sqlserver-init` passa a repetir até conseguir ler o banco do tenant, e só então
  termina. Passa também a usar `sqlcmd -b`, para que falha vire código de saída.
- `DBCC CHECKDB` online não funciona, porque o mount não aceita sparse files. Não é modelo para banco de produção:
  por isso o compose de HML/PRD deixa o banco fora e o backup dele nunca é cópia de arquivo.

Copiar `.dados/sql` só com o SQL parado (`docker compose down`).

### 5. Permissões em Linux

Serviço único `dados-init` (`restart: "no"`), do qual `sqlserver` e a aplicação dependem. Cria as pastas e ajusta o
dono: `.dados/sql` para o uid 10001, `.dados/documentos` e `.dados/chaves` para `${INTRANET_UID:-1654}`. A aplicação
roda com `user: "${INTRANET_UID:-1654}"`. Em Linux, quem usa também `dotnet run` define `INTRANET_UID` com o próprio
uid, para os dois caminhos gravarem na mesma pasta. Em Windows o padrão basta.

O teste descartável rodou só em Windows: este item precisa de verificação em Linux, e o `chown` não pode derrubar o
serviço em Windows.

### 6. `dotnet run` fora de `bin/`

`appsettings.Development.json` ganha os dois caminhos, relativos:

```json
"Documentos": { "Armazenamento": { "Raiz": "../../.dados/documentos" } },
"ProtecaoDeDados": { "PastaDasChaves": "../../.dados/chaves" }
```

Caminho relativo nessas duas chaves passa a ser resolvido a partir do **content root**, no `Program.cs`, antes de
`AddIntranetInfrastructure`. Hoje `SistemaArquivosArquivoStore` usa `Path.GetFullPath`, que depende do diretório de
trabalho do processo. No container, a variável de ambiente da imagem é absoluta e vence o JSON.

### 7. DataProtection

Na Web: `AddDataProtection().SetApplicationName("intranet")`, com `PersistKeysToFileSystem` quando
`Intranet:ProtecaoDeDados:PastaDasChaves` estiver preenchida. Vazia, vale o padrão do framework, que é o que os
testes usam.

- **Por que arquivo e não banco:** a ADR-0035 da plataforma manda estado durável para o banco do produto, e o
  SecureGate faz assim. A Intranet só tem bancos por tenant (ADR-0005), e o conjunto de chaves é da instalação.
- **Nome de aplicação fixo:** o padrão do framework deriva do content root, e os caminhos diferem entre o `dotnet run`
  e o container.
- **Sem cifra em repouso:** o framework registra um aviso no log ao subir, e o guia explica que é esperado.
- **Plataforma:** nada a pedir. São três chamadas de API do framework, sem lacuna do SDK.

Vale registrar em ADR nova da Intranet (ADR-0014), curta, por contrariar a regra geral da plataforma.

### 8. Imagem

O `Dockerfile` usa os caminhos neutros, define `Intranet__ProtecaoDeDados__PastaDasChaves`, cria as duas pastas e
declara as duas como `VOLUME`. `.dockerignore` e `.gitignore` ganham `.dados/`.

Tema ou RCL de fork precisa entrar na imagem. O implementador tenta tornar as cópias genéricas com
`COPY --parents`, se o frontend `docker/dockerfile:1` já o suportar de forma estável. Se não, `docs/temas.md` e
`docs/extensao.md` documentam as linhas a acrescentar no `Dockerfile`.

### 9. Scripts

- `dev.ps1`: sai o `ValidateSet`. O `-Tema` aceita qualquer nome, e a ajuda explica que vale o tema registrado.
- `dev.sh`: já aceita qualquer valor; só o comentário de uso muda.
- Os dois sobem só o SQL (decisão 2) e respeitam o modo de instância existente (decisão 3).
- O script não valida o nome. Nome errado aparece na primeira página, quando a view do tema não é encontrada.

### 10. Compose de HML/PRD, no guia

- Bind mounts em `${INTRANET_DADOS:-/srv/intranet}`: `documentos`, `chaves` e `caddy`. Nenhum volume nomeado.
- Preparo do host com `install -d -o 1654 -g 1654 -m 0700` nas duas pastas da aplicação.
- Connection string do tenant em **sintaxe de lista**, a que o Compose interpola, corrigindo o bug do guia.
- Banco fora do compose, sem o trecho que cita um serviço `sqlserver` que o exemplo não tem.
- O mesmo arquivo serve a HML e PRD, com um `.env` por ambiente. HML usa `ASPNETCORE_ENVIRONMENT=Staging` e **chave
  mestra própria**, nunca a de PRD.
- A tabela de variáveis, a descrição da imagem e o checklist acompanham, incluindo a variável nova das chaves.

### 11. Backup e restauração, no guia

| Peça | Como copiar | Se perder |
|---|---|---|
| Banco de cada tenant | `BACKUP DATABASE` ou `pg_dump`, nunca arquivo | Perde os dados e as chaves embrulhadas dos arquivos |
| Pasta `documentos` (documentos, fotos, imagens do Mural) | Cópia de pasta | Perde os arquivos |
| Chave mestra e chaves aposentadas | Cofre, **separado dos outros dois** | Perde todos os arquivos |
| Pasta `chaves` (DataProtection) | Opcional, cifrada | Todos entram de novo; nada mais se perde |

Pontos que o guia precisa deixar claros:

- **Ordem do backup:** banco primeiro, depois a pasta. Toda linha do banco copiado tem o arquivo na cópia seguinte, e
  um arquivo sem linha é inofensivo. Como trocar foto remove o arquivo anterior, a janela não é zero: backup estrito
  pede a aplicação parada ou um horário sem uso.
- **Chave mestra junto do backup é a mesma coisa que sem cifra.** Quem tem as três peças lê tudo.
- **Rotação:** um backup antigo pode estar embrulhado com uma chave já aposentada. Guarde cada chave enquanto existir
  backup que dependa dela.
- **Pasta `chaves` é segredo:** quem a tem forja sessão de qualquer pessoa, inclusive `intranet-admin`.
- **Ordem da restauração:** configurar as chaves mestras; restaurar o banco; restaurar a pasta com o dono certo;
  opcionalmente a pasta `chaves`; rodar o `migrate` se a imagem for mais nova que o backup; subir; conferir
  `/health/ready`.
- **Login do banco:** ao restaurar em outro servidor, o usuário do banco fica órfão (`ALTER USER ... WITH LOGIN`).
- **Um tenant só:** o banco dele mais `{raiz}/{tenantId}`.
- **Testar:** em ambiente isolado, com `Intranet__Notificacao__HubUrl` vazia para o worker de vencimentos não mandar
  aviso real. Conferir abrindo um documento antigo, uma foto e uma imagem do Mural, porque só a abertura prova a
  decifragem, e comparar a contagem de arquivos com a de linhas. `RESTORE VERIFYONLY` confirma o arquivo de backup,
  mas não substitui esse teste.

O README ganha a versão curta para DEV: `docker compose down`, copiar `.dados/` e `.env`, `docker compose up`.

## Quebras

Quem já usa o arranjo atual precisa ajustar o seguinte. Vai na descrição da entrega, não no guia:

- `docker compose up -d` passa a construir a imagem e exige `NUGET_PAT` no `.env`. Só o SQL:
  `docker compose up -d sqlserver-init`.
- O banco de DEV sai do volume `mssql-data`, que fica órfão, e o banco recomeça vazio.
- O banco e o login de DEV viram `intranet_alfa` e `intranet_alfa_app`: a connection string do `.env` precisa mudar.
- Os documentos de DEV saem de `bin/.../documentos`.
- Imagem: `/var/lib/secco-intranet` vira `/var/lib/intranet`. Mounts e variáveis apontando para o caminho antigo
  precisam mudar.
- Serviço e imagem `secco-intranet-web` viram `intranet-web`, inclusive no `reverse_proxy` do proxy e nos comandos de
  deploy.
- O log chega ao LogStream com `ServiceName` `intranet`: filtro ou painel com `secco-intranet` precisa mudar.

## Testes e verificação

**Automatizados:**

- Resolução de caminho relativo a partir do content root; caminho absoluto fica intacto; chave vazia fica vazia.
- DataProtection: com a pasta preenchida, as chaves vão para lá; um segundo host na mesma pasta decifra o que o
  primeiro cifrou.

**Manual, com Docker** (o CI não sobe o compose):

1. `docker compose up` do zero: aplicação responde em `/health/ready`, e `docker exec ... printenv` não mostra o `sa`
   nem o PAT.
2. Publicar um documento, deixar um formulário aberto no navegador, `docker compose down`, `up`: o documento abre, e o
   formulário é aceito ao enviar. O antiforgery é a prova das chaves, porque em modo aberto não há cookie de sessão.
3. `dotnet clean`, depois `dev.ps1`: o mesmo documento abre pelo `dotnet run`, e `.dados/` fica na raiz do repositório.
4. `dotnet run` e F5 a partir de diretórios de trabalho diferentes usam a mesma `.dados/`.
5. Copiar `.dados/` e `.env` para outra pasta e subir dali: o documento abre.
6. `git status` não mostra `.dados/`; `git check-ignore .dados/` confirma.
7. `dev.ps1 -Tema Qualquer` não é recusado pelo script; `dev.ps1` e `dev.sh` sobem só o SQL.
8. Modo de instância existente: `docker compose config` sem `sqlserver`, com a connection string do `.env`.
9. `docker compose config` sobre o exemplo do guia confirma a connection string com o GUID interpolado.

## Decisões do implementador

- Nome exato da chave `Intranet:ProtecaoDeDados:PastaDasChaves` e onde mora o helper de resolução de caminho.
- Imagem do `dados-init`, desde que pequena e com versão fixa.
- Se `COPY --parents` serve (decisão 8).
