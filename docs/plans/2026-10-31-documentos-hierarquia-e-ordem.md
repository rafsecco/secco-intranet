# Documentos com hierarquia e ordem manual — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O Documentos do setor ganha grupos, dois níveis de hierarquia e ordem manual, e a lista passa a ser o parcial `_ListaDeDocumentos` do contrato de tema.

**Architecture:** Três campos novos no `Documento` (`Tipo`, `ParentId`, `Ordem`), regras de hierarquia nos casos de uso da Application (que enxergam os irmãos), movimentos que renumeram os irmãos de `0` a `n-1`. A página de leitura monta a árvore pronta para o parcial do tema; quem escreve organiza numa tela do core, no molde da `/menu`.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core (SQL Server e Postgres), Sass dos temas, xUnit + AwesomeAssertions, `Secco.SDK.Testing` (`SeccoApiFactory`).

**Spec:** [`docs/specs/2026-10-31-documentos-hierarquia-e-ordem-design.md`](../specs/2026-10-31-documentos-hierarquia-e-ordem-design.md). Ler inteira antes da primeira task.

## Global Constraints

- `TipoDeDocumento`: `Arquivo = 1`, `Grupo = 2`. Colunas pela convention (ADR-0017): `ie_tipo`, `id_fk_documento` (FK para a própria tabela, `Restrict`), `nr_ordem`.
- Propriedade do pai: **`ParentId`** (mesmo nome do `ItemMenu`, o vizinho com a mesma forma). A spec chama de `PaiId`; o plano alinha ao código.
- Grupo: `NomeArquivo`, `ContentType`, `CaminhoRelativo`, `ChaveEmbrulhada` e `Tamanho` nulos; grava `Visibilidade.Setor`. Arquivo: todos esses obrigatórios, como hoje.
- Hierarquia: pai é item **ativo**, da **raiz**, do **mesmo setor**; grupo nunca é filho; não existe neto.
- Ordem: item novo na raiz entra em `0` e os demais descem; filho novo entra no fim dos filhos; toda gravação renumera os irmãos ativos afetados de `0` a `n-1`.
- Mensagem de arquivar com filhos: **"Este item tem documentos abaixo dele. Mova ou arquive esses documentos primeiro."**
- Auditoria, recurso `documento`: `documento.grupo-criar` (`{ setor, titulo }`), `documento.mover` (`{ setor, documento, acao, paiDe, paiPara, de, para }`, `acao` em `subir|descer|recuar|desrecuar`); `documento.publicar` ganha `pai` (nulo na raiz).
- Tema: parcial `_ListaDeDocumentos` sem botões; `Vertical` e `Horizontal`, claro e escuro (skill `secco-intranet-temas`). A tela de organizar usa só `_PageHeader`, `_Card`, `_EmptyState`.
- Banco: skill `secco-db-naming`; migration `DocumentosHierarquia` nos dois engines, mesmo nome.
- Sem instalação em uso: nada de migração de dados nem nota de upgrade.
- Código: tabs de 4 em `.cs`/`.cshtml`, doc XML em português em todo tipo público, `ConfigureAwait(false)` como nos vizinhos. Commits direto na `main`.

## Review Focus

1. **Pai arquivado entre abrir o formulário e enviar** → publicar recusa com erro de validação e **não grava o arquivo** no armazenamento. Teste na Task 2 (`Publicar_PaiArquivado_RecusaSemGravarArquivo`).
2. **POST forjado que tenta recuar um grupo, criar neto ou recuar o primeiro da raiz** → recusa, nada muda no banco. Testes na Task 3 (`Recuar_Grupo_Recusa`, `Recuar_ItemComFilhos_Recusa`, `Recuar_PrimeiroDaRaiz_Recusa`).
3. **Mover um documento de outro setor pela rota deste setor** → mesmo resultado de não encontrado. Teste na Task 3 (`Mover_DocumentoDeOutroSetor_NaoEncontrado`).
4. **Arquivar, pelo botão da página, um item que tem anexos** → volta para a página com a mensagem da recusa, não 404. Teste na Task 5 (`Arquivar_ItemComFilhos_MostraMensagem`).
5. **Grupo vazio** → some da página de leitura e aparece no Organizar. Testes na Task 4 (`Pagina_GrupoVazio_NaoAparece`) e Task 5 (`Organizar_GrupoVazio_Aparece`).

