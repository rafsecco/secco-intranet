# Links do menu principal — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** o `intranet-admin` cria, edita e remove links simples no grupo principal do menu pela tela `/menu`, visíveis para todo usuário do tenant, ordenáveis junto com os outros itens e com a opção de abrir em nova aba.

**Architecture:** entidade e tabela próprias (`LinkDoMenuPrincipal`), sem árvore. Casos de uso em `Application/Menu` validam com a regra `RotaEhValida` que o menu já tem e registram na trilha. A navegação acrescenta os links como terceira fonte do grupo principal (depois de bibliotecas e extras), com chave de posição `link-{id}`, e passa `NovaAba` ao `NavigationItemModel` que os temas já tratam. Nenhuma view de tema muda.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 (SQL Server e PostgreSQL), xUnit, AwesomeAssertions, Playwright (opt-in com `SECCO_UI_TESTS=1`).

**Spec:** [docs/specs/2026-11-18-links-do-menu-principal-design.md](../specs/2026-11-18-links-do-menu-principal-design.md)

## Global Constraints

- Migration sempre em par, mesmo nome nos dois projetos (`Migrations.SqlServer` e `Migrations.Postgres`), gerada com `dotnet ef` (ferramenta local: `dotnet tool restore`). Nomes de tabela e coluna vêm da convenção da ADR-0017; não escrever `ToTable` nem `HasColumnName`.
- Todo POST com `[ValidateAntiForgeryToken]`. Sem `style=""`, `<script>` inline nem `onclick` nas views (CSP em Enforce).
- Nenhuma view de tema (`Themes.Vertical`, `Themes.Horizontal`) e nenhum tipo de `Secco.Intranet.Web.Theming.Contracts` muda.
- `IItemDeMenuPrincipal` não muda.
- Limites: rótulo 256, URL 512, ícone 64.
- URL aceita: caminho local começando com `/` e não com `//` nem `/\`, ou `http`/`https` absoluto (`CriarItemMenuHandler.RotaEhValida`). O resto é recusado.
- "Abrir em nova aba" vem marcado em todo formulário de criação; na edição mostra o valor gravado.
- Ordem padrão do grupo principal: Mural, Diretório, bibliotecas por nome, extras por `Ordem` e rótulo, **links por rótulo** (`StringComparer.OrdinalIgnoreCase`).
- Chave de posição: `link-` + `Id.ToString("D")`.
- Tela de administração continua exclusiva do `intranet-admin` (`[SomenteIntranetAdmin]`, que responde 403 a quem não é). Id de link inexistente responde 404.
- Testes com AwesomeAssertions, nunca FluentAssertions. Amostras fictícias (`suporte.exemplo.com.br`).
- Build sem avisos (`TreatWarningsAsErrors` já ligado). Commits direto na `main`; conferir `git branch --show-current` antes de cada commit.
- Texto de tela e mensagens em português.

## Review Focus

1. **URL com espaços em volta ou esquema em maiúsculas** (`"  HTTPS://Suporte.exemplo.com.br  "`): aceita e gravada aparada; `/\host`, `ftp://x`, `data:text/html,x` e `JavaScript:alert(1)` recusadas com `Menu.RotaInvalida`. Teste na Tarefa 2.
2. **Excluir um link que tinha posição salva e depois mover outro item em `/menu`**: o mover funciona e não regrava a chave do link excluído. Teste na Tarefa 4.
3. **Editar ou excluir id que não existe, ou que existe só no outro tenant**: 404, nada gravado no tenant de nenhum dos dois. Teste na Tarefa 4.
4. **Ícone digitado sem prefixo ou com a classe `bi` junto** (`"headset"`, `"bi bi-headset"`): normalizado para `bi-headset` por `IconeBootstrap.TentarNormalizar`; `"<b>x</b>"` recusado com `Menu.IconeInvalido`. Teste na Tarefa 2.
5. **Rótulo com HTML** (`"<b>Chamado</b>"`): aparece escapado no menu e em `/menu`, nunca como marcação. Teste na Tarefa 4.

---

### Task 0: Pacotes da plataforma na versão nova

A plataforma publicou SecureGate.Client 0.26.0 (só acréscimo: `DirectSignIn`), SDK 0.10.3, SharedKernel 0.7.13 e SDK.ClientCredentials 0.1.23. Commit separado, antes da feature.

**Files:**
- Modify: `Directory.Packages.props`

