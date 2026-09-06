# Mural — desenho do recurso

**Data:** 2026-09-02
**Revisado:** 2026-09-06, depois da segunda entrega da plataforma
**Estado:** aprovado, pronto para plano de implementação

## Problema

O Mural é a página inicial da Intranet e o único item fixo do menu, mas hoje é uma
demonstração: `MuralDemonstracao.cs` devolve seis publicações fixas quando
`Intranet:Demo:Habilitado` está ligado, e nada quando está desligado. Não há entidade, nem
persistência, nem forma de publicar.

Este documento fecha o desenho do recurso real — item da Fase 1 do roadmap.

> **O que a revisão de 2026-09-06 mudou.** O Hub passou a ter criação em lote e canais Teams
> e Slack, e as três demandas que este desenho abriu foram atendidas. Com o lote, **o Hangfire
> saiu**: era a maior adição de infraestrutura da v1 e existia só para evitar N chamadas HTTP.
>
> **O que a revisão de 2026-09-05 mudou.** A versão original tratava auditoria como
> capacidade inexistente e não previa notificação. A plataforma publicou o sink de log, a
> trilha de auditoria e o agendamento de jobs, e o `Secco.NotificationHub` já entregava o
> inbox in-app e o envio de e-mail. Com isso entraram: visibilidade por publicação,
> prioridade, notificação e os verbos de auditoria.

## Decisões

| Eixo | Decisão |
|---|---|
| Autoria | A publicação pertence ao **setor** que publicou |
| Visibilidade | `Setor` ou `Empresa`, como no Documento — o Mural mostra o que o leitor pode ver |
| Autorização | `{slug}-admin` publica, edita e arquiva. Sem role nova (ADR-0001) |
| Conteúdo | **Markdown** guardado cru; HTML gerado na leitura, nunca persistido |
| Edição | Editor com modo WYSIWYG e modo Markdown, como melhoria progressiva |
| Ciclo de vida | Estado **derivado do relógio** — sem coluna de situação, sem job de transição |
| Prioridade | `Normal` / `Importante` / `Urgente`, definindo quais canais disparam |
| Notificação | A Intranet **cria**; o `Secco.NotificationHub` entrega |
| Auditoria | Verbos definidos aqui; a fiação do client é tarefa transversal |
| Tipo | `Aviso` / `Evento` / `Noticia`, apenas como filtro na v1 |

### O que ficou de fora, e por quê

- **Data e local para Evento.** O tipo continua sendo rótulo de filtro. Quando entrar, ganha
  colunas próprias sem quebrar o resto — foi por isso que o discriminador veio antes.
- **Anexos.** Documentos já existem como recurso próprio; ligar os dois é trabalho de outra
  branch.
- **Rascunho sem data.** Toda publicação salva tem hora de entrada no ar. "Rascunho" é uma
  publicação agendada para o futuro.
- **Menção a pessoas** (`@nome` no corpo, notificando o mencionado). Exige varrer o texto e
  resolver nomes contra o diretório, que ainda não existe como recurso real.
- **Menção e anexos** já listados acima seguem fora. Nada mais: as três demandas que este
  desenho tinha aberto na plataforma — canal Teams/Slack, provider SendGrid e criação em lote
  — foram atendidas em 2026-09-06.

## Modelo

`Publicacao`, em `src/Secco.Intranet.Domain/Publicacoes/`, herdando `BaseEntity`. O enum
`TipoPublicacao` **migra** de `Secco.Intranet.Web/Models/Mural/` para o domínio.

O enum de visibilidade é **promovido** de `VisibilidadeDocumento` para um `Visibilidade`
compartilhado no domínio: `Setor` e `Empresa` são o conceito de visibilidade do produto, não
uma característica de documento. Como a coluna guarda o inteiro, renomear o tipo **não gera
migration** para `tb_documentos`.

