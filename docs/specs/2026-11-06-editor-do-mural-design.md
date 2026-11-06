# Editor do Mural: colar do Word, tabela e imagem no texto — desenho do recurso

**Data:** 2026-11-06
**Estado:** aprovada, a implementar
**Revisa:** "Imagem no meio do texto" e "Nenhum caminho deixa a imagem dentro do texto" de [2026-10-31-imagem-na-publicacao-design.md](2026-10-31-imagem-na-publicacao-design.md). A primeira deixa de estar fora do escopo; a segunda continua valendo para imagem embutida em base64.

## Problema

**Segurança.** O formulário de aviso usa o Toast UI Editor 3.2.2, sem publicação desde fevereiro de 2023. Ele embute o DOMPurify 2.5.9, que o `npm audit` marca com alerta alto e não tem correção por esse caminho. A edição autoriza pela escrita no setor, não pelo autor (`EditarPublicacaoHandler`), e o editor abre em WYSIWYG (`editor.js`). Por isso, o texto de uma pessoa carrega no editor de outra.

**Uso.** Os avisos são publicados por gestores e subordinados. Eles não conhecem Markdown, e o caminho mais comum será escrever no Word e colar. Hoje:

- Tabela, tachado e lista de tarefas aparecem no editor, mas o leitor os vê como texto cru (`| Dia | Sala |`, `~~tachado~~`, `[ ] tarefa`). O pipeline do `RenderizadorMarkdown` só tem `DisableHtml` e `UseAutoLinks`.
- Documentos comuns não cabem no limite de 4.000 caracteres do corpo.
- Não há imagem no meio do texto.

## Objetivo

1. Quem escreve cola do Word e vê o que o leitor verá, sem conhecer Markdown.
2. Abrir o texto de um colega no editor não expõe quem edita a um sanitizador vulnerável.
3. Tabela simples funciona de ponta a ponta. O que o Markdown não representa gera aviso, nunca perda silenciosa.
4. O aviso aceita imagem no meio do texto, e a colagem do Word a traz sozinha sempre que possível.

## Fora do escopo