- [ ] **Step 1:** `dotnet list Secco.Intranet.slnx package --outdated` (com o feed privado autenticado; 401 = `gh auth status`). Subir **todos os `Secco.*`** listados para a estável mais recente que o comando mostrar. Esperado, no mínimo: `Secco.SharedKernel` 0.7.13, `Secco.SDK.AspNetCore` 0.10.3, `Secco.SDK.ClientCredentials` 0.1.23, `Secco.SecureGate.Client` 0.26.0.
- [ ] **Step 2:** `dotnet build Secco.Intranet.slnx` → 0 avisos; `dotnet test Secco.Intranet.slnx` → tudo passa.
- [ ] **Step 3:** CHANGELOG, em `[Não lançado]`: uma linha com as versões novas.
- [ ] **Step 4: Commit** `chore(deps): pacotes da plataforma (SecureGate.Client 0.26.0, SDK 0.10.3, SharedKernel 0.7.13)`.

### Task 1: Entidade, tabela e repositório

**Files:**
- Create: `src/Secco.Intranet.Domain/Menu/LinkDoMenuPrincipal.cs`
- Create: `src/Secco.Intranet.Application/Menu/ILinkDoMenuPrincipalRepository.cs`
- Create: `src/Secco.Intranet.Infrastructure/Mappings/LinkDoMenuPrincipalConfiguration.cs`
- Create: `src/Secco.Intranet.Infrastructure/Repositories/LinkDoMenuPrincipalRepository.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs` (DbSet `LinksDoMenuPrincipal`)
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (registro scoped, ao lado de `IPosicaoMenuRepository`)
- Create: migration `LinksDoMenuPrincipal` nos dois projetos de migrations
- Test: `tests/Secco.Intranet.Tests/Unit/LinkDoMenuPrincipalTests.cs`, `tests/Secco.Intranet.Tests/Integration/LinkDoMenuPrincipalPersistenceTests.cs`

**Interfaces:**
- Produces:
  - `sealed class LinkDoMenuPrincipal : BaseEntity` com `string Rotulo`, `string Url`, `string? Icone`, `bool NovaAba` (setters privados), construtor `(string rotulo, string url, string? icone, bool novaAba)`, `void Editar(string rotulo, string url, string? icone, bool novaAba)`, `const string PrefixoDaChave = "link-"`, `string Chave => PrefixoDaChave + Id.ToString("D")`. Construtor e `Editar` lançam `DomainInvariantException` para rótulo ou URL vazios (a validação de verdade é da Application).
  - `ILinkDoMenuPrincipalRepository`:
    - `Task<IReadOnlyList<LinkDoMenuPrincipal>> ListarAsync(CancellationToken)` (sem rastreio)
    - `Task<LinkDoMenuPrincipal?> ObterAsync(Guid id, CancellationToken)` (rastreado)
    - `Task AddAsync(LinkDoMenuPrincipal link, CancellationToken)` (salva)
    - `Task SalvarAsync(CancellationToken)`
    - `Task RemoverAsync(LinkDoMenuPrincipal link, CancellationToken)`: remove o link **e** a `PosicaoMenu` do grupo `Principal` com `Chave == link.Chave`, num único `SaveChangesAsync`

- [ ] **Step 1: Testes que falham** em `LinkDoMenuPrincipalTests`:

```csharp
[Fact] public void Chave_PrefixoLinkMaisIdEmFormatoD()   // link.Chave == "link-" + link.Id.ToString("D"); casa com \A[a-z0-9-]{1,64}\z
[Fact] public void Construtor_RotuloVazio_Lanca()
[Fact] public void Construtor_UrlVazia_Lanca()
[Fact] public void Editar_TrocaOsQuatroCampos()
```

e em `LinkDoMenuPrincipalPersistenceTests` (padrão de `ItemMenuPersistenceTests`, nos dois engines que a suíte já cobre):

```csharp
[Fact] public async Task AddEListar_DevolveOLinkGravado()
[Fact] public async Task Remover_ApagaOLinkESuaPosicaoNoGrupoPrincipal()   // grava PosicaoMenu(Principal, link.Chave, 0) e PosicaoMenu(Principal, "mural", 1); após RemoverAsync sobra só "mural"
[Fact] public async Task Listar_NaoVeLinkDoOutroTenant()
```

- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~LinkDoMenuPrincipal"` → falha de compilação.
- [ ] **Step 3:** Implementar entidade, mapeamento (`HasMaxLength` 256/512/64; só isso), DbSet, repositório e registro.
- [ ] **Step 4: Migrations:**