```text
tb_publicacoes
  id_pk_publicacao     Guid
  id_fk_setor          Guid, obrigatorio, Restrict
  ie_tipo              int    Aviso=0 | Evento=1 | Noticia=2
  ie_visibilidade      int    Setor=0 | Empresa=1
  ie_prioridade        int    Normal=0 | Importante=1 | Urgente=2
  ds_titulo            256
  ds_corpo             4000   markdown cru
  dt_publicado_em      DateTimeOffset
  dt_expira_em         DateTimeOffset?
  fl_ativo             bool
  ds_criado_por        256
  dt_created_at        DateTimeOffset
  dt_atualizado_em     DateTimeOffset?
```

`ds_corpo` fica em 4000 e não mais: acima disso o SQL Server troca `nvarchar(n)` por
`nvarchar(max)`, e a coluna deixa de ter limite real no banco — o tamanho passaria a ser
promessa da validação, não do schema.

Índices: `idx_publicacoes_id_fk_setor` e um composto sobre `fl_ativo, dt_publicado_em`, que é
a consulta do Mural. Constraints e índices nomeados explicitamente na migration, nos dois
providers (ADR-0018).

### Invariantes de domínio

- Título e corpo não vazios.
- `ExpiraEm`, quando presente, é **posterior** a `PublicadoEm`. Uma publicação que expira antes
  de nascer é erro de digitação, e o construtor recusa com `DomainInvariantException`.

### Comportamento

- `Publicacao(setorId, titulo, corpo, tipo, visibilidade, prioridade, publicadoEm, expiraEm, criadoPor)`
- `Editar(...)` — revalida as mesmas invariantes e carimba `AtualizadoEm`
- `Arquivar()` — `Ativo = false`

## Ciclo de vida e visibilidade

Não existe coluna de situação. O estado é calculado, e a consulta do Mural **é** a definição
de "no ar e visível para mim":

```sql
fl_ativo
AND dt_publicado_em <= @agora
AND (dt_expira_em IS NULL OR dt_expira_em > @agora)
AND (ie_visibilidade = 1                       -- Empresa
     OR id_fk_setor IN (@setoresDoLeitor))     -- Setor
```

Expiração não exige job: a publicação some porque a consulta deixa de encontrá-la. Nada pode
divergir do relógio, porque não há estado materializado para divergir.

Os setores do leitor saem das Roles do SecureGate, do mesmo jeito que o menu já faz
(`SetorAcesso.SlugsDoUsuario`). Sem autenticação configurada — o modo aberto de DEV — a
cláusula de visibilidade é dispensada, como no resto do produto.

Na aba do setor, quem administra vê **todas** as publicações daquele setor, com a etiqueta
calculada na hora: *agendada*, *expirada*, *arquivada* ou *no ar*.

### Fuso horário

`DateTimeOffset` na entidade; `@agora` é `DateTimeOffset.UtcNow`. O formulário usa
`datetime-local`, que não carrega offset — o valor é interpretado no fuso do servidor. É uma
simplificação consciente: uma instituição distribuída entre fusos precisará de tratamento
próprio, e isso está fora desta v1.

## Markdown

`Markdig` 1.3.2 com `DisableHtml()` no pipeline. HTML bruto no texto vira texto escapado por
construção — `<script>` nunca sobrevive à renderização. Não há sanitizador a manter
atualizado, porque não há HTML confiável a filtrar.

A renderização mora na **Web** (`Secco.Intranet.Web/Conteudo/`), não na Application:
transformar Markdown em HTML é apresentação. O DTO carrega o Markdown cru; o ViewModel
carrega o `IHtmlContent` já renderizado, que é exatamente o que o parcial `_Card` do tema já
aceita. O tema permanece sem lógica.

## Notificação

A Intranet **cria** a notificação; o `Secco.NotificationHub` **entrega**. Fila, retry, status
de entrega e provider de e-mail já existem lá (`NotificationStatus`, `EmailDispatchScheduler`,
`IEmailSender`/`MailKitEmailSender`, ADR-0015 Camada 2). Construir qualquer uma dessas peças
aqui violaria a ADR-0006.

### Prioridade define o canal

Quem publica escolhe a **urgência**, não os canais — decidir canal é decisão de plataforma, e
ninguém erra menos marcando caixinhas:

| Prioridade | Canais |
|---|---|
| `Normal` | `in_app` |
| `Importante` | `in_app` + `email` |
| `Urgente` | `in_app` + `email` + `teams`/`slack`, e destaque próprio no card do Mural |

