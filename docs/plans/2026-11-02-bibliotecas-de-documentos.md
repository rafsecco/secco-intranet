# Bibliotecas de documentos — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** páginas de documentos sem setor dono (`/bibliotecas/{slug}`), cadastradas pelo `intranet-admin`, com leitura e escrita por permissão `biblioteca-{slug}:read`/`:write` nos perfis existentes, reaproveitando tudo do Documentos do setor.

**Architecture:** o `Documento` passa a ter um dono (`DonoDeDocumento`: setor **ou** biblioteca), e os casos de uso de `Application/Documentos` trocam o slug do setor por uma `ReferenciaDeDono` resolvida num lugar só (`ResolvedorDeDono`). A Web ganha o cadastro (`BibliotecasController`), a página com as mesmas ações do setor (`BibliotecaController`, com as views de Documentos movidas para `Shared`), as bibliotecas no menu principal e a seção "Bibliotecas" em `/acesso`.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core (SqlServer + Postgres), xUnit + AwesomeAssertions, Testcontainers.

**Spec:** [docs/specs/2026-11-02-bibliotecas-de-documentos-design.md](../specs/2026-11-02-bibliotecas-de-documentos-design.md)

## Global Constraints

- Erros de negócio por `Result`/`IntranetErrors` (ADR-0004); exceção só como rede da entidade.
- Biblioteca inexistente, inativa ou sem leitura: **404** indistinto na página, nas ações e no download (ADR-0005).
- O Documentos do setor não muda de comportamento: os testes atuais dele passam, ajustados só na forma das chamadas.
- Slug da biblioteca: `[a-z0-9-]`, de 1 a 53 caracteres, minúsculo, imutável. Ícone padrão `bi-journal-text`.
- Permissões: famílias `biblioteca-{id}:read` ("Ler uma biblioteca de documentos específica") e `biblioteca-{id}:write` ("Alterar uma biblioteca de documentos específica: publicar, organizar, editar e arquivar"); o `{id}` é o slug.
- Documento de biblioteca grava `Visibilidade.Setor` e ninguém lê esse valor para ele.
- Nomes de banco pela convention (ADR-0017); constraints e índices nomeados à mão: `uk_bibliotecas_ds_slug`, `ck_documentos_dono`, `idx_documentos_id_fk_biblioteca_id_fk_parent_nr_ordem`, `fk_documentos_biblioteca`.
- Trilha: recurso `biblioteca`, verbos `biblioteca.criar`/`editar`/`desativar`/`reativar`; nos verbos de documento o payload leva `setor` **ou** `biblioteca` (slug), nunca os dois.
- Indentação com tabs em `.cs`/`.cshtml`; commits em Conventional Commits, português sem acento, com `Co-Authored-By`.

## Review Focus

1. **Setor e biblioteca com o mesmo slug** (`ti`): `setor-ti:read` não abre documento da biblioteca `ti`, nem o contrário. A referência ao dono sempre carrega o tipo. Teste na Task 3 (download) e na Task 6 (página).
2. **Pai de outro dono por POST forjado**: publicar na biblioteca com `ParentId` de um documento de setor (ou de outra biblioteca) é `ParentInvalido`, antes de gravar o arquivo. Teste na Task 3.
3. **Pessoa com só `biblioteca-x:write`** (concedida fora da tela): lê a página e vê o item no menu. Teste na Task 5.
4. **Biblioteca desativada com documento já linkado**: o download devolve 404 mesmo para quem tem leitura. Teste na Task 3.
5. **Salvar um perfil que tem permissão de biblioteca inativa**: a permissão continua na lista, sem aparecer duplicada. Teste na Task 8.

---

### Task 1: Domínio — `Biblioteca` e o dono do documento

**Files:**

- Create: `src/Secco.Intranet.Domain/Bibliotecas/Biblioteca.cs`, `src/Secco.Intranet.Domain/Documentos/DonoDeDocumento.cs`
- Modify: `src/Secco.Intranet.Domain/Documentos/Documento.cs`, os dois chamadores (`PublicarDocumentoHandler`, `CriarGrupoDeDocumentosHandler`) e os testes que constroem `Documento`
- Test: `tests/Secco.Intranet.Tests/Unit/BibliotecaTests.cs`, `tests/Secco.Intranet.Tests/Unit/DonoDeDocumentoTests.cs` (novos)

**Interfaces:**

