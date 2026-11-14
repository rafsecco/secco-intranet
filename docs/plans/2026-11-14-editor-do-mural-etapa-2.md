# Editor do Mural, etapa 2: Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Imagem no meio do texto do aviso, por colagem do Word (via RTF), colagem de arquivo, botão e arrasto, com até 10 imagens por aviso.

**Architecture:** A imagem fica no navegador até o envio. O Markdown usa `![descrição](nova-imagem:N)`; no envio os arquivos seguem num campo múltiplo do mesmo formulário. O servidor processa cada um pelo `Secco.SDK.Imaging`, grava cifrado e troca o marcador por `/publicacoes/{id}/imagens/{imagemId}`. Nenhum arquivo existe no servidor antes de o aviso ser salvo.

**Tech Stack:** Tiptap 3 (`@tiptap/extension-image`), Markdig, EF Core, xUnit, AwesomeAssertions, Playwright.

**Spec:** [docs/specs/2026-11-06-editor-do-mural-design.md](../specs/2026-11-06-editor-do-mural-design.md), seção "Etapa 2".

## Global Constraints

- **Limites:** até 10 imagens no texto por aviso. O limite por arquivo é o atual, de 10 MB, em PNG, JPEG ou WebP, validados pelo conteúdo no servidor.
- **O que o corpo nunca guarda:** `file:`, `blob:`, `data:` nem `pendente:`. A imagem relativa só é aceita se for desta publicação.
- **Imagem final do aviso:** o campo `Imagem` e `/publicacoes/{id}/imagem` não mudam.
- **Banco:** nomes por convention (ADR-0017), migration em par gerada pelo `dotnet ef`, entidade no molde de `RenovacaoDeVencimento` (FK sem navegação, `DbSet` próprio).
- **Front-end:**
  - sem script inline, `style` inline, `innerHTML` nem `prompt()`; a CSP em Enforce não registra violação;
  - os mesmos `editor.js` e `scripts/` nos dois temas, com `npm run build` nos dois;
  - nada de `sed` ou heredoc para código com contrabarra.
