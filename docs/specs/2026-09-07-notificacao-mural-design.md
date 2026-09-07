# Notificação do Mural — desenho do recurso

**Data:** 2026-09-07
**Estado:** aprovado, pronto para plano de implementação

## Problema

O Mural entregou publicação, agendamento, expiração e arquivamento, mas quem publica não
alcança ninguém: o comunicado fica esperando que a pessoa abra a intranet. O
[desenho do Mural](2026-09-02-mural-design.md) já previa notificação e definiu a maior parte
das regras — canais por prioridade, destinatários por visibilidade, lote de 500, relatório
preguiçoso de falha de entrega.

Este documento fecha o que aquele deixou em aberto e corrige uma contradição que só apareceu
com o recurso pronto.

### A contradição

O fluxo original notificava dentro do `PublicarPublicacaoHandler`, logo após persistir. Mas o
estado "no ar" do Mural é **derivado do relógio** — foi essa decisão que dispensou job de
transição e, com ela, o Hangfire. Consequência não percebida: uma publicação agendada para a
semana que vem dispararia e-mail hoje, avisando sobre algo que ninguém consegue abrir, e
**nada** aconteceria na data de entrada no ar, porque não existe evento nesse instante.

## O que o levantamento apurou

Verificado na fonte, não presumido:

| Pergunta | Resposta |
|---|---|
| Quem guarda o "lida" do sino? | O Hub. `CountUnreadInAppNotifications`, `GetUnreadInAppNotifications` e `MarkInAppNotificationAsRead` existem e estão no client |
| O Hub entrega em data futura? | **Não.** `DispatchNotificationBatchRequest` tem `Title`, `Message`, `Source`, `Type`, `Link`, `Channels` e `Destinations` — nenhum campo de agendamento |
| Há como agendar localmente? | Sim. `IBackgroundJobScheduler` e `AddSeccoBackgroundJobs()` já vêm no `Secco.SDK.AspNetCore` 0.5.1, que este produto já referencia |
| `ListUsers` está publicado? | Sim, no `Secco.SecureGate.Client` 0.3.0. `UserDto` traz `Id`, `Email`, `TenantId` e `Roles` |
| Existe endereço por publicação? | **Não.** O Mural é só a listagem paginada em `/` |

## Decisões

| Eixo | Decisão |
|---|---|
| Publicação agendada | Não notifica até o Hub entregar `ScheduledFor`; o formulário avisa antes de salvar |
| Onde a entrega futura mora | No Hub, como demanda de plataforma — não em agendador local |
| Edição | **Nunca** re-notifica |
| Estado de lida | É do Hub; a Intranet só consome |
| Link da notificação | Permalink novo, `/publicacoes/{id}` |
| Onde a regra mora | Dentro do `PublicarPublicacaoHandler`, com portas para SecureGate e Hub |
| Sino | View component, como `Navigation` e `UserMenu` |

### Por que a entrega futura pertence ao Hub

`IBackgroundJobScheduler` está a uma linha de distância e resolveria hoje. Foi recusado
mesmo assim.

O argumento que moveu o leque de destinatários para o Hub — fila, retry e entrega já moram
lá, e reconstruí-los aqui violaria a [ADR-0006](../adr/secco-intranet-adrs.md) — vale igual
para **quando** entregar: o instante da entrega é parte da entrega. Adotar o agendador local
traria de volta o Hangfire e um banco de storage de plataforma, exatamente a infraestrutura
que a revisão de 2026-09-06 do desenho do Mural comemorou remover. Infraestrutura que se
evita é infraestrutura que não se opera.

O critério da [`plataforma.md`](../plataforma.md) fecha a questão: **outro adotante precisaria
da mesma coisa?** Qualquer produto que agende conteúdo e queira avisar na hora certa precisa.

### Por que edição nunca re-notifica

Edição é correção. Uma vírgula corrigida não pode virar e-mail para a empresa inteira — o
canal que interrompe todo mundo por engano é o canal que todo mundo passa a ignorar. Se
aparecer necessidade real, o opt-in explícito entra depois, com o caso concreto na mão, e não
por antecipação.

## Arquitetura

Duas portas novas na Application, dois adaptadores na Infrastructure. A regra fica no handler,
onde é testável sem HTTP.

| Porta (Application) | Adaptador (Infrastructure) | Papel |
|---|---|---|
| `IDiretorioDeUsuarios` | `ListUsersAsync` do SecureGate | Usuários do tenant com `Id`, `Email` e `Roles` |
| `INotificadorDeMensagens` | `DispatchNotificationBatchAsync` do Hub | Um conteúdo, muitos destinos |

```text
PublicarPublicacaoHandler
  1. persiste a publicacao                  (o que o usuario espera ver)
  2. agendada? -> encerra sem notificar      guarda interina
  3. canais <- prioridade                    tabela do desenho do Mural
  4. usuarios <- IDiretorioDeUsuarios        1 chamada
  5. filtra por visibilidade e exclui autor  em memoria
  6. particiona validos / sem e-mail         local, de graca
  7. fatia em blocos de 500                  MaxBatchDestinations do Hub
  8. INotificadorDeMensagens, 1 por bloco    202 Accepted
  9. devolve publicacao + relatorio
```

`PublicarPublicacaoHandler` passa a devolver a publicação **e** o relatório: quem ficou sem
aviso e por quê. Sem isso a tela não teria como contar a verdade, e contar a verdade na mesma
tela é o que evita o polling que o desenho do Mural recusou.

### Excluir quem publicou exige um id, não um nome

`CriadoPor` guarda `User.Identity?.Name` — texto de exibição. Casar isso com `UserDto` para
excluir o autor seria frágil: nome muda, repete e nem sempre existe. O comando ganha
`CriadoPorId` (Guid, do claim `Subject`); `CriadoPor` continua sendo o que aparece na tela.