- Produces:
  - `public sealed class Biblioteca : BaseEntity` — `Biblioteca(string nome, string slug, string? icone = null)`; `const string IconePadrao = "bi-journal-text"`; `const int TamanhoMaximoDoSlug = 53`; `static bool SlugEhValido(string? slug)` (após trim e minúsculas, casa `\A[a-z0-9-]{1,53}\z`); `static bool IconeEhValido(string? icone)` (mesmo `IconeBootstrap` do `Setor`); `Nome`, `Slug`, `Icone`, `Ativo`, `CreatedAt`; `Renomear(string)`, `DefinirIcone(string?)`, `Desativar()`, `Ativar()`. Espelha o `Setor`, sem `Fixo`.
  - `public readonly record struct DonoDeDocumento(Guid? SetorId, Guid? BibliotecaId)` com `static DoSetor(Guid)` e `static DaBiblioteca(Guid)` (lançam `DomainInvariantException` para `Guid.Empty`) e `bool EhValido` (exatamente um preenchido).
  - `Documento`: o primeiro parâmetro do construtor e de `CriarGrupo` passa de `Guid setorId` a `DonoDeDocumento dono`; `Guid? SetorId`, `Guid? BibliotecaId` (setters privados), `DonoDeDocumento Dono => new(SetorId, BibliotecaId)`. Dono inválido lança `DomainInvariantException("Um documento exige exatamente um dono: setor ou biblioteca.")`.

- [ ] **Step 1: Testes que falham** — `DonoDeDocumentoTests`: `Documento_de_biblioteca_nao_tem_setor` (`SetorId` nulo, `BibliotecaId` igual), `Dono_sem_nenhum_id_e_recusado` (`new DonoDeDocumento(null, null)` → `DomainInvariantException`), `Dono_com_os_dois_ids_e_recusado`, `Grupo_de_biblioteca_grava_visibilidade_setor`. `BibliotecaTests`: `Slug_e_normalizado` (`" Manuais "` → `"manuais"`), `SlugEhValido` com `[InlineData]` para `"a"` ✓, `new string('a', 53)` ✓, `new string('a', 54)` ✗, `"com espaco"` ✗, `"acentuação"` ✗, `""` ✗; `Icone_vazio_cai_no_padrao`; `Desativar_e_Ativar`.
- [ ] **Step 2:** `dotnet test --filter "FullyQualifiedName~DonoDeDocumentoTests|FullyQualifiedName~BibliotecaTests"` → falha de compilação.
- [ ] **Step 3:** implemente as interfaces acima. Os dois handlers passam `DonoDeDocumento.DoSetor(setor.Id)`; os testes existentes que constroem `Documento` fazem o mesmo.
- [ ] **Step 4:** em `DocumentoConfiguration`, marque `SetorId` com `IsRequired()`, para o modelo do EF não mudar antes da migration da Task 2 (sem isso, `Guid?` geraria mudança pendente e a integração quebraria). `dotnet build Secco.Intranet.slnx` sem erro e `dotnet test Secco.Intranet.slnx` inteiro passa.
- [ ] **Step 5: Commit** `feat(documentos): biblioteca e dono do documento no dominio (issue #4)`.

### Task 2: Persistência — tabela, constraints e migrations

**Files:**

- Create: `src/Secco.Intranet.Infrastructure/Mappings/BibliotecaConfiguration.cs`, `src/Secco.Intranet.Infrastructure/Repositories/BibliotecaRepository.cs`, `src/Secco.Intranet.Application/Bibliotecas/IBibliotecaRepository.cs`, migrations `Bibliotecas` em `Migrations.SqlServer` e `Migrations.Postgres`
- Modify: `src/Secco.Intranet.Infrastructure/Mappings/DocumentoConfiguration.cs`, `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs`, `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/BibliotecaPersistenciaTests.cs` (novo)

**Interfaces:**

- Produces: `IBibliotecaRepository` — `AddAsync(Biblioteca, ct)`, `SaveChangesAsync(ct)`, `GetByIdAsync(Guid, ct)`, `GetBySlugAsync(string slug, ct)` (sem diferenciar caixa), `ExistsBySlugAsync(string slug, ct)`, `ListarAsync(bool somenteAtivas, ct)` → `IReadOnlyList<Biblioteca>` por `Nome`.