- **Convenções:** `.cs` com tabs; `.js` com 2 espaços; testes `Metodo_Cenario_Resultado`; AwesomeAssertions; textos em português; amostras fictícias.
- **Commits:** Conventional Commits, com o corpo terminando em `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Sem push.

## Review Focus

1. **Reeditar um aviso com imagens sem mudar nada:** as imagens continuam, e nenhum arquivo é apagado nem duplicado.
2. **Imagem colada e depois apagada antes do envio:** o arquivo dela não é enviado, e a numeração dos marcadores não fica com buraco.
3. **Alternar para o modo Markdown com imagem nova no texto:** ao voltar, a imagem continua visível e é enviada.
4. **Recusa do servidor por outro motivo, como título vazio:** a pessoa perde as imagens novas, porque o arquivo não volta no formulário. A mensagem de erro precisa dizer isso.
5. **Imagem de outra publicação, digitada no modo cru:** o servidor recusa ao salvar, e o editor não a carrega.

---

### Task 1: Domínio e persistência das imagens no texto

**Files:**
- Create: `src/Secco.Intranet.Domain/Publicacoes/ImagemNoTexto.cs`
- Create: `src/Secco.Intranet.Application/Publicacoes/IImagemNoTextoRepository.cs`
- Create: `src/Secco.Intranet.Infrastructure/Mappings/ImagemNoTextoConfiguration.cs`
- Create: `src/Secco.Intranet.Infrastructure/Repositories/ImagemNoTextoRepository.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs`, o registro de repositórios em `IntranetInfrastructureExtensions`
- Create: migration `ImagensNoTexto` nos dois engines
- Test: `tests/Secco.Intranet.Tests/Unit/ImagemNoTextoTests.cs`, `Integration/ImagemNoTextoRepositoryTests.cs`

**Interfaces:**
- Produces: `public sealed class ImagemNoTexto : BaseEntity`, com `Guid PublicacaoId`, `string CaminhoRelativo` (256) e `string ChaveEmbrulhada` (256). Construtor `(Guid publicacaoId, string caminhoRelativo, string chaveEmbrulhada)`, com `DomainInvariantException` para id vazio ou texto vazio. Não guarda descrição: ela fica no alt do Markdown, a única fonte.
- Produces: `IImagemNoTextoRepository`, com `void Adicionar(ImagemNoTexto imagem)`, `void Remover(ImagemNoTexto imagem)`, `Task<IReadOnlyList<ImagemNoTexto>> ListarDaPublicacaoAsync(Guid publicacaoId, CancellationToken ct = default)` e `Task<ImagemNoTexto?> GetAsync(Guid publicacaoId, Guid imagemId, CancellationToken ct = default)`. Não tem `SaveChanges` próprio: o `SaveChangesAsync` de `IPublicacaoRepository` grava tudo, porque os dois usam o mesmo `IntranetDbContext` com escopo.
- Produces: `DbSet<ImagemNoTexto> ImagensNoTexto`, tabela `tb_imagens_no_texto`, FK para `tb_publicacoes` com `DeleteBehavior.Restrict` e índice em `PublicacaoId`.

- [ ] **Step 1: Testes que falham**
  - `ImagemNoTextoTests`: invariantes do construtor.
  - `ImagemNoTextoRepositoryTests`, com SQL Server real como os demais de integração:
    - `Adicionar_DuasImagens_ListarDevolveAsDuas`;
    - `GetAsync_ImagemDeOutraPublicacao_DevolveNulo`;
    - `Remover_Imagem_SaiDaLista`.
  - Os dois últimos gravam pelo `SaveChangesAsync` de `IPublicacaoRepository`, o que prova o contexto compartilhado.
- [ ] **Step 2:** Rodar os testes e confirmar a falha.
- [ ] **Step 3:** Implementar e gerar a migration `ImagensNoTexto` nos dois engines.
- [ ] **Step 4:** Rodar a suíte inteira, com `MigracoesPorEngineTests` verde nos dois engines.
- [ ] **Step 5:** Commit `feat(mural): imagens no texto no dominio e no banco`.

---

### Task 2: O corpo com imagens nos casos de uso

**Files:**
- Create: `src/Secco.Intranet.Application/Publicacoes/ImagensDoCorpo.cs`
- Create: `src/Secco.Intranet.Application/Publicacoes/ObterImagemNoTextoHandler.cs`
- Modify: `PublicarPublicacaoHandler.cs`, `EditarPublicacaoHandler.cs`, `ObterImagemDaPublicacaoHandler.cs`, `IntranetErrors.cs`, `IntranetApplicationExtensions` (registro)
- Test: `Unit/ImagensDoCorpoTests.cs`, `Unit/ImagemNoTextoHandlersTests.cs`, `Unit/AuditoriaDoMuralTests.cs`

**Interfaces:**
- Consumes: `IImagemNoTextoRepository`, `ImagemNoTexto` (Task 1).
- Produces: `PublicarPublicacaoCommand` e `EditarPublicacaoCommand` ganham o último parâmetro `IReadOnlyList<ImagemEnviada>? ImagensDoTexto = null`. O índice na lista é o N de `nova-imagem:N`.
- Produces: `ObterImagemNoTextoQuery(Guid PublicacaoId, Guid ImagemId, IReadOnlyList<string> SetoresDoLeitor, IReadOnlySet<string> SetoresComEscrita, bool ExigirVisibilidade)` e `ObterImagemNoTextoHandler.HandleAsync(...) : Task<Result<ImagemGuardada>>`.
- Produces: rota de imagem `CaminhoDaImagemNoTexto(Guid publicacaoId, Guid imagemId) => $"/publicacoes/{publicacaoId}/imagens/{imagemId}"`, em `ImagensDoCorpo`.

**Algoritmo de `ImagensDoCorpo`** (classe interna estática, chamada pelos dois handlers depois da validação do texto):

1. **Ler os destinos de imagem.** Use Markdig com `DisableHtml().UsePipeTables().UsePreciseSourceLocation()` e cada `LinkInline` com `IsImage`, cujo `UrlSpan` dá a posição exata do destino.
2. **Classificar cada destino:**

   | Destino | Tratamento |
   |---|---|
   | `nova-imagem:N`, com `0 <= N < ImagensDoTexto.Count` | Aceito |
   | `nova-imagem:N` sem arquivo | `ImagemNoTextoSemArquivo` |
   | `/publicacoes/{id}/imagens/{imagemId}` com o `id` desta publicação e o `imagemId` entre as imagens dela | Aceito |
   | Qualquer outro caminho relativo, inclusive imagem de outra publicação | `ImagemNoTextoInvalida` |
   | `http` ou `https` | Aceito; o `RenderizadorMarkdown` já o transforma em link |
   | Qualquer outro esquema (`file`, `blob`, `data`, `pendente`, `javascript`...) | `ImagemNoTextoInvalida` |

3. **Limite:** mais de 10 imagens relativas no corpo, somando as novas e as existentes, devolve `ImagensNoTextoDemais`.
4. **Gravar as novas,** uma de cada vez e só as referenciadas, por `ImagemDaPublicacao.GravarAsync`. Se uma falhar, apague as já gravadas desta chamada (`ImagemDaPublicacao.ApagarAsync`) e devolva o erro do processamento. Arquivo sem marcador não é gravado.
5. **Reescrever o corpo,** do fim para o começo pelos `UrlSpan`, trocando cada `nova-imagem:N` pelo caminho da imagem nova. N repetido aponta para a mesma imagem.
6. **Na edição,** as imagens da publicação que não aparecem mais no corpo reescrito saem do repositório e têm o arquivo apagado **depois** do `SaveChangesAsync`, com falha de apagar virando aviso no log, como `ApagarAsync` já faz.
7. **Falha no `SaveChangesAsync`** apaga os arquivos recém-gravados, no molde de `PersistirOuDescartarAsync`.

**Erros novos em `IntranetErrors.Publicacoes`, textos exatos:**
- `ImagemNoTextoSemArquivo`: `"Uma imagem do texto chegou sem o arquivo. Insira a imagem de novo."`
- `ImagemNoTextoInvalida`: `"O texto tem uma imagem que não pode ser usada: só imagens enviadas neste aviso ou endereços https."`
- `ImagensNoTextoDemais`: `"O texto passa de 10 imagens."`

**Visibilidade:** extraia de `ObterImagemDaPublicacaoHandler` a regra de quem vê (escrita no setor, ou no ar e com visibilidade Empresa ou leitura do setor) para um método estático interno, compartilhado pelos dois handlers, sem mudar o comportamento atual.

**Auditoria:** `mural.publicar` e `mural.editar` registram `imagensNoTexto` (a quantidade final).

- [ ] **Step 1: Testes que falham**
  - `ImagensDoCorpoTests`, um por linha da tabela de destinos, mais:
    - `Publicar_DuasImagensNovas_ReescreveOsMarcadores` (o corpo final tem dois `/publicacoes/{id}/imagens/` distintos e nenhum `nova-imagem:`);
    - `Publicar_MarcadorRepetido_GravaUmaImagemSo`;
    - `Publicar_ArquivoSemMarcador_NaoGrava`;
    - `Publicar_SegundaImagemInvalida_ApagaAPrimeira`;
    - `Editar_ImagemRemovidaDoCorpo_ApagaDepoisDeSalvar`;
    - `Editar_SemMudarNada_MantemAsImagens` (Review Focus 1);
    - `Editar_ImagemDeOutraPublicacao_Recusa` (Review Focus 5);
    - `Publicar_OnzeImagens_Recusa`.
  - `ImagemNoTextoHandlersTests`: os mesmos casos de visibilidade dos testes de `ObterImagemDaPublicacaoHandler`, mais `HandleAsync_ImagemDeOutraPublicacao_NotFound`.
  - Os testes atuais de `ObterImagemDaPublicacaoHandler` passam sem mudança.
- [ ] **Step 2:** Rodar e confirmar a falha.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Rodar a suíte inteira.
- [ ] **Step 5:** Commit `feat(mural): corpo com imagens novas, existentes e recusadas`.

---

### Task 3: Formulário, rota e limites

**Files:**
- Modify: `src/Secco.Intranet.Web/Models/Publicacoes/SetorAvisosViewModel.cs`, `Controllers/SetorController.cs` (`SalvarAviso`), `Controllers/MuralController.cs`, `Views/Setor/Avisos.cshtml`
- Test: `tests/Secco.Intranet.Tests/Integration/ImagemNoTextoFluxoTests.cs`

**Interfaces:**
- Consumes: os comandos e o handler da Task 2.
- Produces: `PublicacaoFormViewModel.ImagensDoTexto` (`List<IFormFile>?`), campo `Form.ImagensDoTexto`.
- Produces: `textarea[data-sc-editor-markdown]` ganha `data-sc-publicacao-id`, com o id na edição e vazio na publicação nova.
- Produces: `GET /publicacoes/{id:guid}/imagens/{imagemId:guid}`, no molde de `Imagem` em `MuralController`, com o mesmo cache, ETag, `nosniff` e 404.

**Detalhes:**
- `SalvarAviso` ganha `[RequestSizeLimit(128L * 1024 * 1024)]` e `[RequestFormLimits(MultipartBodyLengthLimit = 128L * 1024 * 1024)]`, que comportam 11 arquivos de 10 MB e os campos.
- Cada arquivo abre o fluxo uma vez e fecha no fim da action. Arquivo vazio conta como "sem arquivo".
- Em recusa, a página volta com o texto e com a mensagem `"As imagens novas do texto não foram guardadas: insira-as de novo antes de enviar."`, além do erro (Review Focus 4). O Markdown ainda traz os marcadores, e o editor os mostra como pendentes (Task 4).

- [ ] **Step 1: Testes de integração que falham**
  - `Publicar_DuasImagensNoTexto_LeituraMostraAsDuas` (multipart com dois PNG válidos e corpo `"a ![um](nova-imagem:0) b ![dois](nova-imagem:1)"`: a página da publicação tem dois `<img src="/publicacoes/{id}/imagens/`, e cada GET responde 200 `image/webp`);
  - `ImagemNoTexto_OutraPublicacao_404`;
  - `ImagemNoTexto_LeitorSemVisibilidade_404`;
  - `Publicar_MarcadorSemArquivo_RecusaComMensagem`;
  - `Publicar_SemAntiforgery_Recusa`.
