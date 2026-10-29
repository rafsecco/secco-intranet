# Menu principal extensível e ordenável — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Projetos externos acrescentam itens ao grupo principal do menu só por estarem referenciados, e o `intranet-admin` define a ordem do grupo principal e da seção Setores por tenant.

**Architecture:** Contrato `IItemDeMenuPrincipal` num projeto novo sem dependência do produto; um catálogo singleton no Web acha as implementações nas application parts no primeiro uso e as instancia por requisição. A ordem salva fica em `tb_posicoes_menu` (por tenant) e é aplicada por uma função pura da Application, a mesma usada pelo menu e pela tela `/menu`.

**Tech Stack:** .NET 10, ASP.NET Core MVC (application parts, view components), EF Core (SQL Server e Postgres), xUnit + AwesomeAssertions, `Secco.SDK.Testing` (`SeccoApiFactory`).

**Spec:** [`docs/specs/2026-10-29-menu-extensivel-e-ordenavel-design.md`](../specs/2026-10-29-menu-extensivel-e-ordenavel-design.md). Ler inteira antes da primeira task.

## Global Constraints

- `Chave` do item extra: 1 a 64 caracteres, `^[a-z0-9-]+$`; `mural` e `diretorio` são reservadas.
- `Url` do item extra: começa com `/` e não com `//`.
- Chave duplicada: vale o tipo de menor `FullName` (ordinal); o outro é ignorado.
- Item inválido ou gate que lança exceção: fora do menu, `LogWarning`; nunca derruba a página.
- Ordem padrão do principal: Mural, Diretório, extras por `Ordem` e depois `Rotulo` (`OrdinalIgnoreCase`). Setores: por `Nome`.
- Chaves de posição: `mural`, `diretorio`, `Chave` do extra, `Id` do setor em formato `D`.
- Tabela `tb_posicoes_menu`, colunas `id_pk_posicao_menu`, `ie_grupo`, `ds_chave` (64), `nr_ordem`; índice único `uk_posicoes_menu_ie_grupo_ds_chave`. `GrupoDoMenu`: `Principal = 1`, `Setores = 2`.
- Auditoria: recurso `menu`; verbos `menu.ordem-mover` (`{ grupo, chave, de, para }`) e `menu.ordem-restaurar` (`{ grupo }`).
- Tela: rota `/menu`, `[SomenteIntranetAdmin]`, item "Menu" `bi-list-ol` no grupo Administração depois de Acesso; `menu` em `SlugsReservados`.
- Tema intocado: nenhum parcial novo no contrato; a tela usa só `_PageHeader`, `_Card`, `_EmptyState` e funciona nos dois temas, claro e escuro (skill `secco-intranet-temas`).
- Banco: skill `secco-db-naming`, migration `PosicoesMenu` nos dois engines.
- Código: tabs de 4 em `.cs`/`.cshtml`, doc XML em português em todo tipo público, `ConfigureAwait(false)` como nos vizinhos.
- Sem instalação em uso: nenhuma nota de upgrade nem compatibilidade com dado existente.
- Commits direto na `main`.

## Review Focus

1. **Gate que lança exceção** → a página renderiza e o item some. Teste na Task 4 (`GateQueLancaExcecao_ItemSome_EPaginaRenderiza`).
2. **Admin clica em "descer" com a tela desatualizada** (um setor foi criado ou excluído entre o GET e o POST) → o POST recalcula a ordem efetiva no servidor; o form manda só `grupo`, `chave` e `paraCima`. Chave que sumiu → mensagem de "item não encontrado", sem erro 500. Teste na Task 5 (`Mover_ChaveQueNaoExisteMais_VoltaComAviso`).
3. **`Url` com query string ou `/` final** (`/institucional/?aba=1`) → o item fica ativo em `/institucional` e em `/institucional/historia`. Teste na Task 4 (`Build_ItemExtraComQueryEBarraFinal_FicaAtivoPeloCaminho`).
4. **Setor desativado depois de ordenado** → some do menu, a posição dele é ignorada e os vizinhos não pulam de lugar. Teste na Task 2 (`Ordenar_PosicaoOrfa_EIgnorada_SemMexerNosVizinhos`).
5. **Dois tenants** → reordenar no Alfa não muda o Beta. Teste na Task 5 (`Mover_NoTenantAlfa_NaoAfetaOBeta`).