- [ ] **Step 1: Testes que falham** (Testcontainers, nos dois engines como os vizinhos): `Slug_repetido_viola_uk_bibliotecas_ds_slug`; `Documento_com_setor_e_biblioteca_viola_ck_documentos_dono` (insert por SQL cru com os dois preenchidos → exceção do banco); `Documento_sem_dono_viola_ck_documentos_dono`; `Documento_de_biblioteca_e_lido_de_volta` (`BibliotecaId` preenchido, `SetorId` nulo).
- [ ] **Step 2:** rode os testes → falham (tabela inexistente).
- [ ] **Step 3:** `BibliotecaConfiguration` (slug até 53, nome no limite do setor, ícone até 64, índice único `uk_bibliotecas_ds_slug`); em `DocumentoConfiguration`, retire o `IsRequired()` da Task 1 (`SetorId` opcional), FK `fk_documentos_biblioteca` para `tb_bibliotecas` (`Restrict`), índice `idx_documentos_id_fk_biblioteca_id_fk_parent_nr_ordem` e `HasCheckConstraint("ck_documentos_dono", "(id_fk_setor IS NOT NULL AND id_fk_biblioteca IS NULL) OR (id_fk_setor IS NULL AND id_fk_biblioteca IS NOT NULL)")`, SQL que vale nos dois engines. Gere as migrations com o mesmo nome:

  ```bash
  dotnet ef migrations add Bibliotecas --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
  dotnet ef migrations add Bibliotecas --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
  ```

  Confira no `.cs` gerado que os nomes de constraint e índice são os da Global Constraints.
- [ ] **Step 4:** os testes da Task passam, e `DocumentoTenantIsolationTests` e `DocumentoRepositoryHierarquiaTests` continuam passando.
- [ ] **Step 5: Commit** `feat(documentos): tabela de bibliotecas e dono xor do documento`.

### Task 3: Casos de uso de Documentos servem os dois donos

**Files:**

- Create: `src/Secco.Intranet.Application/Documentos/ReferenciaDeDono.cs`, `src/Secco.Intranet.Application/Documentos/ResolvedorDeDono.cs`
- Modify: todos os arquivos de `src/Secco.Intranet.Application/Documentos/`, `src/Secco.Intranet.Infrastructure/Repositories/DocumentoRepository.cs`, `src/Secco.Intranet.Application/IntranetErrors.cs`, `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`, `tests/Secco.Intranet.Tests/Support/DocumentoRepositorioEmMemoria.cs`, os chamadores na Web (`SetorController`, `DocumentosController`, `ListaDeDocumentosFactory`) e os testes de Documentos existentes
- Test: `tests/Secco.Intranet.Tests/Unit/DocumentosDeBibliotecaHandlersTests.cs` (novo)

**Interfaces:**

- Produces:
  - `public enum TipoDeDono { Setor = 1, Biblioteca = 2 }`
  - `public sealed record ReferenciaDeDono(TipoDeDono Tipo, string Slug)` com `static DoSetor(string slug)` e `static DaBiblioteca(string slug)`, que normalizam o slug (trim, minúsculas). A igualdade é a do record.
  - `public sealed record DonoResolvido(DonoDeDocumento Dono, ReferenciaDeDono Referencia, string Nome)`
  - `public sealed class ResolvedorDeDono(ISetorRepository setores, IBibliotecaRepository bibliotecas)` — `Task<DonoResolvido?> ResolverAsync(ReferenciaDeDono? referencia, CancellationToken ct)`: nulo para referência nula, slug vazio, dono inexistente ou inativo. `static Error NaoEncontrado(ReferenciaDeDono? referencia)`: `IntranetErrors.Setores.NotFound` para setor e `IntranetErrors.Bibliotecas.NotFound` para biblioteca (crie a classe `Bibliotecas` em `IntranetErrors` com `NotFound` agora; a Task 4 acrescenta o resto).
  - `DocumentoComSetor` vira `DocumentoComDono(Documento Documento, ReferenciaDeDono Dono, string DonoNome, bool DonoAtivo)`.
  - `IDocumentoRepository`: `ListarPorSetorAsync(string)` vira `ListarPorDonoAsync(ReferenciaDeDono dono, ct)`; `ListarIrmaosAsync(Guid setorId, …)` vira `ListarIrmaosAsync(DonoDeDocumento dono, Guid? parentId, ct)`.
  - `DocumentoDto`: `Guid SetorId, string SetorNome, string SetorSlug` viram `ReferenciaDeDono Dono, string DonoNome`.
  - Comandos e consultas: `string? SetorSlug` vira `ReferenciaDeDono? Dono` (`PublicarDocumentoCommand`, `CriarGrupoDeDocumentosCommand`, `ListarDocumentosQuery`); `string SetorSlug` vira `ReferenciaDeDono Dono` (`MoverDocumentoCommand`, `EditarDocumentoCommand`, `ObterDocumentoParaEdicaoQuery`); `IReadOnlySet<string> SetoresAdministrados` vira `IReadOnlySet<ReferenciaDeDono> DonosAdministrados`; em `BaixarDocumentoQuery`, `SlugsDoUsuario` vira `IReadOnlySet<ReferenciaDeDono> DonosComLeitura`.
  - `PosicaoDeDocumentos.PodeSerPai(DocumentoComDono? pai, ReferenciaDeDono dono)`.
  - Payload da trilha: os handlers trocam `setor = slug` por uma chave escolhida pelo tipo, `setor` ou `biblioteca`. Payload de documento de setor fica byte a byte igual ao de hoje.