---

### Task 1: Documento com tipo, pai e ordem

**Files:**
- Modify: `src/Secco.Intranet.Domain/Documentos/Documento.cs` (enum `TipoDeDocumento` no mesmo arquivo, como `TipoDeItemMenu` em `ItemMenu.cs`)
- Modify: `src/Secco.Intranet.Infrastructure/Mappings/DocumentoConfiguration.cs`
- Modify: `src/Secco.Intranet.Application/Documentos/IDocumentoRepository.cs`, `src/Secco.Intranet.Infrastructure/Repositories/DocumentoRepository.cs`
- Modify: `src/Secco.Intranet.Application/Documentos/DocumentoDto.cs` e quem constrói `DocumentoDto`
- Modify: `src/Secco.Intranet.Application/Documentos/BaixarDocumentoHandler.cs` (grupo → `NotFound`)
- Create: migration `DocumentosHierarquia` nos dois projetos de migration
- Test: `tests/Secco.Intranet.Tests/Unit/DocumentoHierarquiaTests.cs`, `tests/Secco.Intranet.Tests/Unit/BaixarDocumentoHandlerTests.cs`, `tests/Secco.Intranet.Tests/Integration/DocumentoRepositoryHierarquiaTests.cs`

**Interfaces:**
- Produces:
  - `enum TipoDeDocumento { Arquivo = 1, Grupo = 2 }`
  - `Documento`: construtor atual continua sendo o do **arquivo** (ganha `Guid? parentId = null, int ordem = 0` no fim); `static Documento CriarGrupo(Guid setorId, string titulo, string? descricao, string criadoPor)`; propriedades `Tipo`, `ParentId`, `Ordem`; campos de arquivo passam a `string?`/`long?`; `void Posicionar(Guid? parentId, int ordem)`; `bool EhGrupo => Tipo == TipoDeDocumento.Grupo`.
  - `DocumentoDto`: ganha `TipoDeDocumento Tipo`, `Guid? ParentId`, `int Ordem`; `NomeArquivo`, `ContentType` viram `string?` e `Tamanho` vira `long?`.
  - `IDocumentoRepository`:
    - `ListarPorSetorAsync` passa a ordenar por `Ordem` (filhos e raiz na mesma lista plana; quem monta a árvore é a Task 2);
    - `Task<IReadOnlyList<Documento>> ListarIrmaosAsync(Guid setorId, Guid? parentId, CancellationToken cancellationToken = default)`: ativos, rastreados, por `Ordem`;
    - `Task<bool> TemFilhosAtivosAsync(Guid documentoId, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Testes do domínio.** Em `DocumentoHierarquiaTests`:
  - `CriarGrupo_SemArquivo_TipoGrupoEVisibilidadeSetor`: `Tipo == Grupo`, campos de arquivo nulos, `Visibilidade == Setor`, `Ativo`.
  - `CriarGrupo_TituloVazio_Lanca` (`DomainInvariantException`).
  - `Arquivo_ContinuaExigindoArquivo`: o construtor de arquivo com `caminhoRelativo` vazio ainda lança.
  - `Posicionar_TrocaPaiEOrdem`.
  - Em `BaixarDocumentoHandlerTests`, `Baixar_Grupo_NaoEncontrado`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --filter "FullyQualifiedName~DocumentoHierarquiaTests|FullyQualifiedName~BaixarDocumentoHandlerTests"` → falha de compilação.
- [ ] **Step 3: Implementar o domínio, o DTO e o download.** O mapeamento declara a FK para a própria tabela como o `ItemMenuConfiguration` faz com `ParentId` (`HasOne<Documento>().WithMany().HasForeignKey(d => d.ParentId).OnDelete(Restrict).IsRequired(false)`) e troca o índice `SetorId` por `new { SetorId, ParentId, Ordem }`. Ajustar quem lia os campos de arquivo como não nulos (download, views) sem mudar comportamento para arquivo.
- [ ] **Step 4: Migration.**

```bash
dotnet ef migrations add DocumentosHierarquia --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add DocumentosHierarquia --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```

