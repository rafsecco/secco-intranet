# Escrevendo um tema para a Secco.Intranet

Um tema controla a aparência inteira da Intranet sem tocar em nenhuma view de página. A
decisão que sustenta isso está em [ADR-0003](adr/secco-intranet-adrs.md#adr-0003-sistema-de-temas-via-razor-class-library-rcl)
e [ADR-0004](adr/secco-intranet-adrs.md#adr-0004-contrato-de-tema-em-projeto-próprio-seccointranetwebtheming).

> **A regra:** o core decide *o que* a página mostra; o tema decide *como* ela parece.

O core não sabe qual framework CSS você usa — carrega o que o seu tema declarar no
`_Layout`. O tema padrão usa Bootstrap, mas isso não é exigência.

Dois temas de saída existem hoje, e vale ler os dois antes de escrever o seu:
`Secco.Intranet.Themes.Vertical` (padrão, menu lateral) e `Secco.Intranet.Themes.Horizontal`
(menu no topo, paleta e tipografia próprias). São a prova de que o contrato abaixo não
esconde uma suposição do primeiro tema — o segundo reaproveita `_PageHeader`, `_Card`,
`_Badge`, `_EmptyState` e `_Pagination` **sem alterar uma linha**, e reescreve só o que é
genuinamente estrutural: cabeçalho, navegação e a folha de estilo.

## Estrutura

Um tema é uma Razor Class Library que referencia **apenas** `Secco.Intranet.Web.Theming`:

```
Secco.Intranet.Themes.<Nome>/
├── Secco.Intranet.Themes.<Nome>.csproj   ← Sdk="Microsoft.NET.Sdk.Razor"
├── Themes/<Nome>/Views/
│   ├── _ViewImports.cshtml
│   └── Shared/
│       ├── _Layout.cshtml
│       ├── _PageHeader.cshtml
│       ├── _Card.cshtml
│       ├── _Badge.cshtml
│       ├── _EmptyState.cshtml
│       ├── _Pagination.cshtml
│       └── Components/
│           ├── Navigation/Default.cshtml
│           ├── UserMenu/Default.cshtml
│           └── Feedback/Default.cshtml
└── wwwroot/                              ← servido em _content/<assembly>/
```

O caminho `Themes/<Nome>/Views` não é decorativo: é onde o `ThemeViewLocationExpander`
procura, e `<Nome>` é o valor de `Intranet:Theme:Nome`.

## O contrato

Cada arquivo acima recebe um modelo de `Secco.Intranet.Web.Theming.Contracts`:

| Arquivo | Modelo | Papel |
|---|---|---|
| `_Layout.cshtml` | — | Casca da página: cabeçalho, menu, área de conteúdo |
| `_PageHeader.cshtml` | `PageHeaderModel` | Título, linha de apoio e ações da página |
| `_Card.cshtml` | `CardModel` | Card de conteúdo das listagens |
| `_Badge.cshtml` | `BadgeModel` | Etiqueta curta de classificação |
| `_EmptyState.cshtml` | `EmptyStateModel` | Tela vazia, com a ação que a resolve |
| `_Pagination.cshtml` | `PaginationModel` | Navegação entre páginas |
| `Components/Navigation/Default.cshtml` | `NavigationModel` | Menu já resolvido para o usuário |
| `Components/UserMenu/Default.cshtml` | `UserMenuModel` | Identidade e menu do avatar |
| `Components/Notificacoes/Default.cshtml` | `NotificacoesModel` | Sino de notificações da barra superior |
| `Components/Feedback/Default.cshtml` | `ToastModel` | Confirmação da ação que acabou de acontecer |

O `Feedback` é invocado **uma vez pelo `_Layout`**, e não pelas views de página: o controller
deixa a mensagem no `TempData` antes do redirect e o tema decide como ela aparece. Duas
consequências para quem escreve um tema: o componente não renderiza nada quando não há
mensagem — então o markup precisa suportar estar ausente —, e o aviso deve **chegar visível do
servidor**, com o JS servindo só para fechá-lo. Um tema que dependa de script para exibir a
confirmação engole o retorno da ação quando o script falha.

O sino mostra **apenas notificações não lidas**, e não oferece "marcar todas como lidas": o
`Secco.NotificationHub` expõe contar não lidas, listar não lidas e marcar **uma** como lida, e
um botão de marcar todas viraria uma chamada por item. Um tema pode mudar a aparência do sino
à vontade, mas não deve prometer o que a capacidade não entrega.

Não existe view de fallback no core: um tema que não traga um destes arquivos quebra em toda
página que o use. Ao acrescentar um item a este contrato, acrescente nos **dois** temas
publicados.

A lógica dos view components fica no core — o tema entrega só o markup. Por isso
nenhuma view de tema injeta serviço.

O `_Layout` precisa expor uma âncora `id="conteudo"` para o link de pular navegação, e
renderizar a seção opcional `Scripts`.

O menu (`NavigationModel`) é uma árvore: `NavigationItemModel.Filhos` traz os subitens, e
`Url` nulo quer dizer que o item só agrupa — é o caso de todo setor, que é o nível 0 da seção
"Setores" e não tem página própria. `Ativo` marca o caminho inteiro até a página atual; use
`aria-current="page"` só no item ativo sem filho ativo. Os dois temas publicados renderizam a
árvore inteira no servidor, em listas aninhadas (agrupador = `<button aria-expanded>`), e um
script **compartilhado pelo contrato** transforma cada lista em painel flutuante:

```cshtml
<script src="~/_content/Secco.Intranet.Web.Theming/js/menu-arvore.js" asp-append-version="true"></script>
```

O tema inclui o script no `_Layout` e só precisa seguir a marcação: gatilho com
`data-sc-submenu` e `aria-controls` apontando para um `<ul class="sc-nav__sub" data-nivel="N">`,
nó com filhos em `<li class="sc-nav__node">`, e `data-sc-abre-abaixo` no `<ul class="sc-nav">`
quando a barra fica no topo (o nível 1 abre abaixo do item; sem o atributo, abre à direita).
Duas armadilhas o script já resolve, e um tema com script próprio teria de resolver: o menu mora
num contêiner com rolagem, então o painel precisa de `position: fixed` com a posição calculada
(um painel `absolute` é cortado); e sem script a árvore tem de aparecer aberta, para tudo
continuar alcançável. Uma terceira fica com o CSS do tema: o contêiner do menu cria contexto de
empilhamento (`sticky`), então ele precisa de `z-index` acima do conteúdo, senão o painel fica
atrás dos cards.

**Testes de tela.** `tests/Secco.Intranet.Tests/Ui` roda o menu num Chromium de verdade, nos dois
temas (hover, clique, teclado, celular, sem JavaScript). São opt-in: instale o navegador uma vez
e ligue a variável.

```powershell
dotnet build tests/Secco.Intranet.Tests
./tests/Secco.Intranet.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
$env:SECCO_UI_TESTS = '1'; dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~Ui"
```

Sem a variável eles ficam ignorados. Mexeu em CSS ou JS de tema? Rode-os — e recompile antes:
o host serve as versões pré-comprimidas geradas no build, não o arquivo do `wwwroot`.

O tema decide como os grupos aparecem. O Vertical mostra o título do grupo como rótulo e os
itens embaixo; o Horizontal, sem largura para uma fileira por setor, transforma cada grupo com
título num nó que só agrupa (Setores ▾, Administração ▾) e renderiza os itens um nível abaixo.
Nada disso muda o contrato — é só outra forma de desenhar a mesma `NavigationModel`.

Ações irreversíveis (excluir perfil, excluir item de menu) vêm em
`<form data-confirmar="Pergunta?">`. O tema precisa, no JS dele, pedir confirmação com esse
texto antes de enviar o formulário e cancelar o envio se a pessoa desistir — os dois temas
publicados fazem isso em `wwwroot/js/theme.js`, com um listener de `submit` no `document`
(vale para qualquer view, inclusive as que ainda não existem). Sem o script, o formulário
envia direto: um tema que esqueça disso não quebra nada, mas perde a confirmação.

## Modo claro e escuro é obrigatório

Todo tema publicado declara as duas variantes por `data-bs-theme`. Não basta inverter as
cores: cores saturadas costumam precisar de tom diferente entre as variantes para manter
contraste legível — no tema padrão, a primária **escurece** no claro e **clareia** no escuro.

Defina a paleta completa em `:root`; redefina **apenas** os tokens nos blocos de variante.
Nenhuma cor pode existir só dentro de uma media query:

```scss
:root                      { --sc-primary: #0B8A64; --sc-surface: #FFFFFF; }
@media (prefers-color-scheme: dark) {
  :root:not([data-bs-theme="light"]) { --sc-primary: #34D399; --sc-surface: #161D26; }
}
[data-bs-theme="dark"]     { --sc-primary: #34D399; --sc-surface: #161D26; }
```

Os três blocos são necessários: o primeiro cobre o padrão, o segundo a preferência do
sistema, o terceiro a escolha explícita do usuário — e o `:not()` garante que escolher
"claro" vença um sistema em modo escuro.

Para não piscar no modo errado a cada carregamento, o `_Layout` aplica a preferência
guardada **antes** da primeira pintura, num script inline no `<head>`.

## A cor do setor

Todo conteúdo da Intranet pertence a um setor, e o tema padrão transforma isso em
informação: `SetorHue.From(slug)` devolve um matiz estável, exposto como `--sc-setor-hue` no
elemento. Saturação e luminosidade ficam por conta do tema, fixas por modo, para o contraste
não depender da cor sorteada.

Seu tema não é obrigado a usar isso — mas se usar cor por setor, use `SetorHue`: é o que faz
o mesmo setor ter a mesma cor no menu, no card e no cabeçalho.

## Piso de qualidade

Um tema aceito no projeto entrega: layout responsivo com o menu acessível em telas
estreitas, foco de teclado visível, link de pular navegação, `aria-current` no item ativo e
`prefers-reduced-motion` respeitado.

## Ativando

```jsonc
{
  "Intranet": {
    "Theme": { "Nome": "Vertical" }
  }
}
```

O projeto do tema precisa estar referenciado por `Secco.Intranet.Web` para que os assets da
RCL sejam publicados. Trocar de tema é configuração — não exige deploy de código novo do core.

## Assets do tema padrão

O tema `Vertical` compila Bootstrap e Bootstrap Icons por Sass e auto-hospeda as fontes.
A razão não é falta de internet — a aplicação tem saída para fora: é que nenhum CDN de terceiro recebe requisição do navegador de quem usa a intranet — e portanto não observa quem acessou, de onde e quando —, e o build fica determinístico, sem depender de um host externo continuar no ar:

```bash
npm --prefix src/Secco.Intranet.Themes.Vertical install
npm --prefix src/Secco.Intranet.Themes.Vertical run build
```

O CSS compilado e os assets copiados são **versionados**: `dotnet run` funciona sem Node
instalado. Rode `npm run build` sempre que mexer em `wwwroot/scss/`.