```bash
dotnet ef migrations add LinksDoMenuPrincipal --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add LinksDoMenuPrincipal --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```

Conferir no arquivo gerado que tabela e colunas saíram pela convenção (prefixos `tb_`, `id_`, `ds_`, `fl_`).
- [ ] **Step 5:** Rodar o filtro do Step 2 → passa.
- [ ] **Step 6: Commit** `feat(menu): links do menu principal no banco do tenant`.

### Task 2: Casos de uso e trilha

**Files:**
- Create: `src/Secco.Intranet.Application/Menu/LinksDoMenuPrincipal.cs` (DTO e comandos)
- Create: `src/Secco.Intranet.Application/Menu/ListarLinksDoMenuPrincipalHandler.cs`, `CriarLinkDoMenuPrincipalHandler.cs`, `EditarLinkDoMenuPrincipalHandler.cs`, `ExcluirLinkDoMenuPrincipalHandler.cs`
- Modify: `src/Secco.Intranet.Application/Menu/AuditoriaDeMenu.cs` (`LinkAsync`)
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs` (classe `LinksDoMenu`)
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs` (quatro handlers scoped)
- Test: `tests/Secco.Intranet.Tests/Unit/LinksDoMenuPrincipalHandlersTests.cs`

**Interfaces:**
- Consumes: `ILinkDoMenuPrincipalRepository`, `LinkDoMenuPrincipal` (Task 1); `CriarItemMenuHandler.RotaEhValida`, `IconeBootstrap.TentarNormalizar`, `ITrilhaDeAuditoria`.
- Produces:
  - `sealed record LinkDoMenuPrincipalDto(Guid Id, string Chave, string Rotulo, string Url, string? Icone, bool NovaAba)` com `static LinkDoMenuPrincipalDto FromEntity(LinkDoMenuPrincipal)`
  - `sealed record CriarLinkDoMenuPrincipalCommand(string? Rotulo, string? Url, string? Icone, bool NovaAba)`
  - `sealed record EditarLinkDoMenuPrincipalCommand(Guid Id, string? Rotulo, string? Url, string? Icone, bool NovaAba)`
  - `ListarLinksDoMenuPrincipalHandler.HandleAsync(CancellationToken) → Task<Result<IReadOnlyList<LinkDoMenuPrincipalDto>>>`, ordenado por rótulo (`OrdinalIgnoreCase`)
  - `CriarLinkDoMenuPrincipalHandler.HandleAsync(CriarLinkDoMenuPrincipalCommand, CancellationToken) → Task<Result<Guid>>`
  - `EditarLinkDoMenuPrincipalHandler.HandleAsync(EditarLinkDoMenuPrincipalCommand, CancellationToken) → Task<Result>`
  - `ExcluirLinkDoMenuPrincipalHandler.HandleAsync(Guid id, CancellationToken) → Task<Result>`
  - Erros: `IntranetErrors.LinksDoMenu.NotFound` (`Error.NotFound("Intranet.LinksDoMenu.NotFound", "Link não encontrado.")`), `RotuloRequired` ("Informe o rótulo do link."), `UrlRequired` ("Informe a URL do link."). URL fora da regra → `IntranetErrors.Menu.RotaInvalida`; ícone → `IntranetErrors.Menu.IconeInvalido`; tamanho → `IntranetErrors.Menu.CampoMuitoLongo("O rótulo" | "A URL", limite)`.
  - Verbos `MenuLinkCriar = "menu.link-criar"`, `MenuLinkEditar = "menu.link-editar"`, `MenuLinkExcluir = "menu.link-excluir"`, recurso `RecursosDeAuditoria.Menu`, id = `link.Id`.
  - `AuditoriaDeMenu.LinkAsync(ITrilhaDeAuditoria, string verbo, Guid linkId, object dados, CancellationToken)`. Dados: criar → `{ rotulo, url, icone, novaAba }`; editar → `{ antes = {...}, depois = {...} }`; excluir → os quatro campos do que foi apagado. Registrar só depois de salvar.

- [ ] **Step 1: Testes que falham** (fakes em memória, padrão de `LinksHandlersTests`):

