---
name: secco-intranet-temas
description: Arquitetura de temas via Razor Class Library (RCL) do produto secco-intranet — estrutura de pastas, variáveis Bootstrap/Sass, suporte obrigatório a dark/light. Usar SEMPRE que a tarefa envolver criação ou edição de tema/layout, o projeto Secco.Intranet.Web, views compartilhadas (_Layout.cshtml), paleta de cores, ou os temas padrão Vertical/Horizontal.
---

# secco-intranet — Sistema de Temas

Resume a decisão registrada em `docs/adr/secco-intranet-adrs.md` (ADR-0003). Ler a ADR
antes de decisões estruturais; este skill é o resumo operacional do dia a dia.

## Um tema é um pacote RCL independente

```
Secco.Intranet.Themes.<Nome>/
├── wwwroot/
│   ├── scss/
│   │   ├── _variables.scss   ← identidade visual do tema (cores, tipografia, radius)
│   │   └── theme.scss        ← @import "bootstrap" + overrides
│   └── css/theme.css         ← compilado
├── Views/
│   └── Shared/_Layout.cshtml
└── Secco.Intranet.Themes.<Nome>.csproj
```

O core (`Secco.Intranet.Web`) não conhece o framework CSS de um tema — carrega o
CSS/JS que o tema declara via `IViewLocationExpander`. O tema padrão usa Bootstrap,
mas isso não é uma restrição pra temas de terceiros.

## Temas de saída disponibilizados pelo projeto

- `Secco.Intranet.Themes.Vertical` — menu lateral (padrão)
- `Secco.Intranet.Themes.Horizontal` — menu no topo

Ambos compartilham a mesma paleta base; a diferença é só a disposição do menu.

## Dark/light é obrigatório em todo tema publicado

Todo tema — inclusive de terceiros — precisa declarar as duas variantes via
`data-bs-theme="light"` / `data-bs-theme="dark"` como CSS custom properties. Não é
suficiente inverter as cores automaticamente: cores saturadas (ex: a cor primária)
geralmente precisam de um tom mais claro/saturado na variante escura pra manter
contraste legível — ajuste manual, não automático.

```scss
[data-bs-theme="light"] { --bs-body-bg: #F1EFE8; --bs-primary: #0F6E56; }
[data-bs-theme="dark"]  { --bs-body-bg: #1A1B19; --bs-primary: #1D9E75; }
```

## Paleta de referência do tema padrão

Primária `#0F6E56` (verde-petróleo), neutros quentes (`#2C2C2A` / `#5F5E5A` /
`#F1EFE8`) em vez de cinza puro, `border-radius` `0.625rem`. Ver a ADR pra contexto
completo da escolha.