- [ ] **Step 1: Testes que falham** (`DocumentosDeBibliotecaHandlersTests`, com `DocumentoRepositorioEmMemoria` ganhando `RegistrarBiblioteca(Biblioteca)`):
  - `Publicar_na_biblioteca_grava_dono_e_visibilidade_setor` (comando com `Visibilidade.Empresa` → documento com `BibliotecaId` e `Visibilidade.Setor`);
  - `Publicar_em_biblioteca_inativa_e_NotFound` → `IntranetErrors.Bibliotecas.NotFound`;
  - `Pai_de_setor_na_biblioteca_e_ParentInvalido` e `Pai_de_outra_biblioteca_e_ParentInvalido`, com `store` falso sem nenhuma gravação;
  - `Mover_documento_de_biblioteca_pela_referencia_de_setor_de_mesmo_slug_e_NotFound`;
  - `Baixar_documento_de_biblioteca_exige_a_biblioteca_em_DonosComLeitura` (com `DoSetor("ti")` no conjunto e documento da biblioteca `ti` → `NotFound`; com `DaBiblioteca("ti")` → sucesso);
  - `Baixar_documento_de_biblioteca_inativa_e_NotFound` (`DonoAtivo` falso, mesmo com a biblioteca no conjunto);
  - `Trilha_de_documento_de_biblioteca_leva_chave_biblioteca_e_nao_setor` (payload JSON tem `biblioteca` e não tem `setor`).
- [ ] **Step 2:** rode o filtro da classe → falha de compilação.
- [ ] **Step 3:** implemente. Em `BaixarDocumentoHandler.PodeLer`: para biblioteca, `DonoAtivo && (!ExigirVinculo || DonosComLeitura.Contains(Dono))`, sem olhar visibilidade; para setor, a regra de hoje. Ajuste os chamadores da Web só na forma (`ReferenciaDeDono.DoSetor(slug)`, conjuntos de `ReferenciaDeDono`) e o `ListaDeDocumentosFactory` (badge nulo quando `Dono.Tipo == TipoDeDono.Biblioteca`). Registre `ResolvedorDeDono` como scoped.
- [ ] **Step 4:** a suíte inteira passa: `dotnet test Secco.Intranet.slnx`. Os testes de Documentos do setor mudam só na construção dos comandos.
- [ ] **Step 5: Commit** `refactor(documentos): casos de uso recebem o dono, setor ou biblioteca`.

### Task 4: Cadastro de bibliotecas na Application e permissões no catálogo

**Files:**

- Create: em `src/Secco.Intranet.Application/Bibliotecas/`: `BibliotecaDto.cs`, `ISlugsReservadosDeBiblioteca.cs`, `CriarBibliotecaHandler.cs`, `EditarBibliotecaHandler.cs`, `ListarBibliotecasHandler.cs`, `ObterBibliotecaPorSlugHandler.cs`
- Modify: `IntranetErrors.cs`, `Auditoria/VerbosDeAuditoria.cs`, `Acesso/IntranetPermissoes.cs`, `Acesso/EditarPermissoesDoPerfilHandler.cs`, `IntranetApplicationExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/CriarBibliotecaHandlerTests.cs`, `tests/Secco.Intranet.Tests/Unit/EditarBibliotecaHandlerTests.cs` (novos); `IntranetPermissoesTests.cs` (ampliar)

**Interfaces:**

