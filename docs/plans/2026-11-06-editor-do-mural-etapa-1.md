# Editor do Mural, etapa 1: Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trocar o Toast UI pelo Tiptap no formulário de aviso, limpar a colagem do Word e fazer a tabela funcionar de ponta a ponta.

**Architecture:** O corpo continua Markdown, e o servidor continua gerando o HTML com `DisableHtml`. A lógica do editor fica em `scripts/` de cada tema, compilada num bundle global `seccoEditor`; `wwwroot/js/editor.js` só liga o bundle ao `textarea` e monta a barra.

**Tech Stack:** Tiptap 3 com `@tiptap/markdown` e `@tiptap/extension-table`, Bootstrap 5.3, esbuild, Sass, Markdig, EF Core, xUnit, AwesomeAssertions, Playwright.

**Spec:** [docs/specs/2026-11-06-editor-do-mural-design.md](../specs/2026-11-06-editor-do-mural-design.md), seção "Etapa 1". A etapa 2 tem plano próprio.

## Global Constraints

- Corpo do aviso: até 20.000 caracteres.
- Sem `@toast-ui/editor` e sem DOMPurify nos dois temas; `npm audit` sem alerta alto ou crítico.
- Sem script inline, `style` inline, `innerHTML` nem `prompt()` no editor; a CSP em Enforce não pode registrar violação.
- O mesmo `editor.js` e os mesmos fontes de `scripts/` nos dois temas, e `Views/Setor/Avisos.cshtml` apontando para o Vertical. O CSS entra em cada tema.
- Assets compilados são versionados: rodar `npm run build` nos dois temas antes de commitar, ou o job `theme-assets` reprova.
- Versões `@tiptap/*` iguais entre si, a estável mais recente consultada ao vivo, sem `^`. A spec testou 3.33.0.
- Migration sempre em par, gerada pelo `dotnet ef` (task `ef: nova migration (ambos os engines)`).
- `.cs` com tabs; `.js` e `.json` com 2 espaços; testes `Metodo_Cenario_Resultado`; AwesomeAssertions; textos em português.
- Commits direto na `main`, em Conventional Commits, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Amostras de teste são fictícias, nunca documentos reais.

## Review Focus

1. **Colar texto puro ou de outro site, sem marcas do Word:** entra normalmente, sem aviso nenhum.
2. **Servidor recusar o corpo acima do limite:** o texto volta ao editor, em vez de a pessoa perder o que colou.
3. **Linhas de tamanhos diferentes numa tabela colada:** faltam células, mas o Markdown fica válido e o texto não muda de coluna.
4. **Colar uma tabela dentro de uma célula já existente:** não passa pela limpeza do HTML do mesmo jeito, e o resultado precisa continuar uma tabela válida.
5. **Enter ou Shift+Enter dentro de uma célula:** a edição normal também não pode gerar `<br>` nem quebrar a tabela.

---

### Task 1: Servidor aceita e exibe tabela, com limite de 20.000

**Files:**
- Modify: `src/Secco.Intranet.Web/Conteudo/RenderizadorMarkdown.cs`
- Modify: `src/Secco.Intranet.Application/Publicacoes/PublicarPublicacaoHandler.cs` (`PipelineDeTexto`)
- Modify: `src/Secco.Intranet.Application/Publicacoes/ValidacaoDePublicacao.cs`, `src/Secco.Intranet.Application/IntranetErrors.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Mappings/PublicacaoConfiguration.cs`
- Create: migration `AumentarCorpoDaPublicacao` nos dois projetos de migrations
- Test: `tests/Secco.Intranet.Tests/Unit/RenderizadorMarkdownTests.cs`, `Unit/AvisoDePublicacaoTests.cs`, `Integration/PublicacaoFluxoTests.cs`

**Interfaces:**
- Produces: corpo de até 20.000 caracteres persistido nos dois engines, e tabela em Markdown renderizada para quem lê.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
// RenderizadorMarkdownTests
[Fact]
public void Renderizar_TabelaEmMarkdown_GeraTabelaHtml()
{
	var html = Renderizar("| Dia | Sala |\n| --- | --- |\n| Seg | 12 |");

	html.Should().Contain("<th>Dia</th>").And.Contain("<td>Seg</td>");
}