---

### Task 1: Posição do menu no domínio e no banco

**Files:**
- Create: `src/Secco.Intranet.Domain/Menu/PosicaoMenu.cs` (com o enum `GrupoDoMenu` no mesmo arquivo, como `TipoDeItemMenu` em `ItemMenu.cs`)
- Create: `src/Secco.Intranet.Application/Menu/IPosicaoMenuRepository.cs`
- Create: `src/Secco.Intranet.Infrastructure/Mappings/PosicaoMenuConfiguration.cs`
- Create: `src/Secco.Intranet.Infrastructure/Repositories/PosicaoMenuRepository.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs` (`DbSet<PosicaoMenu> PosicoesMenu`)
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (registro scoped, perto do `IItemMenuRepository`)
- Create: migration `PosicoesMenu` nos dois projetos de migration
- Test: `tests/Secco.Intranet.Tests/Integration/PosicaoMenuRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `enum GrupoDoMenu { Principal = 1, Setores = 2 }`
  - `sealed class PosicaoMenu : BaseEntity` com `PosicaoMenu(GrupoDoMenu grupo, string chave, int ordem)`, propriedades `Grupo`, `Chave`, `Ordem` (setters privados)
  - `IPosicaoMenuRepository`:
    - `Task<IReadOnlyList<PosicaoMenu>> ListarAsync(CancellationToken cancellationToken = default)`
    - `Task SubstituirGrupoAsync(GrupoDoMenu grupo, IReadOnlyList<string> chavesEmOrdem, CancellationToken cancellationToken = default)`: apaga as linhas do grupo e grava `chavesEmOrdem[i]` com `Ordem = i`, num único `SaveChangesAsync`
    - `Task RemoverGrupoAsync(GrupoDoMenu grupo, CancellationToken cancellationToken = default)`

- [ ] **Step 1: Escrever os testes de persistência** no padrão de `ItemMenuRepositoryTests` (escopo com `SetTenant(factory.TenantAlfa)`):
  - `SubstituirGrupo_GravaNaOrdemDada`: substituir `Principal` com `["diretorio", "mural"]` → `ListarAsync` devolve `diretorio` com `Ordem == 0` e `mural` com `Ordem == 1`.
  - `SubstituirGrupo_DeNovo_TiraAsChavesQueSairam`: substituir com `["a", "b", "c"]` e depois com `["c", "a"]` → só `c` (0) e `a` (1) no grupo.
  - `SubstituirGrupo_NaoMexeNoOutroGrupo`: gravar `Setores` e depois `Principal` → as linhas de `Setores` continuam.
  - `RemoverGrupo_ApagaSoAqueleGrupo`.
  - `MesmaChave_EmGruposDiferentes_Convive`.
- [ ] **Step 2: Rodar e ver falhar.** Run: `dotnet test --filter "FullyQualifiedName~PosicaoMenuRepositoryTests"`. Expected: falha de compilação (`PosicaoMenu` não existe).
- [ ] **Step 3: Implementar entidade, mapeamento e repositório.** No mapeamento, `Chave` com `HasMaxLength(64)` e `HasIndex(p => new { p.Grupo, p.Chave }).IsUnique()`. O nome do índice vem ajustado na migration.
- [ ] **Step 4: Gerar as migrations**

```bash
dotnet ef migrations add PosicoesMenu --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add PosicoesMenu --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```

  Conferir nas duas: tabela `tb_posicoes_menu`, `pk_posicoes_menu`, `uk_posicoes_menu_ie_grupo_ds_chave` (renomear à mão se o EF gerou outro nome, como nas migrations vizinhas).
- [ ] **Step 5: Rodar e ver passar.** Mesmo comando, Expected: PASS. Depois `dotnet ef migrations has-pending-model-changes` nos dois projetos → sem mudanças pendentes.
- [ ] **Step 6: Commit** — `feat(menu): posicao salva do menu por tenant`.

---

### Task 2: Regra de ordenação e casos de uso

**Files:**
- Create: `src/Secco.Intranet.Application/Menu/OrdemDoMenu.cs`
- Create: `src/Secco.Intranet.Application/Menu/PosicoesDoMenuDto.cs`
- Create: `src/Secco.Intranet.Application/Menu/ObterPosicoesDoMenuHandler.cs`
- Create: `src/Secco.Intranet.Application/Menu/MoverPosicaoNoMenuHandler.cs`
- Create: `src/Secco.Intranet.Application/Menu/RestaurarOrdemDoMenuHandler.cs`
- Modify: `src/Secco.Intranet.Application/Menu/AuditoriaDeMenu.cs` (`OrdemAsync`)
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs` (`MenuOrdemMover = "menu.ordem-mover"`, `MenuOrdemRestaurar = "menu.ordem-restaurar"`)
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs` (`Menu.PosicaoNotFound`, `Error.NotFound("Intranet.Menu.PosicaoNotFound", "O item não está mais no menu.")`)
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs` (os três handlers, scoped)
- Test: `tests/Secco.Intranet.Tests/Unit/OrdemDoMenuTests.cs`, `tests/Secco.Intranet.Tests/Integration/OrdemDoMenuHandlersTests.cs`