- Produces:
  - `BibliotecaDto(Guid Id, string Nome, string Slug, string Icone, bool Ativo, DateTimeOffset CreatedAt)`
  - `ISlugsReservadosDeBiblioteca { bool Contem(string? slug); }`
  - `CriarBibliotecaCommand(string? Nome, string? Slug, string? Icone, bool TodosLeem)` → `Result<BibliotecaDto>`
  - `EditarBibliotecaCommand(Guid Id, string? Nome, string? Icone, bool Ativo)` → `Result<BibliotecaDto>` (molde do `EditarSetorCommand`, com `Ativo` decidindo desativar/reativar)
  - `ListarBibliotecasQuery(bool SomenteAtivas)` → `Result<IReadOnlyList<BibliotecaDto>>`, por nome
  - `ObterBibliotecaPorSlugHandler.HandleAsync(string slug, ct)` → `Result<BibliotecaDto>`; inexistente ou inativa → `Bibliotecas.NotFound`
  - `IntranetErrors.Bibliotecas`: `NomeRequired`, `NomeTooLong(int)`, `SlugRequired`, `SlugInvalido`, `SlugAlreadyExists(string)`, `SlugReservado(string)`, `IconeInvalido`, `NotFound`, com textos no tom dos de `Setores`
  - `VerbosDeAuditoria.BibliotecaCriar = "biblioteca.criar"`, `BibliotecaEditar`, `BibliotecaDesativar`, `BibliotecaReativar`; `RecursosDeAuditoria.Biblioteca = "biblioteca"`
  - `IntranetPermissoes.Biblioteca`: `FamiliaRead`, `FamiliaWrite`, `Read(string slug)`, `Write(string slug)`; as duas famílias entram em `Catalog` com as descrições da Global Constraints.

- [ ] **Step 1: Testes que falham** (`GestaoDeAcessoFalsa` e `TrilhaDeAcessoFalsa` dos dublês existentes):
  - `Criar_com_todos_leem_garante_read_no_tenant_member_antes_de_gravar`: `Chamadas` registra `GarantirPermissoesAsync("tenant-member", ["biblioteca-manuais:read"])` e a biblioteca está no repositório;
  - `Criar_com_falha_no_securegate_nao_grava` (`FalharCom` preenchido → falha com esse erro, repositório vazio, nenhuma trilha);
  - `Criar_sem_todos_leem_nao_chama_o_securegate`;
  - recusas, cada uma sem chamar o SecureGate: slug repetido (`SlugAlreadyExists`), slug `"nova"` reservado (dublê de `ISlugsReservadosDeBiblioteca`), slug com 54 caracteres (`SlugInvalido`), ícone `"fa-x"` (`IconeInvalido`), nome vazio;
  - `Criar_registra_biblioteca_criar_com_todosLeem` no payload;
  - `EditarBibliotecaHandlerTests`: renomear grava `biblioteca.editar`; `Ativo=false` grava `biblioteca.desativar`; `Ativo=true` numa inativa grava `biblioteca.reativar`; o slug não muda;
  - `IntranetPermissoesTests`: `Catalog` contém as duas famílias com as descrições exatas; `Biblioteca.Read("manuais") == "biblioteca-manuais:read"`;
  - `EditarPermissoesDoPerfil_acrescenta_read_a_todo_write_de_biblioteca` (`["biblioteca-manuais:write"]` → salva `read` e `write`), em `Unit/` ao lado dos testes de Acesso.
- [ ] **Step 2:** rode os filtros → falham.
- [ ] **Step 3:** implemente. O `CriarBibliotecaHandler` valida tudo, depois chama o SecureGate e só então grava. O comentário cita a ordem da ADR-0001, no tom do `CreateSetorHandler`. O perfil vem de `ClassificacaoDePerfil.PerfilImplicito`. A normalização de `write` ⇒ `read` no `EditarPermissoesDoPerfilHandler` vale só para `biblioteca-*`.
- [ ] **Step 4:** os testes passam.
- [ ] **Step 5: Commit** `feat(bibliotecas): cadastro, trilha e permissoes no catalogo`.

### Task 5: Web — acesso à biblioteca e tela de cadastro

**Files:**

