# Nível de acesso por item da árvore do setor — desenho do recurso

**Data:** 2026-11-06
**Estado:** aprovada e implementada
**Issue:** [#6](https://github.com/rafsecco/secco-intranet/issues/6)
**Revisa:** a decisão "Permissão por nó: fora de escopo" de [2026-09-30-item-menu-por-setor-design.md](2026-09-30-item-menu-por-setor-design.md).

## Problema

O menu mostra um setor a quem tem `setor-{slug}:read`, e todos os itens da árvore aparecem para quem vê o setor. Isso não basta quando um item leva a uma página que exige mais que a leitura do setor, como uma página de RCL com dados pessoais que exige `setor-{slug}:write`.

Hoje, quem só lê o setor vê o item, clica e recebe 404 da própria página. Se a leitura do setor for aberta a toda a empresa com o perfil `tenant-member`, o item aparece para todo mundo.

## Objetivo

O `intranet-admin` escolhe, em cada item da árvore, se ele aparece no menu para quem lê o setor (padrão) ou só para quem escreve nele. O menu para de oferecer um link que a pessoa não consegue usar.

## Fora do escopo

- **Permissão arbitrária por item**, com nome digitado pelo admin.
- **Proteger a página de destino.** O nível só decide a visibilidade no menu; a página continua responsável pela própria autorização.
- **Edição geral do item** (nome, rota, ícone, identificador). Só o nível passa a ser alterável.

## Decisões

| Eixo | Decisão |
|---|---|
| Valores | Enum `NivelDeAcessoItemMenu { Leitura = 0, Escrita = 1 }`, com `Leitura` como padrão |
| Tipos aceitos | Todos os itens: Documentos, Avisos, Vencimentos e Personalizado. A raiz fica sempre em `Leitura`, porque restringi-la equivaleria a restringir o setor |
| Regra de visibilidade | Item `Leitura` exige a leitura do setor; item `Escrita` exige a escrita do setor. Para quem não lê o setor, um item `Leitura` perde o link e só agrupa, senão um item restrito dentro dele ficaria inalcançável |
| Setor no menu | Entra quem lê o setor ou quem escreve num setor com item restrito. A poda tira o setor quando nenhum item fica visível, então quem só tem escrita continua sem ver um setor sem item restrito |
| Herança | Um item restrito esconde toda a sua subárvore. O filho não sobrepõe o pai |
| Onde filtra | Em `IntranetNavigation.Build`, que é pura e recebe os dados já resolvidos |
| Falha ao consultar a escrita | Fail-closed: itens restritos somem, o menu continua de pé e um aviso vai para o log |
| Modo aberto de DEV | Todos os itens aparecem |
| Quem altera | Só o `intranet-admin`, como toda a tela de menu do setor (`[SomenteIntranetAdmin]` em `SetoresController`) |
| Páginas | `SetorController` e os demais controllers não mudam |

## Limites conhecidos

Decorrem de o nível não proteger a página, e a tela e a documentação os explicam:

- **Tipos embutidos continuam acessíveis a quem lê o setor.** As páginas de Documentos, Avisos e Vencimentos só exigem leitura, então marcar um deles como "quem escreve" esconde o link, mas a URL direta continua abrindo. Os avisos do setor também continuam no Mural, que filtra pela leitura do setor.
- **Quem só tem escrita pode cair em 404 em tipo embutido.** O item restrito aparece para essa pessoa, mas a página exige leitura. O `{slug}-admin` sempre recebe as duas permissões, então isso só acontece com perfil montado à mão em `/acesso`.
- **URL da árvore.** Um leitor que digite o caminho de um item Personalizado restrito com rota ainda é redirecionado, e a página de destino decide.

## Modelo de dados

`ItemMenu` ganha a propriedade `NivelDeAcesso`, gravada pela convention (ADR-0017) como `ie_nivel_de_acesso`: inteiro obrigatório, padrão `0`.

- **Construtor:** parâmetro opcional no fim, com padrão `Leitura`.
- **Método novo:** `DefinirNivelDeAcesso(NivelDeAcessoItemMenu nivel)`.
- **Invariante:** o construtor e o método lançam `DomainInvariantException` para valor fora do enum, ou para `Escrita` na raiz.

Migration `ItemMenuNivelDeAcesso` nos dois providers, gerada pelo `dotnet ef` nos projetos `Secco.Intranet.Migrations.SqlServer` e `Secco.Intranet.Migrations.Postgres`.

## Application

**`ItemMenuDto`** ganha `NivelDeAcessoItemMenu NivelDeAcesso = NivelDeAcessoItemMenu.Leitura` como último parâmetro, para não alterar quem já constrói o record.

**`CriarItemMenuCommand`** ganha o mesmo parâmetro. `CriarItemMenuHandler` recusa valor fora do enum com `IntranetErrors.Menu.NivelDeAcessoInvalido`, e o registro `menu.item-criar` passa a incluir o nível.

**`DefinirNivelDeAcessoItemMenuHandler`** (novo), no molde de `AtivarDesativarItemMenuHandler`, recebe `(Guid id, NivelDeAcessoItemMenu nivel, CancellationToken)` e checa, nesta ordem:

1. valor fora do enum: `NivelDeAcessoInvalido`;
2. item inexistente: `NotFound`;
3. raiz: `RaizProtegida`;
4. nível igual ao atual: sucesso, sem gravar nem auditar.

Depois de salvar, registra o verbo novo `VerbosDeAuditoria.MenuItemDefinirNivel` (`menu.item-definir-nivel`), com `setorId`, `nome`, `tipo`, `nivelAnterior` e `nivel`. O handler é registrado em `IntranetApplicationExtensions`.

## Montagem do menu

**`NavigationRequest`** ganha dois conjuntos de ids de setor:

- `SetoresComEscrita` (`IReadOnlySet<Guid>?`, padrão nulo): nulo significa nenhum, para que a omissão esconda em vez de mostrar.
- `SetoresSemLeitura` (`IReadOnlySet<Guid>?`, padrão nulo): setores que estão em `Setores` só por causa da escrita. Nulo significa nenhum, porque por padrão `Setores` traz o que a pessoa lê.

**`IntranetNavigation`:** `SetorNoMenu` calcula as duas permissões e as passa a `Filhos`. Antes da recursão, um item `Escrita` sem a escrita do setor é ignorado com a subárvore. Depois da recursão, um item `Leitura` sem a leitura do setor fica sem destino. O filtro não olha o tipo do item.

A poda existente faz o restante, sem código novo: grupo sem rota e sem filho visível some, e setor sem nenhum filho visível também sai do menu.

**`NavigationViewComponent`** carrega setores e árvores, e `VisibilidadeDosSetoresNoMenu.ResolverAsync` (nova, no molde de `GatesDoMenuPrincipal`, para o modo aberto e as falhas serem testáveis sem subir o host) resolve a visibilidade. No total, são quatro passos:

1. Carrega os setores ativos e as árvores de todos eles. As árvores são filtradas antes de virar menu, e a consulta continua sendo uma só.
2. Consulta a leitura, como hoje. Falha aqui deixa o menu sem setores, como hoje.
3. Seleciona os setores com item ativo em `Escrita` e consulta a escrita só neles. Sem nenhum, não há consulta: o menu roda em toda página e o caso comum não deve custar nada. No modo aberto de DEV, todos contam com leitura e escrita, sem consultar nada. A consulta usa `IPermissoesDeSetor.SlugsComPermissaoAsync(usuario, "write", slugs, ...)`, que já libera o `intranet-admin` e a permissão global `setores:write`.
4. Em exceção na consulta de escrita, registra o aviso e segue sem escrita, no mesmo padrão `CA1031` dos outros carregamentos do componente.

**Não reaproveitar `AcessoAoRecursoDoSetor.SlugsComEscritaAsync`:** ele devolve vazio no modo aberto de DEV, o que esconderia os itens restritos nesse modo.

## Tela de menu do setor

Em `Views/Setores/Menu.cshtml` e `SetoresController`:

- **Formulário de criação:** novo seletor "Quem vê no menu", com as opções "Quem lê o setor" (padrão) e "Quem escreve no setor". Os textos valem para toda a tela.
- **Linha de cada item, exceto a raiz:** um formulário com o seletor e um botão "Salvar", que faz POST para a action nova `DefinirNivelDeItemDeMenu(Guid setorId, Guid itemId, NivelDeAcessoItemMenu nivel)`, com `[ValidateAntiForgeryToken]`, retornando por `VoltarParaMenu`. Sem envio automático por script, para respeitar a CSP.
- **Item debaixo de um restrito:** o texto `sc-meta` informa "Escondido pelo item acima para quem só lê o setor". `Achatar` passa a propagar essa informação.
- **Aviso permanente na página:** o nível só decide quem vê o item no menu e não protege a página. Em Documentos, Avisos e Vencimentos, quem lê o setor continua abrindo a página pela URL direta, e os avisos continuam no Mural.

## Segurança (ADR-0020)

- **Não é controle de acesso.** Esconder o link não fecha a URL. A tela e a documentação dizem isso de forma explícita, inclusive para os tipos embutidos.
- **Entrada externa:** um número fora do enum vira erro de binding e deixaria o valor padrão no parâmetro, então o controller recusa o envio em vez de gravar "quem lê o setor" em silêncio. Na alteração, o campo é obrigatório. Os handlers também validam o enum.
- **Autorização:** a action nova herda `[SomenteIntranetAdmin]` do controller e exige antiforgery.
- **Dados de setor não legível:** as árvores de setores sem leitura ficam em memória só durante a montagem do menu. Só viram menu os itens que passam pela regra de visibilidade.
- **Tenant:** a árvore é lida do banco do tenant da requisição, sem consulta entre tenants.
- **Auditoria:** criar e alterar registram o nível.
- **Negação de serviço:** a consulta de escrita fica limitada aos setores do menu que têm item restrito.

## Critérios de aceite

- [ ] Item novo e itens existentes ficam em "quem lê o setor", sem mudar o que cada pessoa vê (migration com valor padrão, em SqlServer e Postgres).
- [ ] Item com "quem escreve no setor" aparece para quem tem `setor-{slug}:write` e para o `intranet-admin`, e não aparece para quem só tem `setor-{slug}:read`.
- [ ] Um item restrito esconde também os seus filhos.
- [ ] Grupo sem rota e sem nenhum filho visível fica fora do menu.
- [ ] No modo aberto de DEV, todos os itens continuam aparecendo.
- [ ] Falha ao consultar a permissão de escrita esconde os itens restritos sem derrubar o menu.
- [ ] A tela de menu do setor mostra e permite alterar o nível de cada item.
- [ ] A raiz é recusada ao tentar alterar o nível.
- [ ] A documentação explica o nível e lembra que ele não substitui a autorização da página.

## Testes

| Arquivo | Cobertura |
|---|---|
| `Unit/ItemMenuTests` | Padrão `Leitura`; `Escrita` em cada tipo; invariante na raiz e em valor fora do enum |
| `Unit/CriarItemMenuHandlerTests` | Nível aceito; valor inválido recusado |
| `Unit/DefinirNivelDeAcessoItemMenuHandlerTests` (novo) | Cada ramo das checagens; nível igual não grava nem audita |
| `Unit/AuditoriaDeMenuTests` | Verbo novo e nível no registro de criação |
| `Unit/NavegacaoETemaTests` | Restrito com e sem escrita; subárvore escondida; grupo e setor podados; nulo esconde; setor só com escrita |
| `Unit/VisibilidadeDosSetoresNoMenuTests` (novo) | Modo aberto; escrita consultada só com item restrito; só escrita; falhas de leitura e de escrita |
| `Integration/MenuVisibilidadeDeSetorTests` | Leitor, escritor, só escrita e `intranet-admin` de ponta a ponta |
| `Integration/SetorMenuAdministracaoTests` | Criar e alterar o nível pela tela; valor inválido ou ausente; antiforgery; o seletor não aparece na raiz |
| Migrations | SqlServer e Postgres aplicam, e o snapshot fica em dia |

## Documentação

- **[docs/extensao.md](../extensao.md):** seção nova sobre pôr uma página de RCL na árvore de um setor: item Personalizado com rota, o nível de acesso e a página checando a própria permissão com o `IAuthorizationService`. A seção deixa clara a diferença para `IItemDeMenuPrincipal`, que só atende o grupo principal.
- **Skill `secco-intranet-setores`:** nota sobre o nível.
- **[Spec da árvore](2026-09-30-item-menu-por-setor-design.md):** linha "Revisada por" apontando para esta spec.
- **[Roadmap](../roadmap.md):** registrar a entrega.

Não é necessária ADR nova: a decisão é local ao produto, reversível e não muda o modelo de permissões da ADR-0001.
