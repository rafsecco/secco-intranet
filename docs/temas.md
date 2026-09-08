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
│           └── UserMenu/Default.cshtml
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