O Hub passou a reconhecer `teams` e `slack` (ADR-0029 da plataforma), então o `Urgente`
alcança a ferramenta onde a empresa conversa. Os dois só saem se o canal estiver configurado
lá; a Intranet declara a intenção e não sabe se há webhook do outro lado.

### O leque é uma chamada só

O Hub ganhou `POST /batch` (`DispatchNotificationBatch`): **um conteúdo, muitos destinos, uma
requisição**, respondendo `202 Accepted`. Com isso cai a razão que justificava fila local — o
custo de notificar deixou de crescer com o tamanho da empresa.

**O Hangfire sai deste desenho.** A versão anterior previa `IBackgroundJobScheduler` e um banco
de storage próprio para evitar 500 chamadas HTTP; com o lote, sobra uma. Publicar chama o Hub
inline, e o Hub cuida de fila, retry e entrega — que é onde essa responsabilidade sempre
pertenceu. Infraestrutura que se evita é infraestrutura que não se opera.

Duas consequências a assumir, em vez de esconder:

- **Um destino inválido reprova o lote inteiro** (é o contrato declarado do endpoint). Um
  e-mail malformado no cadastro derruba o aviso para todo mundo, então o erro precisa chegar
  a quem publicou, com o destino culpado, e não virar log silencioso.
- **Se o Hub estiver fora do ar no instante da publicação**, a publicação é gravada e o aviso
  não sai. O publicador é avisado disso na mesma tela — publicado, sem notificar. Fila local
  para cobrir esse caso volta a ser discutida quando houver evidência de que acontece, não por
  antecipação.

O fluxo fica:

```text
1. Publicação persistida         (sincrono, e o que o usuario espera ver)
2. Destinatarios resolvidos      ListUsers do SecureGate, 1 chamada,
                                 filtro por role em memoria
3. POST /batch no Hub            1 chamada, 202 Accepted
       ↓
4. Hub: fila, retry, envio       nada disso e codigo nosso
```

### Destinatários

- Visibilidade `Empresa`: todos os usuários do tenant.
- Visibilidade `Setor`: quem tem `{slug}-admin` ou `{slug}-user` daquele setor.

`ListUsers(tenantId)` devolve os usuários **com seus roles** numa única chamada, então o
filtro é local. Quem publicou não recebe notificação da própria publicação.

### O sino

O tema ganha o sino que o wireframe da ADR-0003 previa: contador de não lidas e lista das
últimas, com título, origem e tempo relativo. Isso **cresce o contrato de tema** (ADR-0004) —
passa a existir um parcial `_Notificacoes` que todo tema publicado precisa entregar. É o
primeiro item novo do contrato desde que ele foi criado, e a decisão é consciente.

## Auditoria

A trilha deixou de estar bloqueada (`audit-entries` do `Secco.LogStream.Client`). O contrato
serve direto: `ActorId`, `Action` como verbo canônico, `ResourceType`, `ResourceId`,
`Metadata` e `CorrelationId`.

Este spec **define os verbos** que o Mural emite:

| Verbo | Recurso | Quando |
|---|---|---|
| `mural.publicar` | `publicacao` | Publicação criada |
| `mural.editar` | `publicacao` | Título, corpo, datas, tipo, visibilidade ou prioridade alterados |
| `mural.arquivar` | `publicacao` | Publicação tirada de circulação |

A **fiação** — client, token de máquina via `SeccoClientCredentialsHandler`, e o que fazer
quando o LogStream está fora do ar — é **tarefa transversal**, não desta branch: ela precisa
cobrir também Documentos, que já existe e é o dado mais sensível dos dois. Auditar só o
recurso novo, deixando download de documento sem trilha, seria arbitrário.

## Telas

### Mural (`/`) — leitura

Listagem paginada do que está no ar **e visível para o leitor**, de todos os setores, da mais
recente para a mais antiga, com as abas de filtro por tipo. Cada publicação usa o `_Card` do
contrato de tema, com o badge e o filete na cor do setor autor. Publicação `Urgente` ganha
destaque próprio.

