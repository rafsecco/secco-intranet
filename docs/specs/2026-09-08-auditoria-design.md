# Auditoria transversal — desenho do recurso

**Data:** 2026-09-08
**Estado:** aprovado, pronto para plano de implementação

## Problema

O produto já tem dois recursos que mexem em conteúdo e acesso — Mural e Documentos — e um
terceiro que define quem enxerga o quê, o cadastro de Setor. Nenhum deles deixa rastro: não
há como responder *quem publicou aquele comunicado*, *quem tirou aquele documento do ar* ou
*quem desativou aquele setor*.

O [desenho do Mural](2026-09-02-mural-design.md) definiu os verbos do Mural e parou aí,
dizendo que a fiação é tarefa transversal — porque auditar só o recurso novo, deixando
Documentos sem trilha, seria arbitrário. Este documento faz essa fiação.

A capacidade **já existe** e não depende de nada da plataforma: `audit-entries` está no
`Secco.LogStream.Client` desde a 0.2.0. Isto é consumo, não demanda.

## O que o levantamento apurou

Verificado na fonte, não presumido:

| Pergunta | Resposta |
|---|---|
| Qual o contrato de escrita? | `CreateAuditEntryRequest`: `ActorId`, `ActorType` (`User`\|`Client`), `Action`, `ActorName`, `ResourceType`, `ResourceId`, `Metadata` (string), `CorrelationId` (Guid?), `OccurredAt` (DateTimeOffset?) |
| A escrita é enfileirada do lado do LogStream? | **Não.** `CreateAuditEntryAsync` devolve o `AuditEntryDto` criado — é síncrona. Diferente de `CreateLogEntry`, que devolve `LogEntryAcceptedResponse` |
| Dá para ler a trilha? | Sim, `SearchAuditEntriesAsync`, paginada |
| Os endpoints exigem permissão? | Sim: `AuditEntries.Write` e `AuditEntries.Read` |
| O client aceita credenciais? | **Não.** `LogStreamClientOptions` tem só `BaseUrl` |
| O contexto ambiente carrega o usuário? | **Não.** `SeccoAmbientContext` expõe `TenantId` e `CorrelationId`, e nada mais |
| Versão publicada | `Secco.LogStream.Client` **0.3.1**; ainda não referenciado neste repositório |

## Decisões

| Eixo | Decisão |
|---|---|
| LogStream fora do ar | **Falha aberta sempre**: a operação acontece, o registro se perde, a falha vira aviso no log |
| Leitura de documento | **Não auditada** — ver a seção própria abaixo |
| Alcance | Mural, Documentos **e** Setor |
| Onde a chamada mora | Dentro de cada handler, por porta — o padrão do notificador e do armazenamento |
| Ator | Porta `IAtorAtual`, implementada no Web; nenhum comando ganha campo |

### Por que falha aberta

Auditar não pode derrubar a ação: é a mesma regra que já vale para notificar. A alternativa —
bloquear a operação quando o registro falha — transformaria o LogStream em ponto único de
falha do produto inteiro, e um serviço de observabilidade fora do ar impediria publicar,
editar e arquivar.

O custo é real e fica registrado: **a trilha ganha buracos exatamente quando o sistema está
com problema**, que é quando ela mais importa. Fila local com retry resolveria — o `OccurredAt`
opcional do contrato permite gravar depois sem datar errado —, e foi recusada pelo mesmo
motivo que a fila de notificação: infraestrutura por antecipação. Volta à mesa com evidência
de que os buracos acontecem, não antes.

### Por que a leitura de documento fica fora

**Esta é a decisão mais consequente deste documento, e foi tomada contra a recomendação de
quem o escreveu.** Fica registrada com o trade-off inteiro, porque a pergunta vai voltar.

A trilha cobre escrita: publicar, editar, arquivar. Baixar documento **não** gera registro.

O argumento a favor de auditar era: o recurso de Documentos cifra arquivo em repouso porque o
conteúdo é sensível, e uma trilha que registra quem *publicou* mas não quem *leu* não responde
a pergunta que aparece quando algo vaza — *quem abriu este contrato, e quando*. O custo seria
uma chamada HTTP a mais por download, uma vez, antes do streaming.

A decisão foi não auditar leitura, por volume. A consequência, explícita: **não há como saber
quem baixou um documento.** Se um arquivo restrito circular fora de hora, a investigação
começa e termina em quem o publicou.

Reverter é barato — um verbo a mais e uma chamada no `BaixarDocumentoHandler` — e não exige
migration nem mudança de contrato. Se a necessidade aparecer, o caminho está aberto.

### Por que Setor entra

O pedido original citava Mural e Documentos. Setor entrou porque criar um setor **provisiona
as Roles `{slug}-admin` e `{slug}-user` no SecureGate** (ADR-0001), e desativar um setor tira
o conteúdo dele da vista de todo mundo. São mudanças de controle de acesso — mais
consequentes que publicar um aviso, e a trilha ficaria estranha sem elas.

## Os verbos

| Verbo | `ResourceType` | Quando |
|---|---|---|
| `mural.publicar` | `publicacao` | Publicação criada |
| `mural.editar` | `publicacao` | Conteúdo, agendamento, visibilidade ou prioridade alterados |
| `mural.arquivar` | `publicacao` | Tirada de circulação |
| `documento.publicar` | `documento` | Arquivo enviado |
| `documento.arquivar` | `documento` | Tirado de circulação |
| `setor.criar` | `setor` | Criado — provisiona as Roles no SecureGate |
| `setor.editar` | `setor` | Nome ou ícone alterados |
| `setor.desativar` | `setor` | Passou a inativo |
| `setor.reativar` | `setor` | Voltou a ativo |

