# Editar documento e substituir arquivo — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** quem escreve no setor edita um documento no ar (título, descrição, visibilidade e arquivo) ou um grupo (título e descrição), preservando posição e filhos, e o arquivo substituído é apagado do armazenamento.

**Architecture:** a entidade `Documento` ganha `Editar` e `TrocarArquivo`; um handler de edição segue a ordem validar → gravar novo → salvar → trilha → apagar antigo (mesmo padrão de `FotoDoColaboradorHandler` e `ImagemDaPublicacao`); um handler de leitura alimenta o formulário. O contrato de tema estende `data-confirmar` ao campo de arquivo, para avisar antes da substituição. A Web ganha `GET`/`POST /{slug}/documentos/{id}/editar` e o botão "Editar" em "Organizar documentos".

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core (sem migration), xUnit + AwesomeAssertions, Testcontainers.

**Spec:** [docs/specs/2026-11-01-documentos-editar-e-substituir-arquivo-design.md](../specs/2026-11-01-documentos-editar-e-substituir-arquivo-design.md)

## Global Constraints

- Sem migration: as colunas de arquivo já existem e aceitam nulo.
- Erros de negócio por `Result`/`IntranetErrors` (ADR-0004); nunca exceção para fluxo.
- Fora do alcance, outro setor, arquivado, inexistente e recurso Documentos desligado: **404**, indistinto (ADR-0005).
- Tipo do arquivo pela extensão cruzada com os primeiros bytes (`TiposDeArquivo`), nunca pelo `Content-Type` do cliente; limite `DocumentoOptions.TamanhoMaximoBytes`; extensões `DocumentoOptions.ExtensoesPermitidas`.
- Título até `IntranetOptions.MaxNameLength`; descrição até 4.096 caracteres (coluna `ds_descricao`).
- Log de falha ao apagar: só o tipo da exceção, nunca o caminho.
- Trilha: verbo `documento.editar`, recurso `documento`, sem conteúdo do arquivo.
- Indentação com tabs em `.cs`/`.cshtml`; commits em Conventional Commits, português sem acento, com `Co-Authored-By`.

## Review Focus

1. **Formulário enviado sem escolher arquivo** chega como `IFormFile` nulo ou de tamanho 0: precisa manter o arquivo atual, nunca recusar nem zerar. Teste na Task 3.
2. **Nome de arquivo com caminho** (`C:\fakepath\politica.pdf`, `../politica.pdf`): grava só o nome, com `Path.GetFileName`, como a publicação. Teste na Task 2.
3. **Grupo recebendo arquivo ou visibilidade** por um POST montado à mão: arquivo é recusado (`ArquivoEmGrupo`), visibilidade é ignorada e o grupo segue `Setor`. Teste na Task 2.
4. **Descrição acima de 4.096 caracteres** vinda direto ao handler: erro de validação, não exceção do banco. Teste na Task 2.
5. **Documento de outro setor pelo slug da URL** (`/rh/documentos/{id-do-financeiro}/editar`) com escrita nos dois setores: 404. Teste na Task 2.

---

### Task 1: Entidade — editar metadados e trocar o arquivo

**Files:**

- Modify: `src/Secco.Intranet.Domain/Documentos/Documento.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/DocumentoEdicaoTests.cs` (novo)

**Interfaces:**

- Produces:
  - `void Editar(string titulo, string? descricao)` — título obrigatório (trim), descrição vazia vira `null` (trim). Vale para arquivo e grupo.
  - `string TrocarArquivo(string nomeArquivo, string contentType, long tamanho, string caminhoRelativo, string chaveEmbrulhada)` — troca os cinco campos juntos e devolve o `CaminhoRelativo` anterior. Mesmas guardas do construtor de arquivo. Lança `DomainInvariantException` em grupo.
  - `AlterarVisibilidade` já existe e passa a lançar `DomainInvariantException` em grupo com visibilidade diferente de `Setor`.