- [ ] **Step 2:** Rodar e confirmar a falha.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Rodar a suíte inteira.
- [ ] **Step 5:** Commit `feat(mural): envio e leitura das imagens no texto`.

---

### Task 4: Imagem no editor: botão, arrasto, colagem de arquivo e envio

**Files (nos dois temas):**
- Create: `scripts/imagens-novas.js`
- Modify: `scripts/editor-bundle.js`, `wwwroot/js/editor.js`, `wwwroot/scss/_components.scss`, assets compilados, `package.json` (`@tiptap/extension-image` na mesma versão dos demais `@tiptap/*`)
- Modify, se a CSP bloquear `blob:`: `src/Secco.Intranet.Web/Hosting/IntranetSecurityHeadersExtensions.cs` (`options.ImageSources`)
- Test: `tests/Secco.Intranet.Tests/Ui/EditorDoMuralTelaTests.cs`

**Interfaces:**
- Produces, em `imagens-novas.js`: `registrar(arquivo: File): string`, que devolve `nova-imagem:N`; `urlDe(src: string): string | null`, que dá a `blob:` de um marcador ou o próprio caminho de uma imagem desta publicação; `paraEnvio(markdown: string): { markdown: string, arquivos: File[] }`, que renumera os marcadores na ordem em que aparecem (0..k-1), só com os referenciados (Review Focus 2); e `LIMITE_IMAGENS = 10`.
- Produces: nó `imagemPendente` (atom), que serializa como `![imagem pendente](pendente:)`, é exibido como um botão com o texto *"Esta imagem não veio. Clique para enviar o arquivo."* e, ao ser clicado, abre o seletor de arquivo e é trocado pela imagem.
- Produces: `criarEditor` aceita `publicacaoId: string`.