[Fact]
public void Renderizar_HtmlEmCelula_SaiEscapado()
{
	var html = Renderizar("| a |\n| --- |\n| <img src=x onerror=alert(1)> |");

	html.Should().NotContain("<img").And.Contain("&lt;img");
}
```

Use o helper de renderização que a classe já tiver. Em `AvisoDePublicacaoTests`, `HandleAsync_CorpoComTabela_ResumoSemSeparadoresDeTabela`, com o `Montar` existente e corpo `"Antes\n\n| Dia | Sala |\n| --- | --- |\n| Seg | 12 |"`: a mensagem contém `Dia` e `Seg`, e não contém `|` nem `---`.

Em `PublicacaoFluxoTests`, no molde de `Publicar_SemTitulo_Recusa`:
- `Publicar_CorpoNoLimite_Aceita`: `new string('x', 20_000)` é publicado.
- `Publicar_CorpoAcimaDoLimite_Recusa`: `new string('x', 20_001)` responde com a mensagem contendo `20000`.

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `dotnet test --filter "FullyQualifiedName~RenderizadorMarkdownTests|FullyQualifiedName~AvisoDePublicacaoTests|FullyQualifiedName~PublicacaoFluxoTests"`
Expected: os novos falham.

- [ ] **Step 3: Implementar**

- `.UsePipeTables()` nos dois pipelines.
- `LimiteDoCorpo = 20_000`, `HasMaxLength(20_000)` e a mensagem `"O corpo excede o limite de 20000 caracteres."`.
- Gerar a migration `AumentarCorpoDaPublicacao` nos dois engines. Esperado: `ds_corpo` vira `nvarchar(max)` no SQL Server e `character varying(20000)` no Postgres.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test Secco.Intranet.slnx --nologo --verbosity quiet`
Expected: 0 falhas, inclusive `MigracoesPorEngineTests` nos dois engines.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(mural): tabela para quem le e corpo de ate 20000 caracteres"
```

---

### Task 2: Editor Tiptap no lugar do Toast UI

**Files (cada item nos dois temas, `src/Secco.Intranet.Themes.{Vertical,Horizontal}/`):**
- Modify: `package.json`, `package-lock.json`, `scripts/copy-assets.mjs`, `scripts/editor-bundle.js`
- Create: `scripts/tabela-markdown.js`
- Rewrite: `wwwroot/js/editor.js`
- Modify: `wwwroot/scss/_components.scss`, e os assets compilados
- Delete: `wwwroot/vendor/js/toastui-editor.js`, `wwwroot/vendor/js/i18n/`, `wwwroot/vendor/css/toastui-editor.css`, `wwwroot/vendor/css/toastui-editor-dark.css`
- Modify: `src/Secco.Intranet.Web/Views/Setor/Avisos.cshtml` (seção `Scripts`)
- Modify: `src/Secco.Intranet.Web/Hosting/IntranetSecurityHeadersExtensions.cs`, e os testes de `IntranetSecurityHeadersExtensionsTests` e `SegurancaHttpTests` que citarem os valores
- Create: `tests/Secco.Intranet.Tests/Ui/EditorDoMuralTelaTests.cs`

**Interfaces:**
- Produces: `window.seccoEditor.criarEditor({ elemento: HTMLElement, markdown: string, aoMudar: (markdown: string) => void }): Editor`, de `scripts/editor-bundle.js`.
- Produces: `.sc-editor` (contêiner), `.sc-editor__barra`, `.sc-editor__avisos` (vazio nesta task).
- Produces: `EditorDoMuralFixture` com `IBrowser Navegador`, `Task<IPage> AbrirAvisosAsync(string tema)`, `Task<IPage> AbrirEdicaoAsync(string tema, Guid publicacaoId)`, `Task<Guid> CriarPublicacaoAsync(string corpo)` e `List<string> ViolacoesDeCsp(IPage pagina)`.

- [ ] **Step 1: Escrever a fixture e os testes que falham**

A fixture segue `ConfirmacaoDeArquivoFixture`: Kestrel por tema, `Intranet:Theme:Nome`, `UseStaticWebAssets`, setor criado por `/Setores/Create`, perfil `intranet-admin`, Chromium headless. `AbrirAvisosAsync` navega para `/{Slug}/avisos`, e `AbrirEdicaoAsync` para `/{Slug}/avisos?editar={id}`. `CriarPublicacaoAsync` usa o `PublicarPublicacaoHandler` direto, no escopo do tenant, para gravar corpos que o formulário não produziria. `ViolacoesDeCsp` acumula mensagens de console contendo `Content Security Policy` desde a abertura da página.

Testes com `[UiTheory]`, parametrizados por tema:

```csharp
Editor_Abre_SemViolacaoDeCsp
// `.sc-editor .ProseMirror` visível, textarea escondido, ViolacoesDeCsp vazio.

