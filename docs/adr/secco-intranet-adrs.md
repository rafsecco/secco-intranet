# Secco.Intranet — Architecture Decision Records (ADR)

> Fonte da verdade arquitetural do produto secco-intranet.
> Nenhum código deve contradizer uma ADR com status **Aceita**.
> Para mudar uma decisão, cria-se uma nova ADR que **substitui** a anterior — ADRs nunca são editadas retroativamente nem apagadas.
> Decisões da plataforma (multi-tenancy, `Result<T>`, nomenclatura de banco...) estão em `secco-platform/docs/adr/secco-platform-adrs.md` — este documento cobre apenas decisões específicas deste produto.

**Última atualização:** 2026-07-19 (ADR-0003 adicionada)

---

## Como usar este documento

1. Toda decisão arquitetural relevante (difícil de reverter, ou que afete a extensibilidade do projeto por terceiros) vira uma ADR.
2. Status possíveis: `Proposta` → `Aceita` → `Substituída por ADR-YYYY` ou `Rejeitada`.
3. Ao iniciar qualquer trabalho neste repositório, consultar as ADRs aplicáveis antes de codificar.
4. ADRs curtas são melhores que ADRs completas. Contexto, decisão, consequências. Nada mais.

### Template

```markdown
## ADR-XXXX: Título da decisão

**Status:** Proposta | Aceita | Substituída por ADR-YYYY | Rejeitada
**Data:** AAAA-MM-DD

### Contexto
Qual problema estamos resolvendo e quais forças estão em jogo.

### Decisão
O que foi decidido, em voz ativa: "Usaremos X porque Y."

### Consequências
O que fica mais fácil, o que fica mais difícil, o que passa a ser proibido.
```

---

## ADR-0001: Setor = Role tenant-scoped do Secco.SecureGate

**Status:** Aceita
**Data:** 2026-07-19

### Contexto
A Intranet organiza documentos, processos e outros recursos por Setor/Departamento
(ex: Financeiro, RH, Infraestrutura). Era preciso decidir como representar "usuário
pertence a este setor, com este nível de acesso (admin/user)". O Secco.SecureGate já
resolve autorização multi-tenant através do conceito de Role (`(tenant, role) →
permissões`, com claims no padrão `recurso:ação`, conforme ADR-0021 da plataforma) —
criar um conceito paralelo de "perfil de setor" duplicaria essa infraestrutura.

### Decisão
Cada Setor cadastrado cria automaticamente duas Roles tenant-scoped no SecureGate:
`{slug}-admin` e `{slug}-user`. Vincular um usuário a um setor é vincular ele a uma
dessas Roles via `UserRole` — não existe tabela própria `Usuario↔Setor` na Intranet.
Um usuário com acesso a múltiplos setores recebe múltiplas Roles. A geração das claims
(`intranet:{slug}:{recurso}:{ação}`) para os dois perfis padrão **não é automatizada**
— fica a critério de quem adota o sistema; só será automatizada se um caso de uso
concreto justificar o esforço.

### Consequências
- Nenhuma tabela nova de autorização na Intranet — reaproveita 100% o que o SecureGate
  já resolve (login, claims, resolução de permissão).
- Um "Gerente" que precise acessar vários setores simplesmente acumula Roles, sem
  necessidade de um conceito de "perfil transversal" separado.
- O setor Infraestrutura nasce com a flag `Fixo = true` — não pode ser desativado nem
  excluído — e é o dono nato do recurso de Inventário.
- Fica proibido criar qualquer relação direta de autorização fora do modelo de Role do
  SecureGate (ex: uma flag `IsAdminDoSetor` numa tabela de usuário local) — isso
  fragmentaria a fonte de verdade de autorização.

---

## ADR-0002: Monolito — Secco.Intranet.Web consome a Application layer diretamente

**Status:** Aceita
**Data:** 2026-07-19

### Contexto
Era preciso decidir se o front-end (MVC) chamaria a Application layer em processo, ou
via uma Api HTTP separada (`Secco.Intranet.Api`). Um cenário futuro plausível é migrar
parte ou todo o front para Angular, React ou Blazor WebAssembly, o que favoreceria uma
Api separada desde já — mas a preferência declarada é por um monolito simples de
manter, com uma equipe pequena.

### Decisão
`Secco.Intranet.Web` (MVC) consome a Application layer diretamente (via Mediator/
handlers), sem uma Api HTTP separada por enquanto. Para manter a porta de saída aberta
para uma futura extração, três regras são obrigatórias: (1) Controllers nunca acessam
`DbContext`/repositório diretamente — sempre via handler; (2) Views recebem
ViewModel/DTO, nunca entidade de domínio; (3) nenhuma regra de negócio mora em
Controller ou View.

### Consequências
- Menos complexidade operacional agora (um artefato, sem HTTP interno, sem contrato de
  API pra versionar).
- Seguindo as três regras acima, extrair uma Api de verdade no futuro (para Angular/
  React/Blazor WASM) é um trabalho mecânico de expor os mesmos handlers atrás de
  endpoints — não uma reescrita de regra de negócio.
- Fica proibido qualquer atalho onde a View ou o Controller acessem o EF Core
  diretamente "só dessa vez" — isso quebra a premissa que barateia a extração futura.

---

## ADR-0003: Sistema de temas via Razor Class Library (RCL)

**Status:** Aceita
**Data:** 2026-07-19

### Contexto
O projeto é open source e precisa permitir customização visual profunda por quem
adotar (o autor se descreve como iniciante em design, mas com base de Bootstrap). O
mecanismo precisa: (a) não prender o projeto a um único framework CSS, para não
excluir contribuidores que prefiram outra stack; (b) permitir múltiplos temas de saída
mantidos pelo próprio projeto; (c) suportar modo claro/escuro.

### Decisão
Cada tema é um pacote Razor Class Library (RCL) independente, com sua própria pasta
`wwwroot` (CSS/JS) e `Views`. O core (`Secco.Intranet.Web`) resolve o tema ativo via
`IViewLocationExpander` e não impõe framework CSS — só carrega o que o tema declara. O
projeto disponibiliza dois temas de saída, ambos em Bootstrap customizado via
variáveis Sass: `Secco.Intranet.Themes.Vertical` (menu lateral, padrão) e
`Secco.Intranet.Themes.Horizontal` (menu no topo). Todo tema publicado — inclusive de
terceiros — deve suportar `data-bs-theme="light"` e `data-bs-theme="dark"`.

### Consequências
- Contribuidores externos podem publicar temas em qualquer stack CSS sem tocar no
  core.
- Trocar de tema é configuração, não deploy de código novo do core.
- Cada tema novo tem um custo fixo: declarar as duas variantes de cor (claro/escuro)
  não é automático — cores saturadas geralmente precisam de ajuste manual de tom entre
  as variantes para manter contraste legível.
- Fica proibido um tema "esquecer" o modo escuro — é parte do contrato mínimo de
  qualquer tema aceito no projeto.