**Comportamento:**
- `Image.configure({ allowBase64: false })`, estendido para que `renderHTML` use `urlDe(src)`. Um `src` que `urlDe` não aceite vira `imagemPendente` ao carregar, e o Markdown nunca guarda `blob:`.
- Botão `Imagem` (`bi-image`), depois de Inserir tabela, abre um `input type=file` oculto, múltiplo, com `accept="image/png,image/jpeg,image/webp"`.
- Arrastar arquivo (`handleDrop`) e colar arquivo de imagem sem `text/html` (print) inserem a imagem na posição.
- Arquivo acima de 10 MB, de outro tipo, ou além do limite de 10 imagens no texto não entra e mostra aviso:
  - *"A imagem passa de 10 MB."*
  - *"Use PNG, JPG ou WebP."*
  - *"O texto já tem 10 imagens."*
- A descrição da imagem selecionada é editada num campo `Descrição da imagem` na barra, no molde do campo de link, com até 300 caracteres. Imagem colada entra sem descrição.
- **No envio:**
  - se houver `imagemPendente`, o envio é bloqueado: o foco vai ao primeiro marcador e aparece o aviso *"Envie ou remova as imagens pendentes antes de publicar."*;
  - fora isso, `paraEnvio(area.value)` reescreve o `textarea` e preenche um `input type=file name="Form.ImagensDoTexto" multiple hidden` criado pelo script, via `DataTransfer`.
