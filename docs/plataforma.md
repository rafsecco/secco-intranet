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
| Onde vive o console de operação (futuro do `Secco.AdminPortal`) | Nada trava tecnicamente — a API já existe e é liberada por escopo, não por identidade de tenant do chamador. Falta a ADR formal da plataforma, que este produto se oferece a ancorar em código (ADR-0008 do secco-intranet) | [#4](https://github.com/rafsecco/secco-platform/issues/4) |
| Sincronizar grupos do diretório com perfis (mapeamento explícito, opt-in) | Manter quem pertence a cada perfil a partir do diretório, sem atribuir usuário por usuário. Exige uma ADR nova na plataforma, porque a ADR-0026 estabelece que o AD nunca concede acesso. Listar os grupos (o pré-requisito, #27) já foi entregue | [#28](https://github.com/rafsecco/secco-platform/issues/28) |
| SDK de tratamento de imagem enviada por usuário (recorte, redução, sem metadados) | A foto do diretório organizacional precisa dela. O diretório inteiro sai sem foto (avatar por iniciais) e o upload é a última etapa, ligada à SDK por uma porta; por decisão do dono do produto, biblioteca reutilizável nasce na plataforma. **Replanejada para fora do MVP da plataforma** (2026-09-27), na fila depois de #27/#28 — se a etapa de foto for construída aqui antes da SDK existir, a mitigação mínima descrita no plano da foto (assinatura de arquivo, nunca SVG, nome gerado pelo servidor, `nosniff`, EXIF documentado como não removido) é obrigatória, não opcional | [#29](https://github.com/rafsecco/secco-platform/issues/29) |

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
| Consulta de status de notificação em massa | `Secco.NotificationHub.Client` 0.4.0 (2026-09-06) — `SearchNotifications`, busca paginada com filtros. Torna o relatório de falha de entrega uma chamada só, filtrando por `Source` e `Type` | [#23](https://github.com/rafsecco/secco-platform/issues/23) |
| Entrega agendada de notificação (`ScheduledFor`) | `Secco.NotificationHub.Client` **0.5.0** (2026-09-07) — `ScheduledFor` nos dois DTOs de despacho, e `Schedule(..., DateTimeOffset)` no `IBackgroundJobScheduler` do SDK. A entrega cobriu um caso que a demanda não via: o canal in-app não tinha job de entrega, então agendar só o e-mail faria o aviso aparecer no sino na hora | [#24](https://github.com/rafsecco/secco-platform/issues/24) |
| Extensões de client aceitarem client credentials | `Secco.NotificationHub.Client` **0.6.0** e `Secco.LogStream.Client` **0.4.0** (2026-09-10) — as extensões passam a montar URL, credenciais e scope. As credenciais caem em `Secco:SecureGate` por padrão, e cada pacote traz o scope do próprio produto (`DefaultScope`), então o adotante não pode mais errá-lo | [#25](https://github.com/rafsecco/secco-platform/issues/25) |
| Atribuir/revogar role de um usuário já existente | `Secco.SecureGate.Client` **0.7.0** (2026-09-17) — `AddUserRoleAsync`/`RemoveUserRoleAsync` idempotentes, mais detalhe de perfil e de usuário, membros paginados, criação e exclusão de perfil, ativar/desativar usuário e encerrar sessões (0.11.0 é a versão em uso). Destrava a Área administrativa de acesso e a tela de conceder `inventario-admin` | [#26](https://github.com/rafsecco/secco-platform/issues/26) |
| Listar os grupos do diretório federado (Entra ID) de um tenant | `Secco.SecureGate.Client` **0.13.0** (2026-09-27) — `ListEntraGroups`, paginação por cursor. Exige a app registration ter o consentimento de aplicação `GroupMember.Read.All`, senão devolve 403 claro. Consumir (a tela de escolher quais grupos viram perfil) ainda não foi feito aqui | [#27](https://github.com/rafsecco/secco-platform/issues/27) |
| Nome de exibição (`displayName`) no usuário do SecureGate | `Secco.SecureGate.Client` **0.12.0** (2026-09-27) — `displayName` em `UserDto`/`UserDetailDto`/`RoleMemberDto`, opcional em `CreateUserRequest`, e `SetUserDisplayName` (PUT, escopo `securegate:admin`) para definir ou limpar. O Diretório organizacional continua com o nome guardado localmente, atrás de uma porta — trocar de adaptador e migrar os dados é trabalho próprio, ainda não feito | [#30](https://github.com/rafsecco/secco-platform/issues/30) |

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