- [ ] **Step 1: Testes que falham** em `DocumentoEdicaoTests`:
  - `Editar_TrocaTituloEDescricao_ComTrim` — `"  Novo  "`, `"  desc  "` → `"Novo"`, `"desc"`; `ParentId`, `Ordem`, `CriadoPor`, `CreatedAt` iguais aos de antes.
  - `Editar_DescricaoEmBranco_ViraNula`.
  - `Editar_TituloVazio_Lanca` (`DomainInvariantException`).
  - `TrocarArquivo_TrocaOsCincoCampos_EDevolveOCaminhoAnterior`.
  - `TrocarArquivo_TamanhoZeroOuCaminhoVazio_Lanca`.
  - `TrocarArquivo_EmGrupo_Lanca`.
  - `AlterarVisibilidade_GrupoParaEmpresa_Lanca` e `AlterarVisibilidade_GrupoParaSetor_NaoLanca`.
- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter FullyQualifiedName~DocumentoEdicaoTests` → falha de compilação (métodos inexistentes).
- [ ] **Step 3:** implementar os dois métodos e a guarda em `AlterarVisibilidade`, com `<summary>` no estilo da entidade.
- [ ] **Step 4:** mesmo filtro → passa. Rodar também `--filter FullyQualifiedName~Documento` → nada quebrado.
- [ ] **Step 5:** commit `feat(documentos): editar metadados e trocar o arquivo na entidade`.

### Task 2: Casos de uso — obter para edição e editar

**Files:**

- Create: `src/Secco.Intranet.Application/Documentos/ObterDocumentoParaEdicaoHandler.cs`
- Create: `src/Secco.Intranet.Application/Documentos/EditarDocumentoHandler.cs`
- Modify: `src/Secco.Intranet.Application/Documentos/TiposDeArquivo.cs` (apuração a partir do fluxo)
- Modify: `src/Secco.Intranet.Application/Documentos/PublicarDocumentoHandler.cs` (passa a usar a apuração extraída)
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/EditarDocumentoHandlerTests.cs` (novo)

**Interfaces:**

- Consumes: Task 1.
- Produces:
  - `TiposDeArquivo.ApurarAsync(Stream conteudo, string? nomeArquivo, IEnumerable<string> extensoesPermitidas, CancellationToken cancellationToken) : Task<string?>` — lê `BytesDeAssinatura` bytes, volta o fluxo ao início se posicionável e chama `Apurar`. O `ApurarContentTypeAsync` privado da publicação sai.
  - `VerbosDeAuditoria.DocumentoEditar = "documento.editar"`.
  - `IntranetErrors.Documentos.ArquivoEmGrupo` (`Error.Validation`, "Um grupo não tem arquivo.") e `IntranetErrors.Documentos.DescricaoTooLong(int limit)` (`Error.Validation`, no molde de `TituloTooLong`).
  - `record ObterDocumentoParaEdicaoQuery(Guid DocumentoId, string SetorSlug, IReadOnlySet<string> SetoresAdministrados, bool ExigirVinculo)`.
  - `record DocumentoParaEdicaoDto(Guid Id, bool EhGrupo, string Titulo, string? Descricao, Visibilidade Visibilidade, string? NomeArquivo)`.
  - `ObterDocumentoParaEdicaoHandler(IDocumentoRepository)`, `Task<Result<DocumentoParaEdicaoDto>> HandleAsync(ObterDocumentoParaEdicaoQuery, CancellationToken)`.
  - `internal static bool ObterDocumentoParaEdicaoHandler.Alcanca(DocumentoComSetor? encontrado, string setorSlug, IReadOnlySet<string> administrados, bool exigirVinculo)` — `true` só se existe, está ativo, o `SetorSlug` bate com o da rota (sem diferenciar maiúsculas) e, quando `exigirVinculo`, o setor está em `administrados`. Os dois handlers usam esta regra.
  - `record EditarDocumentoCommand(Guid DocumentoId, string SetorSlug, string? Titulo, string? Descricao, Visibilidade Visibilidade, string? NomeArquivo, long Tamanho, Stream? Conteudo, IReadOnlySet<string> SetoresAdministrados, bool ExigirVinculo)` — `Conteudo` nulo = manter o arquivo.
  - `EditarDocumentoHandler(IDocumentoRepository, IArquivoStore, DocumentoOptions, IntranetOptions, ITrilhaDeAuditoria, ILogger<EditarDocumentoHandler>)`, `Task<Result<Guid>> HandleAsync(EditarDocumentoCommand, CancellationToken)`.
  - Os dois handlers registrados como `AddScoped` ao lado de `ArquivarDocumentoHandler`.