Editor_Digitar_PublicaOTextoDigitado
// Digita "Texto do aviso", preenche o título, envia: o aviso aparece na lista da página.

Editor_Barra_AplicaNegritoETabela
// Clica em aria-label "Negrito" e digita; clica em "Inserir tabela":
// o textarea contém "**" e "| --- |".

Editor_Tabela_EnterNaCelula_SalvaNaMesmaLinha   // Review Focus 5
// Na célula, digita "um", Enter, "dois", Shift+Enter, "três":
// o textarea contém "um dois três" e não contém "<br>".

Editor_SemScript_PublicaPeloTextarea
// Contexto com JavaScriptEnabled = false: textarea visível, envio publica o texto.

Editor_ModoEscuro_UsaOsTokensDoTema
// Com data-bs-theme "dark" e depois "light", a cor de fundo computada de `.sc-editor .ProseMirror` muda.
```

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `$env:SECCO_UI_TESTS='1'; dotnet test --filter "FullyQualifiedName~EditorDoMuralTelaTests"`
Expected: FAIL, porque `.sc-editor` não existe. Se faltar o navegador, instale com o `playwright.ps1` da saída do build de testes.

- [ ] **Step 3: Trocar as dependências nos dois temas**

- `package.json`: sai `@toast-ui/editor`; entram `@tiptap/core`, `@tiptap/pm`, `@tiptap/starter-kit`, `@tiptap/extension-table` e `@tiptap/markdown`.
- Script `build:js`: `esbuild scripts/editor-bundle.js --bundle --minify --format=iife --global-name=seccoEditor --outfile=wwwroot/vendor/js/editor-tiptap.js --log-level=warning`.
- `copy-assets.mjs`: saem as cópias do Toast UI, o `i18n` e os comentários sobre eles.

- [ ] **Step 4: Implementar `scripts/tabela-markdown.js`**

Exporta `Tabela`. A saída nativa alinha colunas com espaços, gera `<br>` em célula e cabeçalho vazio, e o `style` que ela escreve viola a CSP. Este algoritmo foi verificado contra CSP estrita e na ida e volta do Markdown:

```js
const FOLHAS = new Set(['paragraph', 'heading', 'codeBlock']);

function blocosFolha(no, saida = []) {
  (no.content || []).forEach((filho) => {
    if (FOLHAS.has(filho.type)) {
      saida.push(filho);
    } else if (filho.content) {
      blocosFolha(filho, saida);
    }
  });
  return saida;
}

function celula(c, h) {
  return blocosFolha(c)
    .map((b) => (b.content ? h.renderChildren(b.content) : ''))
    .join(' ')
    .replace(/\s*\r?\n\s*/g, ' ')
    .replace(/<br\s*\/?>/gi, ' ')
    .replace(/\\.|\|/g, (m) => (m === '|' ? '\\|' : m))
    .replace(/\s+/g, ' ')
    .trim();
}