- Create: `src/Secco.Intranet.Web/Navigation/IPermissoesDeBiblioteca.cs`, `PermissoesDeBiblioteca.cs`, `AcessoABiblioteca.cs`, `SlugsReservadosDeBibliotecaDasRotas.cs`; `src/Secco.Intranet.Web/Controllers/BibliotecasController.cs`; `src/Secco.Intranet.Web/Models/Bibliotecas/BibliotecaFormViewModel.cs`; `src/Secco.Intranet.Web/Views/Bibliotecas/Index.cshtml`, `Criar.cshtml`, `Editar.cshtml`
- Modify: `src/Secco.Intranet.Web/Program.cs`, `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs` (item "Bibliotecas", `bi-journal-text`, `/bibliotecas`, logo depois de "Setores", na mesma condição dele)
- Test: `tests/Secco.Intranet.Tests/Unit/PermissoesDeBibliotecaTests.cs`, `tests/Secco.Intranet.Tests/Integration/BibliotecasCadastroTelaTests.cs` (novos); `SlugsReservadosTests.cs` (ampliar)

**Interfaces:**

- Consumes: Task 4 inteira.
- Produces:
  - `IPermissoesDeBiblioteca.SlugsComPermissaoAsync(ClaimsPrincipal usuario, string acao, IReadOnlyList<string> slugs, CancellationToken ct)` → `IReadOnlySet<string>`. `intranet-admin` recebe todos. Em `"read"`, vale ter `read` **ou** `write` da biblioteca.
  - `AcessoABiblioteca` (scoped): `Task<BibliotecaDto?> LeituraAsync(ClaimsPrincipal, string slug, ct)` e `Task<BibliotecaDto?> EscritaAsync(ClaimsPrincipal, string slug, ct)`, que devolvem nulo para inexistente, inativa ou sem permissão. Mais `Task<IReadOnlySet<ReferenciaDeDono>> DonosComLeituraAsync(ClaimsPrincipal, ct)`, com todas as bibliotecas **ativas** com leitura. No modo aberto de desenvolvimento, todas passam.
  - `SlugsReservadosDeBibliotecaDasRotas : ISlugsReservadosDeBiblioteca`: os segundos segmentos literais das rotas cujo primeiro segmento literal é `bibliotecas`, lidos uma vez e guardados, como o `SlugsReservadosDasRotas`.
  - Rotas do `BibliotecasController` (`[SomenteIntranetAdmin]`, `[Route("bibliotecas")]`): `GET ""` Index, `GET "nova"` e `POST "nova"` Criar, `GET "{id:guid}/editar"` e `POST "{id:guid}/editar"` Editar.

- [ ] **Step 1: Testes que falham:**
  - `PermissoesDeBibliotecaTests`: `So_write_conta_como_leitura`; `Admin_tem_todas`; `Read_nao_da_write`; `Setor_de_mesmo_slug_nao_conta` (`setor-ti:read` → vazio para a biblioteca `ti`).
  - `SlugsReservadosTests`: `bibliotecas_e_slug_reservado_para_setor`; `nova_e_slug_reservado_para_biblioteca`.
  - `BibliotecasCadastroTelaTests` (molde de `SetoresAutorizacaoTests` e `DocumentoOrganizarTelaTests`): `Nao_admin_recebe_403_em_bibliotecas` (o mesmo status que `/setores` dá hoje); `Admin_cria_e_ve_na_lista`, com a caixa "Todos os membros leem" marcada por padrão no GET; `Admin_edita_nome_e_desativa`; `Slug_reservado_volta_com_erro_no_formulario`; `Menu_de_administracao_mostra_Bibliotecas_so_para_admin`.
- [ ] **Step 2:** rode → falham.
- [ ] **Step 3:** implemente, com as views no molde de `Views/Setores` (`_PageHeader`, `_Card`, `_EmptyState` do contrato). No `Editar`, o slug aparece só para leitura. Depois de criar, a mensagem de retorno diz: `Biblioteca "{nome}" criada. Para definir quem publica nela, conceda "Alterar" ao perfil responsável em Acesso.`
- [ ] **Step 4:** os testes passam.
- [ ] **Step 5: Commit** `feat(bibliotecas): tela de cadastro do intranet-admin`.

### Task 6: Web — página da biblioteca e ações de documento

**Files:**

- Create: `src/Secco.Intranet.Web/Controllers/BibliotecaController.cs`, `src/Secco.Intranet.Web/Models/Documentos/DonoDaPaginaDeDocumentos.cs`
- Move: `src/Secco.Intranet.Web/Views/Setor/{Documentos,OrganizarDocumentos,EditarDocumento}.cshtml` → `src/Secco.Intranet.Web/Views/Shared/`
- Modify: os três view models de `Models/Documentos/` (`SetorDto Setor` vira `DonoDaPaginaDeDocumentos Dono`), as três views, `SetorController.cs` e `DocumentosController.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/BibliotecaPaginaTests.cs` (novo)

