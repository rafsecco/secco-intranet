# Mural — desenho do recurso

**Data:** 2026-09-02
**Estado:** aprovado, pronto para plano de implementação

## Problema

O Mural é a página inicial da Intranet e o único item fixo do menu, mas hoje é uma
demonstração: `MuralDemonstracao.cs` devolve seis publicações fixas quando
`Intranet:Demo:Habilitado` está ligado, e nada quando está desligado. Não há entidade, nem
persistência, nem forma de publicar.

Este documento fecha o desenho do recurso real — item da Fase 1 do roadmap.

## Decisões

| Eixo | Decisão |
|---|---|
| Autoria | A publicação pertence ao **setor** que publicou; o Mural é a união do que está no ar |
| Autorização | `{slug}-admin` publica, edita e arquiva. Sem role nova (ADR-0001) |
| Conteúdo | **Markdown** guardado cru; HTML gerado na leitura, nunca persistido |
| Edição | Editor com modo WYSIWYG e modo Markdown, como melhoria progressiva |
| Ciclo de vida | Estado **derivado do relógio** — sem coluna de situação, sem job |
| Tipo | `Aviso` / `Evento` / `Noticia`, apenas como filtro na v1 |

### O que ficou de fora, e por quê

- **Data e local para Evento.** O tipo continua sendo rótulo de filtro. Quando entrar, ganha
  colunas próprias sem quebrar o resto — foi por isso que o discriminador veio antes.
- **Anexos.** Documentos já existem como recurso próprio; ligar os dois é trabalho de outra
  branch.
- **Rascunho sem data.** Toda publicação salva tem hora de entrada no ar. "Rascunho" é uma
  publicação agendada para o futuro.
- **Trilha de auditoria.** Proibida por [ADR-0006](../adr/secco-intranet-adrs.md). `CriadoPor`
  é metadado do registro, não trilha de ações.

## Modelo

`Publicacao`, em `src/Secco.Intranet.Domain/Publicacoes/`, herdando `BaseEntity`. O enum
`TipoPublicacao` **migra** de `Secco.Intranet.Web/Models/Mural/` para o domínio.

Colunas resultantes da convention (ADR-0017) — nenhuma nomeada à mão:

```
tb_publicacoes
  id_pk_publicacao     Guid
  id_fk_setor          Guid, obrigatorio, Restrict
  ie_tipo              int    Aviso=0 | Evento=1 | Noticia=2
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

- `Publicacao(setorId, titulo, corpo, tipo, publicadoEm, expiraEm, criadoPor)`
- `Editar(titulo, corpo, tipo, publicadoEm, expiraEm)` — revalida as mesmas invariantes e
  carimba `AtualizadoEm`
- `Arquivar()` — `Ativo = false`

## Ciclo de vida

Não existe coluna de situação. O estado é calculado, e a consulta do Mural **é** a definição
de "no ar":

```sql
fl_ativo
AND dt_publicado_em <= @agora
AND (dt_expira_em IS NULL OR dt_expira_em > @agora)
```

Expiração não exige job: a publicação some porque a consulta deixa de encontrá-la. Nada pode
divergir do relógio, porque não há estado materializado para divergir.

Na aba do setor, quem administra vê **todas** as publicações, com a etiqueta calculada na
hora — *agendada*, *expirada*, *arquivada* ou *no ar*. É o único lugar onde o que não está no
ar aparece.

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

## Telas

### Mural (`/`) — leitura

Listagem paginada do que está no ar, de todos os setores, da mais recente para a mais antiga,
com as abas de filtro por tipo que já existem. Cada publicação usa o `_Card` do contrato de
tema, com o badge e o filete na cor do setor autor.

### Aba de avisos do setor (`/setor/{slug}/avisos`) — escrita

Aba nova ao lado de Documentos, na página do setor. Cai naturalmente do modelo: a publicação
pertence ao setor, então nasce onde o setor trabalha, e nenhuma tela precisa perguntar em nome
de qual setor se publica.

O formulário tem título, tipo, corpo, **entrada no ar** e **expiração**:

- **Entrada no ar** é sempre visível e chega **preenchida com o momento atual**. Publicar
  agora é o caso comum e não exige decisão; agendar é mudar um campo que já está à vista, em
  vez de descobrir um recurso escondido atrás de um botão.
- **Expiração** é opcional e vazia por padrão.
- Abaixo dos dois campos, uma **legenda explica a regra**: a publicação aparece no Mural a
  partir da entrada no ar e deixa de aparecer na expiração. É o texto que evita a pergunta
  "por que o que eu publiquei não apareceu".

O editor é o Toast UI Editor 3.2.2 (MIT), com modo WYSIWYG por padrão e alternância para
Markdown. Entra como **melhoria progressiva** sobre um `<textarea name="Corpo">` renderizado
pelo core: um tema que não traga editor continua funcional, e o contrato de tema (ADR-0004)
não cresce.

Ao lado do editor, um botão abre a **legenda da sintaxe Markdown**. Abre por clique, com
`aria-expanded`, e também responde ao passar do mouse — só hover excluiria teclado e toque.

## Autorização

Mesma regra dos documentos, sem conceito novo:

- `{slug}-admin` publica, edita e arquiva no seu setor.
- Qualquer usuário autenticado lê o Mural.
- Publicação inexistente e publicação de setor que o usuário não administra devolvem **o mesmo
  erro**, pelo mesmo motivo do download de documento: distinguir revelaria a existência a quem
  não pode vê-la.
- Sem autenticação configurada (modo aberto de DEV), a checagem é dispensada, como no resto do
  produto.

## Impacto no que já existe

- `MuralDemonstracao.cs` é **apagado**; o Mural deixa de ler `DemoOptions`. `DemoOptions`
  continua existindo para o Diretório.
- O conteúdo de desenvolvimento passa a vir de um `IDevelopmentDataSeeder`, ao lado do de
  setores (ADR-0019).
- `TipoPublicacao` sai de `Web/Models/Mural/` e passa a viver no domínio.
- `SetorController` ganha a aba de avisos; `SetorDocumentosViewModel` e a view de documentos
  não mudam.

## Testes

O coração é a consulta de ciclo de vida, e cada caso ganha teste próprio:

| Caso | Esperado no Mural |
|---|---|
| No ar | aparece |
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
- Isolamento entre tenants, no padrão de `DocumentoTenantIsolationTests`.
- Fluxo HTTP de publicar, editar e arquivar, com antiforgery.