Conferir no arquivo gerado: colunas `ie_tipo`, `id_fk_documento`, `nr_ordem`; FK e índice com nomes no padrão da skill `secco-db-naming` (`fk_`, `idx_`); colunas de arquivo `nullable`.
- [ ] **Step 5: Teste de repositório.** `DocumentoRepositoryHierarquiaTests` (integração, banco do tenant Alfa):
  - `ListarIrmaos_SoAtivosDoPaiNaOrdem`: três filhos de um pai, um arquivado, um de outro pai → volta os dois ativos daquele pai por `Ordem`.
  - `TemFilhosAtivos_IgnoraArquivados`.
  - `ListarPorSetor_OrdenaPorOrdem`.
- [ ] **Step 6: Rodar e ver passar.** Filtro das classes acima → PASS. `dotnet ef migrations has-pending-model-changes` nos dois projetos → sem mudanças. `dotnet test` inteiro → nenhum teste antigo quebra.
- [ ] **Step 7: Commit** `feat(documentos): tipo, pai e ordem no documento`.

---

### Task 2: Árvore, publicar abaixo de um item e criar grupo

**Files:**
- Modify: `src/Secco.Intranet.Application/Documentos/ListarDocumentosHandler.cs`
- Modify: `src/Secco.Intranet.Application/Documentos/PublicarDocumentoHandler.cs`
- Create: `src/Secco.Intranet.Application/Documentos/CriarGrupoDeDocumentosHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs`, `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`, `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Create: `tests/Secco.Intranet.Tests/Support/DocumentoRepositorioEmMemoria.cs` (implementa `IDocumentoRepository` sobre uma lista, com as mesmas regras de ordem e filtro do repositório real)
- Test: `tests/Secco.Intranet.Tests/Unit/DocumentosHierarquiaHandlersTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `sealed record NoDeDocumentoDto(DocumentoDto Documento, IReadOnlyList<DocumentoDto> Filhos)`; `ListarDocumentosHandler.HandleAsync` passa a devolver `Result<IReadOnlyList<NoDeDocumentoDto>>` (raiz por `Ordem`, filhos por `Ordem`; filho cujo pai não está ativo é descartado). Grupos vazios **ficam** no resultado: quem os esconde é a página de leitura.
  - `PublicarDocumentoCommand` ganha `Guid? ParentId = null` no fim.
  - `sealed record CriarGrupoDeDocumentosCommand(string? SetorSlug, string? Titulo, string? Descricao, string CriadoPor)` e `CriarGrupoDeDocumentosHandler.HandleAsync(...) : Task<Result<DocumentoDto>>`.
  - `IntranetErrors.Documentos.ParentInvalido` (validação, "O item escolhido em \"Abaixo de\" não está mais disponível. Escolha outro.").
  - `VerbosDeAuditoria.DocumentoGrupoCriar = "documento.grupo-criar"`.