export const Tabela = Table.extend({
  renderHTML({ HTMLAttributes }) {
    return ['table', mergeAttributes(this.options.HTMLAttributes, HTMLAttributes), ['tbody', 0]];
  },
  renderMarkdown(node, h) {
    const linhas = (node.content || []).map((l) => (l.content || []).map((c) => celula(c, h)));
    const n = Math.max(0, ...linhas.map((l) => l.length));
    if (!n) {
      return '';
    }
    const fmt = (l) => `| ${Array.from({ length: n }, (_, i) => l[i] ?? '').join(' | ')} |`;
    return ['', fmt(linhas[0]), `| ${Array(n).fill('---').join(' | ')} |`, ...linhas.slice(1).map(fmt), ''].join('\n');
  },
});
```

- [ ] **Step 5: Implementar `criarEditor` em `scripts/editor-bundle.js`**

Valores fixos:

```js
injectCSS: false, // A <style> injetada violaria a CSP; o CSS vem do tema.
contentType: 'markdown',
extensions: [
  StarterKit.configure({
    underline: false,
    strike: false,
    link: { autolink: false, linkOnPaste: false, openOnClick: false, isAllowedUri: destinoPermitido },
  }),
  Tabela.configure({ resizable: false }),
  TableRow, TableHeader, TableCell, Markdown,
],
editorProps: { attributes: { role: 'textbox', 'aria-multiline': 'true', 'aria-label': 'Corpo' } },
```

`destinoPermitido(url)`: aceita caminho sem esquema e os esquemas `http`, `https`, `mailto` e `tel`. `aoMudar` recebe `editor.getMarkdown()` em `onUpdate`.

- [ ] **Step 6: Reescrever `wwwroot/js/editor.js`**

Mantém o contrato: procura `[data-sc-editor-markdown]`; sem o elemento ou sem `seccoEditor`, não faz nada; com erro ao criar, remove o contêiner e deixa o `textarea` funcionando; esconde o `textarea` só depois de o editor subir, com a classe `d-none`; sincroniza em `aoMudar` e no `submit`.

Barra montada com DOM API, em botões `type="button"` com `btn btn-sm btn-outline-secondary`, ícone `bi` e `aria-label`:

| `aria-label` | Comando | Ícone |
|---|---|---|
| Título | `toggleHeading({ level: 2 })` | `bi-type-h2` |
| Subtítulo | `toggleHeading({ level: 3 })` | `bi-type-h3` |
| Negrito | `toggleBold` | `bi-type-bold` |
| Itálico | `toggleItalic` | `bi-type-italic` |
| Lista | `toggleBulletList` | `bi-list-ul` |
| Lista numerada | `toggleOrderedList` | `bi-list-ol` |
| Citação | `toggleBlockquote` | `bi-quote` |
| Separador | `setHorizontalRule` | `bi-dash-lg` |
| Código | `toggleCode` | `bi-code` |
| Bloco de código | `toggleCodeBlock` | `bi-code-square` |
| Link | Abre o campo abaixo | `bi-link-45deg` |
| Inserir tabela | `insertTable({ rows: 3, cols: 2, withHeaderRow: true })` | `bi-table` |
| Desfazer / Refazer | `undo` / `redo` | `bi-arrow-counterclockwise` / `bi-arrow-clockwise` |

- Com o cursor em tabela, aparecem "Adicionar linha", "Remover linha", "Adicionar coluna", "Remover coluna" e "Remover tabela".
- O link usa um campo oculto por `hidden` na própria barra, com "Aplicar" e "Remover".
- O botão ativo recebe a classe `active` e `aria-pressed`.

- [ ] **Step 7: CSS nos dois temas**

- Sai `.sc-legenda`.
- `.sc-editor`, com borda e fundo pelos tokens `--sc-border`, `--sc-surface` e `--sc-text`, e altura mínima próxima dos atuais 320 px.
- As regras que o Tiptap injetaria, copiadas de `@tiptap/core/src/style.ts`.
- Tabela em `.sc-editor` e `.sc-card__body`: borda por `--sc-border`, padding nas células, e `display: block; overflow-x: auto; max-width: 100%`, para a tabela larga rolar dentro do card.

Rodar `npm install` e `npm run build` em cada tema.

- [ ] **Step 8: View e CSP**

- `Avisos.cshtml`: a seção `Scripts` passa a ter só `vendor/js/editor-tiptap.js` e `js/editor.js`, do caminho do Vertical.
- `EstilosDeTerceirosPermitidos` fica com `"display:none"` e o comentário é ajustado. Ajuste os testes que citavam os outros valores.

- [ ] **Step 9: Rodar e conferir**

```powershell
$env:SECCO_UI_TESTS='1'; dotnet test Secco.Intranet.slnx --nologo --verbosity quiet
```

Expected: 0 falhas, inclusive os demais testes de tela nos dois temas.

Depois, em cada tema: `npm audit` sem alto ou crítico, `npm ls dompurify` vazio e `git status` sem arquivo inesperado. Confira `diff` vazio entre os temas em `wwwroot/js/editor.js`, `scripts/editor-bundle.js` e `scripts/tabela-markdown.js`.

Navegue por `/diretorio` e a tela de foto em Enforce: a retirada dos valores de `style` não pode gerar violação ali.

- [ ] **Step 10: Commit**

```bash
git commit -m "feat(mural): editor Tiptap no lugar do Toast UI"
```

---

### Task 3: Limpeza da colagem e avisos

**Files (nos dois temas):**
- Create: `scripts/colagem.js`
- Modify: `scripts/editor-bundle.js`, `wwwroot/js/editor.js`, assets compilados
- Test: `tests/Secco.Intranet.Tests/Ui/EditorDoMuralTelaTests.cs`

**Interfaces:**
- Consumes: `criarEditor`, `.sc-editor__avisos`, `EditorDoMuralFixture`.
- Produces: `limparColagem(html: string): { html: string, avisos: string[] }`, exposta em `window.seccoEditor`.
- Produces: `criarEditor` passa a aceitar `aoColar: (avisos: string[]) => void`.
- Produces: helper de teste `Task ColarAsync(IPage pagina, string html, string? texto = null)`, que dispara `ClipboardEvent('paste')` com `DataTransfer` em `.sc-editor .ProseMirror`.

- [ ] **Step 1: Escrever os testes que falham**

Amostras fictícias no formato do Word: `xmlns:o="urn:schemas-microsoft-com:office:office"`, `class="MsoNormal"`, `<o:p>`, `<table class="MsoTableGrid">` e `<li style="mso-list:l0 level1 lfo1">`.

```csharp
Colar_TextoBasicoDoWord_PreservaTituloNegritoItalicoListaELink
// h1, <b>, <i>, ul/li com mso-list, e <a href="https://exemplo.com">:
// textarea com "# ", "**", "*", "- " e "](https://exemplo.com)". Nenhum aviso.