`CriadoPorId` **não é persistido**: serve só para montar a lista de destinos nesta requisição.
Guardá-lo exigiria migration e só teria uso se edição re-notificasse, que este documento
recusa.

### Conteúdo da mensagem

O Hub tem um `Message` só, servindo in-app e e-mail. Vai **excerto em texto puro** do corpo —
`Markdig.Markdown.ToPlainText` com o mesmo pipeline do renderizador, teto de 300 caracteres —
e o `Link` leva ao conteúdo renderizado. Mandar o Markdown cru faria `**negrito**` aparecer
literalmente no e-mail.

## Permalink `/publicacoes/{id}`

Ação nova no `MuralController`, aplicando a **mesma** regra de visibilidade da listagem:
inexistente e fora do alcance devolvem o mesmo erro, pelo motivo de sempre — distinguir
revelaria a existência. Reaproveita o `_Card` do contrato de tema e o `IRenderizadorMarkdown`.

Existe porque a notificação precisa de destino, mas resolve um buraco que já estava lá: hoje
não há como mandar um comunicado para alguém por nenhum meio.

### Desvio do desenho do Mural

Aquele documento põe a contagem de falhas de entrega "na aba do setor". Cumprir ao pé da letra
seria uma chamada `SearchNotifications` por publicação listada.

O relatório fica **no permalink**, para quem administra o setor: uma chamada, só quando alguém
quer a resposta. É o mesmo princípio preguiçoso que aquele documento defende, aplicado ao
lugar certo. A aba do setor segue com zero chamadas extras.

## O sino

`NotificacoesViewComponent` no core, `Components/Notificacoes/Default.cshtml` no tema — o
padrão de `Navigation` e `UserMenu`, e não um parcial como o desenho do Mural supôs. Consome
`CountUnread` para o contador, `GetUnread` para a lista e `MarkAsRead` no clique.

O Hub expõe exatamente três operações in-app: contar não lidas, listar não lidas e marcar
**uma** como lida. Duas consequências que o desenho do Mural não previu, e que ficam
registradas em vez de contornadas:

- A lista do sino mostra **só não lidas**, não "as últimas". Ler é o que remove da lista, e
  histórico de notificação lida não existe pelo client.
- **Não há "marcar todas como lidas"** — seriam N chamadas. O sino não oferece o botão; se a
  necessidade aparecer, é demanda de plataforma, não laço no cliente.

Isso **cresce o contrato de tema** ([ADR-0004](../adr/secco-intranet-adrs.md)), e a decisão é
consciente: `Navigation` e `UserMenu` já são obrigatórios de qualquer tema — o core não tem
nenhuma view de fallback —, então tornar o sino opcional exigiria o core passar a enviar
markup, que é justamente o que a ADR-0004 evita. `docs/temas.md` ganha a linha.

Sem `Subject` resolvido — o modo aberto de DEV — o sino não renderiza, mesma degradação
silenciosa do menu.

## Falhas

| Situação | O que acontece |
|---|---|
| Hub fora do ar | Publicação gravada; a tela diz "publicado, sem notificar" |
| Usuário sem e-mail, canal `email` pedido | Sai da lista antes da chamada; vira relatório nominal na mesma tela |
| E-mail malformado no cadastro | Idem — o lote do Hub é tudo-ou-nada, então a partição é nossa |
| Falha depois do aceite (SMTP, rejeição) | Só o Hub sabe; aparece no permalink via `SearchNotifications` |

**Notificar nunca derruba publicar.** A publicação é o que o usuário pediu; o aviso é
consequência.

## Testes

Fakes das duas portas cobrem a regra inteira em unidade, sem HTTP:

| Caso | Esperado |
|---|---|
| Prioridade `Normal` | só `in_app` |
| Prioridade `Importante` | `in_app` + `email` |
| Prioridade `Urgente` | `in_app` + `email` + `teams`/`slack` |
| Visibilidade `Setor` | só quem tem `{slug}-admin` ou `{slug}-user` |
| Visibilidade `Empresa` | todos do tenant |
| Autor da publicação | nunca entre os destinos |
| Usuário sem e-mail, canal `email` | fora do lote, dentro do relatório |
| 501 destinatários | dois lotes, não 501 chamadas |
| Publicação agendada | nenhuma chamada ao Hub |
| Hub lançando exceção | publicação persistida, relatório acusa |

Integração: permalink devolve o mesmo erro para inexistente e para sem acesso.

## Impacto no que já existe

- `PublicarPublicacaoCommand` ganha `CriadoPorId`; o handler passa a devolver relatório —
  os testes de publicar existentes acompanham.
- `MuralController` ganha a ação de permalink.
- O contrato de tema cresce: `Components/Notificacoes/Default.cshtml`.
- `Directory.Packages.props` ganha `Secco.NotificationHub.Client`.
- Nenhuma migration: nada disso é dado nosso.
- **Nenhuma infraestrutura nova.** Sem Hangfire, sem banco de storage, sem fila local.

## Fora de escopo

- **`ScheduledFor` no Hub** — demanda de plataforma
  ([secco-platform#24](https://github.com/rafsecco/secco-platform/issues/24)); até sair,
  agendada não notifica.
- **Re-notificar em edição** — recusado acima; entra com caso real, se houver.
- **Notificação de arquivamento** — tirar do ar não é evento que interrompe ninguém.
- **Preferências por usuário** (silenciar setor, resumo diário) — sem evidência de demanda.
- **Fila local para Hub indisponível** — o desenho do Mural já recusou por antecipação; volta
  à mesa com evidência de que acontece.
- **Auditoria** — plano próprio, cobrindo Mural e Documentos juntos.