- **Modo Markdown:** os marcadores `nova-imagem:N` aparecem no texto cru, e ao voltar o editor continua mostrando as imagens, porque o registro é do módulo (Review Focus 3). No envio pelo modo cru, `paraEnvio` também vale.

- [ ] **Step 1: Testes de tela que falham** (nos dois temas)
  - `Imagem_Botao_InsereEPublica` (`SetInputFilesAsync` com PNG fictício: aparece no editor; publicado, a leitura tem a imagem);
  - `Imagem_ColarArquivo_Insere`;
  - `Imagem_ApagadaAntesDoEnvio_NaoEEnviada` (o envio leva 0 arquivos e o corpo não tem marcador);
  - `Imagem_ModoMarkdownIdaEVolta_ContinuaEEnviada`;
  - `Imagem_GrandeDemais_AvisaENaoInsere`;
  - `Imagem_OnzeImagens_AvisaENaoInsere`;
  - `Imagem_Pendente_BloqueiaOEnvio`;
  - `Imagem_Descricao_VaiParaOAlt`;
  - `Imagem_EditarAvisoComImagens_MantemAsImagens` (Review Focus 1);
  - `Imagem_SemViolacaoDeCsp`.
- [ ] **Step 2:** Rodar e confirmar a falha.
- [ ] **Step 3:** Implementar e rodar `npm run build` nos dois temas.
- [ ] **Step 4:** Rodar a suíte inteira, com e sem `SECCO_UI_TESTS=1`.
- [ ] **Step 5:** Commit `feat(mural): imagem no editor por botao, arrasto e colagem de arquivo`.

---

### Task 5: Colagem do Word com imagens e fechamento