Colar_TextoQuePareceDominio_NaoViraLink
// "<p>Usamos ASP.NET aqui</p>": textarea sem "](" e sem "http".

Colar_TabelaSemCabecalho_PrimeiraLinhaViraCabecalho
// Duas linhas de td: "| Sala | Dia |" é a 1ª linha da tabela, seguida de "| --- | --- |".
// Publicada, a página não mostra "<" nem "##".

Colar_TituloEQuebraEListaEmCelula_FicamNaMesmaLinha
// Célula com <h2>T</h2>, outra com "a<br>b", outra com ul de dois itens:
// sem "##", "<br>" e "- "; contém "**T**", "a b" e "x y".

Colar_CelulaMesclada_ExpandeEAvisa       // colspan=2 e rowspan=2
// Toda linha do Markdown tem o mesmo número de colunas. Aviso visível com o texto da spec.

Colar_TabelaDentroDeTabela_MantemOTextoEAvisa
// A externa fica, com o texto da interna na célula. Aviso visível.

Colar_LinhasDeTamanhosDiferentes_MantemATabelaValida          // Review Focus 3
// Linha com menos td: colunas iguais em todas as linhas e texto na coluna original.

Colar_TabelaDentroDeCelulaExistente_MantemTabelaValida        // Review Focus 4
// Cursor numa célula de tabela do editor, cola uma tabela:
// o Markdown lido de volta tem uma tabela só, com o texto colado na célula.

