# Árvore de itens de menu por setor (`ItemMenu`) — desenho do recurso

**Data:** 2026-09-30
**Estado:** rascunho, aguardando revisão

## Problema

Hoje `/setor/{slug}` tem exatamente duas abas fixas, embutidas no código de
`SetorController`: Documentos e Avisos. Todo setor ativo as tem, sem exceção — não existe
como desligar uma delas para um setor que não precisa, nem como adicionar algo próprio sem
alterar o controller.

O roadmap já registrava duas necessidades relacionadas, como itens separados:
`Recurso`/`SetorRecurso` (catálogo de módulos habilitáveis por setor, Fase 1) e `ItemMenu`
(tabela autorecursiva + tela de montagem de menu com níveis, Fase 2, hoje a fonte estática
`IntranetNavigation`). No brainstorming que originou esta spec, ficou claro que são o mesmo
problema: "ligar/desligar um recurso para um setor" é, na prática, "existir ou não um nó na
árvore daquele setor". Construir os dois seria desenhar o toggle duas vezes. Esta spec
substitui `Recurso`/`SetorRecurso` inteiramente e entrega a fatia de `ItemMenu` que fica
**dentro** da página de um setor — não o menu inteiro do produto.

Motivação adicional, levantada durante o brainstorming: setores podem precisar de
subdivisões próprias e arbitrárias (o exemplo usado foi um item "Relatórios" com uma filha
por tipo de relatório, algo que só o adotante que estende o produto vai construir). Isso só
é possível com uma árvore genuína — um nível fixo de abas não resolve.

## Decisões

| Eixo | Decisão |
|---|---|
| Fronteira do escopo | O menu lateral/superior (`IntranetNavigation`, `NavigationViewComponent`) **não muda**. Um Setor continua aparecendo ali exatamente como hoje (ativo + permissão de leitura). A árvore vive inteiramente dentro de `/setor/{slug}`, como navegação por abas — recursiva quando um nó tem filhos |
| `Recurso`/`SetorRecurso` | Absorvido por esta spec — não nasce como tabela própria. "Setor X tem o recurso Documentos" é só "existe um `ItemMenu` filho do tipo Documentos, ativo, debaixo da raiz de X" |
| `ItemMenu` além do setor (Mural, Diretório, Inventário, o próprio topo do menu virarem árvore) | Fora de escopo — fica para quando (se) o produto precisar generalizar. Esta spec já usa o nome `ItemMenu` e o formato de tabela para não exigir migração de schema nesse dia, mas não constrói nada fora da árvore de um setor |
| Setor é um nó da árvore | **Sim.** Toda vez que um `Setor` nasce, nasce junto uma linha `ItemMenu` raiz (`Tipo = Setor`, `ParentId = null`) representando-o. Documentos, Avisos e qualquer item personalizado são sempre filhos dela — nunca existe um item "solto" sem pai dentro da árvore de um setor |
| Permissão por nó | Fora de escopo nesta rodada. Todo nó da árvore de um setor usa a mesma permissão do setor (`setor-{slug}:read` para ver, `setor-{slug}:write` para publicar/editar/(des)ativar itens) — sem exceção por nó. Fica registrado como extensão futura possível, não como ausência não pensada |
| Acesso de um usuário de outro setor | Sem mudança — já funciona hoje via atribuição de Role (`{slug}-user`/`{slug}-admin`) a qualquer usuário pela tela `/Acesso`, independente do setor "de origem" dele. Esta spec só precisa continuar checando a mesma permissão, não a associação de membro |
| Item "Personalizado" | Só um rótulo e uma rota opcional (link). Sem mecanismo de extensão/plugin — quem constrói a tela por trás é o adotante, em código próprio; o `ItemMenu` só aponta para lá |

## Modelo de dados

Nova entidade, `Secco.Intranet.Domain.Menu.ItemMenu` (namespace novo — mesmo escopado a
setor nesta rodada, é um conceito que o produto já projeta crescer, então não entra dentro
de `Secco.Intranet.Domain.Setores`):