**Interfaces:**
- Consumes: `IPosicaoMenuRepository`, `GrupoDoMenu` (Task 1).
- Produces:
  - `static class OrdemDoMenu` → `static IReadOnlyList<T> Ordenar<T>(IReadOnlyList<T> naOrdemPadrao, Func<T, string> chave, IReadOnlyDictionary<string, int>? posicoes)`: os com posição primeiro, por posição; os sem posição depois, na ordem de entrada (estável). Chave de posição sem item correspondente não tem efeito.
  - `sealed record PosicoesDoMenuDto(IReadOnlyDictionary<string, int> Principal, IReadOnlyDictionary<string, int> Setores)` com `static PosicoesDoMenuDto Vazias`; dicionários `StringComparer.OrdinalIgnoreCase`.
  - `ObterPosicoesDoMenuHandler.HandleAsync(CancellationToken) → Task<PosicoesDoMenuDto>`
  - `MoverPosicaoNoMenuHandler.HandleAsync(GrupoDoMenu grupo, IReadOnlyList<string> chavesNaOrdemEfetiva, string chave, bool paraCima, CancellationToken) → Task<Result>`
  - `RestaurarOrdemDoMenuHandler.HandleAsync(GrupoDoMenu grupo, CancellationToken) → Task<Result>`

- [ ] **Step 1: Testes unitários de `OrdemDoMenu`**:
  - `Ordenar_SemPosicoes_MantemAOrdemPadrao`: `["a","b","c"]`, posições `null` → `a, b, c`.
  - `Ordenar_ComTodasAsPosicoes_SegueAsPosicoes`: `{c:0, a:1, b:2}` → `c, a, b`.
  - `Ordenar_ItemSemPosicao_VaiParaOFim_NaOrdemPadrao`: entrada `["a","b","novo","c"]`, `{c:0, a:1, b:2}` → `c, a, b, novo`.
  - `Ordenar_PosicaoOrfa_EIgnorada_SemMexerNosVizinhos`: entrada `["a","c"]`, `{c:0, b:1, a:2}` → `c, a`.
  - `Ordenar_ChaveSemDiferenciarCaixa`: posição `{"A":0}` vale para o item `a`.
- [ ] **Step 2: Testes de integração dos handlers** (tenant Alfa):
  - `Mover_ParaCima_TrocaComOAnterior_EGravaOGrupoInteiro`: `["mural","diretorio","x"]`, mover `x` para cima → `ObterPosicoes.Principal` == `{mural:0, x:1, diretorio:2}`.
  - `Mover_PrimeiroParaCima_NaoFazNada_EDaSucesso`: resultado sucesso, nenhuma linha gravada.
  - `Mover_ChaveForaDaLista_FalhaComPosicaoNotFound`.
  - `Mover_RegistraNaTrilha`: o dublê de `ITrilhaDeAuditoria` usado pelos testes de menu vizinhos recebe `menu.ordem-mover`, recurso `menu`, id = chave, JSON com `grupo`, `chave`, `de`, `para`.
  - `Restaurar_ApagaSoOGrupoPedido_ERegistraNaTrilha`.