**Files (nos dois temas):**
- Create: `scripts/imagens-do-rtf.js`
- Modify: `scripts/colagem.js`, `scripts/editor-bundle.js`, assets compilados
- Test: `tests/Secco.Intranet.Tests/Ui/EditorDoMuralTelaTests.cs`
- Docs: `docs/specs/2026-11-06-editor-do-mural-design.md`, `docs/roadmap.md`, `CHANGELOG.md`

**Interfaces:**
- Produces: `imagensDoRtf(rtf: string): File[]`, só PNG e JPEG de `\pict` dentro de `\shppict` (ignorando `\nonshppict`), na ordem do RTF; devolve vazio para RTF acima de 50 MB.
- Consumes: `registrar` e `imagemPendente` (Task 4).

**Algoritmo**, verificado com um documento real em 2026-11-06, com uma imagem só. O grupo `\pict` vira espaço antes de remover as palavras de controle, senão `\bliptag-123` gruda no hexadecimal:

```js
const grupo = /\{\\pict((?:[^{}]|\{[^{}]*\})*)\}/g;
// para cada m: corpo = m[1].replace(/\{[^{}]*\}/g, ' ');
// tipo = /\\(pngblip|jpegblip)/.exec(corpo)?.[1];
// hex = corpo.replace(/\\[a-zA-Z]+-?\d*/g, ' ').replace(/[^0-9a-fA-F]/g, '');
// bytes conferidos pela assinatura PNG (89 50 4E 47) ou JPEG (FF D8 FF)
```

**Colagem:**
- Um listener de `paste` em captura, no elemento do editor, guarda o `text/rtf` do evento para o `transformPastedHTML` do mesmo evento usar.
- Em `limparColagem`, `img` deixa de ser sempre removida:
  - `file:` (Word): associa a N-ésima `img` `file:` à N-ésima imagem do RTF. Se as quantidades forem diferentes, todas viram `imagemPendente`, porque imagem certa no lugar errado é pior que nenhuma;
  - `data:image/png|jpeg|webp`: vira `File` e é registrada;
  - `http(s)` e qualquer outra: `imagemPendente`.
- `AVISO_IMAGEM` passa a ser *"Algumas imagens não vieram. Clique em cada uma para enviar o arquivo."*, mostrado só quando há pendente.

- [ ] **Step 1: Testes de tela que falham** (nos dois temas), com RTF fictício montado no teste a partir de PNGs válidos gerados nele, no formato `{\*\shppict{\pict{\*\picprop}\picw..\pngblip\bliptag-1{\*\blipuid ..}HEX}}{\nonshppict{\pict\wmetafile8 HEX}}`:
  - `ColarDoWord_DuasImagens_EntramNaOrdem` (HTML com duas `img file:`, e cada imagem colada tem a largura natural do PNG correspondente);
  - `ColarDoWord_QuantidadeDiferente_ViraPendente`;
  - `ColarDoWord_SoWmf_ViraPendente`;
  - `Colar_ImagemData_Insere`;
  - `Colar_ImagemExterna_ViraPendente`;
  - `ColarDoWord_ComImagens_Publica` (fluxo completo até a leitura).
- [ ] **Step 2:** Rodar e confirmar a falha.
- [ ] **Step 3:** Implementar e rodar `npm run build` nos dois temas.
- [ ] **Step 4: Docs**
  - **Spec:** `Estado` para "etapas 1 e 2 entregues", com a nota de que a colagem com várias imagens foi provada só com RTF fictício até o teste com o Word real.
  - **Roadmap:** registrar a entrega da etapa 2.
  - **`CHANGELOG.md`:** na seção do editor, a migration `ImagensNoTexto` a aplicar pelo `migrate`, e que o tema de fork precisa do CSS novo do editor (`imagemPendente` e a imagem selecionada).
- [ ] **Step 5:** Rodar a suíte inteira, com e sem `SECCO_UI_TESTS=1`.
- [ ] **Step 6:** Commit `feat(mural): imagens do Word coladas pelo RTF, e docs da etapa 2`.
