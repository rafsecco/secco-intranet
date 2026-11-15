# Links do setor — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** o setor mantém grupos e links no próprio menu, por uma tela que só alcança o trecho abaixo do item "Links", e cada link escolhe se abre em nova página.

**Architecture:** os links são itens `Personalizado` da árvore de menu que já existe, pendurados num nó novo do tipo `Links`. O `ItemMenu` ganha o campo `NovaAba`. Casos de uso próprios (`Application/Links`) conferem que todo item tocado é descendente do nó Links do setor da URL; os handlers do menu do admin não mudam de regra.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 (SQL Server e PostgreSQL), xUnit, AwesomeAssertions, Playwright (opt-in com `SECCO_UI_TESTS=1`).

**Spec:** [docs/specs/2026-11-15-links-do-setor-design.md](../specs/2026-11-15-links-do-setor-design.md)

## Global Constraints

- Migration sempre em par, mesmo nome nos dois projetos (`Migrations.SqlServer` e `Migrations.Postgres`), gerada com `dotnet ef` (ferramenta local: `dotnet tool restore`). Nome de coluna vem da convenção da ADR-0017; não escrever `HasColumnName`.
- Todo POST com `[ValidateAntiForgeryToken]`. Sem `style=""`, `<script>` inline nem `onclick` nas views (CSP em Enforce).
- Sem permissão ou sem o recurso, a resposta é 404, nunca 403.
- Testes com AwesomeAssertions, nunca FluentAssertions. Amostras de teste fictícias.
- As views de setor ficam em `src/Secco.Intranet.Web/Views`; o que é do tema muda igual em `Themes.Vertical` e `Themes.Horizontal`.
- Limites que já valem no menu: nome 256, slug 128, rota 512.
- Endereço aceito: `http`/`https` absoluto, ou caminho local começando com `/` e não com `//` nem `/\` (regra `RotaEhValida` de `CriarItemMenuHandler`).
- Commits direto na `main`; conferir `git branch --show-current` antes de cada commit.
- Texto de tela e mensagens em português, sem citar sistema de terceiros.

## Review Focus

1. **Nome que não gera slug** (só símbolos ou emoji, por exemplo "★★★"): o item é criado com slug de reserva `link`, sem erro. Teste na Tarefa 4.
2. **Grupo de destino inválido ao criar ou editar link** (id de um link, de um grupo de outro setor, de um item fora do trecho, ou o próprio item): recusa com `Links.GrupoInvalido`, nada gravado. Teste na Tarefa 4.
3. **Endereço com espaço em volta ou esquema em maiúsculas** (`"  HTTPS://Exemplo.com  "`): aceito e gravado sem os espaços; `//exemplo.com`, `javascript:alert(1)` e `ftp://x` recusados com `Menu.RotaInvalida`. Teste na Tarefa 4.
4. **Nó Links desativado pelo admin** (`Ativo = false`): some do menu de todos, inclusive "Gerenciar links", e `/{slug}/links` responde 404. Testes nas Tarefas 3 e 5.
5. **Ordens com buraco depois de uma exclusão** (0, 2, 3): subir e descer continua trocando só com o vizinho, renumerando os irmãos. Teste na Tarefa 4.

---

### Task 1: `NovaAba` e o tipo `Links` no item de menu

**Files:**
- Modify: `src/Secco.Intranet.Domain/Menu/ItemMenu.cs`
- Modify: `src/Secco.Intranet.Application/Menu/ItemMenuDtos.cs`
- Create: migration `ItemMenuNovaAba` nos dois projetos de migrations
- Test: `tests/Secco.Intranet.Tests/Unit/ItemMenuTests.cs`, `tests/Secco.Intranet.Tests/Integration/ItemMenuPersistenceTests.cs`

**Interfaces:**
- Produces:
  - `TipoDeItemMenu.Links = 5`
  - `ItemMenu(..., NivelDeAcessoItemMenu nivelDeAcesso = Leitura, bool novaAba = false)` e a propriedade `bool NovaAba { get; private set; }`
  - `void ItemMenu.Editar(string nome, string slug, string? rota, bool novaAba)`: mesmas invariantes de nome e slug do construtor
  - `void ItemMenu.Reposicionar(Guid parentId, int ordem)`
  - `ItemMenuDto(..., NivelDeAcessoItemMenu NivelDeAcesso = Leitura, bool NovaAba = false)`, preenchido em `FromEntity`

