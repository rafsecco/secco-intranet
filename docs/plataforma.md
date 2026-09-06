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
| Onde vive o console de operação (futuro do `Secco.AdminPortal`) | A área administrativa da Fase 2 não tem escopo definido: só o próprio tenant, ou também operação cross-tenant | [#4](https://github.com/rafsecco/secco-platform/issues/4) |
| Consulta de status de notificação em massa | O Hub só expõe `GetNotification` por id. Dizer a quem publicou o que não chegou custaria uma chamada por destinatário — o custo de N que o lote da #15 acabou de eliminar, reaparecendo na leitura | [#23](https://github.com/rafsecco/secco-platform/issues/23) |

## Atendidas

A lista fica: ela é a evidência de que o canal funciona, e o registro de qual versão trouxe
cada capacidade — informação que some se a linha for apagada.

| Demanda | Entregue em | Issue |
|---|---|---|
| `AddLogStream()` — o sink `ILogger` → LogStream | `Secco.SDK.Logging` **0.1.1** (2026-09-06), com fila local, lote por tenant e guarda anti-recursão. **Nunca usar a 0.1.0**: ela foi empacotada contra o `LogStream.Client` 0.2.0 e chama um método que a 0.3.0 renomeou, então o lote falha com `MissingMethodException` — e como o dispatcher engole a falha por design, o sintoma é log que simplesmente não chega | [#1](https://github.com/rafsecco/secco-platform/issues/1) |
| Trilha de auditoria de ação de usuário | `Secco.LogStream.Client` 0.2.0 (2026-09-05) — superfície `audit-entries`, **dentro do LogStream** e não como produto separado | [#2](https://github.com/rafsecco/secco-platform/issues/2) |
| Provisionamento de banco e usuário de tenant | `Secco.SecureGate.Client` 0.3.0 (2026-09-05) — `ProvisionTenantDatabaseAsync` e `GetTenantDatabaseStatusAsync`, ADR-0028 da plataforma | [#3](https://github.com/rafsecco/secco-platform/issues/3) |
| Canal de comunicação corporativa (Teams, Slack) | `Secco.NotificationHub` (2026-09-06), ADR-0029 — `NotificationHubChannels` passou a reconhecer `teams` e `slack` | [#13](https://github.com/rafsecco/secco-platform/issues/13) |
| Provider SendGrid para `IEmailSender` | `Secco.NotificationHub` (2026-09-06) | [#14](https://github.com/rafsecco/secco-platform/issues/14) |
| Criação de notificação em lote | `Secco.NotificationHub` (2026-09-06) — `POST /batch`, um conteúdo para muitos destinos numa chamada | [#15](https://github.com/rafsecco/secco-platform/issues/15) |

A auditoria ter vindo como recurso do LogStream, e não como um `Secco.Audit`, **não muda nada
aqui** — a [ADR-0006](adr/secco-intranet-adrs.md) já previa a bifurcação e registrou que este
produto escolheu a origem da capacidade, não a implementação dela.

Consumir o que chegou é trabalho próprio, ainda não feito: ver os itens correspondentes no
[`roadmap.md`](roadmap.md).

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