**Ordem do `EditarDocumentoHandler` (a spec fixa):** `Alcanca` falso → `NotFound`; título vazio/longo → `TituloRequired`/`TituloTooLong`; descrição > 4.096 → `DescricaoTooLong(4096)`; grupo com `Conteudo` → `ArquivoEmGrupo`; com `Conteudo`: tamanho ≤ 0 → `ArquivoRequired`, > limite → `ArquivoMuitoGrande`, `ApurarAsync` nulo → `ArquivoNaoAceito`; só então `GravarAsync`, `Editar`, `AlterarVisibilidade` (só em arquivo; em grupo a visibilidade do comando é ignorada), `TrocarArquivo` com `Path.GetFileName(NomeArquivo)`; se nada mudou, devolve o id sem salvar e sem trilha; senão `SaveChangesAsync`, trilha, e por último `RemoverAsync(caminhoAnterior)` dentro de `try/catch (Exception falha) when (falha is not OperationCanceledException)` com `LogWarning` só do tipo.

**Payload da trilha** (`JsonSerializer` com `DefaultIgnoreCondition = WhenWritingNull`; só as chaves do que mudou, mais `setor`):

```json
{"setor":"rh","titulo":{"de":"Antigo","para":"Novo"},"visibilidade":{"de":"Setor","para":"Empresa"},"descricaoAlterada":true,"arquivo":{"de":{"nome":"a.pdf","tamanho":10},"para":{"nome":"b.docx","tamanho":3}}}
```

- [ ] **Step 1: Testes que falham** em `EditarDocumentoHandlerTests`, com `DocumentoRepositorioEmMemoria`, `ArquivoStoreFalso` (com `ContadorDeSalvamentos = () => repositorio.Gravacoes`) e uma `TrilhaFalsa` local:
  - `Editar_SoMetadados_NaoGravaNemRemoveArquivo` — `Gravados` e `Removidos` vazios; título e visibilidade novos.
  - `Editar_ComArquivoNovo_GravaSalvaEDepoisRemoveOAntigo` — `Removidos == [caminhoAntigo]`, `SalvamentosNasRemocoes == [gravacoesDepoisDeSalvar]` (remoção depois do salvamento).
  - `Editar_FalhaAoRemoverAntigo_EdicaoValeERegistraAviso` — `FalharAoRemover = true` → sucesso, documento aponta para o arquivo novo.
  - `Editar_ArquivoNaoAceito_NaoGravaNemAltera` — PNG com nome `.pdf` → `ArquivoNaoAceito`, `Gravados` vazio, título original.
  - `Editar_ArquivoMaiorQueOLimite_Recusa`.
  - `Editar_NomeComCaminho_GuardaSoONome` — `"C:\\fakepath\\nova.pdf"` → `NomeArquivo == "nova.pdf"`.
  - `Editar_GrupoComArquivo_RecusaArquivoEmGrupo` e `Editar_GrupoComVisibilidadeEmpresa_IgnoraEMantemSetor`.
  - `Editar_DescricaoAcimaDe4096_RecusaDescricaoTooLong`.
  - `Editar_SemPermissao_Arquivado_Inexistente_OuDeOutroSetor_DevolveNotFound` (`[Theory]`; o caso de outro setor tem os dois slugs em `SetoresAdministrados`).
  - `Editar_SemNenhumaMudanca_NaoSalvaNemRegistra`.
  - `Editar_RegistraTrilhaSoComOQueMudou` — confere o JSON acima para título e arquivo trocados; sem a chave `descricaoAlterada` quando a descrição não mudou.
  - `Obter_DevolveOsDadosAtuais` e `Obter_ForaDoAlcance_DevolveNotFound`.
- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter FullyQualifiedName~EditarDocumentoHandlerTests` → falha de compilação.
- [ ] **Step 3:** extrair `TiposDeArquivo.ApurarAsync` e trocar o uso na publicação; criar erros, verbo, os dois handlers e o registro de DI.
- [ ] **Step 4:** mesmo filtro → passa; `--filter "FullyQualifiedName~Documento|FullyQualifiedName~Auditoria"` → nada quebrado.
- [ ] **Step 5:** commit `feat(documentos): casos de uso de editar documento e substituir arquivo`.

### Task 3: Tela de edição, botão em "Organizar" e documentação

**Files:**

- Modify: `src/Secco.Intranet.Web/Controllers/SetorController.cs`
- Create: `src/Secco.Intranet.Web/Models/Documentos/EditarDocumentoViewModel.cs`
- Create: `src/Secco.Intranet.Web/Views/Setor/EditarDocumento.cshtml`
- Modify: `src/Secco.Intranet.Web/Views/Setor/OrganizarDocumentos.cshtml`
- Modify: `docs/roadmap.md`
- Test: `tests/Secco.Intranet.Tests/Integration/DocumentoEdicaoTelaTests.cs` (novo)

**Interfaces:**

- Consumes: Task 2 (`ObterDocumentoParaEdicaoHandler`, `EditarDocumentoHandler` e seus records).
- Produces:
  - `EditarDocumentoFormViewModel { string? Titulo [Required(ErrorMessage = "O título é obrigatório."), StringLength(256)]; string? Descricao [StringLength(4_096)]; Visibilidade Visibilidade }` — mesmos atributos de `DocumentoFormViewModel`.
  - `record EditarDocumentoViewModel(SetorDto Setor, Guid Id, bool EhGrupo, string? NomeArquivoAtual, EditarDocumentoFormViewModel Form, string UrlDeVolta)`.
  - `[HttpGet("documentos/{id:guid}/editar")] EditarDocumento(string slug, Guid id, CancellationToken)` — guarda igual à de `Organizar` (`ResolverEscritaAsync`), depois `ObterDocumentoParaEdicaoHandler` com `SlugsComEscritaAsync` e `ExigirVinculo: !AcessoAdministrativo.ModoAbertoDeDev(...)`; falha → `NotFound()`.
  - `[HttpPost("documentos/{id:guid}/editar")] [ValidateAntiForgeryToken] [RequestSizeLimit(64L * 1024 * 1024)] EditarDocumento(string slug, Guid id, EditarDocumentoFormViewModel form, IFormFile? arquivo, CancellationToken)` — guarda igual à de `Publicar` (`PodePublicarAsync` e `ResolverTipoAsync(slug, TipoDeItemMenu.Documentos)`). `arquivo` nulo ou com `Length == 0` vira `Conteudo` nulo. `NotFound` do handler → 404; outro erro → a view de volta com `ModelState` e os campos; sucesso → `TempData[FeedbackViewComponent.ChaveDaMensagem] = "Documento atualizado."` e `RedirectToAction(nameof(Organizar), new { slug })`.
  - Na view: formulário `multipart/form-data`; em grupo, só título e descrição; em documento, também visibilidade (`Setor`/`Empresa`) e o campo de arquivo opcional com o texto "Arquivo atual: {NomeArquivoAtual}. Deixe em branco para manter."; botões "Salvar" e "Cancelar" (`UrlDeVolta`).
  - O campo de arquivo leva `data-confirmar="O arquivo atual &quot;{NomeArquivoAtual}&quot; será substituído e apagado. Não dá para desfazer. Continuar?"` (o tema passa a honrá-lo na Task 4; até lá o formulário envia sem perguntar).
  - Em `OrganizarDocumentos.cshtml`, link "Editar" (`btn btn-sm btn-outline-secondary`, `aria-label="Editar {titulo}"`) em cada linha, antes do "Arquivar".

- [ ] **Step 1: Testes que falham** em `DocumentoEdicaoTelaTests`, no molde de `DocumentoOrganizarTelaTests` (cliente com raiz de armazenamento própria em `_raiz`, `TokenAsync`, roles por `RolesDeTesteMiddleware.Header`):
  - `Organizar_MostraEditarEmCadaLinha`.
  - `Get_DocumentoDoSetor_MostraFormularioPreenchido` — título, descrição, "Arquivo atual: …" e o campo de arquivo com `data-confirmar` citando o nome do arquivo atual.
  - `Get_Grupo_NaoMostraArquivoNemVisibilidade`.
  - `Post_SemArquivo_MantemArquivoETrocaTitulo` — formulário multipart com parte de arquivo vazia; o download devolve os bytes originais.
  - `Post_ComArquivoNovo_DownloadEntregaONovoEOAntigoSomeDoDisco` — antes e depois, o número de arquivos sob `_raiz/{TenantAlfa}` é 1, e o download devolve os bytes novos com o nome novo no `Content-Disposition`.
  - `Post_ArquivoNaoAceito_VoltaComMensagemENadaMuda`.
  - `GetEPost_SemEscritaNoSetor_Devolvem404` (role sem `setor-{id}:write`) e `GetEPost_DocumentoArquivado_Devolvem404`.
  - `Post_TenantBeta_NaoAlcancaDocumentoDoAlfa` — 404 e documento intacto.
- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter FullyQualifiedName~DocumentoEdicaoTelaTests` → falha (rota inexistente).
- [ ] **Step 3:** view models, ações, view e botão.
- [ ] **Step 4:** mesmo filtro → passa.
- [ ] **Step 5:** em `docs/roadmap.md`, no item "Documentos do setor com hierarquia e ordem manual", trocar "faltam substituir arquivo com histórico e as bibliotecas" por "a parte 2, editar documento e substituir arquivo, entrou em 2026-11-01 sem histórico de versões ([spec](specs/2026-11-01-documentos-editar-e-substituir-arquivo-design.md)); falta a parte 3, as bibliotecas"; marcar a spec como `**Estado:** aprovado, implementado (parte 2)`.
- [ ] **Step 6:** `dotnet build Secco.Intranet.slnx -c Release` com 0 avisos e `dotnet test Secco.Intranet.slnx -c Release --no-build` com 0 falhas.
- [ ] **Step 7:** commit `feat(documentos): tela de editar documento e substituir arquivo`.