- [ ] **Step 1: Testes que falham** em `ItemMenuTests`:

```csharp
[Fact] public void Construtor_SemInformar_NovaAbaFalso()            // new ItemMenu(...).NovaAba == false
[Fact] public void Construtor_TipoLinksComRota_Lanca()              // DomainInvariantException: o nó Links não tem rota
[Fact] public void Construtor_SemRota_NovaAbaViraFalso()            // novaAba: true + rota null => NovaAba == false
[Fact] public void Editar_TrocaNomeSlugRotaENovaAba()
[Fact] public void Editar_NomeVazio_Lanca()
[Fact] public void Reposicionar_TrocaPaiEOrdem()
```

- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~ItemMenuTests"` → falha de compilação.
- [ ] **Step 3: Implementar** no `ItemMenu` e no DTO. `NovaAba` só é verdadeiro quando há rota, no construtor e no `Editar`.
- [ ] **Step 4: Migration em par**, com a coluna booleana não nula e padrão falso:

```bash
dotnet ef migrations add ItemMenuNovaAba --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add ItemMenuNovaAba --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```

Conferir nos dois arquivos que a coluna segue a convenção (`fl_nova_aba`) e que nada além dela mudou.

- [ ] **Step 5: Teste de persistência** em `ItemMenuPersistenceTests`: `Salvar_ItemComNovaAba_LeDeVolta` (grava `novaAba: true` com rota, relê, confere `NovaAba` e `Tipo == Links` num segundo item).
- [ ] **Step 6:** rodar `ItemMenuTests`, `ItemMenuPersistenceTests` e `MigracoesPorEngineTests` → passam.
- [ ] **Step 7: Commit** `feat(menu): campo nova aba e tipo Links no item de menu`.

### Task 2: menu do admin e criação de setor aceitam Links e nova aba

**Files:**
- Modify: `src/Secco.Intranet.Application/Menu/CriarItemMenuHandler.cs`, `ItemMenuDtos.cs` (`CriarItemMenuCommand`)
- Modify: `src/Secco.Intranet.Application/Setores/CreateSetorHandler.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/SetoresController.cs`, `Models/SetorFormViewModel.cs`, o view model do formulário de item de menu, `Views/Setores/Menu.cshtml`, `Views/Setores/Create.cshtml`
- Test: `Unit/CriarItemMenuHandlerTests.cs`, `Unit/ExcluirItemMenuHandlerTests.cs`, `Integration/SetorMenuAdministracaoTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `CriarItemMenuCommand(..., NivelDeAcessoItemMenu NivelDeAcesso = Leitura, bool NovaAba = false)`
  - `CreateSetorCommand(..., bool HabilitarLinks = true)`; o item nasce como `new ItemMenu(setor.Id, raiz.Id, "Links", "links", TipoDeItemMenu.Links, null, "bi-link-45deg", ordem)`
  - `SetorFormViewModel.HabilitarLinks { get; set; } = true`

Regras novas no `CriarItemMenuHandler`:

| Caso | Resultado |
|---|---|
| Tipo `Links` já existe no setor | `Menu.TipoJaExiste` |
| Tipo `Links` com pai que não é a raiz (`Setor`) | `Menu.PaiInvalido` |
| Pai do tipo `Links` e filho `Personalizado` | aceito |
| Pai do tipo `Links` e filho de outro tipo | `Menu.PaiInvalido` |
| `Personalizado` com rota e `NovaAba: true` | grava `NovaAba` |

- [ ] **Step 1: Testes que falham**, um por linha da tabela, em `CriarItemMenuHandlerTests` (`Criar_Links_DuasVezes_TipoJaExiste`, `Criar_Links_ForaDaRaiz_PaiInvalido`, `Criar_PersonalizadoSobLinks_Aceita`, `Criar_DocumentosSobLinks_PaiInvalido`, `Criar_PersonalizadoComNovaAba_Grava`), mais `Excluir_NoLinks_TipoEmbutidoNaoExclui` em `ExcluirItemMenuHandlerTests` (já passa pela regra atual; fica como trava).
- [ ] **Step 2:** rodar → falham.
- [ ] **Step 3: Implementar** as regras e o `HabilitarLinks`.
- [ ] **Step 4: Telas do admin.** Em `Menu.cshtml`: opção "Links" no seletor de tipo, só quando o setor ainda não tem; caixa "Abrir em nova página" no formulário de item personalizado, marcada por padrão (o view model do formulário nasce com `NovaAba = true`; o padrão do domínio e do command continua falso); a coluna de rota mostra "nova página" quando marcado. Em `Create.cshtml`: caixa "Links" ao lado de Vencimentos.
- [ ] **Step 5: Testes de integração** em `SetorMenuAdministracaoTests`: `CriarSetor_ComHabilitarLinks_NasceComONo`, `Menu_CriarPersonalizadoComNovaAba_MostraNaTela`, `Menu_TipoLinks_SoApareceQuandoNaoExiste`.
- [ ] **Step 6:** rodar as três classes → passam.
- [ ] **Step 7: Commit** `feat(menu): tipo Links e nova aba na tela de menu e na criacao de setor`.

### Task 3: navegação — nova aba, "Gerenciar links" e menu vazio

**Files:**
- Modify: `src/Secco.Intranet.Web.Theming/Contracts/NavigationModel.cs`
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs`
- Modify: `src/Secco.Intranet.Themes.Vertical/Themes/Vertical/Views/Shared/Components/Navigation/Default.cshtml` e o mesmo arquivo no Horizontal
- Test: `tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs`, `Integration/SetorMenuRotaTests.cs`

