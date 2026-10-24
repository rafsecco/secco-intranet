# Aviso de vencimento — desenho do recurso

**Data:** 2026-10-24
**Estado:** rascunho, aguardando aprovação

## Problema

Coisa que vence sem ninguém perceber custa caro e sempre pelo mesmo motivo: a data estava em algum
lugar, mas ninguém foi lembrado a tempo. Certificado digital, alvará, contrato de fornecedor, licença
de software, apólice de seguro, CNH de motorista da frota. Hoje isso vive em planilha ou na agenda
pessoal de alguém, e some quando a pessoa sai de férias ou da empresa.

O recurso dá a cada setor uma lista de itens com data de vencimento. A Intranet avisa os
responsáveis em marcos escalonados antes da data (por padrão 30, 7 e 1 dia antes) e no próprio dia.
Ao renovar, o item ganha a data nova e os avisos recomeçam sozinhos.

## O que o levantamento apurou

Verificado na fonte (`secco-platform` em `9b674ea`, este repositório em `5b8cb64`):

| Pergunta | Resposta |
|---|---|
| O Hub entrega em data futura? | Sim: `ScheduledFor` nos dois DTOs de despacho, já usado pelo Mural |
| O Hub cancela uma entrega agendada? | **Não.** O `NotificationHub` só tem despacho, leitura, contagem e "marcar como lida". O lote devolve os ids, mas nada os cancela |
| Há precedente de trabalho periódico em processo? | Sim. `LogRetentionWorker` (LogStream) e `AuthorityAuditRetentionService` (SecureGate) são `BackgroundService` com `PeriodicTimer` que percorre o `ITenantCatalog`. É a "camada 1" da ADR-0015 da plataforma: manutenção em processo, e perder um ciclo por restart é aceitável |
| Como um escopo fora do HTTP ganha tenant? | `IServiceProvider.SetTenant(tenantId)` do `Secco.SDK.AspNetCore` (`TenantScopeExtensions`) |
| A árvore de menu já prevê o recurso? | Sim: a [spec do menu por setor](2026-09-30-item-menu-por-setor-design.md) deixou "Tipo `Vencimento` como filho embutido" para quando este recurso fosse construído |
| Como o Mural resolve destinatários? | `IDiretorioDeUsuarios.ListarDoTenantAtualAsync` mais filtro por Role `{slug}-admin`/`{slug}-user` (`AvisoDePublicacao.Destinatarios`) |
| O produto tem noção de fuso horário? | **Não.** Nenhum `TimeZoneInfo` nem `TimeProvider` no código |

## Decisões

| Eixo | Decisão |
|---|---|
| Dono do item | O **setor**, como Documentos e Avisos. Novo tipo embutido `Vencimentos` na árvore de menu |
| Quem decide que o aviso é de hoje | A **Intranet**, num worker em processo. O Hub entrega na hora, sem `ScheduledFor` |
| Destinatários | O **responsável** do item, mais quem tem a Role `{slug}-admin` do setor, resolvidos **no dia do envio** |
| Canais | Escalam com a proximidade: faltando mais de 7 dias, o aviso vai só ao sino; faltando 7 ou menos, e no dia, ele vai ao sino e por e-mail. Teams e Slack nunca |
| Renovar | Informa a data nova. Registra o histórico e abre um ciclo novo de avisos |
| Estado | Derivado do relógio, como no Mural (Em dia, A vencer, Vence hoje, Vencido), mais **Encerrado**, que é explícito |
| Plataforma | Nada a pedir |

### Por que a Intranet decide o dia, e não o agendamento do Hub

A [spec da notificação do Mural](2026-09-07-notificacao-mural-design.md) recusou agendador local e
pôs o instante da entrega no Hub. Aqui a conclusão é outra porque o problema é outro.

Uma publicação agendada tem conteúdo e público **fixos** no momento em que é criada. Um vencimento
não tem nenhum dos dois:

- **A data muda.** Renovar, corrigir a data ou encerrar o item invalidaria avisos já agendados, e o
  Hub não cancela agendamento. Agendar no Hub obrigaria a pedir cancelamento à plataforma e a guardar
  aqui os ids de cada aviso de cada ciclo, só para desfazer.
- **O público muda.** Um aviso agendado hoje para daqui a onze meses congela a lista de
  destinatários de hoje. Quem saiu do setor nesse meio-tempo seria avisado, e quem entrou, não.
- **O texto muda.** "Vence em 7 dias" só é verdade se for escrito no dia.

