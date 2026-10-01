# Menu principal com a árvore dos setores — desenho do recurso

**Data:** 2026-10-01
**Estado:** rascunho, aguardando revisão
**Revisa:** [2026-09-30-item-menu-por-setor-design.md](2026-09-30-item-menu-por-setor-design.md) — a parte de dados e administração continua valendo; a navegação daquela spec (página do setor com abas, redirecionamento ao primeiro filho, prefixo `/setor/`) é substituída por esta.

## Problema

A primeira entrega da árvore `ItemMenu` pôs a navegação **dentro** da página do setor: o
menu principal mostrava só o setor; clicar nele redirecionava ao primeiro filho ativo, e os
irmãos apareciam como abas. Usando, ficou claro que esse não é o modelo esperado:

- um item com filhos deixava de ser visível como item — virava só um degrau de
  redirecionamento;
- a árvore não aparecia de onde se navega (o menu principal), só depois de entrar no setor;
- a URL carregava um prefixo técnico (`/setor/financeiro/painel-bi`) em vez do caminho que
  a pessoa vê no menu (`/financeiro/painel-bi`).

O modelo pedido é o de menu com raízes e submenus: o setor é o nível 0, sem link; passar o
mouse abre o nível 1; um filho que tem filhos abre o nível 2, e assim por diante. Todo item
continua existindo e aparecendo, com os filhos agrupados debaixo dele.

## Decisões

| Eixo | Decisão |
|---|---|
| O que é um setor | Continua sendo o mesmo conceito (Role tenant-scoped, permissão `setor-{slug}:read/write`, dono do conteúdo). A raiz `ItemMenu` dele é o **nó de nível 0** do menu principal |
| O que vira árvore no menu principal | **Só a seção dos setores.** Mural, Diretório, Inventário e Administração continuam itens fixos, como hoje |
| Setor no menu | Nó sem link: só agrupa. Hover/foco/toque abre os filhos |
| Item com filhos | Continua sendo um item. Se tem rota/tipo próprio, é link **e** abre os filhos (botão de seta ao lado). Se não tem, só agrupa |
| Regra do clique | Nó com destino navega; nó sem destino só abre/fecha os filhos |
| Nó sem destino e sem filhos ativos | **Não aparece no menu** (nem setor sem item ativo, nem "Relatórios" vazio). Aparece só na tela de administração |
| URL | `/{setor}/{item}/{subitem}/…` — sem prefixo. Documentos e Avisos ficam em `/{setor}/…/documentos` e `/{setor}/…/avisos` |
| URLs `/setor/…` antigas | Deixam de existir, sem redirecionamento — o produto não tem uso real ainda |
| Abas na página do setor | Saem |
| Submenus | Flutuantes nos dois temas (Vertical e Horizontal); em tela estreita abrem recuados embaixo do pai |
| Auditoria | Toda ação na árvore é auditada (criar, ativar/desativar, mover, excluir, reconciliar) |

## 1. Contrato de navegação

`NavigationItemModel` ganha filhos e passa a aceitar item sem URL e sem ícone:

```csharp
public sealed record NavigationItemModel(
    string Texto,
    string? Icone,
    string? Url,                                  // null = só agrupa (não é link)
    bool Ativo = false,                           // página atual OU um descendente dela
    string? SetorSlug = null,
    IReadOnlyList<NavigationItemModel>? Filhos = null);
```

`NavigationGroupModel` e `NavigationModel` não mudam. O grupo "Setores" passa a conter um
item por setor com `Url = null` e os filhos montados da árvore.

`Ativo` marca todo o caminho: em `/financeiro/relatorios/painel-bi`, ficam ativos
Financeiro, Relatórios e Painel BI. O tema usa isso para destacar o setor na barra e o
trilho dentro do submenu; `aria-current="page"` vai só no último.

**Montagem (core).** O `NavigationViewComponent` já filtra os setores visíveis (ativos +
permissão de leitura). Ele passa a carregar as árvores de **todos** esses setores numa
consulta só — `IItemMenuRepository.ListarPorSetoresAsync(IReadOnlyCollection<Guid>)` — e
`IntranetNavigation.Build` recebe as árvores junto com os setores. Regras da montagem:

- só itens ativos entram; um item inativo esconde a subárvore inteira dele;
- ordem entre irmãos por `Ordem`;
- destino de cada nó: Documentos/Avisos → URL do caminho; Personalizado com `Rota` → a
  rota (local ou absoluta); Personalizado sem `Rota` → sem URL;
- nó sem URL e sem filhos visíveis é podado (de baixo para cima — uma cadeia de
  agrupadores vazios some inteira); setor que fica sem filhos some do menu.

`docs/temas.md` documenta o novo campo e o comportamento esperado dos submenus.

## 2. Rotas e páginas

**Rota do setor na raiz.** `SetorController` sai de `[Route("setor/{slug}")]` para
`{slug}/{**caminho}` na raiz. As rotas literais dos outros controllers (`/mural`,
`/diretorio`, `/inventario`, `/setores`, `/acesso`, `/conta`, `/documentos`, …) vencem por
precedência do roteamento.

Os POSTs ficam em `/{slug}/documentos`, `/{slug}/avisos` e
`/{slug}/{documentos|avisos}/{id}/arquivar`, com o mesmo comportamento de hoje (404 quando o
recurso está desligado).

**Slugs reservados.** Para um setor nunca colidir com uma rota do produto,
`CreateSetorHandler` recusa slug reservado (erro novo `IntranetErrors.Setores.SlugReservado`).
A lista fica num lugar só (`SlugsReservados` no Application) e inclui, além dos prefixos dos
controllers, os caminhos de infraestrutura (`_content`, `css`, `js`, `lib`, `health`, …).

Um **teste estrutural** percorre todos os endpoints registrados (`EndpointDataSource`: rota
de atributo, rota convencional, health check) e falha se o primeiro segmento fixo de algum
não estiver em `SlugsReservados`. Um controller novo que esqueça de reservar o nome quebra
o teste, não a produção.

Setores já existentes com slug reservado não são renomeados: os de desenvolvimento não
colidem, e em produção não há setores ainda (ver "URLs antigas").

**O que cada caminho abre:**

| Nó resolvido | Resposta |
|---|---|
| Documentos | Página de documentos do setor, sem abas |
| Avisos | Página de avisos do setor, sem abas |
| Personalizado com rota | Redireciona para a rota |
| Setor (raiz) ou Personalizado sem rota | 404 — é agrupador, o menu não oferece link para ele |
| Caminho que não existe, item inativo ou ancestral inativo | 404 |

**Trilho.** O subtítulo do `PageHeaderModel` mostra os ancestrais
("Financeiro › Relatórios"), sem mudança de contrato do tema.

**Sai do código:** redirecionamento ao primeiro filho, `MontarAbas`, `_AbasDoSetor.cshtml`,
`ItemMenuAbaDto`, a propriedade `Abas` dos ViewModels, `SemItens.cshtml`,
`SemConteudo.cshtml`. `ResolverCaminhoDeMenuHandler` perde `PrimeiroFilhoAtivo` e `Irmaos` e
passa a devolver o nó e os ancestrais.

**Links antigos.** Todo `/setor/{slug}` gerado no código (menu, cards do Mural, detalhe do
setor, redirecionamentos depois de POST, comentários) passa a `/{slug}`. A varredura por
`"/setor/"` e por `asp-controller="Setor"` faz parte do plano.

## 3. Submenus nos temas

A marcação é a mesma nos dois temas; muda só o posicionamento e o estilo.

**Marcação.** O servidor renderiza a árvore inteira como listas aninhadas:

- nó com destino → `<a href>`;
- nó sem destino → `<button type="button" aria-expanded="false" aria-haspopup="true">`
  (anunciado como expansível, não como link);
- nó com destino e filhos → o `<a>` mais um botão de seta para abrir os filhos sem navegar.

**Posicionamento.** O menu dos dois temas está num contêiner com rolagem
(`overflow-y: auto` na barra lateral do Vertical, `overflow-x: auto` na barra do Horizontal);
um submenu `position: absolute` seria cortado. Por isso o painel usa `position: fixed` e o
`theme.js` calcula a posição a partir do item ao abrir:

- Vertical: à direita da barra; níveis seguintes à direita do painel anterior;
- Horizontal: nível 1 abaixo do item; níveis seguintes à direita;
- sem espaço, abre para o lado oposto;
- Vertical recolhido (só ícones): o painel do setor traz o nome dele no topo.

**Abrir e fechar.**

- Mouse: abre no hover; fecha ~300 ms depois de sair (atravessar na diagonal não fecha).
- Toque: um toque no agrupador abre; tocar fora fecha.
- Teclado: Enter/Espaço/seta abre e foca o primeiro filho; setas navegam entre irmãos;
  Esc fecha e devolve o foco a quem abriu. `aria-expanded` acompanha o estado.
- Abrir um submenu fecha os irmãos abertos.

**Tela estreita.** Na gaveta (Vertical) e no painel (Horizontal) do celular, o submenu não
flutua: abre recuado embaixo do pai, por toque.

**Sem JavaScript.** A árvore aparece toda aberta, em listas recuadas — tudo alcançável,
mesmo princípio do aviso de feedback que chega visível do servidor.

`prefers-reduced-motion` desliga qualquer transição dos painéis.

## 4. Auditoria

As ações da árvore entram na trilha (`ITrilhaDeAuditoria`), só depois de salvas:

| Verbo | Quando | Payload |
|---|---|---|
| `menu.item-criar` | `CriarItemMenuHandler` | setor, item, pai, nome, slug, tipo, rota |
| `menu.item-ativar` / `menu.item-desativar` | `AtivarDesativarItemMenuHandler` | setor, item, nome, tipo |
| `menu.item-mover` | `MoverItemMenuHandler` (só quando muda de posição) | setor, item, nome, posição anterior e nova |
| `menu.item-excluir` | `ExcluirItemMenuHandler` | setor, item, nome, slug, rota |
| `menu.reconciliar` | `ReconciliarItensDeMenuHandler` (só quando criou algo) | quantidade de setores alterados |

Recurso novo `RecursosDeAuditoria.Menu = "menu"`; o identificador do registro é o id do
item (ou `"reconciliacao"`). A criação automática da árvore junto com o setor não gera
registro próprio — já está coberta por `setor.criar`.

## Testes

- **Unidade**
  - montagem da navegação: setor sem itens ativos some; agrupador vazio (inclusive cadeia)
    some; item inativo esconde a subárvore; ordem por `Ordem`; `Ativo` marca o caminho
    inteiro; Personalizado com rota vira link para a rota;
  - `CreateSetorHandler` recusa slug reservado;
  - `ResolverCaminhoDeMenuHandler`: devolve nó e ancestrais; 404 nos casos da tabela;
  - cada handler da árvore registra o verbo de auditoria certo, e não registra quando a
    ação falha.
- **Estrutural:** endpoints registrados × `SlugsReservados`.
- **Integração**
  - `SetorMenuRotaTests` e `SetorMenuAdministracaoTests` reescritos para `/{slug}/…`, sem
    abas e sem redirecionamento; `/setor/{slug}/…` responde 404;
  - o menu renderizado traz o setor como `<button aria-expanded>` com os filhos aninhados,
    nos dois temas;
  - setor sem permissão de leitura não aparece no menu — o filtro do componente não muda;
    por HTTP ele não é alcançável no ambiente Testing (sem autenticação configurada, o
    componente mostra todo setor ativo), então continua coberto em `PermissoesDeSetorTests`;
  - POST de Documentos/Avisos nas rotas novas, inclusive 404 com recurso desligado.
- **Comportamento dos submenus (JS):** sem teste automatizado; verificação no navegador nos
  dois temas — mouse, teclado, Vertical recolhido, largura de celular e sem script.

## Fora de escopo

- arrastar para reordenar;
- permissão por item (vale a do setor);
- item que aponta para outro setor;
- cache da árvore do menu (uma consulta por página basta por ora);
- transformar Mural/Diretório/Inventário/Administração em nós da árvore.