**Interfaces:**
- Consumes: `ItemMenuDto.NovaAba`, `TipoDeItemMenu.Links`.
- Produces: `NavigationItemModel(string Texto, string? Icone, string? Url, bool Ativo = false, string? SetorSlug = null, IReadOnlyList<NavigationItemModel>? Filhos = null, bool NovaAba = false)`. A entrada de gestão é `new NavigationItemModel("Gerenciar links", "bi-gear", $"/{setorSlug}/links")`, último filho do nó Links, só com `acesso.PodeEscrever`.

- [ ] **Step 1: Testes que falham** em `NavegacaoETemaTests` (montar `NavigationRequest` com `Arvores` e `SetoresComEscrita`):

```csharp
[Fact] public void Links_ComLinkEGrupo_LeitorVeGruposELinks_SemGerenciar()
[Fact] public void Links_Escritor_UltimoFilhoEGerenciarLinks()            // Url == "/financeiro/links"
[Fact] public void Links_Vazio_LeitorNaoVeONo_EscritorVeSoGerenciar()
[Fact] public void Links_GrupoVazio_NaoApareceParaLeitor()
[Fact] public void Links_NoDesativado_NinguemVe()                          // nem "Gerenciar links"
[Fact] public void Personalizado_ComNovaAba_ItemTemNovaAba()
[Fact] public void Personalizado_SemNovaAba_ItemNaoTemNovaAba()
```

- [ ] **Step 2:** rodar → falham.
- [ ] **Step 3: Implementar** em `Filhos`/`Destino`: `Links` não tem URL própria; passa `NovaAba` do DTO; acrescenta "Gerenciar links" depois dos filhos reais. Conferir onde a árvore filtra item inativo e manter o nó desativado fora, com a entrada de gestão junto.
- [ ] **Step 4: Temas.** No `<a>` do item, quando `item.NovaAba`: `target="_blank" rel="noopener noreferrer"`. Igual nos dois temas, em todos os níveis do menu.
- [ ] **Step 5: Teste de integração** em `SetorMenuRotaTests`, com `[Theory]` nos dois temas: `Menu_LinkComNovaAba_TemTargetERel` (regex no HTML: o `<a href="https://exemplo.test/…">` tem os dois atributos) e `Menu_LinkSemNovaAba_NaoTemTarget`.
- [ ] **Step 6:** rodar as duas classes → passam.
- [ ] **Step 7: Commit** `feat(menu): links em nova aba e entrada de gestao no no Links`.

### Task 4: casos de uso de links, com a trava de alcance

**Files:**
- Create: `src/Secco.Intranet.Application/Links/TrechoDeLinks.cs`, `SlugDeLink.cs`, `LinksDtos.cs`, `ListarLinksDoSetorHandler.cs`, `CriarGrupoDeLinksHandler.cs`, `CriarLinkHandler.cs`, `EditarLinkHandler.cs`, `MoverLinkHandler.cs`, `ExcluirLinkHandler.cs`
- Modify: `IntranetErrors.cs` (classe `Links`), `Auditoria/VerbosDeAuditoria.cs`, `IntranetApplicationExtensions.cs` (registro dos handlers)
- Test: `tests/Secco.Intranet.Tests/Unit/TrechoDeLinksTests.cs`, `SlugDeLinkTests.cs`, `LinksHandlersTests.cs`