- [ ] **Step 1: Testes.** Em `DocumentosHierarquiaHandlersTests`, com `DocumentoRepositorioEmMemoria` e store falso que conta gravações:
  - `Publicar_NaRaiz_EntraNoTopo`: dois existentes com `Ordem` 0 e 1 → novo fica 0, os outros 1 e 2.
  - `Publicar_AbaixoDeItem_EntraNoFimDosFilhos`.
  - `Publicar_AbaixoDeGrupo_Aceita`.
  - `Publicar_PaiArquivado_RecusaSemGravarArquivo`: erro `ParentInvalido`, store com zero gravações.
  - `Publicar_PaiQueEFilho_Recusa` (seria neto).
  - `Publicar_PaiDeOutroSetor_Recusa`.
  - `Publicar_AuditoriaLevaPai`: payload com `pai` igual ao id do pai; na raiz, `pai` nulo.
  - `CriarGrupo_EntraNoTopoDaRaizEAudita`: `documento.grupo-criar` com `{ setor, titulo }`.
  - `CriarGrupo_TituloVazioOuLongo_Recusa` (mesmos erros e limite do título de documento).
  - `Listar_MontaArvore_RaizEFilhosNaOrdem` e `Listar_GrupoVazio_ContinuaNoResultado`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --filter "FullyQualifiedName~DocumentosHierarquiaHandlersTests"` → falha de compilação.
- [ ] **Step 3: Implementar.** A validação do pai acontece **antes** de `store.GravarAsync`. A renumeração usa `ListarIrmaosAsync` + `Posicionar` e um único `SaveChangesAsync`. Ajustar os testes antigos de publicar e o `SetorController` para o novo retorno do `ListarDocumentosHandler` (a página continua igual nesta task: lista plana dos itens da raiz e filhos, na ordem).
- [ ] **Step 4: Rodar e ver passar.** Filtro acima → PASS; `dotnet test` inteiro → PASS.
- [ ] **Step 5: Commit** `feat(documentos): arvore, publicar abaixo de um item e grupo`.

---

### Task 3: Movimentos e arquivar com filhos

**Files:**
- Create: `src/Secco.Intranet.Application/Documentos/MoverDocumentoHandler.cs`
- Modify: `src/Secco.Intranet.Application/Documentos/ArquivarDocumentoHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs`, `VerbosDeAuditoria.cs`, `IntranetApplicationExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/MoverDocumentoHandlerTests.cs`, `tests/Secco.Intranet.Tests/Unit/AuditoriaDeDocumentosTests.cs`

**Interfaces:**
- Consumes: Tasks 1 e 2 (`DocumentoRepositorioEmMemoria`).
- Produces:
  - `enum MovimentoDeDocumento { Subir = 1, Descer = 2, Recuar = 3, Desrecuar = 4 }`
  - `sealed record MoverDocumentoCommand(string SetorSlug, Guid DocumentoId, MovimentoDeDocumento Acao)`; `MoverDocumentoHandler.HandleAsync(...) : Task<Result>`. A permissão de escrita é conferida pelo controller, antes; o handler só garante que o documento é do setor da rota.
  - `IntranetErrors.Documentos.MovimentoInvalido` (validação, "Não foi possível mover este item.") e `IntranetErrors.Documentos.ItemComFilhos` (validação, texto das Global Constraints).
  - `VerbosDeAuditoria.DocumentoMover = "documento.mover"`.

- [ ] **Step 1: Testes.** Em `MoverDocumentoHandlerTests`:
  - `Subir_TrocaComIrmaoDeCima` e `Descer_TrocaComIrmaoDeBaixo`.
  - `Subir_Primeiro_Recusa`, `Descer_Ultimo_Recusa` (`MovimentoInvalido`, nada muda).
  - `Subir_FilhoPrimeiro_NaoSaiDoPai`: recusa, continua filho.
  - `Recuar_ViraUltimoFilhoDoIrmaoDeCima`, e a raiz é renumerada sem buraco.
  - `Recuar_Grupo_Recusa`, `Recuar_ItemComFilhos_Recusa`, `Recuar_PrimeiroDaRaiz_Recusa`, `Recuar_JaEFilho_Recusa`.
  - `Desrecuar_VaiParaLogoDepoisDoPai`, e os antigos irmãos são renumerados.
  - `Desrecuar_NaRaiz_Recusa`.
  - `Mover_DocumentoDeOutroSetor_NaoEncontrado` e `Mover_Arquivado_NaoEncontrado` (`Documentos.NotFound`).
  - `Mover_Audita`: `documento.mover` com `{ setor, documento, acao, paiDe, paiPara, de, para }`, `acao` em minúsculas.
  - Em `AuditoriaDeDocumentosTests` (ou num arquivo novo do mesmo molde): `Arquivar_ItemComFilhosAtivos_Recusa` (`ItemComFilhos`, sem auditar), `Arquivar_GrupoVazio_Arquiva`, `Arquivar_RenumeraIrmaos`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --filter "FullyQualifiedName~MoverDocumentoHandlerTests|FullyQualifiedName~AuditoriaDeDocumentosTests"` → falha de compilação.
- [ ] **Step 3: Implementar.** Cada movimento lê os irmãos com `ListarIrmaosAsync`, reposiciona com `Posicionar` e grava num único `SaveChangesAsync`. Recuar e desrecuar renumeram os dois conjuntos de irmãos (o de origem e o de destino).
- [ ] **Step 4: Rodar e ver passar.** Filtro acima → PASS; `dotnet test` inteiro → PASS.
- [ ] **Step 5: Commit** `feat(documentos): mover itens e recusar arquivar com filhos`.

---

### Task 4: Parcial `_ListaDeDocumentos` e a página de leitura