```csharp
[Fact] public async Task Criar_UrlExterna_GravaETrilhaMenuLinkCriar()          // "Abrir um chamado", "https://suporte.exemplo.com.br", null, true
[Fact] public async Task Criar_CaminhoLocal_Aceita()                            // "/diretorio"
[Theory]
[InlineData("javascript:alert(1)")] [InlineData("JavaScript:alert(1)")] [InlineData("//outro.host")]
[InlineData("/\\outro.host")] [InlineData("ftp://x")] [InlineData("data:text/html,x")] [InlineData("suporte.exemplo.com.br")]
public async Task Criar_UrlRecusada_RotaInvalida_NadaGravado(string url)        // também: trilha vazia
[Fact] public async Task Criar_UrlComEspacosEEsquemaMaiusculo_GravaAparada()   // "  HTTPS://Suporte.exemplo.com.br  "
[Fact] public async Task Criar_RotuloVazio_RotuloRequired()
[Fact] public async Task Criar_UrlVazia_UrlRequired()
[Fact] public async Task Criar_RotuloDe257_CampoMuitoLongo()
[Fact] public async Task Criar_UrlDe513_CampoMuitoLongo()
[Theory] [InlineData("headset")] [InlineData("bi bi-headset")]
public async Task Criar_IconeNormalizado(string icone)                          // grava "bi-headset"
[Fact] public async Task Criar_IconeInvalido_IconeInvalido()                    // "<b>x</b>"
[Fact] public async Task Editar_TrocaCamposETrilhaAntesEDepois()
[Fact] public async Task Editar_IdInexistente_NotFound()
[Fact] public async Task Excluir_RemoveETrilhaMenuLinkExcluir()
[Fact] public async Task Excluir_IdInexistente_NotFound()
[Fact] public async Task Listar_OrdenaPorRotuloSemDiferenciarCaixa()            // "webmail", "Abrir um chamado" → "Abrir um chamado", "webmail"
```

- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~LinksDoMenuPrincipalHandlersTests"` → falha de compilação.
- [ ] **Step 3:** Implementar. Ordem da validação: rótulo, URL (apara, vazia, tamanho, `RotaEhValida`), ícone.
- [ ] **Step 4:** Filtro do Step 2 → passa.
- [ ] **Step 5: Commit** `feat(menu): criar, editar e excluir links do menu principal com trilha`.

### Task 3: Links na navegação e no catálogo

**Files:**
- Modify: `src/Secco.Intranet.Web/Navigation/ItemExtraDoMenu.cs` (`EntradaDoMenuPrincipal`)
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs`
- Modify: `src/Secco.Intranet.Web/Navigation/CatalogoDeItensDoMenuPrincipal.cs`
- Modify: `src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs`, `tests/Secco.Intranet.Tests/Unit/CatalogoDeItensDoMenuPrincipalTests.cs`

**Interfaces:**
- Consumes: `LinkDoMenuPrincipalDto`, `ListarLinksDoMenuPrincipalHandler` (Task 2); `LinkDoMenuPrincipal.PrefixoDaChave` (Task 1).
- Produces:
  - `EntradaDoMenuPrincipal(string Chave, string Rotulo, string? Icone, string Url, bool NovaAba = false)`
  - `IntranetNavigation.PrincipalNaOrdemPadrao(bool incluirDiretorio, IEnumerable<ItemExtraDoMenu> extras, IEnumerable<BibliotecaDto>? bibliotecas = null, IEnumerable<LinkDoMenuPrincipalDto>? links = null)`: links por último, por rótulo `OrdinalIgnoreCase`
  - `NavigationRequest(..., IReadOnlyList<LinkDoMenuPrincipalDto>? LinksDoMenu = null)`, novo parâmetro no fim
  - `Build` passa `NovaAba: entrada.NovaAba` ao `NavigationItemModel`; entrada cuja URL não começa com `/` nunca fica `Ativo`

- [ ] **Step 1: Testes que falham:**

```csharp
// NavegacaoETemaTests
[Fact] public void Build_LinkDoMenu_EntraDepoisDosExtrasComNovaAba()        // item "Abrir um chamado", Url "https://suporte.exemplo.com.br", NovaAba true, depois de um extra
[Fact] public void Build_LinksDoMenu_OrdenadosPorRotulo()
[Fact] public void Build_PosicaoSalvaDoLink_LevaParaEntreMuralEDiretorio()  // Posicoes.Principal: mural 0, link-{id} 1, diretorio 2
[Fact] public void Build_LinkExterno_NuncaAtivo()                           // CaminhoAtual "/"
[Fact] public void Build_LinkLocal_AtivoPeloPrefixo()                       // Url "/diretorio/organograma", CaminhoAtual "/diretorio/organograma"
[Fact] public void Build_ItensFixos_NovaAbaFalso()
// CatalogoDeItensDoMenuPrincipalTests
[Fact] public void Criar_ChaveComPrefixoLink_ItemFicaDeFora()               // item de teste com Chave "link-qualquer" some, com aviso no log
```

- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~NavegacaoETemaTests|FullyQualifiedName~CatalogoDeItensDoMenuPrincipalTests"` → falha.
- [ ] **Step 3:** Implementar. No catálogo, recusa ao lado da de `biblioteca-`: `$"o prefixo '{LinkDoMenuPrincipal.PrefixoDaChave}' é reservado aos links do menu"`. No `NavigationViewComponent`, `CarregarLinksAsync()` no mesmo molde de `CarregarBibliotecasAsync`: sem tenant resolvido → `[]`; falha no handler ou exceção → `[]` com `LogWarning("Não foi possível carregar os links do menu; exibindo o menu sem eles.")`. Sem filtro por usuário.
- [ ] **Step 4:** Filtro do Step 2 → passa; `dotnet build Secco.Intranet.slnx` → 0 avisos.
- [ ] **Step 5: Commit** `feat(menu): links no grupo principal com nova aba e prefixo link- reservado`.