```csharp
public sealed class ItemMenu : BaseEntity
{
    public Guid SetorId { get; }        // denormalizado em toda linha, inclusive a raiz — evita
                                         // subir a árvore só para saber de qual setor é
    public Guid? ParentId { get; }      // null só na linha raiz (Tipo = Setor)
    public string Nome { get; }         // rótulo — sempre obrigatório, mesmo em Personalizado
    public string Slug { get; }         // único entre irmãos; monta a URL (/setor/x/relatorios/vendas)
    public TipoDeItemMenu Tipo { get; } // Setor | Documentos | Avisos | Personalizado
    public string? Rota { get; }        // só usado em Personalizado
    public string? Icone { get; }       // Bootstrap Icons (mesma validação de Setor.Icone); vazio = sem ícone
    public int Ordem { get; }           // posição entre irmãos; sem papel na raiz
    public bool Ativo { get; }          // soft toggle; sem papel na raiz (segue Setor.Ativo)
}
```

A raiz (`Tipo = Setor`) não usa `Ordem`/`Ativo`/`Ícone`/`Rota`/`Slug` de forma
significativa — a mesma assimetria que já existe em `Rota` (só vale para `Personalizado`).
São gravados com um valor fixo (`Ordem = 0`, `Ativo = true`, `Ícone`/`Rota` nulos, `Slug` =
o `Slug` do `Setor`) e nunca lidos por nenhuma tela: a URL nunca soletra a raiz — o próprio
`{slug}` de `/setor/{slug}` já a identifica, e o caminho começa nos filhos dela. `Nome` da
raiz recebe o `Nome` do `Setor` no momento da criação — nem ele nem `Slug` são
sincronizados depois se o setor for renomeado (existem só para as colunas não ficarem
vazias, e para quem inspecionar a tabela direto reconhecer do que se trata). Aceito
deliberadamente manter uma tabela só e uma árvore genuína, em vez de um tipo especial fora
do modelo.

Invariantes:

- `Nome` obrigatório sempre.
- `Slug` único entre irmãos (mesmos `ParentId`) — não precisa ser único no setor inteiro,
  só dentro do mesmo nível.
- No máximo um filho `Documentos` e um `Avisos` por setor — o recurso por trás é único por
  setor (não existem dois repositórios de documentos do mesmo setor); `Personalizado` não
  tem esse limite.
- `ParentId`, quando informado, precisa apontar para um `ItemMenu` do **mesmo** `SetorId`.
- Sem ciclo: um nó não pode ser ancestral de si mesmo. Nesta rodada isso vale **por
  construção**: `ParentId` só é definido na criação, apontando para um nó que já existe, e
  nenhuma ação o altera depois — um item novo não pode ser ancestral de ninguém. Quando
  existir uma ação de "mover para outro pai", ela traz a checagem (o algoritmo de
  `RegrasDeGestor.CriariaCiclo`, do Diretório); escrevê-la agora seria código morto.
- Toda linha `Tipo = Setor` é única por `SetorId` e nasce/morre junto com o `Setor` — não é
  criável nem excluível pela tela.
- `Documentos`/`Avisos` nunca se excluem pela tela, só desativam (`Ativo = false`) — o
  histórico (documentos publicados, avisos arquivados) continua existindo mesmo desligado.
  `Personalizado` pode ser excluído de verdade — é o único tipo sem dado embutido atrás.

## Rotas e renderização

`GET /setor/{slug}/{**caminho}`, onde `caminho` é a sequência de `Slug` descendo a árvore
daquele setor a partir da raiz (vazio = raiz).

Resolução, um segmento por vez:

1. Acha o `Setor` pelo slug (como hoje).
2. Acha a linha raiz (`Tipo = Setor`) daquele setor.
3. Desce um nível por segmento do `caminho`, casando com o `Slug` de um filho **ativo**
   naquele nível. Qualquer segmento sem match, ou nó desativado, é 404 — mesma resposta de
   setor inexistente (não revela se o item existe desativado).