**Files:**
- Create: `src/Secco.Intranet.Web.Theming/Contracts/ListaDeDocumentosModel.cs` (os dois records da seção 6 da spec, com doc XML no molde do `OrganogramaModel`)
- Create: `src/Secco.Intranet.Themes.Vertical/Themes/Vertical/Views/Shared/_ListaDeDocumentos.cshtml` e o equivalente em `Themes.Horizontal`
- Modify: `src/Secco.Intranet.Themes.Vertical/wwwroot/scss/_components.scss`, o equivalente no Horizontal, e o CSS compilado (`npm --prefix <tema> run build`)
- Create: `src/Secco.Intranet.Web/Models/Documentos/ListaDeDocumentosFactory.cs`
- Modify: `src/Secco.Intranet.Application/Documentos/TiposDeArquivo.cs` (`Etiqueta`)
- Modify: `src/Secco.Intranet.Web/Models/Documentos/SetorDocumentosViewModel.cs`, `src/Secco.Intranet.Web/Views/Setor/Documentos.cshtml`, `src/Secco.Intranet.Web/Controllers/SetorController.cs` (`Publicar` repassa `ParentId`)
- Test: `tests/Secco.Intranet.Tests/Unit/ListaDeDocumentosFactoryTests.cs`, `tests/Secco.Intranet.Tests/Integration/DocumentoHierarquiaTelaTests.cs`

**Interfaces:**
- Consumes: Task 2 (`NoDeDocumentoDto`, `PublicarDocumentoCommand.ParentId`).
- Produces:
  - `ListaDeDocumentosModel`, `ItemDaListaDeDocumentosModel` (spec, seção 6).
  - `static ListaDeDocumentosModel ListaDeDocumentosFactory.Montar(IReadOnlyList<NoDeDocumentoDto> nos, Func<Guid, string> urlDeDownload)`: grupo sem filhos fica de fora; `Detalhe` = `"{tamanho} · {dd/MM/yyyy}"` no formato que a view usa hoje; `Badge` como a view monta hoje.
  - `static string? TiposDeArquivo.Etiqueta(string? nomeArquivo)`: extensão sem ponto, em maiúsculas (`"relatorio.pdf"` → `"PDF"`); nulo sem extensão.
  - `DocumentoFormViewModel.ParentId` (`Guid?`); `SetorDocumentosViewModel` ganha a lista montada e as opções de "Abaixo de" (itens ativos da raiz, documentos e grupos, inclusive grupo vazio).