### Task 4: Contrato de tema — `data-confirmar` no campo de arquivo

**Files:**

- Modify: `src/Secco.Intranet.Themes.Vertical/wwwroot/js/theme.js` (handler de `submit` que já trata `data-confirmar`, ~linha 111)
- Modify: `src/Secco.Intranet.Themes.Horizontal/wwwroot/js/theme.js` (mesmo handler, ~linha 106)
- Modify: `docs/temas.md` (trecho que descreve `data-confirmar`, ~linha 158)
- Test: `tests/Secco.Intranet.Tests/Ui/ConfirmacaoDeArquivoTelaTests.cs` (novo, opt-in por `SECCO_UI_TESTS`, no molde de `MenuDaArvoreTelaTests`)

**Interfaces:**

- Consumes: a tela de edição e o `data-confirmar` do campo de arquivo da Task 3.
- Produces: no envio de um `<form>`, além da pergunta do próprio form, o tema procura `input[type=file][data-confirmar]` dentro dele; para cada um com `files.length > 0`, chama `window.confirm(pergunta)` e cancela o envio (`preventDefault`) se a resposta for não. Sem arquivo escolhido, não pergunta. A pergunta do form, se houver, vem primeiro. Comportamento idêntico nos dois temas.

- [ ] **Step 1: Teste de tela que falha** (Playwright, nos dois temas), na tela de edição da Task 3 com um documento publicado no setor:
  - `ArquivoEscolhido_PerguntaECancelarNaoEnvia` — o diálogo cita o nome do arquivo atual; ao recusar, a URL não muda.
  - `ArquivoEscolhido_ConfirmarEnvia`.
  - `SemArquivo_EnviaSemPerguntar` — nenhum diálogo.
- [ ] **Step 2:** `SECCO_UI_TESTS=1 dotnet test tests/Secco.Intranet.Tests --filter FullyQualifiedName~ConfirmacaoDeArquivoTelaTests` → falha (sem diálogo).
- [ ] **Step 3:** implementar nos dois `theme.js`, mantendo o comentário de que sem JS o formulário envia direto; registrar o uso no campo de arquivo em `docs/temas.md`.
- [ ] **Step 4:** mesmo comando → passa nos dois temas. O job `theme-assets` do CI não muda: `theme.js` não é gerado pelo build.
- [ ] **Step 5: Verificação final:** `dotnet build Secco.Intranet.slnx -c Release` com 0 avisos e `dotnet test Secco.Intranet.slnx -c Release --no-build` com 0 falhas.
- [ ] **Step 6:** commit `feat(temas): data-confirmar no campo de arquivo pergunta so com arquivo escolhido`.