### Task 4: Telas em `/menu`

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/MenuController.cs`
- Modify: `src/Secco.Intranet.Web/Models/MenuOrdemViewModel.cs` (`Links`)
- Create: `src/Secco.Intranet.Web/Models/LinkDoMenuFormViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Views/Menu/Index.cshtml` (card Links)
- Create: `src/Secco.Intranet.Web/Views/Menu/LinkForm.cshtml`
- Test: `tests/Secco.Intranet.Tests/Integration/LinksDoMenuPrincipalTests.cs`

**Interfaces:**
- Consumes: os quatro handlers (Task 2); `PrincipalNaOrdemPadrao(..., links)` (Task 3).
- Produces:
  - `LinkDoMenuFormViewModel { Guid? Id; string? Rotulo; string? Url; string? Icone; bool NovaAba }`
  - `MenuOrdemViewModel(IReadOnlyList<LinhaDaOrdem> Principal, IReadOnlyList<LinhaDaOrdem> Setores, IReadOnlyList<LinkDoMenuPrincipalDto> Links)`
  - Rotas (atributo nas ações do `MenuController`): `GET /menu/links/novo` (`NovaAba = true`), `POST /menu/links/novo`, `GET /menu/links/{id:guid}/editar`, `POST /menu/links/{id:guid}/editar`, `POST /menu/links/{id:guid}/excluir`. Sucesso redireciona a `/menu` com `TempData` "Link criado." / "Link salvo." / "Link excluído."; `NotFound` do handler → 404; erro de validação → o formulário de novo, com a mensagem no campo: `RotuloRequired` e `CampoMuitoLongo("O rótulo")` em `Rotulo`, `UrlRequired`, `RotaInvalida` e `CampoMuitoLongo("A URL")` em `Url`, `IconeInvalido` em `Icone` (mapear pelo `Error.Code`).
  - `LinhasAsync(GrupoDoMenu.Principal)` passa os links a `PrincipalNaOrdemPadrao`, para que `Mover` e a tela os vejam.

- [ ] **Step 1: Testes que falham** (padrão de `MenuOrdemAdministracaoTests`: header `intranet-admin`, token de antiforgery lido do HTML):

```csharp
[Fact] public async Task Criar_LinkExterno_ApareceNoMenuDeUsuarioComumComTargetERel()   // POST novo "Abrir um chamado" → https://suporte.exemplo.com.br, novaAba=true; GET "/" sem role: href, target="_blank", rel="noopener noreferrer"
[Fact] public async Task Criar_TemaHorizontal_MostraOLinkComTarget()                   // UseSetting("Intranet:Theme:Nome", "Horizontal")
[Fact] public async Task GetNovo_CaixaDeNovaAbaVemMarcada()                            // checkbox NovaAba com checked
[Theory] [InlineData("javascript:alert(1)")] [InlineData("//outro.host")]
public async Task Criar_UrlRecusada_FormularioComMensagem(string url)                  // 200, mensagem de RotaInvalida no HTML, link não aparece em "/"
[Fact] public async Task Link_ApareceNaOrdenacaoEPodeSubirEntreOsFixos()               // Mover paraCima até ficar logo depois do Mural; menu de "/" segue a ordem
[Fact] public async Task Ordem_ValePorTenant()                                          // a posição salva no Alfa não muda o menu do Beta
[Fact] public async Task Editar_TrocaRotuloEUrl_MenuMostraONovo()
[Fact] public async Task Excluir_SomeDoMenuEDaOrdenacao()
[Fact] public async Task Excluir_ComPosicaoSalva_MoverOutroItemContinuaFuncionando()   // Review Focus 2
[Fact] public async Task EditarOuExcluir_IdInexistenteOuDeOutroTenant_404()            // Review Focus 3
[Fact] public async Task RotuloComHtml_ApareceEscapado()                                // Review Focus 5: "&lt;b&gt;Chamado&lt;/b&gt;" em "/" e "/menu"
[Fact] public async Task SemIntranetAdmin_GetEPosts_403()
[Fact] public async Task PostSemAntiforgery_400()
[Fact] public async Task CriarEditarExcluir_RegistramNaTrilha()                         // menu.link-criar, menu.link-editar, menu.link-excluir no tenant
```

- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~LinksDoMenuPrincipalTests"` → falha.
- [ ] **Step 3:** Implementar. Card Links no topo de `Index.cshtml` com `_Card`, lista `sc-list` (ícone, rótulo, URL, "nova aba" quando marcado, editar, excluir com `data-confirmar="Excluir o link &quot;{rótulo}&quot;?"`), botão "+ Link" e `_EmptyState` ("Nenhum link", "Atalhos para sistemas que valem para a empresa toda."). Formulário no molde de `Views/Setor/LinkForm.cshtml`, com o texto de ajuda da URL "Caminho da intranet (/...) ou endereço http/https." Atualizar o subtítulo de `/menu` para citar os links.
- [ ] **Step 4:** Filtro do Step 2 → passa; `MenuOrdemAdministracaoTests` e `MenuBibliotecasTests` continuam passando.
- [ ] **Step 5: Commit** `feat(menu): telas de links do menu principal em /menu`.