4. No nó final:
   - **Tem filhos ativos** (inclusive a própria raiz, quando `caminho` é vazio) → renderiza
     como abas dos filhos. Um componente só, usado tanto para a raiz do setor quanto para
     qualquer nó-pai personalizado (ex.: "Relatórios") — sem view duplicada por nível.
   - **Folha, `Documentos`** → a tela de Documentos de hoje. Handler inalterado.
   - **Folha, `Avisos`** → a tela de Avisos de hoje. Handler inalterado.
   - **Folha, `Personalizado`, com `Rota`** → redireciona para lá.
   - **Folha, `Personalizado`, sem `Rota`** → página "sem conteúdo ainda".

As rotas de **ação** de Documentos/Avisos (publicar, arquivar) continuam exatamente
`/setor/{slug}/documentos` e `/setor/{slug}/avisos` — como só pode haver um nó de cada tipo
por setor, não há ambiguidade em não passar pelo caminho da árvore. Zero mudança nos
handlers `PublicarDocumentoHandler`/`ArquivarHandler`/`PublicarPublicacaoHandler`/etc.

## Administração

Nova ação `SetoresController.Menu(Guid id)`, página própria (não embutida em `Edit`) —
mesmo padrão de `AcessoController.Perfil` ser uma tela de detalhe separada da listagem.

- Lista a árvore daquele setor, indentada (pai → filhos), com ícone, nome, tipo, badge
  ativo/inativo.
- Formulário de novo item: Nome (gera o Slug automaticamente, editável), Tipo — o dropdown
  não oferece Documentos/Avisos se aquele setor já os tem —, Ícone opcional, Rota (só
  aparece com Tipo = Personalizado), Pai (dropdown dos nós existentes do setor; a raiz
  sempre aparece como opção).
- Cada linha: ativar/desativar (Documentos, Avisos, Personalizado — nunca a raiz), excluir
  (só Personalizado), mover para cima/para baixo entre irmãos (troca `Ordem` com o vizinho
  — sem drag-and-drop).

## Criação, seed e migração

- **Criar um Setor**: o formulário ganha dois checkboxes, **marcados por padrão** —
  "Habilitar Documentos" e "Habilitar Avisos". Ao salvar, `CreateSetorHandler` cria o
  `Setor`, cria a linha raiz `ItemMenu` (sempre, não é escolha) e, para cada checkbox
  marcado, cria o filho correspondente, em ordem alfabética do nome (`Avisos` antes de
  `Documentos`). Depois de criado, adicionar/remover é só usar a tela da seção anterior —
  os checkboxes são um atalho para o caso comum, não o único caminho.
- **Setor já existente** (criado antes desta feature, sem nenhum `ItemMenu`): ação
  "Reconciliar itens de menu" na listagem de Setores, mesmo padrão de "Reconciliar
  permissões" já entregue. Cria a linha raiz que falta e, incondicionalmente (não é
  escolha — já estão em uso de verdade), Documentos e Avisos. Rodar de novo não duplica.

## Autorização

- **Ler** a árvore (raiz e qualquer nó, em `/setor/{slug}/{**caminho}`) exige
  `setor-{slug}:read` — mesma permissão de sempre, `{slug}-user`/`{slug}-admin` já a têm.
- **Administrar** a árvore (criar, editar, (des)ativar, excluir, reordenar item — a tela de
  `SetoresController.Menu`) é **exclusiva do `intranet-admin`**, a mesma fronteira do
  cadastro/edição do próprio Setor (`SomenteIntranetAdminAttribute`) — não
  `setor-{slug}:write`. Um `{slug}-admin` continua podendo publicar/arquivar Documentos e
  Avisos (isso não muda), mas não decide se esses itens existem, em que ordem, nem cria
  itens `Personalizado`.