**Interfaces:**
- Consumes: `IItemMenuRepository`, `ItemMenu.Editar`, `ItemMenu.Reposicionar`, `ITrilhaDeAuditoria`, `AuditoriaDeMenu.ItemAsync`.
- Produces:

```csharp
// TrechoDeLinks: a trava. Tudo o que os handlers tocam passa por aqui.
internal sealed class TrechoDeLinks
{
    public static TrechoDeLinks? De(IReadOnlyList<ItemMenu> arvoreDoSetor);   // null sem nó Links ativo
    public ItemMenu No { get; }
    public IReadOnlyList<ItemMenu> Grupos { get; }                            // filhos diretos sem rota, por ordem
    public IReadOnlyList<ItemMenu> FilhosDe(Guid parentId);                   // por ordem
    public ItemMenu? Item(Guid id);                                           // null se não é descendente do nó (o nó não conta)
    public ItemMenu? Grupo(Guid id);                                          // null se não é grupo do trecho
}

internal static class SlugDeLink
{
    public static string Gerar(string nome, IEnumerable<string> usadosEntreIrmaos);
    // minúsculas, sem acento, [a-z0-9] separados por hífen, até 120 caracteres;
    // vazio => "link"; repetido => sufixo "-2", "-3"...
}

public sealed record LinkDto(Guid Id, string Nome, string Rota, bool NovaAba, Guid? GrupoId);
public sealed record GrupoDeLinksDto(Guid Id, string Nome, IReadOnlyList<LinkDto> Links);
// Grupos e links soltos se misturam pela Ordem: cada entrada é um grupo OU um link solto.
public sealed record EntradaDeLinksDto(GrupoDeLinksDto? Grupo, LinkDto? Link);
// Entradas: na ordem do menu. Grupos: todos os grupos, para o seletor do formulário.
public sealed record LinksDoSetorDto(IReadOnlyList<EntradaDeLinksDto> Entradas, IReadOnlyList<GrupoDeLinksDto> Grupos);

public sealed record CriarLinkCommand(Guid SetorId, string? Nome, string? Rota, bool NovaAba, Guid? GrupoId);
public sealed record EditarLinkCommand(Guid SetorId, Guid Id, string? Nome, string? Rota, bool NovaAba, Guid? GrupoId);

ListarLinksDoSetorHandler.HandleAsync(Guid setorId, ct)                 : Task<Result<LinksDoSetorDto>>
CriarGrupoDeLinksHandler.HandleAsync(Guid setorId, string? nome, ct)    : Task<Result<Guid>>
CriarLinkHandler.HandleAsync(CriarLinkCommand, ct)                      : Task<Result<Guid>>
EditarLinkHandler.HandleAsync(EditarLinkCommand, ct)                    : Task<Result>   // grupo: só Nome; Rota e GrupoId ignorados
MoverLinkHandler.HandleAsync(Guid setorId, Guid id, bool paraCima, ct)  : Task<Result>
ExcluirLinkHandler.HandleAsync(Guid setorId, Guid id, ct)               : Task<Result>
```

Erros novos em `IntranetErrors.Links`: `NotFound` ("Links não encontrado neste setor."), `GrupoInvalido` ("Escolha um grupo deste setor."), `EnderecoRequired` ("Informe o endereço do link."). Reaproveitar `Menu.NomeRequired`, `Menu.CampoMuitoLongo`, `Menu.RotaInvalida`, `Menu.ItemComFilhos`. Verbo novo: `MenuItemEditar = "menu.item-editar"`; os demais são os de menu que já existem.

Regras:

| Regra | Onde |
|---|---|
| Sem nó Links ativo, ou item que `TrechoDeLinks.Item` não acha: `Links.NotFound` | todos |
| Link novo entra por último entre os irmãos do destino (nó Links ou grupo) | Criar |
| Trocar de grupo ao editar: sai do pai antigo, entra por último no novo, renumera os dois | Editar |
| Grupo é item sem rota, filho direto do nó; não tem grupo pai | Criar grupo |
| Mover troca com o vizinho e renumera todos os irmãos 0..n-1 (mesma técnica do `MoverItemMenuHandler`) | Mover |
| Grupo com filhos: `Menu.ItemComFilhos` | Excluir |
| A validação de rota é a de `CriarItemMenuHandler.RotaEhValida`: tornar esse método `internal static` e chamá-lo, sem copiar | Criar, Editar |

