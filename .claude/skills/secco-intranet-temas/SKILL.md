---
name: secco-intranet-temas
description: Convenção de temas via Razor Class Library (RCL) do produto secco-intranet — estrutura de pastas, contrato de parciais, paleta e suporte obrigatório a dark/light. Usar SEMPRE que a tarefa envolver criação ou edição de tema/layout, o projeto Secco.Intranet.Web, views compartilhadas (_Layout.cshtml), paleta de cores, ou os temas padrão Vertical/Horizontal.
---

# secco-intranet — Sistema de Temas

Resume as decisões registradas em `docs/adr/secco-intranet-adrs.md` (ADR-0003 e ADR-0004).
Ler as ADRs antes de decisões estruturais; este skill é o resumo operacional do dia a dia.
O guia completo para quem escreve um tema está em `docs/temas.md`.

## Três projetos

| Projeto | Papel |
|---|---|
| `Secco.Intranet.Web.Theming` | Contratos: `ThemeOptions`, `ThemeViewLocationExpander`, `SetorHue` e os models dos parciais |
| `Secco.Intranet.Themes.<Nome>` | RCL do tema: `_Layout`, parciais e `wwwroot` |
| `Secco.Intranet.Web` | Controllers, ViewModels, views de página, registro de navegação |

O projeto de contratos existe para quebrar o ciclo (o core referencia as RCLs pelos assets,
então o tema não pode referenciar o core) e para que um tema de terceiro dependa de um
projeto pequeno, não da Web inteira.

## A regra que sustenta tudo

> **O core decide o que a página mostra; o tema decide como ela parece.**

Views de página do core **não** escrevem markup de sidebar, card ou badge à mão — compõem os
parciais do contrato. Uma view que emite markup estrutural próprio cria uma página que um
tema novo não consegue reestilizar.

## Estrutura de um tema

```
Secco.Intranet.Themes.<Nome>/
├── Themes/<Nome>/Views/Shared/     ← onde o expander procura
│   ├── _Layout.cshtml
│   ├── _PageHeader.cshtml  _Card.cshtml  _Badge.cshtml
│   ├── _EmptyState.cshtml  _Pagination.cshtml
│   └── Components/Navigation/Default.cshtml
│       Components/UserMenu/Default.cshtml
└── wwwroot/                        ← servido em _content/<assembly>/
    ├── scss/  _variables.scss _tokens.scss _fonts.scss _components.scss theme.scss
    ├── css/theme.css               ← compilado E VERSIONADO
    └── vendor/                     ← fontes e JS copiados do npm
```

Os view components `Navigation` e `UserMenu` têm a lógica no core e o markup no tema —
por isso nenhuma view de tema injeta serviço.

## Paleta do tema padrão (`Vertical`)

Esmeralda com neutros cinza-frios. A primária **escurece** no claro e **clareia** no escuro:
inversão automática não serve, cor saturada perde contraste sobre fundo escuro.

| Papel | Claro | Escuro |
|---|---|---|
| Primária | `#0B8A64` | `#34D399` |
| Texto | `#0F172A` | `#E8EDF2` |
| Texto suave | `#64748B` | `#94A3B8` |
| Superfície | `#FFFFFF` | `#161D26` |
| Fundo | `#F6F8FA` | `#0F141A` |
| Borda | `#E3E8EF` | `#263039` |

`border-radius` 12px, sombra difusa de baixa opacidade, e borda de 1px sempre presente nas
superfícies — é a borda que segura o card no modo escuro, onde a sombra some.

**Tipografia:** Instrument Sans (títulos), Inter (corpo), JetBrains Mono (data, tamanho,
contagem, slug). Todas auto-hospedadas — não por falta de internet, que a aplicação tem,
mas porque nenhum CDN de terceiro deve observar quem acessa a intranet, e o build não deve
depender de um host externo continuar no ar.

## Dark/light é obrigatório em todo tema publicado

Três blocos, sempre: `:root` com a paleta completa, a media query
`@media (prefers-color-scheme: dark) { :root:not([data-bs-theme="light"]) { … } }` para a
preferência do sistema, e `[data-bs-theme="dark"]` para a escolha explícita. Nenhuma cor
pode ter sua única definição dentro de uma media query.

O `_Layout` aplica a preferência guardada em `localStorage` num script inline no `<head>`,
antes da primeira pintura — sem isso a página pisca no modo errado a cada carregamento.

## Assinatura: a cor do setor

Todo conteúdo pertence a um setor (ADR-0001). `SetorHue.From(slug)` devolve um matiz estável
(FNV-1a, nunca `GetHashCode` — o do runtime é aleatorizado por processo), exposto como
`--sc-setor-hue` no elemento. Saturação e luminosidade são fixas por modo. O matiz aparece em
três lugares e só neles: o badge do card, o filete de 3px na borda esquerda do card, e o
ícone do item de menu do setor.

O menu já não usa ponto: o setor tem ícone próprio, escolhido no cadastro (coluna
`ds_icone`, classe do Bootstrap Icons). O matiz passou a tingir esse ícone — a cor continua
dizendo de quem é o item, e o glifo passou a dizer o que ele é. O ponto continua vivo no
`_PageHeader`, marcando o título da página do setor.

## Layout do tema `Vertical`

- **Header** em superfície (não na cor primária), borda inferior de 1px: marca à esquerda;
  à direita o alternador de tema e o avatar do usuário.
- **Sidebar** de 220px, recolhível para trilho de 64px (preferência em `localStorage`);
  abaixo de 768px vira gaveta sobre o conteúdo. Item ativo com fundo suave e filete de 3px
  na cor primária.
- **Conteúdo** com largura máxima de 1180px, cabeçalho de página e cards.

No tema `Horizontal` a composição é a mesma, com o menu migrando para uma barra abaixo do
header.

## Build de assets

```bash
npm --prefix src/Secco.Intranet.Themes.Vertical run build
```

Compila o Sass e copia fontes e JS do `node_modules`. **O resultado é versionado** —
`dotnet run` precisa funcionar sem Node instalado. Rodar sempre que mexer em `wwwroot/scss/`.