- `intranet-admin` continua liberado em tudo, como em todo o resto do produto (ADR-0008).
- Nenhuma permissão nova nasce nesta spec — reaproveita o par que a spec do modelo de
  permissões (2026-09-27) já criou.
- **Achado ao planejar a implementação (2026-09-30):** `GET /setor/{slug}` (Documentos e
  Avisos) hoje **não** checa `setor-{slug}:read` — só exige autenticação; só a escrita
  (`PodePublicarAsync`) checa permissão do setor. Confirmado com o dono do produto: esta
  spec fecha essa lacuna junto — a resolução da árvore passa a checar `read` do mesmo jeito
  que a escrita já checa `write`, usando o `IPermissoesDeSetor` que já existe. É uma mudança
  de comportamento em Documentos/Avisos, não só a superfície nova do `ItemMenu`.

## Testes

- **Unitários:** ciclo em `ParentId` é recusado; segundo `Documentos`/segundo `Avisos` no
  mesmo setor é recusado; `Slug` duplicado entre irmãos é recusado; mover para cima/baixo
  troca `Ordem` corretamente nas bordas (primeiro/último item); a raiz nunca é
  criável/excluível pela tela; reconciliação roda duas vezes sem duplicar.
- **Integração:**
  - Resolução de rota: caminho vazio abre a raiz (abas dos filhos ativos); item desativado
    no meio do caminho é 404; item com filhos abre abas; item folha abre a tela certa por
    `Tipo`; `Personalizado` sem `Rota` mostra o placeholder, com `Rota` redireciona.
  - Autorização de leitura: usuário sem `setor-{slug}:read` recebe 404 em qualquer nó da
    árvore, inclusive `GET /setor/{slug}` "puro". **Sem teste de integração possível**: a
    checagem tem o mesmo bypass da escrita (`PodePublicarAsync` — sem autenticação
    configurada, libera), e o ambiente `Testing` nunca configura autenticação; é a mesma
    limitação já registrada em `MenuVisibilidadeDeSetorTests`. A permissão em si é coberta
    em `PermissoesDeSetorTests`; a chamada pelo controller é conferida em revisão.
  - Recurso desligado (item Documentos/Avisos desativado, ou ancestral desativado): 404 na
    leitura **e** na escrita (`POST` de publicar/arquivar) — desligar não pode só esconder
    a aba.
  - Autorização de administração: mesma matriz que já existe para as rotas de
    `SetoresController` (intranet-admin liberado, `{slug}-admin` sem `intranet-admin`
    bloqueado, usuário comum bloqueado) — reaproveitada 1:1 para as rotas de
    `SetoresController.Menu`, sem teste novo a inventar.
  - Criação de setor: checkboxes marcados geram os dois filhos, em ordem alfabética;
    desmarcados geram só a raiz.
  - Reconciliação: setor sem nenhum `ItemMenu` passa a ter raiz + Documentos + Avisos.

## Fora de escopo

- Permissão própria por nó da árvore (ex.: um filho visível só com uma permissão extra,
  mesmo para quem já tem `setor-{slug}:read`) — decisão explícita de deixar para depois.
- Tipo `Vencimento`/`ItemVencimento` como filho embutido — fica para quando esse recurso
  (Fase 3 do roadmap) for construído; a árvore já suporta como `Personalizado` até lá.
- Mecanismo de extensão/plugin para `Personalizado` renderizar algo dentro do próprio
  layout — o campo `Rota` (link) é a única integração desta rodada.
- Generalizar `ItemMenu` para fora da árvore de um setor (Mural, Diretório, Inventário, o
  topo do menu virarem linhas da mesma tabela) — mencionado no roadmap, não decidido aqui.
- Reordenação por arrastar-e-soltar — os botões mover para cima/para baixo bastam por
  ora.
- Tela própria de "conceder acesso de outro setor" — já existe via `/Acesso`, sem mudança
  necessária.