**Uma entrada por operação.** O `EditarSetorHandler` altera nome, ícone e situação de uma vez:
quando a situação vira, o verbo é `setor.desativar` ou `setor.reativar` — porque é a mudança
que afeta quem enxerga o quê; nos demais casos, `setor.editar`. O que mudou vai no metadata.

`ResourceId` é sempre o identificador da entidade.

## Arquitetura

Duas portas na Application, adaptadores nas bordas. A regra fica no handler, testável com
fakes e sem HTTP.

| Porta (Application) | Adaptador | Papel |
|---|---|---|
| `ITrilhaDeAuditoria` | `CreateAuditEntryAsync` do LogStream (Infrastructure) | Registra o verbo |
| `IAtorAtual` | `HttpContext.User` (Web) | Quem está agindo |

```text
Handler
  1. faz o que o usuario pediu       (persistir, arquivar, publicar)
  2. monta o registro                 verbo, recurso, id, metadata
  3. ITrilhaDeAuditoria.RegistrarAsync
       ↓
     adaptador: resolve ator e correlacao, chama o LogStream
     falhou? LogWarning e segue         auditar nunca derruba a acao
```

### O ator não vem de comando, nem do contexto ambiente

`SeccoAmbientContext` carrega `TenantId` e `CorrelationId` — não carrega usuário. E passar o
ator em cada comando repetiria, oito vezes, o que a notificação precisou fazer uma vez com o
`CriadoPorId`.

A saída é uma porta mínima, `IAtorAtual`, cuja implementação no Web lê `HttpContext.User`:
`ActorId` do claim `sub`, `ActorName` de `Identity.Name`, `ActorType.User`. **Nenhum comando
existente muda.**

O `CorrelationId` sai de graça do `SeccoAmbientContext`, e o `OccurredAt` é preenchido pelo
adaptador no instante da chamada.

### Sem ator, não registra

No modo aberto de DEV não há usuário autenticado. Registrar com um ator inventado sujaria a
trilha com entradas que não provam nada, então o adaptador não registra e loga em `Debug`.
Em produção a autenticação está configurada e o ator sempre existe.

### O client é registrado à mão

`LogStreamClientOptions` só aceita `BaseUrl`, mas os endpoints exigem `AuditEntries.Write`.
O `HttpClient` é registrado aqui com `SeccoClientCredentialsHandler` e um `SeccoAccessTokenStore`
próprio — least privilege, um store por recurso —, exatamente como foi feito para o
NotificationHub.

É o **segundo** adotante a esbarrar na mesma lacuna. Vale abrir demanda de plataforma para as
extensões de client aceitarem client credentials, como as três do SecureGate aceitam; até lá,
a composição local resolve e não bloqueia nada.

Sem `Intranet:Auditoria:LogStreamUrl` configurada, adaptador no-op — o mesmo desenho do Hub.

## Metadata

JSON curto, com o que identifica a ação e mais nada:

| Recurso | Vai no metadata |
|---|---|
| `publicacao` | título, tipo, visibilidade, prioridade, slug do setor |
| `documento` | título, nome do arquivo, visibilidade, slug do setor, tamanho |
| `setor` | nome, slug, ícone, situação |

**Nunca** o corpo da publicação nem bytes do documento. A trilha diz *o que aconteceu*; ela
não é uma segunda cópia do conteúdo, e duplicar conteúdo sensível num serviço de
observabilidade é criar um segundo lugar de onde ele pode vazar (ADR-0020).

## Testes

Fake da porta cobre a regra inteira, sem HTTP:

| Caso | Esperado |
|---|---|
| Publicar, editar, arquivar publicação | verbo, `ResourceType` e `ResourceId` corretos |
| Publicar e arquivar documento | idem |
| Criar setor | `setor.criar` |
| Editar setor mudando só o nome | `setor.editar` |
| Editar setor desativando | `setor.desativar`, não `setor.editar` |
| Editar setor reativando | `setor.reativar` |
| LogStream lançando exceção | a operação conclui com sucesso mesmo assim |
| Sem ator resolvido | nenhum registro, nenhuma exceção |
| Metadata de publicação | não contém o corpo |
| Baixar documento | **nenhum** registro — a ausência é decisão, e um teste a fixa |

## Impacto no que já existe

- Sete handlers ganham a chamada: publicar/editar/arquivar publicação, publicar/arquivar
  documento, criar/editar setor.
- `Directory.Packages.props` ganha `Secco.LogStream.Client` 0.3.1.
- Nenhuma migration: a trilha não é dado nosso.
- Nenhuma mudança de comando, graças ao `IAtorAtual`.
- Nenhuma infraestrutura nova: sem fila, sem storage.

## Fora de escopo

- **Tela para ler a trilha.** `SearchAuditEntries` existe, mas quem consulta e por qual tela
  encosta na demanda [#4](https://github.com/rafsecco/secco-platform/issues/4) da plataforma,
  ainda aberta: a área administrativa da Fase 2 não tem escopo definido.
- **Auditoria de leitura** — decidida acima, com o trade-off registrado.
- **Fila local com retry** — recusada por antecipação; volta com evidência.
- **`AddLogStream()` do `Secco.SDK.Logging`** — o sink `ILogger` → LogStream é outro assunto,
  com plano próprio. Atenção: a **0.1.0 nunca deve ser usada** (ver `docs/plataforma.md`).