- [ ] **Step 1: Testes.**
  - `ListaDeDocumentosFactoryTests`: `Montar_GrupoVazio_FicaDeFora`, `Montar_Grupo_SemUrlIconeNemBadge`, `Montar_Filhos_NaOrdem`, `Montar_Arquivo_DetalheEEtiqueta`.
  - `TiposDeArquivo`: `Etiqueta_ExtensaoEmMaiusculas`, `Etiqueta_SemExtensao_Nulo`.
  - `DocumentoHierarquiaTelaTests` (integração, nos dois temas como o `MenuItemExtraTests` faz com `Intranet:Theme:Nome`):
    - `Pagina_MostraFilhoRecuadoAbaixoDoPai` (o filho aparece depois do pai, dentro do marcador de filhos do parcial);
    - `Pagina_GrupoVazio_NaoAparece`;
    - `Publicar_AbaixoDe_ViraFilho`;
    - `Pagina_SemEscrita_NaoMostraAbaixoDe`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --filter "FullyQualifiedName~ListaDeDocumentosFactoryTests|FullyQualifiedName~DocumentoHierarquiaTelaTests"` → falha de compilação.
- [ ] **Step 3: Implementar.** A view troca a `<ul>` atual por `<partial name="_ListaDeDocumentos" model="..." />`; o `_EmptyState` continua fora do parcial. A ação "Organizar" no cabeçalho entra na Task 5, junto com a tela. O parcial padrão dos dois temas: ícone pelo tipo, etiqueta do tipo, grupo como subtítulo, filhos recuados; tokens do tema para cor, nada fixo, claro e escuro.
- [ ] **Step 4: Rodar e ver passar.** Filtro acima → PASS; `dotnet test` inteiro → PASS.
- [ ] **Step 5: Commit** `feat(documentos): parcial _ListaDeDocumentos e publicar abaixo de um item`.

---

### Task 5: Tela de organizar

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/SetorController.cs`
- Create: `src/Secco.Intranet.Web/Models/Documentos/OrganizarDocumentosViewModel.cs`
- Create: `src/Secco.Intranet.Web/Views/Setor/OrganizarDocumentos.cshtml`
- Modify: `src/Secco.Intranet.Web/Views/Setor/Documentos.cshtml` (ação "Organizar" no cabeçalho, só para quem escreve)
- Test: `tests/Secco.Intranet.Tests/Integration/DocumentoOrganizarTelaTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 3 e 4.
- Produces (rotas dentro do `[Route("{slug:setor}")]` do `SetorController`; o segmento literal `documentos` vence o `{**caminho}`):
  - `GET documentos/organizar` → `Organizar`;
  - `POST documentos/{id:guid}/mover` (form: `acao`) → `MoverDocumento`;
  - `POST documentos/grupos` (form: `Titulo`, `Descricao`) → `CriarGrupo`;
  - `POST documentos/{id:guid}/arquivar` ganha o campo opcional `origem` (`organizar` volta para a tela de organizar; ausente volta para a página).
  - Todas exigem escrita no setor e o item Documentos ativo: sem isso, `NotFound()`, como o `Publicar`.
  - Recusa de movimento e `ItemComFilhos` → `TempData[FeedbackViewComponent.ChaveDaMensagemDeErro]` com a descrição do erro e redirect; `NotFound` do handler → `NotFound()`.
  - `OrganizarDocumentosViewModel`: setor, título e trilho da página, e as linhas na ordem da árvore, cada uma com `Id`, `Titulo`, `EhGrupo`, `EhFilho`, `PodeSubir`, `PodeDescer`, `PodeRecuar`, `PodeDesrecuar`, `PodeArquivar` (= sem filhos ativos), calculados pelas mesmas regras da Task 3.

- [ ] **Step 1: Testes** em `DocumentoOrganizarTelaTests` (integração, cliente com escrita no setor de teste):
  - `Organizar_SemEscrita_NaoEncontrado` (GET e os três POSTs).
  - `Pagina_SemEscrita_NaoMostraOrganizar` e `Pagina_ComEscrita_MostraOrganizar`.
  - `Organizar_GrupoVazio_Aparece`.
  - `Organizar_BotoesDesabilitados`: primeiro sem subir, último sem descer, grupo sem recuar, item com filhos sem arquivar.
  - `CriarGrupo_EntraNoTopo`.
  - `Mover_Recuar_EAPaginaReflete`: recua, a página de leitura mostra o item como filho.
  - `Mover_Invalido_VoltaComMensagem`: POST forjado de recuar um grupo → redirect para organizar, mensagem de erro, nada muda.
  - `Arquivar_ItemComFilhos_MostraMensagem`: pelo formulário da página (sem `origem`) → volta para a página com a mensagem; com `origem=organizar` → volta para organizar.
  - `Mover_SemAntiforgery_Recusa`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --filter "FullyQualifiedName~DocumentoOrganizarTelaTests"` → falha.
- [ ] **Step 3: Implementar.** A tela segue a `Views/Menu/Index.cshtml`: `_PageHeader` com "Voltar" para a página, um `_Card` com as linhas (grupo destacado, filho recuado) e os botões por linha, outro `_Card` com o formulário "Novo grupo". `_EmptyState` quando não há item.
- [ ] **Step 4: Rodar e ver passar.** Filtro acima → PASS; `dotnet test` inteiro → PASS.
- [ ] **Step 5: Conferência visual.** Subir o app em DEV, publicar um documento, um anexo e um grupo, e conferir a página e o Organizar nos temas `Vertical` e `Horizontal`, claro e escuro.
- [ ] **Step 6: Commit** `feat(documentos): tela de organizar`.

---

### Task 6: Documentação

**Files:**
- Modify: `docs/temas.md` (linha de `_ListaDeDocumentos` na tabela do contrato, com o model)
- Modify: `docs/roadmap.md` (item entregue, apontando para a parte 1 da issue #4)
- Modify: `docs/specs/2026-10-31-documentos-hierarquia-e-ordem-design.md` (estado "aprovado, implementado"; `PaiId` → `ParentId` na tabela da seção 1)

- [ ] **Step 1: Escrever** as três mudanças.
- [ ] **Step 2: Conferir** os links relativos (abrir os arquivos e seguir cada link novo).
- [ ] **Step 3: Commit** `docs(documentos): contrato, roadmap e spec da hierarquia` (sem `Closes #4`: a issue tem mais duas partes).