- [ ] **Step 1: Testes que falham.** `TrechoDeLinksTests`: `De_SemNoLinks_Nulo`, `De_NoDesativado_Nulo`, `Item_ForaDoTrecho_Nulo` (Documentos, personalizado do admin na raiz), `Item_OProprioNo_Nulo`, `Item_LinkDentroDeGrupo_Acha`, `Grupo_IdDeLink_Nulo`. `SlugDeLinkTests`: `"Internet Banking" => "internet-banking"`, `"Portal de Notas (NF-e)" => "portal-de-notas-nf-e"`, `"★★★" => "link"`, repetido => `"erp-2"`, depois `"erp-3"`, nome de 300 letras => até 120 caracteres.
- [ ] **Step 2: `LinksHandlersTests`** com o dublê de `IItemMenuRepository` que os testes de menu já usam:

```csharp
Criar_LinkSemGrupo_EntraPorUltimoSobONo
Criar_LinkEmGrupo_EntraPorUltimoNoGrupo
Criar_EnderecoComEspacosEMaiusculas_AceitaEApara        // "  HTTPS://Exemplo.com  " => "HTTPS://Exemplo.com"
Criar_EnderecoInvalido_RotaInvalida                      // [Theory] "//exemplo.com", "javascript:alert(1)", "ftp://x", "exemplo.com"
Criar_SemEndereco_EnderecoRequired
Criar_GrupoInvalido_GrupoInvalido                        // [Theory] id de link, id de item fora do trecho, id inexistente
Criar_NomeQueNaoGeraSlug_UsaLink
Criar_NomeMaiorQue256_CampoMuitoLongo
Criar_SemNoLinks_NotFound
CriarGrupo_NomeRepetido_GeraSlugComSufixo
Editar_TrocaDeGrupo_RenumeraOsDois
Editar_ParaOProprioIdComoGrupo_GrupoInvalido
Editar_ItemForaDoTrecho_NotFound                         // Documentos e personalizado do admin
Editar_ItemDeOutroSetor_NotFound
Editar_Grupo_SoTrocaONome
Mover_ComBuracosNaOrdem_TrocaSoComOVizinho               // ordens 0,2,3 => depois 0,1,2 na nova sequência
Mover_PrimeiroParaCima_NaoMuda
Mover_ItemForaDoTrecho_NotFound
Excluir_GrupoComLinks_ItemComFilhos
Excluir_Link_RemoveEAudita
Excluir_NoLinks_NotFound
Listar_DevolveEntradasNaOrdem_GruposComSeusLinks
```

- [ ] **Step 3:** rodar as três classes → falham.
- [ ] **Step 4: Implementar** `TrechoDeLinks`, `SlugDeLink`, os handlers e os registros. Cada handler de escrita registra na trilha com `AuditoriaDeMenu.ItemAsync`.
- [ ] **Step 5:** rodar → passam. Rodar também `CriarItemMenuHandlerTests` (o `RotaEhValida` mudou de visibilidade).
- [ ] **Step 6: Commit** `feat(links): casos de uso de links do setor limitados ao trecho do no Links`.

### Task 5: tela "Gerenciar links"

**Files:**
- Create: `src/Secco.Intranet.Web/Controllers/LinksController.cs`, `Models/Links/GerenciarLinksViewModel.cs`, `Views/Setor/Links.cshtml`, `Views/Setor/LinkForm.cshtml`
- Test: `tests/Secco.Intranet.Tests/Integration/LinksDoSetorTests.cs`

**Interfaces:**
- Consumes: handlers da Task 4; `AcessoAoRecursoDoSetor.PodeEscreverAsync` e `ResolverTipoAsync(slug, TipoDeItemMenu.Links, ct)`.
- Produces as rotas, com `[Route("{slug:" + SlugDeSetorRouteConstraint.Nome + "}/links")]`:

