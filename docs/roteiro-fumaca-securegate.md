# Roteiro de fumaça contra o SecureGate real

A gestão de acesso (`/Acesso`) fala com o SecureGate por um adaptador que a suíte comum testa
com **dublês**. Dublê não prova que o contrato real é o que o adaptador assume — este roteiro
prova. Rode-o antes de mexer em `SecureGateGestaoDeAcesso`, ao subir a versão do
`Secco.SecureGate.Client`, e antes de uma entrega que toque a área de acesso.

São duas partes: uma **automatizada** (o adaptador e os handlers contra a API real) e uma
**manual** (as telas da Intranet contra a mesma API).

## 0. Subir o SecureGate de DEV

Vem do monorepo `secco-platform` (o [guia de testes](https://github.com/rafsecco/secco-platform/blob/main/docs/testing-guide.md)
de lá tem o roteiro completo da plataforma; aqui só o que esta fumaça precisa).

```bash
cd ../secco-platform
docker compose up -d        # SQL Server (1433) + MailHog — só a infra
```

Suba a API do SecureGate em **modo self-issued** (ela valida os próprios tokens, que é o que a
API de gestão exige), em HTTP e fora das portas de F5. Três armadilhas, todas já encontradas:

- `SecureGate:PublicBaseUrl` precisa ser a URL em que você sobe (o padrão de DEV é `https://localhost:4001`);
- `Secco:Authentication:Authority` e `DevelopmentSigningKey` são mutuamente exclusivos, e o
  `appsettings.Development.json` preenche a chave — zere-a com **um espaço** (string vazia
  pode apagar a variável no Windows e trazer o valor do appsettings de volta);
- sem isso o token sai com um `issuer` que a própria API recusa (`The issuer '...' is invalid`).

```bash
export ASPNETCORE_ENVIRONMENT=Development
export SecureGate__PublicBaseUrl=http://localhost:4101
export Secco__Authentication__Authority=http://localhost:4101
export Secco__Authentication__RequireHttpsMetadata=false
export Secco__Authentication__DevelopmentSigningKey=" "
export Secco__Authentication__Issuer=" "
dotnet run --project src/SecureGate/Secco.SecureGate.Api --no-launch-profile --urls http://localhost:4101
```

Confirme: `curl http://localhost:4101/health/ready` → `200`. O seed de DEV cria o tenant
`018f0000-0000-7000-8000-000000000001` e o client `secco-dev-console` (client credentials, com
`securegate:admin`) — credenciais em `docs/testing-guide.md` da plataforma.

## 1. Parte automatizada

Testes opt-in em `tests/Secco.Intranet.Tests/Smoke/`. Sem a variável abaixo, aparecem como
**pulados** na suíte comum — não rodam em CI e não exigem nada de quem só quer a suíte verde.

```bash
export SECCO_SMOKE_SECUREGATE_URL=http://localhost:4101
dotnet test tests/Secco.Intranet.Tests --filter SecureGateGestaoDeAcessoFumacaTests
```

Opcionais (os valores de DEV são o padrão): `SECCO_SMOKE_SECUREGATE_TENANT`,
`SECCO_SMOKE_SECUREGATE_CLIENT_ID`, `SECCO_SMOKE_SECUREGATE_CLIENT_SECRET`.

O que cobrem, com o client gerado real e client credentials reais:

| Cenário | O que prova |
|---|---|
| Criar, listar, obter, excluir perfil | Mapeamento dos DTOs; 409 em nome repetido; 404 depois de excluído |
| Perfil com membros não exclui | 409 da plataforma vira `PerfilComMembros` |
| Atribuir/retirar (idempotentes) | Reflexo em `GetUser`, `ListRoleMembers` e `ListUsers` |
| Desativar, reativar, encerrar sessões | Situação muda de fato; sessão encerra sem erro |
| Usuário inexistente | 404 vira `UsuarioNaoEncontrado` |
| Perfil reservado | Handler recusa antes de chamar; `installation-operator` não é atribuído |
| Último `intranet-admin` | Recusa retirar **e** desativar o único ativo, com a paginação real da API |
| SecureGate inacessível | Vira `Indisponivel`, sem exceção |
| Diretório: usuários ativos | A fonte do diretório lista os ativos e deixa de fora os desativados, contra a API real |

Os testes criam usuários e perfis com sufixo único e limpam no fim (perfis excluídos; usuários
**desativados**, já que a API não exclui usuário). É repetível: rode duas vezes seguidas para conferir.

## 2. Parte manual — as telas contra a API real

Sobe a própria Intranet apontando para esse SecureGate. Sem `Secco:SecureGate:Authority` a
autenticação fica desligada (modo aberto de DEV), mas com `BaseUrl`/`ClientId`/`ClientSecret` a
composição escolhe o **adaptador real** — é exatamente isso que se quer ver.

O tenant de DEV da Intranet (`Intranet:Development:TenantId`) já é o mesmo do seed do SecureGate.
Falta só um banco para a Intranet. Use um descartável, com login próprio `db_owner` — a aplicação
nunca usa `sa` (ADR-0007):

```bash
# Git Bash converte caminhos de /opt/...; MSYS_NO_PATHCONV evita isso
export MSYS_NO_PATHCONV=1
SQL="docker exec secco-platform-sqlserver-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P Secco@Dev123 -C"
$SQL -Q "CREATE DATABASE secco_intranet_smoke; CREATE LOGIN smoke_app WITH PASSWORD='Smoke#App2026x', CHECK_POLICY=OFF;"
$SQL -d secco_intranet_smoke -Q "CREATE USER smoke_app FOR LOGIN smoke_app; ALTER ROLE db_owner ADD MEMBER smoke_app;"
```

(o `USE` não funciona no mesmo lote que o `CREATE DATABASE`, por isso são duas chamadas.)

```bash
env "ASPNETCORE_ENVIRONMENT=Development" \
    "Secco__Tenancy__Tenants__018f0000-0000-7000-8000-000000000001__ConnectionString=Server=localhost,1433;Database=secco_intranet_smoke;User Id=smoke_app;Password=Smoke#App2026x;TrustServerCertificate=true" \
    "Secco__SecureGate__BaseUrl=http://localhost:4101" \
    "Secco__SecureGate__ClientId=secco-dev-console" \
    "Secco__SecureGate__ClientSecret=secco-dev-console-secret-32-chars-min!" \
    dotnet run --project src/Secco.Intranet.Web --no-launch-profile --urls http://localhost:5250
```

O `env` é necessário: a variável do catálogo tem hífens (o GUID) e o shell não a aceita com `export`.

Abra `http://localhost:5250/Acesso` e confira:

1. **Perfis.** Lista o `dev-admin` do seed, e oferece **Criar intranet-admin** e **Criar inventario-admin**.
2. **Usuários.** Lista os usuários reais do tenant, com situação e perfis; a busca por e-mail filtra.
3. **Criar perfil** `gerente de compras` (com espaços) → toast **vermelho** com a regra do nome; nada é criado.
4. **Criar perfil** `smoke-ui` → toast verde. Criar de novo → toast vermelho "já existe" (409 real).
5. **Atribuir** `smoke-ui` ao usuário `dev@secco.local` pela tela do perfil → aparece nos membros.
6. **Excluir** `smoke-ui` com membro → recusado ("ainda tem membros"). **Retirar** → **Excluir** → ok.
7. **Reservado.** Atribuir `installation-operator` → recusado, sem chamar a plataforma.
8. **Último admin.** Crie `intranet-admin` pela tela, atribua ao `dev@secco.local`; tentar **retirar**
   ou **desativar** esse usuário → toast vermelho "último intranet-admin ativo"; a conta segue ativa
   com o perfil (confira na API).
9. **Usuário inexistente.** `/Acesso/Usuario/<guid aleatório>` → 404.

### Diretório organizacional

Com a Intranet no ar contra o mesmo SecureGate (modo aberto de DEV, sem `Authority`, todo acesso é
liberado), confira `http://localhost:5250/diretorio`:

10. **Lista real.** Aparecem os usuários **ativos** do tenant, pelo e-mail; os desativados não
    aparecem, e os colaboradores fictícios que o seeder de DEV grava **não** aparecem (perfil de quem
    não é usuário ativo é ignorado).
11. **Edição.** Abra uma pessoa → **Editar**: defina nome, cargo e setor de lotação → toast
    "Dados atualizados". Definir um gestor que fecharia um ciclo (A→B e depois B→A) é recusado.
12. **Organograma.** `/diretorio/organograma` mostra a chefia antes da equipe, em blocos que
    recolhem. Desative no SecureGate o gestor de alguém e espere 60 s (a lista de usuários fica em
    cache): a pessoa **continua na tela** — como raiz se tiver equipe, ou em "Sem posição" com a
    marca "gestor inativo" — e o gestor desativado some do diretório.
13. **Importação.** `/diretorio/importar` com um CSV de teste (`email;nome;cargo`) mostra os totais
    **sem gravar** (e-mail que não é de usuário ativo vira erro da linha); confirme, e a lista passa a
    mostrar o cargo. Reenviar o mesmo arquivo dá "sem alteração".

"Meu perfil" (`/diretorio/perfil`) precisa de um usuário logado — no modo aberto ele explica isso. Para
vê-lo de verdade, registre a Intranet como client OIDC e entre com login real, com a Role `diretorio-user`.

Sem ator identificado (modo aberto) a regra de **autoexclusão** não é exercitável — ela depende de
quem está logado, e é coberta pelos testes unitários. Para vê-la de verdade, registre a Intranet
como client OIDC no SecureGate e entre com login real (ver a seção de autenticação do README).

## 3. Limpar

```bash
# parar a Intranet e a API do SecureGate (Ctrl+C nos terminais, ou pelo gerenciador de processos)
$SQL -Q "ALTER DATABASE secco_intranet_smoke SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE secco_intranet_smoke; DROP LOGIN smoke_app;"
cd ../secco-platform && docker compose stop
```

## O que a fumaça já encontrou

Registro do que a API real fez diferente do que se assumia — o motivo de o roteiro existir.

- **`ListRoleMembers` inclui contas desativadas** (com `status: "Deactivated"`); por isso a regra do
  último `intranet-admin` filtra por situação **ativa** em vez de contar membros. Um perfil só com
  desativados **não** pode ser excluído (409), mesmo listando "sem ninguém ativo".
- **`totalPages` é `0` quando o perfil não tem membros** (não `1`); o laço de contagem trata isso.
- **Em DEV, o `PublicBaseUrl` precisa casar com a URL em que a API sobe**, senão o token que ela
  emite é recusado por ela mesma — não é problema da Intranet, mas custa tempo a quem sobe o ambiente.

Última execução completa: 2026-09-26, contra o SecureGate do monorepo na `main` (client 0.11.0).
Parte automatizada: 9/9 (a nona é a do Diretório). Parte manual: itens 1–13 conforme descrito.
