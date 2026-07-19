# Secco.Intranet

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

Decisões arquiteturais específicas deste produto (Setor = Role, monolito, sistema de temas)
estão documentadas em [`docs/adr/secco-intranet-adrs.md`](docs/adr/secco-intranet-adrs.md) —
consultar antes de mudanças estruturais. O plano de fases está em
[`docs/roadmap.md`](docs/roadmap.md).

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

## Pós-geração (checklist)

1. **Gerar as migrations iniciais** (uma por engine, ADR-0018):
   ```bash
   dotnet ef migrations add Initial --project src/Secco.Intranet.Migrations.SqlServer --output-dir Migrations
   dotnet ef migrations add Initial --project src/Secco.Intranet.Migrations.Postgres --output-dir Migrations
   ```
2. **Montar a solution**: `dotnet new sln -n Secco.Intranet && dotnet sln add **/*.csproj`
3. **Apontar o `nuget.config`** para o feed onde `Secco.SecureGate.Client`,
   `Secco.LogStream.Client`, `Secco.NotificationHub.Client` e `Secco.SharedKernel` são
   publicados pelo secco-platform.
4. **CI**: workflow próprio deste repositório (não compartilha o `ci.yml` do monorepo).
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