Decidir "qual marco cabe hoje" é regra do produto, não entrega. A entrega (fila, retry, canais,
estado de lida) continua inteira no Hub, então a [ADR-0006](../adr/secco-intranet-adrs.md) segue
respeitada. O worker em processo não traz Hangfire nem banco de storage, que era a objeção do Mural.
Ele segue o padrão que a própria plataforma já usa na retenção.

## Modelo

### `ItemVencimento`

| Campo | Regra |
|---|---|
| `SetorId` | Setor dono. Obrigatório |
| `Titulo` | Obrigatório, até `IntranetOptions.MaxNameLength` |
| `Descricao` | Opcional, até `IntranetOptions.MaxDescriptionLength`. Texto puro |
| `DataVencimento` | `DateOnly`. Dia, não instante: "vence em 15/03" é a forma como as pessoas pensam |
| `ResponsavelId` | Opcional. Id de usuário do SecureGate, escolhido entre quem tem a Role `{slug}-admin` ou `{slug}-user` do setor, ou seja, quem consegue abrir o link do aviso |
| `Antecedencias` | Lista de dias antes do vencimento. De 1 a 6 valores distintos, cada um de 1 a 365. Padrão `30, 7, 1`. O aviso **no dia** sempre existe e não entra na lista |
| `Ciclo` | Começa em 1. Sobe a cada mudança de data (renovação ou correção) |
| `EncerradoEm` / `EncerradoPor` | Preenchidos quando o item é encerrado. Nulos = ativo |
| `CreatedAt` / `CreatedBy` / `UpdatedAt` | Padrão do `BaseEntity` |

Invariantes:

- A data de vencimento, na **criação**, pode estar no passado: cadastrar o que já venceu é
  legítimo, e o item já nasce Vencido, sem disparar aviso retroativo (ver "Atraso e recuperação").
- **Renovar** exige data nova posterior a hoje e à data atual.
- Item encerrado não renova nem edita. Reativar exige data posterior a hoje e abre um ciclo novo.

### `RenovacaoDeVencimento`

Histórico, só de inserção: `ItemVencimentoId`, `CicloAnterior`, `DataAnterior`, `DataNova`,
`RenovadoPor`, `RenovadoEm`, `Observacao` opcional (até `MaxDescriptionLength`).

### `AvisoDeVencimentoEnviado`

Registro de qual marco de qual ciclo já foi tratado: `ItemVencimentoId`, `Ciclo`, `Marco` (os dias
de antecedência; `0` = no dia), `TratadoEm`, `Resultado` (`Enviado`, `Pulado`, `SemDestinatario`) e
`QuantidadeDeDestinos`.

**Índice único em (`ItemVencimentoId`, `Ciclo`, `Marco`).** É ele que garante um aviso por marco
mesmo com mais de uma instância da aplicação rodando o worker ao mesmo tempo (ver "Worker").

Os nomes de tabela e coluna seguem a notação húngara do produto (skill `secco-db-naming`). Migration
nos dois engines (SQL Server e Postgres), como toda tabela do produto.

### Estado exibido

Derivado na leitura, nunca gravado, a partir de `hoje` no fuso configurado:

| Estado | Quando |
|---|---|
| Encerrado | `EncerradoEm` preenchido |
| Vencido | `hoje > DataVencimento` |
| Vence hoje | `hoje == DataVencimento` |
| A vencer | `hoje >= DataVencimento - max(Antecedencias)` |
| Em dia | Nenhum dos anteriores |

## Avisos

### Quando um marco vence

O marco `N` de um item ativo está devido quando `hoje >= DataVencimento - N`. O marco `0` está
devido quando `hoje >= DataVencimento`.

Só geram aviso os itens de setor **ativo** com o item de menu Vencimentos **ligado**. Os demais são
ignorados sem gravar nada, então, ao religar, a regra de recuperação abaixo manda um aviso só, com a
situação do dia.

### Atraso e recuperação

Em cada execução, por item, entre os marcos devidos e ainda não tratados no ciclo atual:

- **Só o mais próximo do vencimento é enviado.** Os demais são gravados como `Pulado`.
- O texto usa os **dias que realmente faltam**, não o número do marco.

Exemplo: item criado hoje, vencendo em 5 dias, com antecedências `30, 7, 1`. Os marcos 30 e 7 já
estão devidos. Sai **um** aviso, "vence em 5 dias", e os dois marcos ficam como tratados. O marco
1 sai daqui a 4 dias, e o marco 0 no dia.

O mesmo vale para a aplicação que ficou fora do ar por alguns dias: ela volta, manda um aviso só,
com a situação real, e não uma rajada atrasada.