| Verbo e rota | Ação |
|---|---|
| `GET ""` | Tela de gestão |
| `GET "novo"` e `GET "{id:guid}/editar"` | Formulário de link (página própria, sem script) |
| `POST "novo"` | `CriarLinkHandler` |
| `POST "grupos"` | `CriarGrupoDeLinksHandler` (formulário na própria tela de gestão) |
| `POST "{id:guid}/editar"` | `EditarLinkHandler` |
| `POST "{id:guid}/mover"` (campo `paraCima`) | `MoverLinkHandler` |
| `POST "{id:guid}/excluir"` | `ExcluirLinkHandler` |

Toda ação começa por: sem escrita no setor ou `ResolverTipoAsync` nulo → `NotFound()`. `Links.NotFound` dos handlers também vira `NotFound()`; os demais erros voltam para a tela pelo `TempData` de feedback, como em `SetorController`. O formulário de link tem nome, endereço, seletor de grupo ("Sem grupo" mais os grupos) e a caixa "Abrir em nova página". No formulário novo a caixa vem sempre marcada (decisão do Rafael: o padrão é nova aba em todos os casos); na edição, vale o que está gravado.

- [ ] **Step 1: Testes que falham** em `LinksDoSetorTests` (molde de `ImagemNoTextoFluxoTests`: `CriarClienteAdmin`, `NovoSetorAsync`, token antiforgery):

```csharp
Get_SemEscrita_404                               // cliente com Role de leitura do setor
Get_SetorSemNoLinks_404
Get_NoLinksDesativado_404
Post_SemAntiforgery_400                          // [Theory] nas cinco rotas de POST
CriarGrupoELink_ApareceNoMenuDeQuemLe            // HTML do Mural contém o nome e o href
CriarLink_NovaAba_MenuTemTarget
CriarLink_EnderecoInvalido_VoltaComMensagem      // "javascript:alert(1)"
Editar_ItemDeDocumentosPelaRotaDeLinks_404       // id do item Documentos do mesmo setor
Editar_LinkDeOutroSetor_404
Excluir_GrupoComLinks_MensagemENaoExclui
Mover_TrocaAOrdemNoMenu
OutroTenant_NaoVeOsLinks
```

- [ ] **Step 2:** rodar → falham.
- [ ] **Step 3: Implementar** controller, view models e views. A tela de gestão lista `Entradas` na ordem, com os botões de subir, descer, editar e excluir em formulários POST; link mostra "nova página" ou "mesma página".
- [ ] **Step 4:** rodar `LinksDoSetorTests` → passam.
- [ ] **Step 5: Commit** `feat(links): tela de gestao dos links do setor`.

### Task 6: testes de tela e documentação

**Files:**
- Create: `tests/Secco.Intranet.Tests/Ui/LinksDoSetorTelaTests.cs`
- Modify: `CHANGELOG.md`, `docs/roadmap.md`, `docs/temas.md`, `docs/specs/2026-11-15-links-do-setor-design.md` (estado)

- [ ] **Step 1: Testes de tela**, `[UiTheory]` nos dois temas, no molde de `MenuDaArvoreTelaTests`:

```csharp
CriarGrupoELinkPelaTela_ApareceNoMenu
Link_NovaAba_AbreOutraPagina                 // RunAndWaitForPopupAsync no clique; endereço de teste servido pelo próprio host
Link_MesmaPagina_NavegaNaMesmaAba
Organizar_SubirEDescer_MudaAOrdemNoMenu
Gerenciar_SemViolacaoDeCsp
```

- [ ] **Step 2:** `SECCO_UI_TESTS=1 dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~LinksDoSetorTelaTests"` → passam.
- [ ] **Step 3: Documentação.** `CHANGELOG.md`, em `[Não lançado]`, seção "Quebras — Links do setor": migration `ItemMenuNovaAba` (rodar o `migrate`) e o campo `NovaAba` em `NavigationItemModel` (tema de fork escreve `target="_blank" rel="noopener noreferrer"`; sem isso tudo abre na mesma página). `docs/temas.md`: o mesmo, na parte do contrato de navegação. `docs/roadmap.md`: desdobrar "Ferramentas do dia a dia" em Links do setor (feito, com link para spec e plano) e Reserva de sala (pendente). Spec: estado "entregue".
- [ ] **Step 4: Suíte inteira:** `SECCO_UI_TESTS=1 dotnet test Secco.Intranet.slnx` → tudo passa. Conferir `dotnet ef migrations has-pending-model-changes` nos dois projetos → sem mudança pendente.
- [ ] **Step 5: Commit** `test(links): telas dos links do setor, changelog e roadmap` e conferir o CI no commit.