- [ ] **Step 3: Rodar e ver falhar.** Run: `dotnet test --filter "FullyQualifiedName~OrdemDoMenu"`. Expected: falha de compilação.
- [ ] **Step 4: Implementar.** `Mover`: acha o índice de `chave` (`OrdinalIgnoreCase`); vizinho fora da lista → `Result.Success()` sem gravar; senão troca na cópia da lista, chama `SubstituirGrupoAsync` e audita. A auditoria é gravada depois de salvar, como no `MoverItemMenuHandler`. `grupo` vai ao JSON pelo nome do enum.
- [ ] **Step 5: Rodar e ver passar.** Mesmo filtro, Expected: PASS.
- [ ] **Step 6: Commit** — `feat(menu): regra de ordenacao e casos de uso da ordem do menu`.

---

### Task 3: Contrato de extensão e catálogo

**Files:**
- Create: `src/Secco.Intranet.Web.Extensibility/Secco.Intranet.Web.Extensibility.csproj` (`Microsoft.NET.Sdk`, `FrameworkReference Microsoft.AspNetCore.App`, comentário no estilo do `.csproj` do Theming explicando por que não referencia o Web)
- Create: `src/Secco.Intranet.Web.Extensibility/IItemDeMenuPrincipal.cs` (assinatura exata na seção 1 da spec, com doc XML de cada regra)
- Create: `src/Secco.Intranet.Web.Extensibility/RegistroDeItemDeMenuPrincipal.cs`
- Create: `src/Secco.Intranet.Web.Extensibility/ItemDeMenuPrincipalServiceCollectionExtensions.cs`
- Create: `src/Secco.Intranet.Web/Navigation/CatalogoDeItensDoMenuPrincipal.cs`
- Modify: `Secco.Intranet.slnx` (`dotnet sln Secco.Intranet.slnx add src/Secco.Intranet.Web.Extensibility`)
- Modify: `src/Secco.Intranet.Web/Secco.Intranet.Web.csproj` (referência ao projeto novo)
- Modify: `src/Secco.Intranet.Web/Program.cs` (`AddSingleton<CatalogoDeItensDoMenuPrincipal>()`, logo depois de `AddIntranetTheming()`, com comentário citando a ADR-0011)
- Test: `tests/Secco.Intranet.Tests/Unit/CatalogoDeItensDoMenuPrincipalTests.cs`

**Interfaces:**
- Produces:
  - `IItemDeMenuPrincipal` (spec, seção 1)
  - `sealed record RegistroDeItemDeMenuPrincipal(Type Tipo)`
  - `static IServiceCollection AddItemDeMenuPrincipal<T>(this IServiceCollection services) where T : class, IItemDeMenuPrincipal`: registra `RegistroDeItemDeMenuPrincipal(typeof(T))` como singleton
  - `sealed class CatalogoDeItensDoMenuPrincipal(ApplicationPartManager partManager, IEnumerable<RegistroDeItemDeMenuPrincipal> registros, ILogger<CatalogoDeItensDoMenuPrincipal> logger)`
    - `IReadOnlyList<IItemDeMenuPrincipal> Criar(IServiceProvider escopo)`: instancia por `ActivatorUtilities.CreateInstance` e devolve só os válidos e sem chave repetida, na ordem de `FullName`
    - `const string ChaveMural = "mural"`, `const string ChaveDiretorio = "diretorio"`
  - Os tipos (application parts ∪ registros, sem repetir) são calculados uma vez, no primeiro `Criar` (`Lazy<T>`). O aviso de item inválido sai uma vez por tipo (`ConcurrentDictionary<Type, bool>`), não a cada requisição.