**O marco 0 só sai no próprio dia.** Item que já passou da data sem o aviso do dia (cadastrado já
vencido, ou aplicação fora do ar na data) tem o marco 0 gravado como `Pulado`, sem envio. O estado
Vencido aparece na lista; avisar de algo que venceu há semanas, sem ninguém ter pedido, é ruído.

**Um marco mais distante que outro já tratado no ciclo nunca sai:** é gravado como `Pulado`. O caso
é a antecedência acrescentada no meio do ciclo: incluir "30 dias" num item que vence em 10 e já recebeu
o aviso de 7 repetiria o que já foi dito. Se nenhum marco foi tratado ainda, o marco novo sai
normalmente, porque ninguém foi avisado.

### Destinatários

Resolvidos no momento do envio, a partir de `IDiretorioDeUsuarios`:

- o responsável, se houver e se ainda tiver Role do setor (quem saiu do setor deixa de receber, e o
  detalhe do item mostra o responsável como "sem acesso ao setor");
- todo usuário com a Role `{slug}-admin` do setor dono.

Sem repetição: o responsável que também é admin recebe uma vez. Ninguém para avisar → marco gravado
como `SemDestinatario` e um log de aviso, para não tentar de novo a cada ciclo.

Num marco que pede e-mail, quem não tem e-mail cadastrado **recebe só no sino**: são dois lotes, um
de sino e e-mail para quem tem endereço e outro só de sino para quem não tem. O lote do Hub é
tudo-ou-nada, então a separação acontece aqui, como no `AvisoDePublicacao.Particionar` do Mural. A
diferença é que o Mural descarta quem não tem e-mail, e aqui ninguém pode ficar sem aviso de prazo.

### Mensagem

- **Título:** `"{Titulo do item} vence em {n} dia(s)"`, `"{Titulo} vence hoje"`.
- **Resumo:** setor, data de vencimento por extenso e o responsável, se houver.
- **Link:** a tela do item, `/{slug}/vencimentos/{id}`.
- **Origem/Tipo** (`Source`/`Type`): `vencimentos` / `aviso`.

### Falha do Hub

`NotificacaoIndisponivelException` no envio → o registro daquele marco é **desfeito**, e a próxima
execução tenta de novo. Os outros itens do mesmo tenant seguem.

## Worker

- **Implementação:** `BackgroundService` com `PeriodicTimer`, no molde do `LogRetentionWorker` da
  plataforma. Roda uma vez ao subir e depois a cada intervalo.
- **Por execução:** percorre o `ITenantCatalog`. Para cada tenant, cria um escopo de DI, chama
  `SetTenant(tenantId)` e executa um handler de aplicação (`ProcessarAvisosDeVencimentoHandler`) com
  o `hoje` já calculado. Falha num tenant é logada e não interrompe os demais.
- **Configuração** (seção `Intranet:Vencimentos`):
  - `Habilitado` (padrão `true`). **Sem Hub configurado o worker não roda**, qualquer que seja este
    valor, e loga o motivo ao subir. O Mural, nesse caso, usa o `NotificadorSilencioso`; aqui isso
    gravaria marcos como enviados sem ninguém ter sido avisado, e eles não voltariam quando o Hub
    fosse ligado;
  - `IntervaloEmMinutos` (padrão `60`);
  - `FusoHorario`, um id IANA (padrão `America/Sao_Paulo`). Valor inválido → o worker não sobe e
    loga erro, fail-safe como na retenção.
- **Mais de uma instância:** cada envio começa gravando o `AvisoDeVencimentoEnviado`. Se o índice
  único recusar, outra instância já tratou aquele marco e este envio é descartado. Grava primeiro,
  envia depois.
- **Relógio:** `TimeProvider` injetado, para os testes controlarem o dia.

**Ponto de atenção para o construtor:** o adaptador `SecureGateDiretorioDeUsuarios` hoje só roda
dentro de requisição HTTP. É preciso confirmar que ele resolve o tenant pelo `TenantContext`
preenchido por `SetTenant`, e não por algo que só existe no pipeline HTTP. Se não resolver, ajustar o
adaptador, sem criar um segundo caminho.

## Telas

Tudo debaixo da árvore do setor, nos dois temas oficiais, com claro e escuro (skill
`secco-intranet-temas`):

- **Lista** (`/{slug}/vencimentos`): ordenada pela data de vencimento, os mais próximos
  primeiro, com o selo de estado, responsável e data. Os encerrados ficam fora; uma opção "mostrar
  encerrados" os inclui.
- **Novo / editar**: título, descrição, data, responsável (escolhido entre as pessoas do tenant) e
  antecedências (campo com os dias, pré-preenchido com `30, 7, 1`).