**Interfaces:**

- Consumes: `AcessoABiblioteca` (Task 5), os comandos com `ReferenciaDeDono` (Task 3).
- Produces:
  - `public sealed record DonoDaPaginaDeDocumentos(string Nome, string Slug, string? Icone, bool MostraVisibilidade)`. O setor passa `MostraVisibilidade: true` e a biblioteca, `false`. Com `false`, as views escondem o seletor "Quem pode ver".
  - `BibliotecaController`, `[Route("bibliotecas/{slug}")]`, com **os mesmos nomes de action** do `SetorController`, para os `asp-action` das views compartilhadas resolverem no controller corrente: `GET ""` (página), `POST "documentos"` Publicar, `GET "documentos/organizar"` Organizar, `GET`/`POST "documentos/{id:guid}/editar"` EditarDocumento, `POST "documentos/{id:guid}/mover"` MoverDocumento, `POST "documentos/grupos"` CriarGrupo, `POST "documentos/{id:guid}/arquivar"` Arquivar.
  - `DocumentosController.Download` monta `DonosComLeitura` com os setores de hoje unidos a `AcessoABiblioteca.DonosComLeituraAsync`.

- [ ] **Step 1: Testes que falham** (`BibliotecaPaginaTests`, com `PermissionResolverDeTeste` dando as permissões por Role):
  - `Sem_leitura_inexistente_e_inativa_dao_o_mesmo_404` (três GETs, mesmo status e mesmo corpo);
  - `Leitor_ve_a_lista_sem_acoes_de_escrita` (sem "Organizar", sem formulário de publicar);
  - `Escritor_publica_cria_grupo_organiza_edita_e_arquiva` (um fluxo, como o `DocumentoFluxoTests`);
  - `Formulario_da_biblioteca_nao_tem_quem_pode_ver` e `Lista_da_biblioteca_nao_tem_badge`;
  - `Leitor_baixa_documento_e_quem_le_so_o_setor_de_mesmo_slug_recebe_404`;
  - `POST_forjado_sem_write_e_404` para publicar, mover e arquivar;
  - `Documentos_do_setor_continuam_iguais`: os testes atuais `DocumentoFluxoTests`, `DocumentoOrganizarTelaTests` e `DocumentoEdicaoTelaTests` passam sem mudança de asserção.
- [ ] **Step 2:** rode → falham.
- [ ] **Step 3:** implemente. O trilho da página da biblioteca é só o nome dela. O `BibliotecaController` só orquestra os handlers (ADR-0002, regra 1), como o `SetorController`.
- [ ] **Step 4:** a suíte inteira passa.
- [ ] **Step 5: Commit** `feat(bibliotecas): pagina da biblioteca com as acoes de documentos`.

### Task 7: Menu principal

**Files:**

- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs`, `CatalogoDeItensDoMenuPrincipal.cs`, `src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs`, `src/Secco.Intranet.Web/Controllers/MenuController.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/CatalogoDeItensDoMenuPrincipalTests.cs`, `NavegacaoETemaTests.cs` (ampliar); `tests/Secco.Intranet.Tests/Integration/MenuBibliotecasTests.cs` (novo)

**Interfaces:**

- Produces:
  - `CatalogoDeItensDoMenuPrincipal.PrefixoDeBiblioteca = "biblioteca-"` e `static string ChaveDeBiblioteca(string slug)`.
  - `PrincipalNaOrdemPadrao(bool incluirDiretorio, IEnumerable<ItemExtraDoMenu> extras, IEnumerable<BibliotecaDto>? bibliotecas = null)`: Mural, Diretório, bibliotecas (por nome, `OrdinalIgnoreCase`) e por fim os extras.
  - `NavigationRequest` ganha `IReadOnlyList<BibliotecaDto>? Bibliotecas = null`. O item da biblioteca fica ativo quando o caminho começa por `/bibliotecas/{slug}`.

- [ ] **Step 1: Testes que falham:**
  - `Extra_com_prefixo_biblioteca_fica_fora_com_aviso` (chave `biblioteca-x` → fora do menu, mensagem "o prefixo 'biblioteca-' é reservado às bibliotecas");
  - `Bibliotecas_entram_depois_do_diretorio_e_antes_dos_extras`;
  - `MenuBibliotecasTests`: `Item_aparece_so_para_quem_le`; `Biblioteca_inativa_nao_aparece`; `Admin_ordena_a_biblioteca_em_menu_e_a_posicao_vale_no_menu` (usa a tela `/menu` como o `MenuOrdemAdministracaoTests`).
- [ ] **Step 2:** rode → falham.
- [ ] **Step 3:** implemente. O `NavigationViewComponent` lista as ativas e filtra por `IPermissoesDeBiblioteca` com `"read"`, ou por nada no modo aberto. O `MenuController` passa todas as ativas.
- [ ] **Step 4:** os testes passam.
- [ ] **Step 5: Commit** `feat(bibliotecas): biblioteca como item do menu principal`.

### Task 8: `/acesso` — seção "Bibliotecas" em todo perfil

**Files:**

- Modify: `src/Secco.Intranet.Web/Models/Acesso/AcessoViewModels.cs` (`PerfilViewModel(PerfilTelaDto Tela, IReadOnlyList<BibliotecaDto> Bibliotecas)`), `src/Secco.Intranet.Web/Controllers/AcessoController.cs` (ação `Perfil` lista as ativas), `src/Secco.Intranet.Web/Views/Acesso/Perfil.cshtml`
- Test: `tests/Secco.Intranet.Tests/Integration/AcessoTelasTests.cs` (ampliar)

- [ ] **Step 1: Testes que falham:**
  - `Perfil_comum_e_todos_os_membros_mostram_ler_e_alterar_de_cada_biblioteca_ativa` (`value="biblioteca-manuais:read"` e `value="biblioteca-manuais:write"`, com o rótulo "Alterar (inclui ler)");
  - `Biblioteca_inativa_nao_entra_na_secao`;
  - `Permissao_de_biblioteca_inativa_continua_como_extra_e_aparece_uma_vez`;
  - `Perfil_reservado_nao_mostra_a_secao`.
- [ ] **Step 2:** rode → falham.
- [ ] **Step 3:** na view, a seção "Bibliotecas" fica dentro do mesmo `<form>`, com checkboxes `name="permissoes"`, uma linha por biblioteca e duas caixas. O laço de "extras" passa a excluir as permissões já mostradas na seção.
- [ ] **Step 4:** os testes passam.
- [ ] **Step 5: Commit** `feat(acesso): permissoes de biblioteca em todo perfil`.

### Task 9: Documentação

**Files:**

- Modify: `docs/adr/secco-intranet-adrs.md` (ADR-0012), `docs/specs/2026-09-07-colecoes-documentos-design.md`, `docs/specs/2026-11-02-bibliotecas-de-documentos-design.md`, `docs/extensao.md`, `docs/guia-de-deploy.md`, `docs/roadmap.md`, `.claude/skills/secco-intranet-setores/SKILL.md`

- [ ] **Step 1:** ADR-0012, "Bibliotecas: documentos sem setor dono", no formato das vizinhas (Contexto, Decisão, Consequências). Registra: o dono XOR; a permissão por biblioteca nos perfis existentes, sem perfil próprio, e por que isso difere da ADR-0001; o "todos leem" pelo `tenant-member` (ADR-0056 da plataforma); a visibilidade gravada como `Setor`, a favor do fechamento; e que este desenho substitui o de Coleções.
- [ ] **Step 2:** na spec de Coleções, `**Estado:** substituída pela ADR-0012 e pela [spec das bibliotecas](2026-11-02-bibliotecas-de-documentos-design.md)`. Na spec desta parte, `**Estado:** aprovado, implementado (parte 3)`.
- [ ] **Step 3:** `extensao.md` com o prefixo `biblioteca-` entre as chaves reservadas. No `guia-de-deploy.md`, onde as famílias de setor aparecem, acrescente as de biblioteca. No `roadmap.md`, o item da issue #4 passa a citar a parte 3 entregue, com os links da spec e deste plano, e perde o "falta a parte 3". Na skill `secco-intranet-setores`, a seção "Recursos sem setor dono" ganha a biblioteca como dono alternativo com permissão por instância, em contraste com o Inventário.
- [ ] **Step 4:** `dotnet build Secco.Intranet.slnx` e a suíte completa passam uma última vez.
- [ ] **Step 5: Commit** `docs(bibliotecas): adr-0012, roadmap e guias (issue #4, parte 3)`.