- [ ] **Step 1: Testes unitários do catálogo.** Montar `ApplicationPartManager` com `new AssemblyPart(typeof(CatalogoDeItensDoMenuPrincipalTests).Assembly)` e itens de exemplo como classes aninhadas públicas no próprio arquivo de teste. Elas também aparecem no teste de integração da Task 4: dar a todas um gate que exige uma role de teste, para não poluir o menu dos outros testes.
  - `Criar_AchaOItemNasApplicationParts`
  - `Criar_IncluiOItemRegistradoPeloDi_SemDuplicarQuemTambemEstaNasParts`
  - `Criar_InjetaServicoDoEscopo`: item com dependência no construtor recebe a instância do `IServiceProvider` passado
  - `Criar_IgnoraChaveInvalida` (`"Com Espaco"`, `""`, 65 caracteres), `Criar_IgnoraChaveReservada` (`mural`), `Criar_IgnoraUrlNaoLocal` (`https://x`, `//x`, `x`)
  - `Criar_ChaveDuplicada_FicaOTipoDeMenorFullName`
  - `Criar_ItemInvalido_AvisaUmaVezSo`: dois `Criar` → um `LogWarning` (logger falso, como nos vizinhos)
- [ ] **Step 2: Rodar e ver falhar.** Run: `dotnet test --filter "FullyQualifiedName~CatalogoDeItensDoMenuPrincipalTests"`. Expected: falha de compilação.
- [ ] **Step 3: Implementar** contrato, extensão de DI, catálogo, referência no Web, linha no `Program.cs` e entrada no `.slnx`. Descoberta: `partManager.ApplicationParts.OfType<AssemblyPart>().SelectMany(p => p.Types)`, só classes públicas, concretas e não genéricas que implementam a interface.
- [ ] **Step 4: Rodar e ver passar.** Mesmo filtro, Expected: PASS. Depois `dotnet build Secco.Intranet.slnx` sem warning novo.
- [ ] **Step 5: Commit** — `feat(menu): contrato IItemDeMenuPrincipal e catalogo pelas application parts`.

---

### Task 4: Montagem do menu com extras e ordem