- **Detalhe** (`/{slug}/vencimentos/{id}`): os dados, o estado, os próximos avisos do ciclo
  atual com a data de cada um, o histórico de renovações e os avisos já tratados.
- **Ações no detalhe:** Renovar (data nova e observação opcional), Encerrar (com confirmação) e
  Reativar.

**Editar com mudança de data** abre um ciclo novo, como a renovação, mas não entra no histórico de
renovações: é correção, e vai para a trilha como edição.

## Árvore de menu

- `TipoDeItemMenu.Vencimentos = 4`, embutido, no máximo um por setor, só desativável, nunca
  excluível. Mesmas regras de Documentos e Avisos.
- Criar setor ganha o checkbox "Habilitar Vencimentos", **desmarcado por padrão**: nem todo setor
  cuida de prazos, e Documentos e Avisos ficaram marcados por serem de uso geral.
- "Reconciliar itens de menu" **não** cria Vencimentos: o recurso é opcional e não existia antes.
  Setor já cadastrado ganha o item pela tela de menu.
- O dropdown de tipo na tela `/Setores/Menu/{id}` passa a oferecer Vencimentos quando o setor ainda
  não tem.

## Autorização

- **Ler** (lista e detalhe): `setor-{slug}:read`.
- **Escrever** (criar, editar, renovar, encerrar, reativar): `setor-{slug}:write`.
- `intranet-admin` é liberado em tudo, como no resto do produto.
- **Recurso desligado** (item de menu inativo ou ancestral inativo): 404 na leitura **e** na escrita.
- Nenhuma permissão nova no catálogo: a descrição da família `setor-{id}:write` passa a citar
  vencimentos ("publicações, documentos, vencimentos e menu").
- O worker não passa por autorização de usuário: ele lê todos os itens ativos do tenant.

## Auditoria

Verbos novos em `VerbosDeAuditoria`, com o recurso novo `vencimento` em `RecursosDeAuditoria`:
`vencimento.criar`, `vencimento.editar`, `vencimento.renovar`, `vencimento.encerrar` e
`vencimento.reativar`.

O envio automático **não** vai à trilha: não é ação de usuário, e o registro dele já mora em
`AvisoDeVencimentoEnviado`, visível no detalhe do item.

## Testes

- **Unitários (domínio e regra de marcos):**
  - antecedências fora da faixa, repetidas, ou mais de 6, são recusadas;
  - renovar com data igual ou anterior à atual, ou anterior a hoje, é recusado;
  - item encerrado não renova nem edita;
  - estado derivado em cada fronteira (véspera da janela, primeiro dia da janela, dia do
    vencimento, dia seguinte);
  - recuperação: vários marcos devidos geram um envio só, do mais próximo, e os outros como
    `Pulado`;
  - marco 0 depois do dia: `Pulado`, sem envio;
  - marco mais distante que um já tratado no ciclo: `Pulado`, sem envio;
  - setor inativo ou recurso desligado: nada enviado nem gravado;
  - responsável que perdeu a Role do setor deixa de receber;
  - sem e-mail cadastrado, num marco de e-mail: recebe no sino;
  - texto com os dias reais, não o número do marco;
  - destinatários: responsável mais `{slug}-admin`, sem repetição; ninguém → `SemDestinatario`;
  - canais pelos dias que faltam (mais de 7, só sino; 7 ou menos e no dia, sino e e-mail).
- **Integração:**
  - índice único: duas execuções concorrentes do mesmo marco geram um envio só;
  - falha do Hub desfaz o registro, e a execução seguinte envia;
  - renovar abre ciclo novo e os marcos voltam a valer;
  - recurso desligado: 404 na leitura e na escrita;
  - criação de setor com o checkbox marcado cria o item de menu, e desmarcado não cria;
  - o worker percorre mais de um tenant e a falha de um não para o outro.

## Fora de escopo

- Lembrete repetido depois do vencimento ("venceu há 7 dias e ninguém renovou").
- Anexar o arquivo do item (o certificado, o contrato). O repositório de Documentos do setor já
  serve; um vínculo entre os dois fica para quando houver pedido.
- Vencimento sem setor dono, da empresa toda.
- Importação por CSV.
- Teams e Slack.
- Exportar para calendário (ICS).
- Antecedência em horas.
- Renovação automática ou integração com quem emite o item.

## Deixado ao construtor

- Nomes exatos de tabela e coluna, pela skill `secco-db-naming`.
- Como guardar `Antecedencias`: coluna de texto com conversor ou tabela filha.
- O componente de escolha do responsável, desde que use as pessoas do tenant que o Diretório já lê.
- A divisão das views entre o core e os temas, dentro do contrato de tema.
