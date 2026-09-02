# Dependências da plataforma

> O que este produto espera do [`secco-platform`](https://github.com/rafsecco/secco-platform) e
> ainda não existe. Uma linha por demanda, com o link da issue onde a discussão acontece.

Duas regras sustentam esta lista:

1. **Capacidade de plataforma não é implementada aqui.** Se a peça que falta é transversal —
   logging, auditoria, provisionamento de banco, identidade —, ela é pedida ao monorepo, não
   reescrita neste repositório. Ver [ADR-0006](adr/secco-intranet-adrs.md) e
   [ADR-0007](adr/secco-intranet-adrs.md).
2. **A discussão mora na issue, não neste arquivo.** Aqui fica só o registro de que a
   dependência existe e o que ela trava. Desenho, alternativas e decisão acontecem no
   `secco-platform`.

## Demandas abertas

| Demanda | O que trava aqui | Issue |
|---|---|---|
| `AddLogStream()` — o sink `ILogger` → LogStream que a ADR-0008 da plataforma promete | A intranet não envia log nenhum. Fase 0 parada, e nem erro de aplicação chega ao LogStream | [#1](https://github.com/rafsecco/secco-platform/issues/1) |
| Trilha de auditoria de ação de usuário (`LogEntry` não tem campo de ator) | Sem registro consultável de quem fez o quê. A intranet **não** constrói trilha local — ver ADR-0006 | [#2](https://github.com/rafsecco/secco-platform/issues/2) |
| Provisionamento de banco e usuário de tenant | Os exemplos de desenvolvimento usam SA; não há caminho para criar banco de tenant novo com privilégio mínimo | [#3](https://github.com/rafsecco/secco-platform/issues/3) |
| Onde vive o console de operação (futuro do `Secco.AdminPortal`) | A área administrativa da Fase 2 não tem escopo definido: só o próprio tenant, ou também operação cross-tenant | [#4](https://github.com/rafsecco/secco-platform/issues/4) |

## Como registrar uma demanda nova

1. Confirmar que é mesmo capacidade de plataforma, e não recurso deste produto. O critério
   prático: **outro adotante precisaria da mesma coisa?** Se sim, é da plataforma.
2. Abrir issue no `secco-platform` com a label `adopter-demand`, na estrutura que as quatro
   acima seguem: **lacuna · evidência · impacto aqui · workaround recusado**. Evidência é
   caminho de arquivo e trecho de ADR, não impressão.
3. Registrar o desenho **só** quando ele já for decisão; caso contrário, descrever a lacuna e
   deixar a rodada de design acontecer lá.
4. Acrescentar a linha nesta tabela e, se a demanda travar um item do roadmap, marcar o item em
   [`roadmap.md`](roadmap.md) apontando para a issue.