**Files:**
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs`
- Modify: `src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs` (acrescentar a `AssemblyPart` do assembly de testes via `services.AddMvcCore().ConfigureApplicationPartManager(...)` no `ConfigureWebHost`, com comentário de que é assim que o "projeto externo" é simulado)
- Test: `tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs`, `tests/Secco.Intranet.Tests/Integration/MenuItemExtraTests.cs`

**Interfaces:**
- Consumes: `OrdemDoMenu.Ordenar`, `PosicoesDoMenuDto`, `ObterPosicoesDoMenuHandler` (Task 2); `CatalogoDeItensDoMenuPrincipal`, `IItemDeMenuPrincipal` (Task 3).
- Produces (em `Secco.Intranet.Web.Navigation`):
  - `sealed record ItemExtraDoMenu(string Chave, string Rotulo, string? Icone, string Url, int Ordem)`, com `static ItemExtraDoMenu De(IItemDeMenuPrincipal item)`
  - `sealed record EntradaDoMenuPrincipal(string Chave, string Rotulo, string? Icone, string Url)`
  - `IntranetNavigation.PrincipalNaOrdemPadrao(bool incluirDiretorio, IEnumerable<ItemExtraDoMenu> extras) → IReadOnlyList<EntradaDoMenuPrincipal>`: Mural, Diretório (se pedido), extras por `Ordem` e depois `Rotulo` (`OrdinalIgnoreCase`). É usado pelo `Build` e pela tela da Task 5.
  - `static class GatesDoMenuPrincipal` → `static Task<IReadOnlyList<ItemExtraDoMenu>> FiltrarAsync(IReadOnlyList<IItemDeMenuPrincipal> itens, ClaimsPrincipal usuario, bool modoAberto, ILogger logger, CancellationToken cancellationToken)`: no modo aberto devolve todos sem chamar gate; exceção no gate = item fora, `LogWarning`
  - `NavigationRequest` ganha, no fim e com padrão `null`: `IReadOnlyList<ItemExtraDoMenu>? ItensExtras = null, PosicoesDoMenuDto? Posicoes = null` (os testes existentes continuam compilando)

- [ ] **Step 1: Testes unitários do `Build`** em `NavegacaoETemaTests` (os existentes ficam como estão: sem extras e sem posições, o menu de hoje):
  - `Build_ComItemExtra_EntraDepoisDosFixos`: extra `institucional` com `MostrarDiretorio: true` → principal == `Mural, Diretório, Institucional`.
  - `Build_ExtrasPorOrdem_EDepoisPorRotulo`: `("b", "Beta", Ordem 1)`, `("a", "Alfa", Ordem 1)`, `("z", "Zeta", Ordem 0)` → `Zeta, Alfa, Beta`.
  - `Build_ComPosicoes_ReordenaOPrincipalInclusiveOsFixos`: posições `{institucional:0, mural:1, diretorio:2}` → `Institucional, Mural, Diretório`.
  - `Build_ComPosicoesDeSetores_ReordenaASecaoSetores`: setores "A" e "B" com árvore mínima, posição `{B.Id:0}` → `B, A`.
  - `Build_ItemExtraNaRotaDele_FicaAtivo`: caminho `/institucional/historia` → só o extra ativo, Mural não.
  - `Build_ItemExtraComQueryEBarraFinal_FicaAtivoPeloCaminho`: `Url = "/institucional/?aba=1"`, caminho `/institucional` → ativo.
  - `Build_PosicaoDoDiretorio_SemAcessoAoDiretorio_NaoFazODiretorioAparecer`.
  - Em `tests/Secco.Intranet.Tests/Unit/GatesDoMenuPrincipalTests.cs`: `FiltrarAsync_ModoAberto_DevolveTodos_SemChamarOGate` (gate falso conta chamadas → 0), `FiltrarAsync_GateFalso_TiraOItem`, `FiltrarAsync_GateQueLanca_TiraOItem_EAvisa`.
- [ ] **Step 2: Testes de integração** em `MenuItemExtraTests` (padrão de `MenuVisibilidadeDeSetorTests`: cliente com `RolesDeTesteMiddleware.Header`). Itens de exemplo públicos no próprio arquivo:
  - `ItemDeMenuDeTeste`: chave `extra-de-teste`, rótulo "Extra de teste", URL `/extra-de-teste`, visível só com a role `extra-de-teste-leitor`
  - `ItemDeMenuQueExplode`: chave `extra-que-explode`, lança `InvalidOperationException` quando o usuário tem a role `extra-explode`, e é invisível nos outros casos

  Testes:
  - `ProjetoReferenciado_SemARoleDoGate_ItemNaoAparece`: GET `/` → HTML sem "Extra de teste".
  - `ProjetoReferenciado_ComARoleDoGate_ItemApareceNoGrupoPrincipal`: o HTML tem `href="/extra-de-teste"`, nos temas Vertical e Horizontal (como `TemaHorizontal_RenderizaAMesmaArvore`).
  - `GateQueLancaExcecao_ItemSome_EPaginaRenderiza`: role `extra-explode` → 200, sem "extra-que-explode".
  - `ComPosicaoSalva_OMenuSegueAOrdemDoTenant`: gravar `Principal = ["extra-de-teste", "mural"]` no Alfa via `MoverPosicaoNoMenuHandler`/repositório → no HTML, `/extra-de-teste` aparece antes de `href="/"`.

  O caso "visível no modo aberto de DEV" é provado em `GatesDoMenuPrincipalTests` (Step 1): o host de teste roda em `Testing`, que nunca é modo aberto.
- [ ] **Step 3: Rodar e ver falhar.** Run: `dotnet test --filter "FullyQualifiedName~NavegacaoETemaTests|FullyQualifiedName~MenuItemExtraTests"`. Expected: falha de compilação.
- [ ] **Step 4: Implementar.**
  - `Build` monta `PrincipalNaOrdemPadrao`, ordena com `OrdemDoMenu.Ordenar(..., e => e.Chave, request.Posicoes?.Principal)` e calcula o `Ativo`: o Mural mantém `EhMural`; os outros usam `Corresponde(caminho, CaminhoDe(url))`, onde `CaminhoDe` corta a partir de `?`/`#` e tira o `/` final (exceto de `/`). Os setores passam por `Ordenar(..., s => s.Id.ToString("D"), request.Posicoes?.Setores)` antes do `SetorNoMenu`.
  - `NavigationViewComponent` recebe `CatalogoDeItensDoMenuPrincipal`, cria os itens com `serviceProvider` e avalia os gates: no modo aberto não chama o gate; exceção vira invisível com `LogWarning` e o mesmo `#pragma CA1031` dos vizinhos. As posições carregam num `CarregarPosicoesAsync` no molde de `CarregarArvoresAsync`: sem tenant ou com falha, `PosicoesDoMenuDto.Vazias`.
  - A avaliação dos gates mora em `GatesDoMenuPrincipal.FiltrarAsync` (ver Interfaces), não dentro do view component, para que o modo aberto e a exceção tenham teste unitário: o Web não tem `InternalsVisibleTo`, e o host de teste nunca roda em modo aberto.