Colar_Imagem_DescartaEAvisa              // <img src="file:///C:/x/clip_image001.png">
// O textarea não contém "![" nem "file:". Aviso visível.

Colar_TextoPuro_InsereSemAviso           // Review Focus 1
// Só text/plain: o texto entra, sem aviso.

Colar_LinkComEsquemaProibido_SaiSemLink  // <a href="javascript:alert(1)">x</a>
// Na publicação lida, "x" sem <a>.

Aviso_Fechar_Remove
// O botão fechar remove o aviso; duas colagens iguais mostram um aviso só.
```

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `$env:SECCO_UI_TESTS='1'; dotnet test --filter "FullyQualifiedName~EditorDoMuralTelaTests"`
Expected: os novos falham.

- [ ] **Step 3: Implementar `limparColagem`**

Interpreta com `new DOMParser().parseFromString(html, 'text/html')`, que não executa nada. Regras:

| Entrada | Saída |
|---|---|
| Primeira linha de cada `table` | `td` vira `th` |
| Título em célula | `<strong>` com o texto |
| Quebra, parágrafos ou lista em célula | Texto na mesma linha, separado por espaço |
| `colspan` ou `rowspan` maior que 1 | Grade expandida: primeira célula com o texto, as demais vazias, sem os atributos, mais `AVISO_MESCLADA` |
| Linha com menos células | Completada com células vazias até a maior largura |
| `table` dentro de célula | Substituída pelo texto dela, mais `AVISO_ANINHADA` |
| Parágrafo vazio, ou só com `&nbsp;` ou `<o:p>` | Removido |
| `img` | Removida, mais `AVISO_IMAGEM` |

Textos exatos, sem repetir aviso:

```js
export const AVISO_MESCLADA = 'Esta tabela tinha células mescladas, que não são suportadas. Confira o resultado ou envie a tabela como imagem.';
export const AVISO_ANINHADA = 'Esta tabela tinha outra tabela dentro dela, o que não é suportado. Confira o resultado ou envie a tabela como imagem.';
export const AVISO_IMAGEM = 'A imagem não foi colada. Use o campo Imagem.';
```

A spec usa um único aviso para os dois casos de tabela. `AVISO_ANINHADA` existe porque "células mescladas" seria falso para esse caso.

Ligue em `editorProps.transformPastedHTML`, chamando `aoColar(avisos)` e devolvendo o `html`.

- [ ] **Step 4: Avisos em `editor.js`**

Em `.sc-editor__avisos`, um `alert alert-warning alert-dismissible` por texto, montado com DOM API e `textContent`, com botão `btn-close` e `data-bs-dismiss="alert"`. O contêiner tem `role="status"`. Rodar `npm run build` nos dois temas.

- [ ] **Step 5: Rodar**

Run: `$env:SECCO_UI_TESTS='1'; dotnet test --filter "FullyQualifiedName~EditorDoMuralTelaTests"`
Expected: 0 falhas.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(mural): limpa a colagem do Word e avisa o que nao e suportado"
```

---

### Task 4: Segurança, ida e volta, e fechamento

**Files:**
- Test: `tests/Secco.Intranet.Tests/Ui/EditorDoMuralTelaTests.cs`
- Modify: `docs/roadmap.md`, `docs/specs/2026-10-31-imagem-na-publicacao-design.md`, `docs/specs/2026-11-06-editor-do-mural-design.md`

**Interfaces:**
- Consumes: `EditorDoMuralFixture`, `ColarAsync`.
- Produces: `EditorDoMuralFixture` ganha um terceiro app, com `Intranet:Csp:Modo=ReportOnly`, para a CSP não mascarar o resultado dos ataques.