- **Mudar quem pode publicar, editar ou ver.** Isso continua definido pelo `intranet-admin` nas permissões atuais.
- **Representar célula mesclada** e guardar HTML no corpo. O corpo continua Markdown, e o servidor continua sem aceitar HTML bruto.
- **Preservar cor, fonte, tamanho e sublinhado do Word.**
- **Rascunho automático** contra perda de texto quando a sessão expira. É um risco que já existe hoje.
- **Modo Markdown cru no editor.** O `textarea` continua só como alternativa quando o script não carrega.
- **Imagem no e-mail ou no sino.** As notificações seguem com título, resumo e link.
- **Unificar a imagem final do aviso com as imagens do texto.**
- **Tirar do core o caminho fixo do tema Vertical.** O item segue no roadmap, com o Marketplace de temas.
- **Compatibilidade com avisos antigos.** Não há dado de produção a preservar (ver [Quebras](#quebras-a-registrar-na-entrega)).

## Evidência

Teste descartável em 2026-11-06, com Tiptap 3.33.0, o Markdig 1.6.0 no pipeline do produto mais `UsePipeTables`, e documentos reais colados do Word. Nenhum artefato do teste entra no repositório.

| Item | Resultado |
|---|---|
| Títulos, negrito e links reais | Preservados |
| Listas com marcadores | Preservadas, 7 de 7 itens, sem limpeza extra |
| Tabela | Conteúdo preservado, com linha de cabeçalho vazia em cima |
| Célula mesclada | Perdida: 7 células mescladas, 45 células viram 60 |
| Título em célula | O leitor vê `##` |
| Quebra de linha em célula | O leitor vê `<br>` |
| Sublinhado | O leitor vê `<u>` |
| Texto como "ASP.NET" | Vira link: 0 links no original, 20 ou mais no editor |
| Tamanho do Markdown | 4.359 a 11.145 caracteres nos trechos testados |
| Imagem no HTML do Word | `file:///.../msohtmlclip/...`, ilegível para o navegador, sem arquivo na área de transferência |
| Imagem no RTF do Word | JPEG de 859×1280 (175 KB), legível, mais uma cópia WMF a ignorar. O RTF inteiro tinha 7 MB. **Testado com uma imagem só** |
| Pacote | 163 KB comprimidos com tabela e Markdown, contra 177 KB do Toast UI |
| Dependências | Sem DOMPurify na árvore e `npm audit` zerado |
| Conteúdo hostil carregado no editor, sem CSP | 12 cargas (`<script>`, `onerror`, `svg`, `iframe`, `ontoggle`, mXSS, `javascript:`, `data:`, célula e bloco de código): nenhuma executou. `![](javascript:...)` chega ao `src`, sem executar |

A amostra é pequena e o Word usado foi um só. O plano começa repetindo a colagem com mais de uma imagem.

## Decisões

| Eixo | Decisão | De quem |
|---|---|---|
| Caminho principal | Colar do Word | Usuário |
| Permissões | Sem mudança, definidas pelo `intranet-admin` | Usuário |
| Tabela | Mantida, simples, em Markdown | Usuário |
| Tabela complexa | Enviada como imagem | Usuário |
| Imagem no meio do texto | Dentro do escopo | Usuário |
| Editor | Tiptap, por ser MIT, ativo e passar nos testes acima | Autor da spec |
| Barra do editor | Botões do Bootstrap com Bootstrap Icons, e CSS pelos tokens do tema, como o usuário sugeriu aproveitar | Autor da spec |
| Formato guardado | Markdown, como hoje | Autor da spec |
| Limite do corpo | 20.000 caracteres | Autor da spec |
| Primeira linha da tabela | Vira cabeçalho, porque a tabela em Markdown exige um | Autor da spec |
| Imagem final do aviso | Continua, para o aviso que é só um cartaz | Autor da spec |
| Imagens no texto | Até 10 por aviso, com os limites atuais por arquivo | Autor da spec |
| Envio das imagens | Junto com o formulário, sem envio antecipado | Autor da spec |
| Sequência | Duas etapas, com a primeira entregue antes por resolver o risco de segurança | Autor da spec |

## Etapa 1: trocar o editor e corrigir a tabela

### Editor

`wwwroot/js/editor.js` continua sendo melhoria progressiva sobre o `textarea[data-sc-editor-markdown]`, que segue como o campo enviado.

- **Extensões:** núcleo do Tiptap, tabela e Markdown oficiais, com o mínimo necessário do `StarterKit`. Sem sublinhado, tachado e lista de tarefas.
- **Link:** sem `autolink` e sem transformar texto colado em link. Protocolos `http`, `https`, `mailto` e `tel`, os mesmos do `RenderizadorMarkdown`.
- **Barra:** título, negrito, itálico, listas, citação, separador, tabela (inserir e remover linha e coluna), link, código, bloco de código, desfazer e refazer. Cada botão tem `aria-label` em português e funciona por teclado.
- **Saem:** o seletor de modo, a legenda "Sintaxe do Markdown", o CSS do Toast UI e o `MutationObserver` do modo escuro. O tema escuro passa a vir dos tokens.
- **Versões:** as estáveis mais recentes no momento da implementação, consultadas ao vivo.

Os arquivos seguem a estrutura atual: o mesmo `editor.js` e o mesmo bundle nos dois temas, e `Views/Setor/Avisos.cshtml` apontando para o Vertical. O CSS do editor (`.sc-editor`) entra no `_components.scss` de **cada** tema.

Saem dos dois temas `@toast-ui/editor`, os passos em `copy-assets.mjs` e `editor-bundle.js`, e os arquivos em `wwwroot/vendor`.

### Limpeza na colagem

Roda antes de o conteúdo entrar no editor, para o que a pessoa vê ser o que o leitor verá.

| O Word manda | O editor recebe |
|---|---|
| Tabela sem cabeçalho | Primeira linha como cabeçalho |
| Título em célula | Texto em negrito |
| Quebra, parágrafos ou lista em célula | Texto na mesma linha, separado por espaço |
| Célula mesclada | Células normais, a primeira com o texto e as outras vazias, mais o aviso abaixo |
| Tabela dentro de tabela | Tabela externa com o texto da interna, mais o aviso abaixo |
| `&nbsp;` isolado e parágrafo vazio | Removidos |
| Imagem | Na etapa 1: descartada, mais o aviso abaixo |

Os avisos aparecem junto ao editor até a pessoa fechá-los:

- *"Esta tabela tinha células mescladas, que não são suportadas. Confira o resultado ou envie a tabela como imagem."*
- *"A imagem não foi colada. Use o campo Imagem."* (substituído na etapa 2)

Se a saída em Markdown vier com colunas alinhadas por espaços, ela é compactada, porque esse preenchimento consome boa parte do limite. Se a extensão não permitir, a limpeza fica de fora: o limite de 20.000 já comporta o tamanho observado.

### Servidor

- **`RenderizadorMarkdown`:** acrescenta `UsePipeTables()`. `DisableHtml` e a lista de esquemas não mudam.
- **Resumo da notificação:** `PipelineDeTexto` em `PublicarPublicacaoHandler` passa a entender tabela, e o resumo não pode trazer `|` nem `---`.
- **Limite:** `ValidacaoDePublicacao.LimiteDoCorpo` e a mensagem `CorpoTooLong` vão a 20.000. A coluna `ds_corpo` muda por migration nos dois providers, gerada pelo `dotnet ef`.
- **Leitura:** tabela larga rola dentro do próprio card, sem quebrar o layout, nos dois temas.

### CSP

`EstilosDeTerceirosPermitidos` perde os valores que só existiam pelo Toast UI e fica com o necessário, como o `display:none` do `asp-validation-summary`. Se o Tiptap escrever `style` em tabela, a preferência é evitar por configuração. A exceção por valor exato fica como último recurso, porque um valor variável, como largura de coluna, não cabe nela.

## Etapa 2: imagem no meio do texto

### Como a imagem entra

| Origem | Comportamento |
|---|---|
| Colagem do Word | Lê o `text/rtf` e associa a N-ésima imagem `file://` do HTML à N-ésima imagem PNG ou JPEG do RTF, ignorando `\nonshppict` |
| Imagem copiada sozinha ou print | Usa o arquivo da área de transferência |
| Botão "Imagem" ou arrastar | Abre o seletor de arquivo ou usa o arquivo solto |
| Não recuperável | Fica um marcador visível no lugar: *"Esta imagem não veio do Word. Clique para enviar o arquivo."* |

**Não recuperável** inclui RTF sem imagem, só EMF ou WMF, RTF acima de 50 MB e imagem externa com `http`. Se o número de imagens do HTML e do RTF não bater, **todas** viram marcador, porque imagem certa no lugar errado é pior que imagem nenhuma.

O formulário não envia enquanto houver marcador, e nunca salva `file://`, `blob:` ou `data:` no corpo.

### Envio

A imagem fica no navegador até o envio, e nenhum arquivo existe no servidor antes de o aviso ser salvo. Isso dispensa rascunho, limpeza de órfãos e uma rota nova de upload.

1. O editor mostra a imagem por URL `blob:` e, no Markdown, usa `![descrição](nova-imagem:N)`.
2. No envio, os arquivos vão num campo múltiplo do mesmo formulário, na ordem de N.
3. O servidor processa e grava cada um, depois troca os marcadores por `/publicacoes/{id}/imagens/{imagemId}`.

### Validação do corpo no servidor

Vale sempre, inclusive quando o `textarea` é enviado sem o script. Destino de imagem no corpo:

| Destino | Tratamento |
|---|---|
| `nova-imagem:N` com arquivo correspondente | Aceito |
| `nova-imagem:N` sem arquivo | Recusado |
| Imagem desta publicação | Aceito |
| Caminho relativo, inclusive imagem de outra publicação | Recusado |
| Externo com `http` ou `https` | Aceito; o `RenderizadorMarkdown` já o transforma em link |

Arquivo sem marcador é ignorado e não é gravado. Mais de 10 imagens ou arquivo acima do limite recusa o envio com a mensagem de validação do formulário.

### Armazenamento e leitura

- **Tabela nova** ligada à publicação, com caminho, chave embrulhada e descrição, seguindo a convenção de nomes do banco. Migration nos dois providers.
- **Processamento e gravação:** os mesmos da imagem atual (`ProcessarImagemDePublicacaoAsync`, `PastasDeArquivo.ImagensDoMural`).
- **Rota `/publicacoes/{id}/imagens/{imagemId}`:** mesma autorização, mesmos cabeçalhos e mesmos 404 de `/publicacoes/{id}/imagem`. Uma imagem só é servida pela publicação a que pertence.
- **Ordem de gravação:** como hoje. Grava as novas, salva, e só então apaga as que saíram do corpo. Falha ao salvar apaga as recém-gravadas.
- **Arquivar:** mantém as imagens, como hoje.
- **Auditoria:** `mural.publicar` e `mural.editar` passam a registrar a quantidade de imagens no texto.
- **Limite da requisição:** `SalvarAviso` ganha limite explícito que comporte 11 arquivos de 10 MB.
- **CSP:** a pré-visualização precisa de `blob:` em `img-src`. O `recorte-foto.js` já usa `createObjectURL`; confirmar a política em Enforce e, se faltar, usar `ImageSources` do SDK.

## Segurança (ADR-0020)

- **Risco principal:** deixa de haver um sanitizador vulnerável entre o texto do colega e quem edita. O conteúdo passa pelo esquema do editor, que descarta o que não conhece, e a CSP em Enforce continua como segunda barreira.
- **Limite da evidência:** 12 cargas não provam ausência de falha. O critério de aceite roda o mesmo conjunto com a CSP desligada, e ele passa a ser teste permanente.
- **Destino de imagem no editor:** carregar `![](/qualquer/rota)` geraria um GET autenticado na tela de quem edita. O editor só carrega imagem desta publicação ou `blob:`, e mostra os outros destinos como marcador.
- **Quem lê:** a proteção não muda. O servidor gera o HTML a partir do Markdown, com `DisableHtml` e a lista de esquemas.
- **Colagem:** o HTML e o RTF do Word são entrada não confiável. O RTF só é lido até o limite de tamanho, e o servidor revalida cada imagem pelo conteúdo com o `Secco.SDK.Imaging`.
- **Negação de serviço:** limites de quantidade, de tamanho por arquivo e de requisição. Só quem tem escrita no setor envia, e as imagens são processadas uma de cada vez.
- **Tenant:** imagem e publicação vivem no banco e no armazenamento do tenant da requisição.
- **Dependências:** `@tiptap/*` são MIT e ativos. A árvore é auditada de novo na implementação.

## Critérios de aceite

### Etapa 1

- [ ] Sem `@toast-ui/editor` e sem DOMPurify nos dois temas, e `npm audit` sem alerta alto ou crítico.
- [ ] Colar do Word preserva títulos, negrito, itálico, listas e links reais.
- [ ] Texto como "ASP.NET" não vira link ao colar.
- [ ] Tabela colada aparece igual no editor e para o leitor, sem linha vazia em cima, `##` ou `<br>` visíveis.
- [ ] Tabela com célula mesclada mostra o aviso.
- [ ] Imagem colada mostra o aviso, em vez de sumir em silêncio.
- [ ] Salvar e reabrir um aviso não altera o texto, inclusive com `<`, `|` e `*` literais.
- [ ] As 12 cargas hostis não executam ao abrir no editor com a CSP desligada.
- [ ] Sem violação de CSP ao usar o editor em Enforce, nos dois temas e nos modos claro e escuro.
- [ ] O formulário funciona sem o script, pelo `textarea`.
- [ ] Corpo de até 20.000 caracteres é aceito e, acima disso, recusado, nos dois providers.
- [ ] O resumo da notificação de um aviso com tabela não traz `|` nem `---`.

### Etapa 2

- [ ] Colar do Word com duas ou mais imagens as traz na ordem certa, ou mostra marcadores.
- [ ] Print e imagem copiada sozinha entram por colagem, e arquivo por botão ou arrasto.
- [ ] O aviso publicado mostra as imagens na posição escolhida, em WebP, a quem pode ver o aviso.
- [ ] Quem não pode ver o aviso recebe 404, também pela URL direta da imagem.
- [ ] O formulário não envia com marcador pendente.
- [ ] Enviar `nova-imagem:N` sem arquivo, ou imagem de outra publicação no corpo, é recusado.
- [ ] Tirar uma imagem do corpo e salvar remove a linha e o arquivo.
- [ ] Falha ao salvar não deixa arquivo novo para trás.
- [ ] O corpo nunca guarda `file:`, `blob:` ou `data:`.
- [ ] A imagem final do aviso continua funcionando como hoje.

## Testes

| Onde | Cobertura |
|---|---|
| `Unit/RenderizadorMarkdownTests` | Tabela renderizada; HTML em célula continua escapado; imagem da publicação continua como imagem |
| Testes do resumo em `PublicarPublicacaoHandler` | Tabela e imagem fora do resumo |
| Handlers de publicar e editar | Limite novo; marcadores; imagem de outra publicação; arquivo sem marcador; apagar as que saíram; descarte em falha |
| `Integration` do formulário e da rota | Fluxo por publicar, editar e ler; 404 por visibilidade; antiforgery; limites |
| `MigracoesPorEngineTests` | Já cobre as migrations novas nos dois providers |
| Playwright, opt-in como os demais de tela | Colagem simulada com HTML e RTF fictícios no formato do Word; cargas hostis; salvar e reabrir; CSP em Enforce; dois temas |
| Manual, no roteiro da entrega | Colagem de um Word real, porque o evento simulado não prova o comportamento do Word |

As amostras versionadas são fictícias, nunca documentos reais.

## Documentação

- **[Roadmap](../roadmap.md):** registrar a entrega de cada etapa e revisar a menção ao Toast UI na linha da CSP.
- **[Spec da imagem na publicação](2026-10-31-imagem-na-publicacao-design.md):** linha "Revisada por" apontando para esta.
- **`IntranetSecurityHeadersExtensions`:** ajustar o comentário sobre os valores liberados.

Não vejo necessidade de ADR nova: a troca é de biblioteca do formulário, reversível, e o formato guardado continua Markdown. Também não vi capacidade de plataforma aqui, porque a colagem é só do formulário deste produto e o processamento de imagem já vem do SDK. Se outro produto quiser o mesmo editor, a extração do RTF pode ir para a plataforma depois.

## Quebras a registrar na entrega

- **Etapa 1:** um aviso antigo com `![](...)` ou tachado perde esses elementos ao ser reaberto e salvo no editor. O texto salvo não muda até alguém editar.
- **Etapa 2:** um corpo com imagem de caminho relativo que não seja desta publicação passa a ser recusado ao salvar.
- **Ambas:** há migrations a aplicar pelo `migrate`.

## O que fica para quem implementa

- Nomes de classes, da tabela nova e das mensagens de erro.
- Componente visual do link e do menu de tabela, sem `prompt()` e sem script ou estilo inline.
- Como pedir a descrição da imagem. Ela é opcional e tem o limite atual de 300 caracteres; imagem colada entra sem descrição e pode recebê-la depois.
- Divisão dos commits dentro de cada etapa.