- [ ] **Step 5: Rodar e ver passar.** Mesmo filtro, Expected: PASS. Depois a suíte inteira: `dotnet test`, Expected: nenhum teste antigo quebra.
- [ ] **Step 6: Commit** — `feat(menu): itens extras e ordem do tenant na montagem do menu`.

---

### Task 5: Tela de ordenação

**Files:**
- Create: `src/Secco.Intranet.Web/Controllers/MenuController.cs`
- Create: `src/Secco.Intranet.Web/Models/MenuOrdemViewModel.cs` (ou onde moram os view models vizinhos, como `SetorMenuViewModel`)
- Create: `src/Secco.Intranet.Web/Views/Menu/Index.cshtml`
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs` (item "Menu" na Administração)
- Modify: `src/Secco.Intranet.Application/Setores/SlugsReservados.cs` (`"menu"` na linha dos controllers, em ordem alfabética)
- Test: `tests/Secco.Intranet.Tests/Integration/MenuOrdemAdministracaoTests.cs`, `tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs`

**Interfaces:**
- Consumes: `CatalogoDeItensDoMenuPrincipal` (Task 3); `IntranetNavigation.PrincipalNaOrdemPadrao`, `ItemExtraDoMenu.De` (Task 4); `OrdemDoMenu.Ordenar`, os três handlers (Task 2); `SearchSetoresHandler` com `ApenasAtivos: true` e o mesmo limite do menu.
- Produces:
  - `GET /menu` → `Index`
  - `POST /menu/mover` (`GrupoDoMenu grupo, string chave, bool paraCima`) → redirect para `/menu` com `TempData` de mensagem
  - `POST /menu/restaurar` (`GrupoDoMenu grupo`) → idem
  - `sealed record MenuOrdemViewModel(IReadOnlyList<LinhaDaOrdem> Principal, IReadOnlyList<LinhaDaOrdem> Setores)`, `sealed record LinhaDaOrdem(string Chave, string Rotulo, string? Icone)`

- [ ] **Step 1: Teste unitário**: `Build_ComMostrarAdministracao_TemOItemMenuDepoisDeAcesso` → Administração == `Setores, Acesso, Menu` (`/menu`, `bi-list-ol`).
- [ ] **Step 2: Testes de integração** (padrão de `SetorMenuAdministracaoTests`, com `TokenAsync` para o antiforgery):
  - `SemIntranetAdmin_Recebe403`: GET e POST.
  - `Tela_ListaFixosExtrasESetores_NaOrdemEfetiva`: com a role `intranet-admin`, sem a role do gate do item de teste → mesmo assim "Extra de teste" está na lista.
  - `Mover_DesceOMural_EOMenuDeOutroUsuarioRefleteAOrdem`: como admin, POST mover `mural` para baixo duas vezes (passa do Diretório e do extra) → GET `/` com outro cliente, só com a role `extra-de-teste-leitor`, mostra `href="/extra-de-teste"` antes de `href="/"`. Esse usuário não vê o Diretório, e a conferência é só pela posição relativa entre Mural e o extra.
  - `Mover_ChaveQueNaoExisteMais_VoltaComAviso`: POST com `chave = Guid.NewGuid()` → 302 para `/menu`, mensagem com "não está mais no menu".
  - `Restaurar_VoltaAOrdemPadrao`.
  - `Mover_NoTenantAlfa_NaoAfetaOBeta`.
- [ ] **Step 3: Rodar e ver falhar.** Run: `dotnet test --filter "FullyQualifiedName~MenuOrdemAdministracaoTests|FullyQualifiedName~NavegacaoETemaTests"`. Expected: falha (404 em `/menu`).
- [ ] **Step 4: Implementar.** O controller monta as duas listas efetivas com um método privado `ChavesEfetivasAsync(GrupoDoMenu)`, usado no GET e **recalculado no POST**: o form nunca manda a lista. O principal usa `PrincipalNaOrdemPadrao(incluirDiretorio: true, catalogo.Criar(HttpContext.RequestServices).Select(ItemExtraDoMenu.De))`, sem gate. A view compõe `_PageHeader` e um `_Card` por grupo. Cada linha tem dois forms POST pequenos (subir e descer, desabilitados nas pontas), com `aria-label` "Subir {rótulo}" e "Descer {rótulo}". "Restaurar padrão" usa `confirm()`, como os botões de exclusão vizinhos. Lista vazia de setores → `_EmptyState`. O markup segue `Views/Setores/Menu.cshtml`.
- [ ] **Step 5: Rodar e ver passar.** Mesmo filtro, Expected: PASS. Rodar também `SlugsReservadosTests`, Expected: PASS.
- [ ] **Step 6: Conferência visual.** Subir o app (skill `run` / `scripts/dev.ps1`) e abrir `/menu` nos temas Vertical e Horizontal, claro e escuro. Mover um item e conferir o menu lateral/superior. Registrar no commit o que foi visto.
- [ ] **Step 7: Commit** — `feat(menu): tela de ordenacao do menu na administracao`.

---

### Task 6: ADR, guia de extensão e roadmap

**Files:**
- Modify: `docs/adr/secco-intranet-adrs.md` (ADR-0011, no formato das vizinhas: Status Aceita, Data 2026-10-29, Contexto/Decisão/Consequências)
- Create: `docs/extensao.md`
- Modify: `docs/temas.md` (link para `extensao.md` na introdução)
- Modify: `docs/roadmap.md`
- Modify: `docs/specs/2026-10-29-menu-extensivel-e-ordenavel-design.md` (`Estado: aprovado, implementado`)

- [ ] **Step 1: ADR-0011.**
  - *Contexto:* o ciclo Web ↔ RCL externa e o conflito de fork.
  - *Decisão:* projeto próprio, descoberta pelas application parts no primeiro uso, `Chave` estável, ordem por tenant.
  - *Consequências:* mudar `IItemDeMenuPrincipal` quebra quem estende; item inválido some em vez de falhar o startup; a ordem é do tenant, não do usuário.
- [ ] **Step 2: `docs/extensao.md`.** Passo a passo:
  1. criar a RCL;
  2. referenciar `Secco.Intranet.Web.Extensibility`;
  3. implementar `IItemDeMenuPrincipal`, com um exemplo de item visível para todo usuário autenticado (`usuario.Identity?.IsAuthenticated == true`) e outro usando `IAuthorizationService` injetado;
  4. referenciar a RCL no `Secco.Intranet.Web`;
  5. a alternativa `AddItemDeMenuPrincipal<T>()`.

  Fecha com as regras de `Chave`/`Url`, o aviso sobre o primeiro segmento da `Url` coincidir com o slug de um setor, e um parágrafo dizendo que a posição final é do admin (`/menu`). Sem nome de empresa ou fork.
- [ ] **Step 3: Roadmap e estado da spec.**
- [ ] **Step 4: Commit** — `docs(menu): ADR-0011, guia de extensao e roadmap` com `Closes #2` no corpo.