- [ ] **Step 1: Escrever os testes**

`Editor_ConteudoHostil_NaoExecuta`, um caso por carga, no app em `ReportOnly`. Grava com `CriarPublicacaoAsync`, define `window.__xss = []` com `AddInitScriptAsync`, abre a edição e espera o editor:

```text
<script>window.__xss.push('script')</script>
<img src=x onerror="window.__xss.push('imgOnerror')">
<svg onload="window.__xss.push('svgOnload')"></svg>
<iframe srcdoc="<script>parent.__xss.push('iframe')</script>"></iframe>
<details open ontoggle="window.__xss.push('detalhes')">x</details>
<math><mtext><table><mglyph><style><img src=x onerror="window.__xss.push('mxss')">
[clique](javascript:window.__xss.push('linkJs'))
[clique](data:text/html,<script>window.__xss.push('linkData')</script>)
![a](x" onerror="window.__xss.push('imgAtributo'))
![a](javascript:window.__xss.push('imgJs'))
| a |\n| --- |\n| <img src=x onerror="window.__xss.push('celula')"> |
```

e um bloco de código com `<img src=x onerror="window.__xss.push('codigo')">`. Assertivas: `window.__xss` vazio; nenhum elemento com atributo `on*` em `.sc-editor`; nenhum `script`, `iframe`, `svg`, `math`, `details`, `object` ou `embed` em `.sc-editor`; nenhum `a[href]` começando por `javascript:` ou `data:`.

Depois, no mesmo caso, envia o formulário sem editar e confere em `/publicacoes/{id}` que `window.__xss` continua vazio.

```csharp
Editor_SalvarEReabrir_MantemOTextoVisivel
// Corpo com "1 < 2", "a * b", "a\|b" em célula, título, lista e tabela. Publica, lê o texto
// de `.sc-card__body`, edita sem mudar nada e salva: o texto visível é igual, e uma segunda
// ida e volta não altera mais o Markdown.

Publicar_CorpoAcimaDoLimite_MantemOTextoNoEditor              // Review Focus 2
// Preenche o editor com 20.001 caracteres e envia: a mensagem de limite aparece
// e o texto continua no editor.
```

- [ ] **Step 2: Rodar**

Run: `$env:SECCO_UI_TESTS='1'; dotnet test --filter "FullyQualifiedName~EditorDoMuralTelaTests"`
Expected: 0 falhas. **Se uma carga executar, pare e reporte:** é o motivo da troca, e o teste não pode ser enfraquecido.

- [ ] **Step 3: Documentar**

- **`roadmap.md`:** registrar a entrega na seção de administração entregue, com o link da spec, e trocar na linha da CSP a menção ao Toast UI pelo valor que restou.
- **Spec de 2026-10-31:** linha "Revisada por".
- **Spec desta entrega:** `Estado` para "etapa 1 entregue, etapa 2 a implementar".
- **Memória `toastui-dompurify-vulneravel`:** remover, porque a pendência foi resolvida.

- [ ] **Step 4: Verificação final**

```powershell
$env:SECCO_UI_TESTS='1'; dotnet test Secco.Intranet.slnx --nologo --verbosity quiet
```

Expected: 0 falhas. Em seguida, `Remove-Item Env:SECCO_UI_TESTS` e a suíte normal, como o CI roda.

- [ ] **Step 5: Commit e push**

A mensagem registra a quebra, como pede a spec:

```bash
git commit -m "test(mural): conteudo hostil e ida e volta no editor; roadmap

Quebra: um aviso antigo com imagem ou tachado no texto perde esses elementos ao ser reaberto e salvo no editor. Ha migration a aplicar pelo migrate."
git push origin main
```

Confira que o CI do push passou, nos jobs `build-test` e `theme-assets`.

- [ ] **Step 6: Colagem de um Word real**

Peça ao usuário para colar um documento real na tela do aviso, com título, lista, tabela simples, tabela mesclada e imagem. O evento simulado não prova o comportamento do Word. Registre qualquer divergência antes de iniciar a etapa 2.