### Aba de avisos do setor (`/setor/{slug}/avisos`) — escrita

Aba nova ao lado de Documentos, na página do setor. Cai naturalmente do modelo: a publicação
pertence ao setor, então nasce onde o setor trabalha, e nenhuma tela precisa perguntar em nome
de qual setor se publica.

O formulário tem título, tipo, corpo, visibilidade, prioridade, **entrada no ar** e
**expiração**:

- **Entrada no ar** é sempre visível e chega **preenchida com o momento atual**. Publicar
  agora é o caso comum e não exige decisão; agendar é mudar um campo que já está à vista, em
  vez de descobrir um recurso escondido atrás de um botão.
- **Expiração** é opcional e vazia por padrão.
- Abaixo dos dois campos, uma **legenda explica a regra**: a publicação aparece no Mural a
  partir da entrada no ar e deixa de aparecer na expiração. É o texto que evita a pergunta
  "por que o que eu publiquei não apareceu".
- A **prioridade** mostra, ao lado de cada nível, quais canais ela dispara. Quem escolhe
  precisa saber que `Importante` manda e-mail para a empresa toda.

O editor é o Toast UI Editor 3.2.2 (MIT), com modo WYSIWYG por padrão e alternância para
Markdown. Entra como **melhoria progressiva** sobre um `<textarea name="Corpo">` renderizado
pelo core: um tema que não traga editor continua funcional, e o contrato de tema não cresce
por causa dele.

Ao lado do editor, um botão abre a **legenda da sintaxe Markdown**. Abre por clique, com
`aria-expanded`, e também responde ao passar do mouse — só hover excluiria teclado e toque.

## Autorização

Mesma regra dos documentos, sem conceito novo:

- `{slug}-admin` publica, edita e arquiva no seu setor.
- Leitura segue a visibilidade da publicação.
- Publicação inexistente e publicação fora do alcance devolvem **o mesmo erro**, pelo mesmo
  motivo do download de documento: distinguir revelaria a existência a quem não pode vê-la.
- Sem autenticação configurada (modo aberto de DEV), a checagem é dispensada.

## Impacto no que já existe

- `MuralDemonstracao.cs` é **apagado**; o Mural deixa de ler `DemoOptions`, que continua
  existindo para o Diretório.
- O conteúdo de desenvolvimento passa a vir de um `IDevelopmentDataSeeder` (ADR-0019).
- `TipoPublicacao` sai de `Web/Models/Mural/` e passa a viver no domínio.
- `VisibilidadeDocumento` vira `Visibilidade`, compartilhado. Sem migration.
- `SetorController` ganha a aba de avisos.
- **O contrato de tema cresce**: parcial `_Notificacoes` para o sino.
- **Nenhuma infraestrutura nova.** A versão anterior deste desenho trazia Hangfire e um banco
  de storage; o lote do Hub tornou os dois desnecessários.

## Testes

O coração são as duas regras que decidem o que aparece — relógio e visibilidade:

| Caso | Esperado no Mural |
|---|---|
| No ar, visibilidade Empresa | aparece para qualquer leitor |
| No ar, visibilidade Setor, leitor com a role | aparece |
| No ar, visibilidade Setor, leitor sem a role | não aparece |
| Agendada para o futuro | não aparece |
| Expirada | não aparece |
| Arquivada | não aparece |
| `dt_expira_em` exatamente igual a agora | não aparece (a comparação é `>`) |
| Sem expiração | aparece indefinidamente |

Além disso:

- Invariantes de domínio, incluindo expiração anterior à entrada no ar.
- Renderização: `<script>` vira texto escapado; `**negrito**` vira `<strong>`.
- Autorização: quem não é `{slug}-admin` não publica, não edita e não arquiva, e recebe o mesmo
  erro de inexistente.
- Notificação: prioridade define os canais; destinatários respeitam a visibilidade; quem
  publicou não se notifica; publicar **enfileira** em vez de chamar o Hub na requisição.
- Isolamento entre tenants, no padrão de `DocumentoTenantIsolationTests`.
- Fluxo HTTP de publicar, editar e arquivar, com antiforgery.