### Task 5: Documentação, tela e suíte inteira

**Files:**
- Modify: `docs/adr/secco-intranet-adrs.md` (ADR-0016)
- Modify: `CHANGELOG.md`, `docs/extensao.md`, `docs/roadmap.md`, `docs/specs/2026-11-18-links-do-menu-principal-design.md` (Estado)
- Create: `tests/Secco.Intranet.Tests/Ui/LinksDoMenuPrincipalTelaTests.cs`

- [ ] **Step 1: ADR-0016**, "Links do menu principal cadastrados pelo admin", no formato da ADR-0015 (Status Aceita, Data 2026-11-18, Contexto, Decisão, Alternativas descartadas, Consequências). Decisão: dado do tenant em tabela própria, só `intranet-admin`, folha visível a todos, chave `link-{id}` com prefixo reservado no catálogo, URL pela regra `RotaEhValida`, nova aba pelo `NovaAba` do `NavigationItemModel` e marcada por padrão no cadastro; `IItemDeMenuPrincipal` segue para item em código. Descartadas: `ItemMenu` com setor nulo e lista JSON na configuração do tenant (motivos da spec).
- [ ] **Step 2: CHANGELOG** em `[Não lançado]`: a funcionalidade; quebras "rodar o `migrate` (migration `LinksDoMenuPrincipal`)" e "item de RCL com chave começando por `link-` sai do menu, com aviso no log". `extensao.md`: `link-` junto de `biblioteca-` nos prefixos reservados. `roadmap.md`: item marcado, com links para spec e plano. Spec: `**Estado:** entregue em <data>.`
- [ ] **Step 3: Teste de tela** (opt-in, padrão de `LinksDoSetorTelaTests`): criar "Abrir um chamado" por `/menu`, ver no menu, o clique abre nova página (`context.WaitForPageAsync`), subir na ordenação, excluir; sem violação de CSP no console; nos dois temas.
- [ ] **Step 4: Suíte inteira:** `SECCO_UI_TESTS=1 dotnet test Secco.Intranet.slnx` → tudo passa; `dotnet build Secco.Intranet.slnx` → 0 avisos; `dotnet ef migrations has-pending-model-changes` nos dois projetos → sem mudança pendente.
- [ ] **Step 5: Commit** `docs: ADR-0016 e registro dos links do menu principal`.
