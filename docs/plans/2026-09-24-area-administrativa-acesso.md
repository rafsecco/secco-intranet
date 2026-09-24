# Área administrativa de acesso — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Uma área `/Acesso`, exclusiva do `intranet-admin`, em que ele lista e cria perfis, atribui e retira perfis de usuários, desativa/reativa usuários e encerra sessões — tudo pelo `ISecureGateClient` já configurado — e fecha a lacuna de autorização do `SetoresController`.

**Architecture:** Uma porta `IGestaoDeAcesso` na Application (DTOs próprios, falhas como `Result`, nunca fail-open), um adaptador `SecureGateGestaoDeAcesso` na Infrastructure e um no-op `GestaoDeAcessoIndisponivel` quando o SecureGate não está configurado. As regras (reservado, último admin, autoexclusão, perfil protegido) vivem em handlers. A autorização é um `IAuthorizationFilter` declarativo (`[SomenteIntranetAdmin]`) aplicado na classe do controller, com `Order` menor que o do filtro antifalsificação — assim o `POST` sem role recebe 403 de verdade, e nenhuma action nova nasce desprotegida.

**Tech Stack:** .NET 10, ASP.NET Core MVC, `Secco.SecureGate.Client` 0.11.0, `Secco.SharedKernel.Results`/`Pagination`, xUnit + AwesomeAssertions.

**Spec:** [`docs/specs/2026-09-23-area-administrativa-acesso-design.md`](../specs/2026-09-23-area-administrativa-acesso-design.md)

## Global Constraints

- **Commits vão direto na `main`**, por caminho explícito — nunca `git add -A`. Trailer: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- **Só `intranet-admin` acessa a área.** Sem o OR do Inventário: `{slug}-admin`, `inventario-admin` e qualquer outra Role são bloqueados em **toda** rota, `GET` e `POST` (ADR-0008).
- **Bypass de "modo aberto" só em `IWebHostEnvironment.IsDevelopment() && !IntranetAuthenticationExtensions.IsConfigured(configuration)`** — nunca no ambiente `Testing`, que também não configura autenticação.
- **Controller nunca acessa `DbContext`/repositório nem o client do SecureGate** (ADR-0002 regra 1); a view recebe ViewModel/DTO, nunca tipo do client gerado.
- **Falha do SecureGate vira `Result` com mensagem sem detalhe interno** — não é fail-open como a auditoria. Timeout do `HttpClient` (`OperationCanceledException` sem cancelamento do chamador) também vira `Result`; cancelamento do chamador é relançado.
- **Perfil reservado da plataforma nunca é atribuível nem criável** pela tela: `installation-operator`, `platform-operator`, `installation-log-reader`, `installation-auditor` (espelho de `RoleInputRules.ReservedNames` da plataforma, comparação sem diferenciar caixa).
- **Nome de perfil:** `^[a-zA-Z0-9](?:[a-zA-Z0-9._-]*[a-zA-Z0-9])?$`, até 100 caracteres, sem espaço.
- **Toda comparação de nome de perfil ignora caixa** (`StringComparison.OrdinalIgnoreCase`), como `SetorAcesso`.
- **Verbos de auditoria** (recurso `acesso`): `acesso.perfil-criar`, `acesso.perfil-excluir`, `acesso.perfil-atribuir`, `acesso.perfil-retirar`, `acesso.usuario-desativar`, `acesso.usuario-reativar`, `acesso.sessoes-encerrar`. Metadata sem dado sensível: perfil, id e e-mail do usuário afetado. Leitura não é auditada.
- **Views só com os partials do contrato de tema** (`_PageHeader`, `_Badge`, `_EmptyState`, `_Pagination`); nenhum arquivo em `Themes/*` muda por causa desta área.
- **Nada no repositório, na documentação ou nos commits faz referência a sistema de terceiro analisado.**
- **Estilo:** tabs (4) em `.cs`; 4 espaços em `.cshtml`; XML doc `<summary>` em todo membro público; build com **0 avisos**; suíte inteira verde antes de cada commit. Testes de integração precisam de SQL Server (container ou `SECCO_TEST_SQLSERVER`); se o ambiente não tiver, rode só os unitários e diga isso no relatório.

## Review Focus

Entradas e condições que a spec implica e nenhum caminho feliz exercita; cada linha tem o teste que a fixa, indicado na tarefa dona:

1. **`POST` de não-admin sem token antifalsificação deve dar 403, não 400** (filtro de autorização roda antes do de antiforgery). Task 1 (unitário da ordem) e Task 11 (HTTP).
2. **Autoexclusão e último admin:** o `intranet-admin` que tenta retirar o próprio perfil, desativar a própria conta, ou retirar/desativar o último `intranet-admin` ativo (inclusive com os membros espalhados em várias páginas da API) é recusado sem chamar a plataforma. Task 6.
3. **Timeout do SecureGate** durante uma ação da tela vira toast de erro, não 500; cancelamento do chamador continua sendo relançado. Task 7.
4. **Caixa e forma do nome de perfil:** `Intranet-Admin` é tratado como `intranet-admin` nas regras; `gerente de compras`, vazio, 101 caracteres e `-x` são recusados na criação. Tasks 4 e 6.
5. **SecureGate não configurado** (o estado de todo ambiente de DEV/Testing): toda tela do admin abre em 200 com a explicação, nenhum `POST` dá 500; e página de usuários além do fim da lista, ou usuário sem e-mail, não quebram a view. Tasks 8–11.

---

## Task 1: Gate de autorização — `SomenteIntranetAdmin` e o filtro declarativo

**Files:**
- Modify: `src/Secco.Intranet.Web/Navigation/AcessoAdministrativo.cs`
- Create: `src/Secco.Intranet.Web/Authentication/SomenteIntranetAdminAttribute.cs`
- Modify: `tests/Secco.Intranet.Tests/Unit/AcessoAdministrativoTests.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/SomenteIntranetAdminAttributeTests.cs`

**Interfaces:**
- Produces: `AcessoAdministrativo.SomenteIntranetAdmin(ClaimsPrincipal?) : bool`; `AcessoAdministrativo.ModoAbertoDeDev(IWebHostEnvironment, IConfiguration) : bool`; `[SomenteIntranetAdmin]` (classe ou método) que responde `StatusCodeResult(403)` a quem não é `intranet-admin` (fora do modo aberto de DEV), com `Order = int.MinValue`.

- [ ] **Step 1: Escrever os testes que falham — `AcessoAdministrativoTests`**

Acrescente ao fim da classe existente `AcessoAdministrativoTests` (antes do `}` final):

```csharp
	[Fact]
	public void SomenteIntranetAdmin_UsuarioNulo_SemAcesso()
	{
		AcessoAdministrativo.SomenteIntranetAdmin(null).Should().BeFalse();
	}

	[Fact]
	public void SomenteIntranetAdmin_ComIntranetAdmin_TemAcesso()
	{
		AcessoAdministrativo.SomenteIntranetAdmin(Usuario("intranet-admin")).Should().BeTrue();
	}

	[Fact]
	public void SomenteIntranetAdmin_IgnoraCaixa()
	{
		AcessoAdministrativo.SomenteIntranetAdmin(Usuario("Intranet-Admin")).Should().BeTrue();
	}

	[Theory]
	[InlineData("inventario-admin")]
	[InlineData("financeiro-admin")]
	[InlineData("financeiro-user")]
	[InlineData("intranet-admin-falso")]
	public void SomenteIntranetAdmin_QualquerOutraRole_SemAcesso(string role)
	{
		AcessoAdministrativo.SomenteIntranetAdmin(Usuario(role)).Should().BeFalse(
			"a Área administrativa não é delegável: sem o OR que o Inventário tem (ADR-0008)");
	}

	[Fact]
	public void SomenteIntranetAdmin_AdminDeTodosOsSetores_SemAcesso()
	{
		AcessoAdministrativo.SomenteIntranetAdmin(Usuario("a-admin", "b-admin", "c-admin")).Should().BeFalse();
	}
```

- [ ] **Step 2: Escrever os testes que falham — `SomenteIntranetAdminAttributeTests`**

```csharp
// tests/Secco.Intranet.Tests/Unit/SomenteIntranetAdminAttributeTests.cs
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Secco.Intranet.Web.Authentication;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class SomenteIntranetAdminAttributeTests
{
	private sealed class AmbienteFalso(string nome) : IWebHostEnvironment
	{
		public string ApplicationName { get; set; } = "Teste";

		public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

		public string WebRootPath { get; set; } = string.Empty;

		public string EnvironmentName { get; set; } = nome;

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private static AuthorizationFilterContext Contexto(string ambiente, bool autenticacaoConfigurada, params string[] roles)
	{
		var configuracao = new ConfigurationBuilder()
			.AddInMemoryCollection(autenticacaoConfigurada
				? new Dictionary<string, string?> { ["Secco:SecureGate:Authority"] = "https://securegate.exemplo" }
				: [])
			.Build();

		var servicos = new ServiceCollection()
			.AddSingleton<IWebHostEnvironment>(new AmbienteFalso(ambiente))
			.AddSingleton<IConfiguration>(configuracao)
			.BuildServiceProvider();

		var http = new DefaultHttpContext { RequestServices = servicos };

		if (roles.Length > 0)
		{
			http.User = new ClaimsPrincipal(new ClaimsIdentity(
				roles.Select(role => new Claim(SeccoClaims.Role, role)), authenticationType: "Teste"));
		}

		return new AuthorizationFilterContext(
			new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
	}

	private static bool Bloqueou(AuthorizationFilterContext contexto) =>
		contexto.Result is StatusCodeResult { StatusCode: StatusCodes.Status403Forbidden };

	[Fact]
	public void SemRole_Bloqueia()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false);

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Theory]
	[InlineData("financeiro-admin")]
	[InlineData("inventario-admin")]
	public void OutraRole_Bloqueia(string role)
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false, role);

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Fact]
	public void IntranetAdmin_Libera()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false, "intranet-admin");

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public void ModoAbertoDeDev_SemAutenticacaoConfigurada_Libera()
	{
		var contexto = Contexto("Development", autenticacaoConfigurada: false);

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		contexto.Result.Should().BeNull("é o modo aberto de DEV local, sem SecureGate para emitir roles");
	}

	[Fact]
	public void Development_ComAutenticacaoConfigurada_NaoLiberaSemRole()
	{
		var contexto = Contexto("Development", autenticacaoConfigurada: true);

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue("com o SecureGate configurado o bypass de DEV não vale");
	}

	[Fact]
	public void Testing_NuncaTemBypass()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false);

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue(
			"o ambiente Testing também não configura autenticação; se o bypass valesse lá, não haveria como testar o bloqueio");
	}

	[Fact]
	public void Order_RodaAntesDoFiltroAntifalsificacao()
	{
		// ValidateAntiForgeryToken tem Order = 1000. Um Order menor faz o 403 sair antes do 400,
		// então o POST de quem não é admin não depende de ter (ou não) um token.
		new SomenteIntranetAdminAttribute().Order.Should().BeLessThan(1000);
	}
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "AcessoAdministrativoTests|SomenteIntranetAdminAttributeTests"`
Expected: falha de compilação (`SomenteIntranetAdmin`, `SomenteIntranetAdminAttribute` não existem).

- [ ] **Step 4: Implementar `AcessoAdministrativo.SomenteIntranetAdmin` e `ModoAbertoDeDev`**

Em `src/Secco.Intranet.Web/Navigation/AcessoAdministrativo.cs`, adicione os `using`s `Microsoft.AspNetCore.Hosting;`, `Microsoft.Extensions.Configuration;` e `Secco.Intranet.Web.Authentication;` no topo, e estes membros dentro da classe, depois de `TemAcesso`:

```csharp
	/// <summary>
	/// Indica se o usuário é <see cref="RoleIntranetAdmin"/> — e só isso. É o oposto deliberado
	/// de <see cref="TemAcesso"/>: a Área administrativa não é delegável, então nenhuma Role
	/// específica (nem <c>inventario-admin</c>, nem <c>{slug}-admin</c>) abre o que ela guarda
	/// (ADR-0008).
	/// </summary>
	/// <param name="usuario">Usuário atual; <c>null</c> devolve <c>false</c>.</param>
	public static bool SomenteIntranetAdmin(ClaimsPrincipal? usuario) =>
		usuario is not null
		&& usuario.FindAll(SeccoClaims.Role).Any(claim =>
			string.Equals(claim.Value, RoleIntranetAdmin, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Modo aberto de DEV: só em <c>Development</c> de verdade e sem SecureGate configurado.
	/// Nunca vale em <c>Testing</c>, que também não configura autenticação — se valesse, não
	/// haveria como provar "sem a role, bloqueado".
	/// </summary>
	/// <param name="environment">Ambiente de hospedagem.</param>
	/// <param name="configuration">Configuração do host.</param>
	public static bool ModoAbertoDeDev(IWebHostEnvironment environment, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(environment);
		ArgumentNullException.ThrowIfNull(configuration);

		return environment.IsDevelopment() && !IntranetAuthenticationExtensions.IsConfigured(configuration);
	}
```

- [ ] **Step 5: Implementar o atributo**

```csharp
// src/Secco.Intranet.Web/Authentication/SomenteIntranetAdminAttribute.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Secco.Intranet.Web.Navigation;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Restringe o controller (ou a action) ao <c>intranet-admin</c> e a mais ninguém (ADR-0008).
/// É um filtro de autorização declarativo, aplicado na classe: nenhuma action nova nasce
/// desprotegida, e a regra não depende de cada action lembrar de chamar um método.
/// </summary>
/// <remarks>
/// <see cref="Order"/> é menor que o do <c>[ValidateAntiForgeryToken]</c> (1000), então quem não
/// é admin recebe 403 mesmo num <c>POST</c> sem token — sem isso o filtro antifalsificação
/// responderia 400 antes, e o teste de "bloqueado" provaria o filtro errado.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SomenteIntranetAdminAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
	/// <inheritdoc />
	public int Order => int.MinValue;

	/// <inheritdoc />
	public void OnAuthorization(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var servicos = context.HttpContext.RequestServices;
		var ambiente = (IWebHostEnvironment)servicos.GetService(typeof(IWebHostEnvironment))!;
		var configuracao = (IConfiguration)servicos.GetService(typeof(IConfiguration))!;

		if (AcessoAdministrativo.ModoAbertoDeDev(ambiente, configuracao)
			|| AcessoAdministrativo.SomenteIntranetAdmin(context.HttpContext.User))
		{
			return;
		}

		context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
	}
}
```

Adicione `using Microsoft.Extensions.Configuration;` ao topo do arquivo.

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "AcessoAdministrativoTests|SomenteIntranetAdminAttributeTests"`
Expected: PASS (todos), build sem avisos.

- [ ] **Step 7: Commit**

```bash
git add src/Secco.Intranet.Web/Navigation/AcessoAdministrativo.cs src/Secco.Intranet.Web/Authentication/SomenteIntranetAdminAttribute.cs tests/Secco.Intranet.Tests/Unit/AcessoAdministrativoTests.cs tests/Secco.Intranet.Tests/Unit/SomenteIntranetAdminAttributeTests.cs
git commit -m "feat(acesso): gate exclusivo do intranet-admin como filtro declarativo

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 2: Fechar o `SetoresController` e o grupo "Administração" do menu

Hoje qualquer usuário autenticado que digite `/Setores/Create` cria um setor (e, com ele, duas Roles no SecureGate). Esta tarefa aplica o gate, corrige o menu e conserta os testes existentes que criavam setor sem role.

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/SetoresController.cs`
- Modify: `src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/DocumentoFluxoTests.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/FeedbackDeAcaoTests.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/PublicacaoFluxoTests.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/WebSmokeTests.cs`
- Create: `tests/Secco.Intranet.Tests/Integration/SetoresAutorizacaoTests.cs`

**Interfaces:**
- Consumes: `[SomenteIntranetAdmin]` e `AcessoAdministrativo.SomenteIntranetAdmin`/`ModoAbertoDeDev` (Task 1); `RolesDeTesteMiddleware.Header` (já existe).

- [ ] **Step 1: Escrever a matriz de autorização de Setores (falha hoje)**

```csharp
// tests/Secco.Intranet.Tests/Integration/SetoresAutorizacaoTests.cs
using System.Net;
using AwesomeAssertions;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O cadastro de setores cria Roles no SecureGate; só o <c>intranet-admin</c> pode (ADR-0008).
/// Antes desta regra o controller não tinha checagem nenhuma.
/// </summary>
public class SetoresAutorizacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente(params string[] roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	public static TheoryData<string[]> UsuariosSemAcesso => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin" },
		new[] { "inventario-admin" },
		new[] { "financeiro-user" },
	};

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Get_SemIntranetAdmin_Bloqueado(string[] roles)
	{
		var client = CriarCliente(roles);
		var id = Guid.NewGuid();

		foreach (var url in new[] { "/Setores", "/Setores/Create", $"/Setores/Edit/{id}", $"/Setores/Details/{id}" })
		{
			var resposta = await client.GetAsync(url);

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {url} exige intranet-admin");
		}
	}

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Post_SemIntranetAdmin_BloqueadoMesmoSemToken(string[] roles)
	{
		var client = CriarCliente(roles);
		var corpo = new FormUrlEncodedContent([new KeyValuePair<string, string>("Nome", "Setor Pirata")]);

		var criar = await client.PostAsync("/Setores/Create", corpo);
		var editar = await client.PostAsync($"/Setores/Edit/{Guid.NewGuid()}", corpo);

		criar.StatusCode.Should().Be(HttpStatusCode.Forbidden,
			"403 e não 400: o filtro de autorização roda antes do antifalsificação");
		editar.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task ComIntranetAdmin_Liberado()
	{
		var resposta = await CriarCliente("intranet-admin").GetAsync("/Setores");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task PostComIntranetAdminSemToken_PassaDoGateEBateNoAntifalsificacao()
	{
		var resposta = await CriarCliente("intranet-admin").PostAsync(
			"/Setores/Create", new FormUrlEncodedContent([new KeyValuePair<string, string>("Nome", "X")]));

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest,
			"o gate liberou o admin, e quem barrou foi o token antifalsificação — a ordem dos filtros está certa");
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter SetoresAutorizacaoTests`
Expected: FAIL (`/Setores` devolve 200 a quem não tem role).

- [ ] **Step 3: Aplicar o gate no controller**

Em `SetoresController.cs`, adicione `using Secco.Intranet.Web.Authentication;` e o atributo na classe; ajuste o `<summary>`:

```csharp
/// <summary>
/// Controller fino do recurso Setor (ADR-0002 regra 1): nunca acessa
/// <c>DbContext</c>/repositório diretamente — só orquestra os handlers existentes da
/// Application layer, que carregam toda a regra de negócio (regra 3). Exclusivo do
/// <c>intranet-admin</c> (ADR-0008): criar setor cria Roles no SecureGate.
/// </summary>
/// <param name="createHandler">Caso de uso de criação de setor.</param>
/// <param name="editarHandler">Caso de uso de edição de setor.</param>
/// <param name="getByIdHandler">Caso de uso de leitura pontual de setor.</param>
/// <param name="searchHandler">Caso de uso de busca paginada de setores.</param>
[SomenteIntranetAdmin]
public sealed class SetoresController(
```

- [ ] **Step 4: Consertar o menu — o grupo "Administração" passa a ser do `intranet-admin`**

Em `NavigationViewComponent.cs`, leia o construtor e acrescente `IWebHostEnvironment environment` (com `using Microsoft.AspNetCore.Hosting;` se faltar). Depois troque o `InvokeAsync`:

```csharp
	public async Task<IViewComponentResult> InvokeAsync()
	{
		var autenticacaoAtiva = IntranetAuthenticationExtensions.IsConfigured(configuration);

		// Modo aberto de DEV = Development sem SecureGate. No Testing o menu também precisa
		// respeitar a role, senão nenhum teste distingue quem vê o quê.
		var modoAberto = AcessoAdministrativo.ModoAbertoDeDev(environment, configuration);

		var request = new NavigationRequest(
			await CarregarSetoresAsync(HttpContext.User, autenticacaoAtiva).ConfigureAwait(false),
			HttpContext.Request.Path.Value ?? "/",
			MostrarAdministracao: modoAberto || AcessoAdministrativo.SomenteIntranetAdmin(HttpContext.User),
			demoOptions.Habilitado,
			MostrarInventario: modoAberto || AcessoAdministrativo.TemAcesso(HttpContext.User, AcessoAdministrativo.RoleInventarioAdmin));

		return View(IntranetNavigation.Build(request));
	}
```

`CarregarSetoresAsync` continua recebendo `autenticacaoAtiva` — a visibilidade dos setores no menu não é autorização de administração e não muda aqui.

- [ ] **Step 5: Consertar os testes existentes que criavam setor sem role**

Em cada um de `DocumentoFluxoTests.cs`, `FeedbackDeAcaoTests.cs`, `PublicacaoFluxoTests.cs` e `WebSmokeTests.cs` (o teste `GetSetores_WithTenantHeader_Returns200`), o cliente que chama `/Setores...` precisa ser `intranet-admin`. Nos que têm `CriarCliente()`, acrescente depois da linha do tenant:

```csharp
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");
```

(com `using Secco.Intranet.Tests.Integration.TestAuthentication;`). No `WebSmokeTests`, faça o mesmo no `client` do teste `GetSetores_WithTenantHeader_Returns200` apenas.

Rode a suíte inteira. Se algum teste que **não** usa `/Setores` regredir por causa do header (o header muda o `User` de toda a requisição), separe um `CriarClienteAdmin()` e use-o só nas chamadas a `/Setores`.

- [ ] **Step 6: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASS, 0 avisos. Se algum teste de menu/Inventário assumia o link visível para anônimo no `Testing`, ajuste para enviar a role correspondente (o menu agora respeita a role em `Testing`).

- [ ] **Step 7: Teste do menu**

Acrescente a `SetoresAutorizacaoTests`:

```csharp
	[Fact]
	public async Task Menu_AdminDeSetor_NaoVeOGrupoDeAdministracao()
	{
		var html = await CriarCliente("financeiro-admin").GetStringAsync("/");

		html.Should().NotContain("href=\"/setores\"");
	}

	[Fact]
	public async Task Menu_IntranetAdmin_VeOGrupoDeAdministracao()
	{
		var html = await CriarCliente("intranet-admin").GetStringAsync("/");

		html.Should().Contain("href=\"/setores\"");
	}
```

Confira no `Navigation/Default.cshtml` do tema Vertical como o `href` é escrito e ajuste a string se o markup for outro.

Run: `dotnet test --filter SetoresAutorizacaoTests` → PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Secco.Intranet.Web/Controllers/SetoresController.cs src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs tests/Secco.Intranet.Tests/Integration/DocumentoFluxoTests.cs tests/Secco.Intranet.Tests/Integration/FeedbackDeAcaoTests.cs tests/Secco.Intranet.Tests/Integration/PublicacaoFluxoTests.cs tests/Secco.Intranet.Tests/Integration/WebSmokeTests.cs tests/Secco.Intranet.Tests/Integration/SetoresAutorizacaoTests.cs
git commit -m "fix(setores): cadastro de setores exclusivo do intranet-admin

Antes qualquer usuario autenticado criava setor (e duas Roles no SecureGate).
O grupo Administracao do menu tambem passa a ser so do intranet-admin.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 3: Toast de erro — `FeedbackViewComponent` ganha o produtor de `ToastVariante.Erro`

Os dois temas já renderizam `ToastVariante.Erro` (`sc-toast--erro`); falta só o produtor no core. Esta área é o primeiro grande gerador de recusas (último admin, autoexclusão...).

**Files:**
- Modify: `src/Secco.Intranet.Web/ViewComponents/FeedbackViewComponent.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/InventarioController.cs`
- Modify: `tests/Secco.Intranet.Tests/Unit/FeedbackViewComponentTests.cs`

**Interfaces:**
- Produces: `FeedbackViewComponent.ChaveDaMensagemDeErro` (const `"MensagemDeErro"`) — o controller grava aí o texto de uma recusa e o componente renderiza `ToastModel(texto, ToastVariante.Erro)`. Se as duas chaves existirem, o erro vence e as duas são consumidas.

- [ ] **Step 1: Testes que falham**

Acrescente a `FeedbackViewComponentTests` (usa o `Montar()` que já existe; veja o teste de sucesso existente para o formato de asserção sobre `ViewViewComponentResult`):

```csharp
	[Fact]
	public void MensagemDeErro_RenderizaToastDeErro()
	{
		var (componente, tempData, _) = Montar();
		tempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = "Não foi possível.";

		var resultado = componente.Invoke().Should().BeOfType<ViewViewComponentResult>().Subject;

		resultado.ViewData!.Model.Should().BeOfType<ToastModel>()
			.Which.Should().Be(new ToastModel("Não foi possível.", ToastVariante.Erro));
	}

	[Fact]
	public void ErroEsucessoJuntos_OErroVence_EAsDuasChavesSaoConsumidas()
	{
		var (componente, tempData, _) = Montar();
		tempData[FeedbackViewComponent.ChaveDaMensagem] = "Feito.";
		tempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = "Falhou.";

		var resultado = componente.Invoke().Should().BeOfType<ViewViewComponentResult>().Subject;

		resultado.ViewData!.Model.Should().Be(new ToastModel("Falhou.", ToastVariante.Erro));
		tempData.ContainsKey(FeedbackViewComponent.ChaveDaMensagem).Should().BeFalse();
	}

	[Fact]
	public void MensagemDeErroEmBranco_NaoRenderizaNada()
	{
		var (componente, tempData, _) = Montar();
		tempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = "  ";

		componente.Invoke().Should().BeOfType<ContentViewComponentResult>();
	}
```

Run: `dotnet test --filter FeedbackViewComponentTests` → falha de compilação.

- [ ] **Step 2: Implementar**

Substitua o corpo de `FeedbackViewComponent.cs` por:

```csharp
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Web.Theming.Contracts;

namespace Secco.Intranet.Web.ViewComponents;

/// <summary>
/// Feedback de ação concluída ou recusada. Os controllers deixam a mensagem no
/// <c>TempData</c> antes do redirect e o layout invoca este componente uma vez — nenhuma view
/// de página escreve o aviso à mão, e qualquer tela nova ganha o retorno de graça (ADR-0004).
/// </summary>
public sealed class FeedbackViewComponent : ViewComponent
{
	/// <summary>Chave onde os controllers deixam a mensagem de sucesso.</summary>
	public const string ChaveDaMensagem = "Mensagem";

	/// <summary>Chave onde os controllers deixam o texto de uma ação recusada.</summary>
	public const string ChaveDaMensagemDeErro = "MensagemDeErro";

	/// <summary>Renderiza o aviso, ou nada quando não há mensagem pendente.</summary>
	public IViewComponentResult Invoke()
	{
		// Ler consome: a mensagem vale para a página que veio do redirect, e não sobrevive
		// até a próxima navegação. As duas chaves são lidas sempre, para que nenhuma sobre.
		var erro = TempData[ChaveDaMensagemDeErro] as string;
		var sucesso = TempData[ChaveDaMensagem] as string;

		if (!string.IsNullOrWhiteSpace(erro))
		{
			return View(new ToastModel(erro, ToastVariante.Erro));
		}

		return string.IsNullOrWhiteSpace(sucesso)
			? Content(string.Empty)
			: View(new ToastModel(sucesso));
	}
}
```

- [ ] **Step 3: O Inventário passa a usar a variante de erro**

Em `InventarioController.cs`, substitua o `<summary>` e o corpo de `AposMudarStatus` (e remova a "limitação conhecida" do comentário):

```csharp
	private IActionResult AposMudarStatus(Guid id, string? mensagemDeErro)
	{
		if (mensagemDeErro is null)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = "Item atualizado.";
		}
		else
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = mensagemDeErro;
		}

		return RedirectToAction(nameof(Details), new { id });
	}
```

Pesquise `grep -rn "AposMudarStatus\|Item atualizado" tests` — se algum teste assumia o texto de erro no toast de sucesso, ajuste-o para a nova chave.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test` → PASS, 0 avisos.

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Web/ViewComponents/FeedbackViewComponent.cs src/Secco.Intranet.Web/Controllers/InventarioController.cs tests/Secco.Intranet.Tests/Unit/FeedbackViewComponentTests.cs
git commit -m "feat(feedback): produtor de toast de erro no core

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 4: Application — tipos, porta `IGestaoDeAcesso`, classificação de perfil, erros e verbos

Só contratos e uma função pura; sem I/O. Deixa a base sobre a qual as tarefas 5–8 trabalham em paralelo de ideia, mas em sequência de execução.

**Files:**
- Create: `src/Secco.Intranet.Application/Acesso/AcessoDtos.cs`
- Create: `src/Secco.Intranet.Application/Acesso/ClassificacaoDePerfil.cs`
- Create: `src/Secco.Intranet.Application/Acesso/IGestaoDeAcesso.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs`
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`
- Create: `tests/Secco.Intranet.Tests/Support/DublesDeAcesso.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/ClassificacaoDePerfilTests.cs`

**Interfaces:**
- Produces (todos em `Secco.Intranet.Application.Acesso`):
  - `enum TipoDePerfil { Comum, Setor, Produto }`, `enum SituacaoDoUsuario { Desconhecida, Ativo, Desativado, Bloqueado }`, `enum PapelNoSetor { Usuario, Administrador }`
  - `record PerfilDto(string Nome, int TotalDePermissoes, TipoDePerfil Tipo, bool Reservado)`
  - `record PerfilDetalheDto(string Nome, IReadOnlyList<string> Permissoes, bool Reservado, int TotalDeMembros, TipoDePerfil Tipo)`
  - `record MembroDoPerfilDto(Guid UsuarioId, string Email, SituacaoDoUsuario Situacao)`
  - `record PaginaDeMembros(IReadOnlyList<MembroDoPerfilDto> Itens, int Pagina, int TotalDePaginas, long Total)`
  - `record UsuarioDto(Guid Id, string Email, SituacaoDoUsuario Situacao, IReadOnlyList<string> Perfis)`
  - `record UsuarioDetalheDto(Guid Id, string Email, SituacaoDoUsuario Situacao, DateTimeOffset? BloqueadoAte, IReadOnlyList<string> Perfis, IReadOnlyList<string> Permissoes, IReadOnlyList<string> LoginsExternos, bool DoisFatoresAtivo)`
  - `static class ClassificacaoDePerfil`: `IntranetAdmin`, `InventarioAdmin`, `PerfisDoProduto`, `Tipo(string)`, `EhReservado(string)`, `EhDoProduto(string)`, `DoSetor(string) : (string Slug, PapelNoSetor Papel)?`, `NomeValido(string?)`
  - `interface IGestaoDeAcesso` (assinaturas no Step 4)
  - `IntranetErrors.Acesso.*` (lista no Step 5); `VerbosDeAuditoria.Acesso*`; `RecursosDeAuditoria.Acesso`
  - Dublês de teste em `Secco.Intranet.Tests.Support`: `GestaoDeAcessoFalsa`, `TrilhaDeAcessoFalsa`, `AtorDeAcessoFalso`

- [ ] **Step 1: Testes de `ClassificacaoDePerfil` (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/ClassificacaoDePerfilTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ClassificacaoDePerfilTests
{
	[Theory]
	[InlineData("intranet-admin", TipoDePerfil.Produto)]
	[InlineData("Intranet-Admin", TipoDePerfil.Produto)]
	[InlineData("inventario-admin", TipoDePerfil.Produto)]
	[InlineData("financeiro-admin", TipoDePerfil.Setor)]
	[InlineData("financeiro-user", TipoDePerfil.Setor)]
	[InlineData("gerente-de-compras", TipoDePerfil.Comum)]
	[InlineData("-admin", TipoDePerfil.Comum)]
	[InlineData("all-users", TipoDePerfil.Comum)]
	public void Tipo_ClassificaPeloNome(string nome, TipoDePerfil esperado)
	{
		ClassificacaoDePerfil.Tipo(nome).Should().Be(esperado);
	}

	[Theory]
	[InlineData("installation-operator")]
	[InlineData("Installation-Operator")]
	[InlineData("platform-operator")]
	[InlineData("installation-log-reader")]
	[InlineData("installation-auditor")]
	public void EhReservado_ReconheceOsReservadosDaPlataforma(string nome)
	{
		ClassificacaoDePerfil.EhReservado(nome).Should().BeTrue();
	}

	[Fact]
	public void EhReservado_PerfilComum_Falso()
	{
		ClassificacaoDePerfil.EhReservado("gerente-de-compras").Should().BeFalse();
	}

	[Fact]
	public void DoSetor_ExtraiSlugEPapel()
	{
		ClassificacaoDePerfil.DoSetor("Marketing-Admin").Should().Be(("Marketing", PapelNoSetor.Administrador));
		ClassificacaoDePerfil.DoSetor("rh-user").Should().Be(("rh", PapelNoSetor.Usuario));
	}

	[Theory]
	[InlineData("gerente-de-compras")]
	[InlineData("intranet-admin")]
	[InlineData("-user")]
	public void DoSetor_NaoEDeSetor_Nulo(string nome)
	{
		ClassificacaoDePerfil.DoSetor(nome).Should().BeNull();
	}

	[Theory]
	[InlineData("gerente-de-compras", true)]
	[InlineData("a", true)]
	[InlineData("a.b_c-d", true)]
	[InlineData("Equipe1", true)]
	[InlineData("gerente de compras", false)]
	[InlineData("-x", false)]
	[InlineData("x-", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	[InlineData("ação", false)]
	public void NomeValido_SegueARegraDaPlataforma(string? nome, bool esperado)
	{
		ClassificacaoDePerfil.NomeValido(nome).Should().Be(esperado);
	}

	[Fact]
	public void NomeValido_Com101Caracteres_Falso()
	{
		ClassificacaoDePerfil.NomeValido(new string('a', 101)).Should().BeFalse();
		ClassificacaoDePerfil.NomeValido(new string('a', 100)).Should().BeTrue();
	}
}
```

- [ ] **Step 2: Escrever os DTOs**

```csharp
// src/Secco.Intranet.Application/Acesso/AcessoDtos.cs
namespace Secco.Intranet.Application.Acesso;

/// <summary>De onde o perfil vem, pelo nome — a plataforma não guarda essa distinção.</summary>
public enum TipoDePerfil
{
	/// <summary>Criado à mão pelo <c>intranet-admin</c> (ex.: <c>gerente-de-compras</c>).</summary>
	Comum = 0,

	/// <summary>Role de setor: <c>{slug}-admin</c> ou <c>{slug}-user</c> (ADR-0001).</summary>
	Setor = 1,

	/// <summary>Perfil fixo do produto: <c>intranet-admin</c> ou <c>inventario-admin</c>.</summary>
	Produto = 2,
}

/// <summary>Situação da conta, como o SecureGate a informa.</summary>
public enum SituacaoDoUsuario
{
	/// <summary>Valor que a Intranet não reconhece.</summary>
	Desconhecida = 0,

	/// <summary>Conta ativa.</summary>
	Ativo = 1,

	/// <summary>Conta desativada.</summary>
	Desativado = 2,

	/// <summary>Conta bloqueada por tentativas de login.</summary>
	Bloqueado = 3,
}

/// <summary>Papel de uma Role de setor.</summary>
public enum PapelNoSetor
{
	/// <summary><c>{slug}-user</c>: só lê.</summary>
	Usuario = 0,

	/// <summary><c>{slug}-admin</c>: altera o próprio setor.</summary>
	Administrador = 1,
}

/// <summary>Perfil na listagem.</summary>
/// <param name="Nome">Nome do perfil (a Role no SecureGate).</param>
/// <param name="TotalDePermissoes">Quantas permissões o perfil carrega.</param>
/// <param name="Tipo">Classificação pelo nome.</param>
/// <param name="Reservado">Se é reservado da plataforma.</param>
public sealed record PerfilDto(string Nome, int TotalDePermissoes, TipoDePerfil Tipo, bool Reservado);

/// <summary>Detalhe de um perfil.</summary>
/// <param name="Nome">Nome do perfil.</param>
/// <param name="Permissoes">Permissões <c>recurso:acao</c>, somente leitura neste corte.</param>
/// <param name="Reservado">Se a plataforma o marca como reservado.</param>
/// <param name="TotalDeMembros">Quantos usuários o têm.</param>
/// <param name="Tipo">Classificação pelo nome.</param>
public sealed record PerfilDetalheDto(
	string Nome, IReadOnlyList<string> Permissoes, bool Reservado, int TotalDeMembros, TipoDePerfil Tipo);

/// <summary>Um membro de um perfil.</summary>
/// <param name="UsuarioId">Identificador do usuário.</param>
/// <param name="Email">E-mail — a identidade exibível, o SecureGate não guarda nome.</param>
/// <param name="Situacao">Situação da conta.</param>
public sealed record MembroDoPerfilDto(Guid UsuarioId, string Email, SituacaoDoUsuario Situacao);

/// <summary>Página de membros de um perfil.</summary>
/// <param name="Itens">Membros da página.</param>
/// <param name="Pagina">Página atual (1-based).</param>
/// <param name="TotalDePaginas">Total de páginas.</param>
/// <param name="Total">Total de membros.</param>
public sealed record PaginaDeMembros(
	IReadOnlyList<MembroDoPerfilDto> Itens, int Pagina, int TotalDePaginas, long Total);

/// <summary>Usuário na listagem.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Situacao">Situação da conta.</param>
/// <param name="Perfis">Perfis atribuídos.</param>
public sealed record UsuarioDto(Guid Id, string Email, SituacaoDoUsuario Situacao, IReadOnlyList<string> Perfis);

/// <summary>Detalhe de um usuário.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Situacao">Situação da conta.</param>
/// <param name="BloqueadoAte">Fim do bloqueio, quando bloqueado.</param>
/// <param name="Perfis">Perfis atribuídos.</param>
/// <param name="Permissoes">Permissões efetivas, resolvidas pela plataforma.</param>
/// <param name="LoginsExternos">Provedores de login externo vinculados.</param>
/// <param name="DoisFatoresAtivo">Se o segundo fator está ligado.</param>
public sealed record UsuarioDetalheDto(
	Guid Id,
	string Email,
	SituacaoDoUsuario Situacao,
	DateTimeOffset? BloqueadoAte,
	IReadOnlyList<string> Perfis,
	IReadOnlyList<string> Permissoes,
	IReadOnlyList<string> LoginsExternos,
	bool DoisFatoresAtivo);
```

- [ ] **Step 3: Escrever `ClassificacaoDePerfil`**

```csharp
// src/Secco.Intranet.Application/Acesso/ClassificacaoDePerfil.cs
using System.Text.RegularExpressions;

namespace Secco.Intranet.Application.Acesso;

/// <summary>
/// Regras puras sobre nome de perfil. A plataforma só conhece o nome da Role; a distinção
/// entre perfil de setor, do produto e comum é convenção desta Intranet (ADR-0001, ADR-0008).
/// </summary>
public static partial class ClassificacaoDePerfil
{
	/// <summary>Superusuário da instalação (o mesmo valor de <c>AcessoAdministrativo.RoleIntranetAdmin</c> no Web).</summary>
	public const string IntranetAdmin = "intranet-admin";

	/// <summary>Administrador do Inventário.</summary>
	public const string InventarioAdmin = "inventario-admin";

	/// <summary>Sufixo de Role de administração de setor.</summary>
	public const string SufixoAdmin = "-admin";

	/// <summary>Sufixo de Role de leitura de setor.</summary>
	public const string SufixoUsuario = "-user";

	/// <summary>Tamanho máximo do nome de um perfil, igual ao da plataforma.</summary>
	public const int TamanhoMaximoDoNome = 100;

	/// <summary>Perfis que o produto conhece e oferece criar quando faltam.</summary>
	public static readonly IReadOnlyList<string> PerfisDoProduto = [IntranetAdmin, InventarioAdmin];

	/// <summary>
	/// Reservados da plataforma — espelho de <c>RoleInputRules.ReservedNames</c> do SecureGate.
	/// A recusa definitiva vem do <c>IsReserved</c> do <c>GetRole</c>; este conjunto só evita a
	/// chamada e esconde o perfil dos seletores.
	/// </summary>
	private static readonly HashSet<string> Reservados = new(StringComparer.OrdinalIgnoreCase)
	{
		"installation-operator",
		"platform-operator",
		"installation-log-reader",
		"installation-auditor",
	};

	[GeneratedRegex("^[a-zA-Z0-9](?:[a-zA-Z0-9._-]*[a-zA-Z0-9])?$", RegexOptions.CultureInvariant)]
	private static partial Regex FormatoDoNome();

	/// <summary>Indica se o perfil é do produto (<c>intranet-admin</c>, <c>inventario-admin</c>).</summary>
	/// <param name="nome">Nome do perfil.</param>
	public static bool EhDoProduto(string nome) =>
		PerfisDoProduto.Any(perfil => string.Equals(perfil, nome, StringComparison.OrdinalIgnoreCase));

	/// <summary>Indica se o perfil é reservado da plataforma.</summary>
	/// <param name="nome">Nome do perfil.</param>
	public static bool EhReservado(string nome) => Reservados.Contains(nome);

	/// <summary>Extrai slug e papel de uma Role de setor; <c>null</c> se não for uma.</summary>
	/// <param name="nome">Nome do perfil.</param>
	public static (string Slug, PapelNoSetor Papel)? DoSetor(string nome)
	{
		if (EhDoProduto(nome))
		{
			return null;
		}

		if (nome.EndsWith(SufixoAdmin, StringComparison.OrdinalIgnoreCase) && nome.Length > SufixoAdmin.Length)
		{
			return (nome[..^SufixoAdmin.Length], PapelNoSetor.Administrador);
		}

		if (nome.EndsWith(SufixoUsuario, StringComparison.OrdinalIgnoreCase) && nome.Length > SufixoUsuario.Length)
		{
			return (nome[..^SufixoUsuario.Length], PapelNoSetor.Usuario);
		}

		return null;
	}

	/// <summary>Classifica o perfil pelo nome.</summary>
	/// <param name="nome">Nome do perfil.</param>
	public static TipoDePerfil Tipo(string nome) =>
		EhDoProduto(nome) ? TipoDePerfil.Produto
		: DoSetor(nome) is not null ? TipoDePerfil.Setor
		: TipoDePerfil.Comum;

	/// <summary>Valida o nome pela regra da plataforma: letras, dígitos, <c>.</c>, <c>_</c> e <c>-</c>, sem espaço.</summary>
	/// <param name="nome">Nome candidato, já aparado.</param>
	public static bool NomeValido(string? nome) =>
		!string.IsNullOrEmpty(nome) && nome.Length <= TamanhoMaximoDoNome && FormatoDoNome().IsMatch(nome);
}
```

- [ ] **Step 4: A porta**

```csharp
// src/Secco.Intranet.Application/Acesso/IGestaoDeAcesso.cs
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>
/// Porta da gestão de acesso do tenant atual: perfis (Roles) e usuários, no SecureGate
/// (ADR-0006: a Intranet não guarda identidade). Toda falha de infraestrutura volta como
/// <see cref="Result"/> — diferente da auditoria, aqui quem opera precisa saber que a ação
/// não aconteceu, então não há falha aberta.
/// </summary>
public interface IGestaoDeAcesso
{
	/// <summary>Lista os perfis do tenant.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<PerfilDto>>> ListarPerfisAsync(CancellationToken cancellationToken = default);

	/// <summary>Detalhe de um perfil.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<PerfilDetalheDto>> ObterPerfilAsync(string nome, CancellationToken cancellationToken = default);

	/// <summary>Uma página de membros de um perfil (100 por página).</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="pagina">Página (1-based).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<PaginaDeMembros>> ListarMembrosAsync(string nome, int pagina, CancellationToken cancellationToken = default);

	/// <summary>Lista todos os usuários do tenant (a API não pagina; busca e paginação são da tela).</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(CancellationToken cancellationToken = default);

	/// <summary>Detalhe de um usuário.</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<UsuarioDetalheDto>> ObterUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Cria um perfil.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> CriarPerfilAsync(string nome, CancellationToken cancellationToken = default);

	/// <summary>Exclui um perfil sem membros.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> ExcluirPerfilAsync(string nome, CancellationToken cancellationToken = default);

	/// <summary>Atribui um perfil a um usuário (idempotente).</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> AtribuirPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default);

	/// <summary>Retira um perfil de um usuário (idempotente).</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> RetirarPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default);

	/// <summary>Desativa a conta de um usuário.</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> DesativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Reativa a conta de um usuário.</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> ReativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Encerra as sessões de um usuário (efeito nos produtos em até um TTL de cache, ADR-0032 da plataforma).</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> EncerrarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Erros e verbos**

Em `IntranetErrors.cs`, acrescente ao fim da classe `IntranetErrors` (antes do `}` final) o grupo:

```csharp
	/// <summary>Erros da gestão de acesso (perfis e usuários).</summary>
	public static class Acesso
	{
		/// <summary>SecureGate não configurado neste ambiente.</summary>
		public static readonly Error NaoConfigurado =
			Error.Unavailable(
				"Intranet.Acesso.NaoConfigurado",
				"O SecureGate não está configurado neste ambiente, então a gestão de acesso não está disponível.");

		/// <summary>SecureGate fora do ar, lento ou recusando — sem detalhe interno (ADR-0020).</summary>
		public static readonly Error Indisponivel =
			Error.Unavailable(
				"Intranet.Acesso.Indisponivel",
				"Não foi possível falar com o SecureGate agora. Tente novamente em instantes.");

		/// <summary>Perfil não informado.</summary>
		public static readonly Error PerfilRequired =
			Error.Validation("Intranet.Acesso.PerfilRequired", "Informe o perfil.");

		/// <summary>Nome fora da regra da plataforma.</summary>
		public static readonly Error PerfilNomeInvalido =
			Error.Validation(
				"Intranet.Acesso.PerfilNomeInvalido",
				"O nome do perfil aceita letras, dígitos, ponto, sublinhado e hífen, sem espaços, com até 100 caracteres.");

		/// <summary>Perfil inexistente no tenant.</summary>
		public static readonly Error PerfilNaoEncontrado =
			Error.NotFound("Intranet.Acesso.PerfilNaoEncontrado", "Perfil não encontrado.");

		/// <summary>Usuário inexistente no tenant.</summary>
		public static readonly Error UsuarioNaoEncontrado =
			Error.NotFound("Intranet.Acesso.UsuarioNaoEncontrado", "Usuário não encontrado.");

		/// <summary>Já existe um perfil com esse nome.</summary>
		public static readonly Error PerfilJaExiste =
			Error.Conflict("Intranet.Acesso.PerfilJaExiste", "Já existe um perfil com esse nome.");

		/// <summary>Perfil reservado da plataforma.</summary>
		public static readonly Error PerfilReservado =
			Error.Validation(
				"Intranet.Acesso.PerfilReservado",
				"Perfis reservados da plataforma não podem ser criados nem atribuídos por aqui.");

		/// <summary>Perfil do produto ou de setor não se exclui.</summary>
		public static readonly Error PerfilProtegido =
			Error.Validation(
				"Intranet.Acesso.PerfilProtegido",
				"Este perfil é do produto ou de um setor e não pode ser excluído. Para tirar um setor de uso, desative o setor.");

		/// <summary>Perfil com membros não se exclui.</summary>
		public static readonly Error PerfilComMembros =
			Error.Conflict("Intranet.Acesso.PerfilComMembros", "O perfil ainda tem membros. Retire todos antes de excluir.");

		/// <summary>Deixaria a instalação sem <c>intranet-admin</c> ativo.</summary>
		public static readonly Error UltimoIntranetAdmin =
			Error.Validation(
				"Intranet.Acesso.UltimoIntranetAdmin",
				"Este é o último intranet-admin ativo. Atribua o perfil a outra pessoa antes.");

		/// <summary>O admin tentou retirar o próprio <c>intranet-admin</c>.</summary>
		public static readonly Error AutoRemocaoDeIntranetAdmin =
			Error.Validation(
				"Intranet.Acesso.AutoRemocaoDeIntranetAdmin",
				"Você não pode retirar o seu próprio perfil intranet-admin. Peça a outro intranet-admin.");

		/// <summary>O admin tentou desativar a própria conta.</summary>
		public static readonly Error AutoDesativacao =
			Error.Validation(
				"Intranet.Acesso.AutoDesativacao",
				"Você não pode desativar a sua própria conta.");

		/// <summary>A plataforma recusou a desativação (ex.: último operador da instalação).</summary>
		public static readonly Error DesativacaoRecusada =
			Error.Conflict(
				"Intranet.Acesso.DesativacaoRecusada",
				"A plataforma recusou a desativação (por exemplo, o último operador da instalação).");
	}
```

Em `VerbosDeAuditoria.cs`, dentro de `VerbosDeAuditoria`:

```csharp
	/// <summary>Perfil criado no SecureGate.</summary>
	public const string AcessoPerfilCriar = "acesso.perfil-criar";

	/// <summary>Perfil excluído.</summary>
	public const string AcessoPerfilExcluir = "acesso.perfil-excluir";

	/// <summary>Perfil atribuído a um usuário.</summary>
	public const string AcessoPerfilAtribuir = "acesso.perfil-atribuir";

	/// <summary>Perfil retirado de um usuário.</summary>
	public const string AcessoPerfilRetirar = "acesso.perfil-retirar";

	/// <summary>Conta de usuário desativada.</summary>
	public const string AcessoUsuarioDesativar = "acesso.usuario-desativar";

	/// <summary>Conta de usuário reativada.</summary>
	public const string AcessoUsuarioReativar = "acesso.usuario-reativar";

	/// <summary>Sessões de um usuário encerradas.</summary>
	public const string AcessoSessoesEncerrar = "acesso.sessoes-encerrar";
```

e dentro de `RecursosDeAuditoria`:

```csharp
	/// <summary>Perfil ou usuário do SecureGate, na gestão de acesso.</summary>
	public const string Acesso = "acesso";
```

- [ ] **Step 5b: Dublês de teste compartilhados**

```csharp
// tests/Secco.Intranet.Tests/Support/DublesDeAcesso.cs
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Tests.Support;

/// <summary>Trilha que guarda o que foi registrado.</summary>
public sealed class TrilhaDeAcessoFalsa : ITrilhaDeAuditoria
{
	/// <summary>Registros na ordem em que chegaram.</summary>
	public List<RegistroDeAuditoria> Registros { get; } = [];

	/// <inheritdoc />
	public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
	{
		Registros.Add(registro);

		return Task.CompletedTask;
	}
}

/// <summary>Ator fixo (ou ausente) para os handlers que comparam "quem agiu" com o alvo.</summary>
/// <param name="usuarioId">Id do ator; <c>null</c> simula o modo aberto de DEV.</param>
public sealed class AtorDeAcessoFalso(Guid? usuarioId) : IAtorAtual
{
	/// <inheritdoc />
	public AtorDaAcao? Atual() =>
		usuarioId is null ? null : new AtorDaAcao(usuarioId.Value.ToString(), "admin@exemplo.com");
}

/// <summary>
/// Gestão de acesso em memória. As chamadas de escrita ficam em <see cref="Chamadas"/>; os
/// membros de um perfil saem dos <see cref="Usuarios"/> que o têm, então atribuir e retirar
/// mudam o que as leituras seguintes devolvem.
/// </summary>
public sealed class GestaoDeAcessoFalsa : IGestaoDeAcesso
{
	/// <summary>Perfis existentes.</summary>
	public List<PerfilDetalheDto> Perfis { get; } = [];

	/// <summary>Usuários existentes.</summary>
	public List<UsuarioDetalheDto> Usuarios { get; } = [];

	/// <summary>Escritas recebidas, no formato <c>acao:alvo</c>.</summary>
	public List<string> Chamadas { get; } = [];

	/// <summary>Quando não nulo, toda escrita falha com este erro.</summary>
	public Error? FalharCom { get; set; }

	/// <summary>Tamanho da página de membros — reduza para exercitar a paginação.</summary>
	public int TamanhoDaPagina { get; set; } = 100;

	/// <summary>Acrescenta um perfil.</summary>
	public GestaoDeAcessoFalsa ComPerfil(string nome, bool reservado = false, params string[] permissoes)
	{
		Perfis.Add(new PerfilDetalheDto(nome, permissoes, reservado, 0, ClassificacaoDePerfil.Tipo(nome)));

		return this;
	}

	/// <summary>Acrescenta um usuário.</summary>
	public GestaoDeAcessoFalsa ComUsuario(
		Guid id, string email, SituacaoDoUsuario situacao = SituacaoDoUsuario.Ativo, params string[] perfis)
	{
		Usuarios.Add(new UsuarioDetalheDto(id, email, situacao, null, perfis, [], [], false));

		return this;
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDto>>> ListarPerfisAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<PerfilDto> lista =
			[.. Perfis.Select(p => new PerfilDto(p.Nome, p.Permissoes.Count, p.Tipo, p.Reservado))];

		return Task.FromResult(Result.Success(lista));
	}

	/// <inheritdoc />
	public Task<Result<PerfilDetalheDto>> ObterPerfilAsync(string nome, CancellationToken cancellationToken = default)
	{
		var perfil = Perfis.FirstOrDefault(p => string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase));

		return Task.FromResult(perfil is null
			? Result.Failure<PerfilDetalheDto>(IntranetErrors.Acesso.PerfilNaoEncontrado)
			: Result.Success(perfil with { TotalDeMembros = MembrosDe(nome).Count }));
	}

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(
		string nome, int pagina, CancellationToken cancellationToken = default)
	{
		var todos = MembrosDe(nome);
		var totalDePaginas = todos.Count == 0 ? 0 : (int)Math.Ceiling(todos.Count / (double)TamanhoDaPagina);
		IReadOnlyList<MembroDoPerfilDto> itens = [.. todos.Skip((pagina - 1) * TamanhoDaPagina).Take(TamanhoDaPagina)];

		return Task.FromResult(Result.Success(new PaginaDeMembros(itens, pagina, totalDePaginas, todos.Count)));
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<UsuarioDto> lista = [.. Usuarios.Select(u => new UsuarioDto(u.Id, u.Email, u.Situacao, u.Perfis))];

		return Task.FromResult(Result.Success(lista));
	}

	/// <inheritdoc />
	public Task<Result<UsuarioDetalheDto>> ObterUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = Usuarios.FirstOrDefault(u => u.Id == usuarioId);

		return Task.FromResult(usuario is null
			? Result.Failure<UsuarioDetalheDto>(IntranetErrors.Acesso.UsuarioNaoEncontrado)
			: Result.Success(usuario));
	}

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		Escrever($"perfil-criar:{nome}", () => ComPerfil(nome));

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		Escrever($"perfil-excluir:{nome}", () => Perfis.RemoveAll(p => string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase)));

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Escrever($"perfil-atribuir:{usuarioId}:{perfil}", () => Alterar(usuarioId, u => u with { Perfis = [.. u.Perfis, perfil] }));

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Escrever($"perfil-retirar:{usuarioId}:{perfil}", () => Alterar(usuarioId, u => u with
		{
			Perfis = [.. u.Perfis.Where(p => !string.Equals(p, perfil, StringComparison.OrdinalIgnoreCase))],
		}));

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Escrever($"usuario-desativar:{usuarioId}", () => Alterar(usuarioId, u => u with { Situacao = SituacaoDoUsuario.Desativado }));

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Escrever($"usuario-reativar:{usuarioId}", () => Alterar(usuarioId, u => u with { Situacao = SituacaoDoUsuario.Ativo }));

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Escrever($"sessoes-encerrar:{usuarioId}", () => { });

	private List<MembroDoPerfilDto> MembrosDe(string nome) =>
		[.. Usuarios
			.Where(u => u.Perfis.Any(p => string.Equals(p, nome, StringComparison.OrdinalIgnoreCase)))
			.Select(u => new MembroDoPerfilDto(u.Id, u.Email, u.Situacao))];

	private void Alterar(Guid usuarioId, Func<UsuarioDetalheDto, UsuarioDetalheDto> mudanca)
	{
		var indice = Usuarios.FindIndex(u => u.Id == usuarioId);

		if (indice >= 0)
		{
			Usuarios[indice] = mudanca(Usuarios[indice]);
		}
	}

	private Task<Result> Escrever(string chamada, Action efeito)
	{
		Chamadas.Add(chamada);

		if (FalharCom is not null)
		{
			return Task.FromResult(Result.Failure(FalharCom));
		}

		efeito();

		return Task.FromResult(Result.Success());
	}
}
```

- [ ] **Step 6: Rodar**

Run: `dotnet test --filter ClassificacaoDePerfilTests` → PASS (build sem avisos; o `GeneratedRegex` exige a classe `partial`, que já é).

- [ ] **Step 7: Commit**

```bash
git add src/Secco.Intranet.Application/Acesso src/Secco.Intranet.Application/IntranetErrors.cs src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs tests/Secco.Intranet.Tests/Support tests/Secco.Intranet.Tests/Unit/ClassificacaoDePerfilTests.cs
git commit -m "feat(acesso): porta IGestaoDeAcesso, classificacao de perfil, erros e verbos

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 5: Application — handlers de leitura

**Files:**
- Create: `src/Secco.Intranet.Application/Acesso/ListarPerfisHandler.cs`
- Create: `src/Secco.Intranet.Application/Acesso/ObterPerfilHandler.cs`
- Create: `src/Secco.Intranet.Application/Acesso/ListarUsuariosHandler.cs`
- Create: `src/Secco.Intranet.Application/Acesso/ObterUsuarioHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/LeituraDeAcessoHandlersTests.cs`

**Interfaces:**
- Consumes: `IGestaoDeAcesso`, DTOs e `ClassificacaoDePerfil` (Task 4).
- Produces:
  - `ListarPerfisHandler.HandleAsync(ct) : Task<Result<PerfisDaTelaDto>>`; `record PerfisDaTelaDto(IReadOnlyList<PerfilDto> Perfis, IReadOnlyList<string> PerfisDoProdutoFaltando)`
  - `ObterPerfilHandler.HandleAsync(ObterPerfilQuery, ct) : Task<Result<PerfilTelaDto>>`; `record ObterPerfilQuery(string Nome, int Pagina)`; `record PerfilTelaDto(PerfilDetalheDto Perfil, PaginaDeMembros Membros, IReadOnlyList<UsuarioDto> Candidatos)` — `Candidatos` = usuários **ativos** que ainda não têm o perfil (vazio se o perfil é reservado)
  - `ListarUsuariosHandler.HandleAsync(ListarUsuariosQuery, ct) : Task<Result<PagedResult<UsuarioDto>>>`; `record ListarUsuariosQuery(string? Busca, int Pagina)`; 20 por página, ordenado por e-mail, busca por trecho do e-mail sem diferenciar caixa
  - `ObterUsuarioHandler.HandleAsync(Guid, ct) : Task<Result<UsuarioTelaDto>>`; `record PerfilNoSetorDto(string Slug, IReadOnlyList<PapelNoSetor> Papeis)`; `record UsuarioTelaDto(UsuarioDetalheDto Usuario, IReadOnlyList<PerfilNoSetorDto> Setores, IReadOnlyList<string> OutrosPerfis, IReadOnlyList<string> SlugsDeSetorDisponiveis, IReadOnlyList<string> OutrosPerfisAtribuiveis)`

- [ ] **Step 1: Testes (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/LeituraDeAcessoHandlersTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LeituraDeAcessoHandlersTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();

	[Fact]
	public async Task ListarPerfis_OrdenaPorNome_EApontaOsPerfisDoProdutoQueFaltam()
	{
		var gestao = new GestaoDeAcessoFalsa().ComPerfil("rh-user").ComPerfil("intranet-admin").ComPerfil("a-comum");

		var resultado = await new ListarPerfisHandler(gestao).HandleAsync();

		resultado.Value.Perfis.Select(p => p.Nome).Should().Equal("a-comum", "intranet-admin", "rh-user");
		resultado.Value.PerfisDoProdutoFaltando.Should().Equal("inventario-admin");
	}

	[Fact]
	public async Task ListarPerfis_ProdutoFaltandoIgnoraCaixa()
	{
		var gestao = new GestaoDeAcessoFalsa().ComPerfil("Intranet-Admin").ComPerfil("INVENTARIO-ADMIN");

		var resultado = await new ListarPerfisHandler(gestao).HandleAsync();

		resultado.Value.PerfisDoProdutoFaltando.Should().BeEmpty();
	}

	[Fact]
	public async Task ObterPerfil_TrazMembrosECandidatosAtivosSemOPerfil()
	{
		var gestao = new GestaoDeAcessoFalsa()
			.ComPerfil("gerente-de-compras")
			.ComUsuario(Ana, "ana@x.com", SituacaoDoUsuario.Ativo, "gerente-de-compras")
			.ComUsuario(Bruno, "bruno@x.com", SituacaoDoUsuario.Ativo)
			.ComUsuario(Carla, "carla@x.com", SituacaoDoUsuario.Desativado);

		var resultado = await new ObterPerfilHandler(gestao).HandleAsync(new ObterPerfilQuery("gerente-de-compras", 1));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Membros.Itens.Select(m => m.Email).Should().Equal("ana@x.com");
		resultado.Value.Candidatos.Select(c => c.Email).Should().Equal("bruno@x.com");
	}

	[Fact]
	public async Task ObterPerfil_Reservado_NaoOfereceCandidatos()
	{
		var gestao = new GestaoDeAcessoFalsa()
			.ComPerfil("installation-operator", reservado: true)
			.ComUsuario(Bruno, "bruno@x.com");

		var resultado = await new ObterPerfilHandler(gestao).HandleAsync(new ObterPerfilQuery("installation-operator", 1));

		resultado.Value.Candidatos.Should().BeEmpty();
	}

	[Fact]
	public async Task ObterPerfil_Inexistente_DevolveNotFound()
	{
		var resultado = await new ObterPerfilHandler(new GestaoDeAcessoFalsa()).HandleAsync(new ObterPerfilQuery("nao-existe", 1));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado);
	}

	[Fact]
	public async Task ListarUsuarios_BuscaPorEmailSemDiferenciarCaixa_EOrdena()
	{
		var gestao = new GestaoDeAcessoFalsa()
			.ComUsuario(Bruno, "bruno@x.com")
			.ComUsuario(Ana, "ANA@x.com")
			.ComUsuario(Carla, "carla@y.com");

		var resultado = await new ListarUsuariosHandler(gestao).HandleAsync(new ListarUsuariosQuery("@x", 1));

		resultado.Value.Items.Select(u => u.Email).Should().Equal("ANA@x.com", "bruno@x.com");
		resultado.Value.TotalCount.Should().Be(2);
	}

	[Fact]
	public async Task ListarUsuarios_PaginaAlemDoFim_DevolveListaVaziaSemQuebrar()
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Ana, "ana@x.com");

		var resultado = await new ListarUsuariosHandler(gestao).HandleAsync(new ListarUsuariosQuery(null, 99));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Items.Should().BeEmpty();
		resultado.Value.TotalCount.Should().Be(1);
	}

	[Fact]
	public async Task ListarUsuarios_UsuarioSemEmail_NaoQuebraABusca()
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Ana, string.Empty).ComUsuario(Bruno, "bruno@x.com");

		var resultado = await new ListarUsuariosHandler(gestao).HandleAsync(new ListarUsuariosQuery("bruno", 1));

		resultado.Value.Items.Should().ContainSingle();
	}

	[Fact]
	public async Task ObterUsuario_AgrupaOsPerfisPorSetor_EOfereceSoOQueAindaNaoTem()
	{
		var gestao = new GestaoDeAcessoFalsa()
			.ComPerfil("marketing-admin").ComPerfil("marketing-user").ComPerfil("rh-user").ComPerfil("ti-admin")
			.ComPerfil("gerente-de-compras").ComPerfil("all-users").ComPerfil("installation-operator", reservado: true)
			.ComUsuario(Ana, "ana@x.com", SituacaoDoUsuario.Ativo, "marketing-admin", "marketing-user", "rh-user", "gerente-de-compras");

		var resultado = await new ObterUsuarioHandler(gestao).HandleAsync(Ana);

		var tela = resultado.Value;
		tela.Setores.Should().HaveCount(2);
		tela.Setores.Single(s => s.Slug == "marketing").Papeis
			.Should().BeEquivalentTo([PapelNoSetor.Administrador, PapelNoSetor.Usuario]);
		tela.Setores.Single(s => s.Slug == "rh").Papeis.Should().Equal(PapelNoSetor.Usuario);
		tela.OutrosPerfis.Should().Equal("gerente-de-compras");
		tela.SlugsDeSetorDisponiveis.Should().BeEquivalentTo("marketing", "rh", "ti");
		tela.OutrosPerfisAtribuiveis.Should().Equal("all-users");
	}

	[Fact]
	public async Task ObterUsuario_Inexistente_DevolveNotFound()
	{
		var resultado = await new ObterUsuarioHandler(new GestaoDeAcessoFalsa()).HandleAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}
}
```

Run: `dotnet test --filter LeituraDeAcessoHandlersTests` → falha de compilação.

- [ ] **Step 2: Implementar os quatro handlers**

```csharp
// src/Secco.Intranet.Application/Acesso/ListarPerfisHandler.cs
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Perfis da aba "Perfis".</summary>
/// <param name="Perfis">Perfis existentes, por nome.</param>
/// <param name="PerfisDoProdutoFaltando">Perfis do produto que ainda não existem no tenant e podem ser criados.</param>
public sealed record PerfisDaTelaDto(IReadOnlyList<PerfilDto> Perfis, IReadOnlyList<string> PerfisDoProdutoFaltando);

/// <summary>Lista os perfis e aponta quais perfis do produto faltam.</summary>
public sealed class ListarPerfisHandler(IGestaoDeAcesso gestao)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PerfisDaTelaDto>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var lidos = await gestao.ListarPerfisAsync(cancellationToken).ConfigureAwait(false);

		if (lidos.IsFailure)
		{
			return Result.Failure<PerfisDaTelaDto>(lidos.Error);
		}

		IReadOnlyList<PerfilDto> perfis = [.. lidos.Value.OrderBy(perfil => perfil.Nome, StringComparer.OrdinalIgnoreCase)];

		IReadOnlyList<string> faltando =
			[.. ClassificacaoDePerfil.PerfisDoProduto.Where(produto =>
				!perfis.Any(perfil => string.Equals(perfil.Nome, produto, StringComparison.OrdinalIgnoreCase)))];

		return new PerfisDaTelaDto(perfis, faltando);
	}
}
```

```csharp
// src/Secco.Intranet.Application/Acesso/ObterPerfilHandler.cs
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Pedido do detalhe de um perfil.</summary>
/// <param name="Nome">Nome do perfil.</param>
/// <param name="Pagina">Página de membros (1-based).</param>
public sealed record ObterPerfilQuery(string Nome, int Pagina);

/// <summary>Tela de detalhe de um perfil.</summary>
/// <param name="Perfil">Detalhe do perfil, com permissões só para leitura.</param>
/// <param name="Membros">Página de membros.</param>
/// <param name="Candidatos">Usuários ativos que ainda não têm o perfil; vazio se o perfil é reservado.</param>
public sealed record PerfilTelaDto(PerfilDetalheDto Perfil, PaginaDeMembros Membros, IReadOnlyList<UsuarioDto> Candidatos);

/// <summary>Detalhe de um perfil, com membros e candidatos a membro.</summary>
public sealed class ObterPerfilHandler(IGestaoDeAcesso gestao)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Perfil e página.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PerfilTelaDto>> HandleAsync(ObterPerfilQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var perfil = await gestao.ObterPerfilAsync(query.Nome, cancellationToken).ConfigureAwait(false);

		if (perfil.IsFailure)
		{
			return Result.Failure<PerfilTelaDto>(perfil.Error);
		}

		var membros = await gestao
			.ListarMembrosAsync(perfil.Value.Nome, Math.Max(1, query.Pagina), cancellationToken)
			.ConfigureAwait(false);

		if (membros.IsFailure)
		{
			return Result.Failure<PerfilTelaDto>(membros.Error);
		}

		IReadOnlyList<UsuarioDto> candidatos = [];

		if (!perfil.Value.Reservado && !ClassificacaoDePerfil.EhReservado(perfil.Value.Nome))
		{
			var usuarios = await gestao.ListarUsuariosAsync(cancellationToken).ConfigureAwait(false);

			if (usuarios.IsFailure)
			{
				return Result.Failure<PerfilTelaDto>(usuarios.Error);
			}

			candidatos =
			[
				.. usuarios.Value
					.Where(usuario => usuario.Situacao == SituacaoDoUsuario.Ativo
						&& !usuario.Perfis.Any(p => string.Equals(p, perfil.Value.Nome, StringComparison.OrdinalIgnoreCase)))
					.OrderBy(usuario => usuario.Email, StringComparer.OrdinalIgnoreCase),
			];
		}

		return new PerfilTelaDto(perfil.Value, membros.Value, candidatos);
	}
}
```

```csharp
// src/Secco.Intranet.Application/Acesso/ListarUsuariosHandler.cs
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Pedido da aba "Usuários".</summary>
/// <param name="Busca">Trecho do e-mail (opcional).</param>
/// <param name="Pagina">Página (1-based).</param>
public sealed record ListarUsuariosQuery(string? Busca, int Pagina);

/// <summary>
/// Lista usuários com busca e paginação em memória: <c>ListUsers</c> do SecureGate devolve o
/// tenant inteiro de uma vez, e paginar é da tela.
/// </summary>
public sealed class ListarUsuariosHandler(IGestaoDeAcesso gestao)
{
	private const int TamanhoDaPagina = 20;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Busca e página.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PagedResult<UsuarioDto>>> HandleAsync(
		ListarUsuariosQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var lidos = await gestao.ListarUsuariosAsync(cancellationToken).ConfigureAwait(false);

		if (lidos.IsFailure)
		{
			return Result.Failure<PagedResult<UsuarioDto>>(lidos.Error);
		}

		var busca = query.Busca?.Trim();

		var filtrados = lidos.Value
			.Where(usuario => string.IsNullOrEmpty(busca)
				|| (usuario.Email ?? string.Empty).Contains(busca, StringComparison.OrdinalIgnoreCase))
			.OrderBy(usuario => usuario.Email, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var pagina = new PageRequest(Math.Max(1, query.Pagina), TamanhoDaPagina);
		IReadOnlyList<UsuarioDto> itens = [.. filtrados.Skip(pagina.Skip).Take(pagina.Size)];

		return PagedResult.Create(itens, pagina, filtrados.Count);
	}
}
```

```csharp
// src/Secco.Intranet.Application/Acesso/ObterUsuarioHandler.cs
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Perfis de setor de um usuário agrupados por setor.</summary>
/// <param name="Slug">Slug do setor.</param>
/// <param name="Papeis">Papéis que o usuário tem nesse setor (pode ter os dois).</param>
public sealed record PerfilNoSetorDto(string Slug, IReadOnlyList<PapelNoSetor> Papeis);

/// <summary>Tela de detalhe de um usuário.</summary>
/// <param name="Usuario">Detalhe do usuário.</param>
/// <param name="Setores">Perfis de setor, agrupados por setor.</param>
/// <param name="OutrosPerfis">Perfis que não são de setor (comuns e do produto).</param>
/// <param name="SlugsDeSetorDisponiveis">Setores para os quais existe Role, alimentam o seletor de setor.</param>
/// <param name="OutrosPerfisAtribuiveis">Perfis comuns e do produto, não reservados, que o usuário ainda não tem.</param>
public sealed record UsuarioTelaDto(
	UsuarioDetalheDto Usuario,
	IReadOnlyList<PerfilNoSetorDto> Setores,
	IReadOnlyList<string> OutrosPerfis,
	IReadOnlyList<string> SlugsDeSetorDisponiveis,
	IReadOnlyList<string> OutrosPerfisAtribuiveis);

/// <summary>Detalhe de um usuário com o que a tela precisa para oferecer os perfis.</summary>
public sealed class ObterUsuarioHandler(IGestaoDeAcesso gestao)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<UsuarioTelaDto>> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = await gestao.ObterUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure<UsuarioTelaDto>(usuario.Error);
		}

		var perfis = await gestao.ListarPerfisAsync(cancellationToken).ConfigureAwait(false);

		if (perfis.IsFailure)
		{
			return Result.Failure<UsuarioTelaDto>(perfis.Error);
		}

		IReadOnlyList<PerfilNoSetorDto> setores =
		[
			.. usuario.Value.Perfis
				.Select(nome => (Nome: nome, Setor: ClassificacaoDePerfil.DoSetor(nome)))
				.Where(x => x.Setor is not null)
				.GroupBy(x => x.Setor!.Value.Slug, StringComparer.OrdinalIgnoreCase)
				.OrderBy(grupo => grupo.Key, StringComparer.OrdinalIgnoreCase)
				.Select(grupo => new PerfilNoSetorDto(
					grupo.Key,
					[.. grupo.Select(x => x.Setor!.Value.Papel).Distinct().OrderByDescending(papel => papel)])),
		];

		IReadOnlyList<string> outros =
			[.. usuario.Value.Perfis.Where(nome => ClassificacaoDePerfil.DoSetor(nome) is null).OrderBy(nome => nome, StringComparer.OrdinalIgnoreCase)];

		IReadOnlyList<string> slugs =
		[
			.. perfis.Value
				.Select(perfil => ClassificacaoDePerfil.DoSetor(perfil.Nome))
				.Where(setor => setor is not null)
				.Select(setor => setor!.Value.Slug)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(slug => slug, StringComparer.OrdinalIgnoreCase),
		];

		IReadOnlyList<string> atribuiveis =
		[
			.. perfis.Value
				.Where(perfil => ClassificacaoDePerfil.DoSetor(perfil.Nome) is null
					&& !perfil.Reservado
					&& !ClassificacaoDePerfil.EhReservado(perfil.Nome)
					&& !usuario.Value.Perfis.Any(p => string.Equals(p, perfil.Nome, StringComparison.OrdinalIgnoreCase)))
				.Select(perfil => perfil.Nome)
				.OrderBy(nome => nome, StringComparer.OrdinalIgnoreCase),
		];

		return new UsuarioTelaDto(usuario.Value, setores, outros, slugs, atribuiveis);
	}
}
```

- [ ] **Step 3: Registrar no DI**

Em `IntranetApplicationExtensions.cs`, acrescente `using Secco.Intranet.Application.Acesso;` e, antes do `return services;`:

```csharp
		services.AddScoped<ListarPerfisHandler>();
		services.AddScoped<ObterPerfilHandler>();
		services.AddScoped<ListarUsuariosHandler>();
		services.AddScoped<ObterUsuarioHandler>();
```

- [ ] **Step 4: Rodar**

Run: `dotnet test --filter LeituraDeAcessoHandlersTests` → PASS, 0 avisos.

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Application/Acesso src/Secco.Intranet.Application/IntranetApplicationExtensions.cs tests/Secco.Intranet.Tests/Unit/LeituraDeAcessoHandlersTests.cs
git commit -m "feat(acesso): handlers de leitura de perfis e usuarios

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 6: Application — handlers de escrita com as regras e a auditoria

O coração da feature: as regras que a plataforma **não** protege (`intranet-admin` nunca fica sem membro ativo; ninguém se tranca para fora; reservado nunca é atribuível; perfil de produto/setor não se exclui).

**Files:**
- Create: `src/Secco.Intranet.Application/Acesso/AuditoriaDeAcesso.cs`
- Create: `src/Secco.Intranet.Application/Acesso/RegrasDeIntranetAdmin.cs`
- Create: `src/Secco.Intranet.Application/Acesso/CriarPerfilHandler.cs`
- Create: `src/Secco.Intranet.Application/Acesso/ExcluirPerfilHandler.cs`
- Create: `src/Secco.Intranet.Application/Acesso/AtribuirPerfilHandler.cs`
- Create: `src/Secco.Intranet.Application/Acesso/RetirarPerfilHandler.cs`
- Create: `src/Secco.Intranet.Application/Acesso/UsuarioHandlers.cs` (Desativar, Reativar, EncerrarSessoes)
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/EscritaDeAcessoHandlersTests.cs`

**Interfaces:**
- Consumes: `IGestaoDeAcesso`, `ITrilhaDeAuditoria`, `IAtorAtual`, `ClassificacaoDePerfil`, `IntranetErrors.Acesso`, dublês (Task 4).
- Produces (todos `HandleAsync(..., CancellationToken ct = default) : Task<Result>`):
  - `CriarPerfilHandler(IGestaoDeAcesso, ITrilhaDeAuditoria)` — `HandleAsync(string nome)`
  - `ExcluirPerfilHandler(IGestaoDeAcesso, ITrilhaDeAuditoria)` — `HandleAsync(string nome)`
  - `AtribuirPerfilHandler(IGestaoDeAcesso, ITrilhaDeAuditoria)` — `HandleAsync(AtribuirPerfilCommand)`; `record AtribuirPerfilCommand(Guid UsuarioId, string Perfil)`
  - `RetirarPerfilHandler(IGestaoDeAcesso, ITrilhaDeAuditoria, IAtorAtual)` — `HandleAsync(RetirarPerfilCommand)`; `record RetirarPerfilCommand(Guid UsuarioId, string Perfil)`
  - `DesativarUsuarioHandler(IGestaoDeAcesso, ITrilhaDeAuditoria, IAtorAtual)`, `ReativarUsuarioHandler(IGestaoDeAcesso, ITrilhaDeAuditoria)`, `EncerrarSessoesHandler(IGestaoDeAcesso, ITrilhaDeAuditoria)` — `HandleAsync(Guid usuarioId)`

- [ ] **Step 1: Testes (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/EscritaDeAcessoHandlersTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class EscritaDeAcessoHandlersTests
{
	private static readonly Guid Admin = Guid.NewGuid();
	private static readonly Guid OutroAdmin = Guid.NewGuid();
	private static readonly Guid Ana = Guid.NewGuid();

	private static GestaoDeAcessoFalsa Cenario() => new GestaoDeAcessoFalsa()
		.ComPerfil("intranet-admin").ComPerfil("marketing-admin").ComPerfil("gerente-de-compras").ComPerfil("installation-operator", reservado: true)
		.ComUsuario(Admin, "admin@x.com", SituacaoDoUsuario.Ativo, "intranet-admin")
		.ComUsuario(Ana, "ana@x.com");

	// --- CriarPerfil ---

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("gerente de compras")]
	[InlineData("-x")]
	public async Task CriarPerfil_NomeInvalido_RecusaSemChamarAPlataforma(string nome)
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new CriarPerfilHandler(gestao, trilha).HandleAsync(nome);

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilNomeInvalido);
		gestao.Chamadas.Should().BeEmpty();
		trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task CriarPerfil_Com101Caracteres_Recusa()
	{
		var resultado = await new CriarPerfilHandler(Cenario(), new TrilhaDeAcessoFalsa()).HandleAsync(new string('a', 101));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilNomeInvalido);
	}

	[Theory]
	[InlineData("installation-operator")]
	[InlineData("Installation-Auditor")]
	public async Task CriarPerfil_Reservado_Recusa(string nome)
	{
		var gestao = Cenario();

		var resultado = await new CriarPerfilHandler(gestao, new TrilhaDeAcessoFalsa()).HandleAsync(nome);

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task CriarPerfil_Valido_CriaEAudita()
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new CriarPerfilHandler(gestao, trilha).HandleAsync("  equipe-financeiro ");

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal("perfil-criar:equipe-financeiro");
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.AcessoPerfilCriar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Acesso);
		registro.RecursoId.Should().Be("equipe-financeiro");
	}

	[Fact]
	public async Task CriarPerfil_QuandoAPlataformaFalha_NaoAudita()
	{
		var gestao = Cenario();
		gestao.FalharCom = IntranetErrors.Acesso.Indisponivel;
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new CriarPerfilHandler(gestao, trilha).HandleAsync("equipe-financeiro");

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		trilha.Registros.Should().BeEmpty("a ação não aconteceu, então não entra na trilha");
	}

	// --- ExcluirPerfil ---

	[Theory]
	[InlineData("intranet-admin")]
	[InlineData("Inventario-Admin")]
	[InlineData("marketing-admin")]
	[InlineData("rh-user")]
	public async Task ExcluirPerfil_DoProdutoOuDeSetor_Protegido(string nome)
	{
		var gestao = Cenario();

		var resultado = await new ExcluirPerfilHandler(gestao, new TrilhaDeAcessoFalsa()).HandleAsync(nome);

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilProtegido);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task ExcluirPerfil_Reservado_Recusa()
	{
		var resultado = await new ExcluirPerfilHandler(Cenario(), new TrilhaDeAcessoFalsa()).HandleAsync("installation-operator");

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
	}

	[Fact]
	public async Task ExcluirPerfil_Comum_ExcluiEAudita()
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new ExcluirPerfilHandler(gestao, trilha).HandleAsync("gerente-de-compras");

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal("perfil-excluir:gerente-de-compras");
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoPerfilExcluir);
	}

	// --- AtribuirPerfil ---

	[Fact]
	public async Task AtribuirPerfil_Reservado_RecusaSemChamarAPlataforma()
	{
		var gestao = Cenario();

		var resultado = await new AtribuirPerfilHandler(gestao, new TrilhaDeAcessoFalsa())
			.HandleAsync(new AtribuirPerfilCommand(Ana, "Installation-Operator"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task AtribuirPerfil_MarcadoComoReservadoPelaPlataforma_Recusa()
	{
		// Um reservado que a lista local não conhece: a decisão definitiva vem do IsReserved.
		var gestao = Cenario().ComPerfil("novo-reservado", reservado: true);

		var resultado = await new AtribuirPerfilHandler(gestao, new TrilhaDeAcessoFalsa())
			.HandleAsync(new AtribuirPerfilCommand(Ana, "novo-reservado"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task AtribuirPerfil_PerfilInexistente_DevolveNotFound()
	{
		var resultado = await new AtribuirPerfilHandler(Cenario(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new AtribuirPerfilCommand(Ana, "nao-existe"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado);
	}

	[Fact]
	public async Task AtribuirPerfil_UsuarioInexistente_DevolveNotFound()
	{
		var resultado = await new AtribuirPerfilHandler(Cenario(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new AtribuirPerfilCommand(Guid.NewGuid(), "gerente-de-compras"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}

	[Fact]
	public async Task AtribuirPerfil_Valido_AtribuiEAuditaComEmailDoAfetado()
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new AtribuirPerfilHandler(gestao, trilha)
			.HandleAsync(new AtribuirPerfilCommand(Ana, "gerente-de-compras"));

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal($"perfil-atribuir:{Ana}:gerente-de-compras");
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.AcessoPerfilAtribuir);
		registro.Metadata.Should().Contain("gerente-de-compras").And.Contain("ana@x.com").And.Contain(Ana.ToString());
	}

	// --- RetirarPerfil ---

	[Fact]
	public async Task RetirarPerfil_IntranetAdminDeSiMesmo_Recusa()
	{
		var gestao = Cenario().ComUsuario(OutroAdmin, "outro@x.com", SituacaoDoUsuario.Ativo, "intranet-admin");

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Admin))
			.HandleAsync(new RetirarPerfilCommand(Admin, "intranet-admin"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.AutoRemocaoDeIntranetAdmin);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task RetirarPerfil_UltimoIntranetAdminAtivo_Recusa()
	{
		var gestao = Cenario();

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(OutroAdmin))
			.HandleAsync(new RetirarPerfilCommand(Admin, "intranet-admin"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.UltimoIntranetAdmin);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task RetirarPerfil_OutroAdminDesativadoNaoConta_ComoRestante()
	{
		var gestao = Cenario().ComUsuario(OutroAdmin, "outro@x.com", SituacaoDoUsuario.Desativado, "intranet-admin");

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Guid.NewGuid()))
			.HandleAsync(new RetirarPerfilCommand(Admin, "intranet-admin"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.UltimoIntranetAdmin,
			"um intranet-admin desativado não abre a área — não pode ser o 'outro' que sobra");
	}

	[Fact]
	public async Task RetirarPerfil_ComOutroAdminAtivo_Retira()
	{
		var gestao = Cenario().ComUsuario(OutroAdmin, "outro@x.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new RetirarPerfilHandler(gestao, trilha, new AtorDeAcessoFalso(OutroAdmin))
			.HandleAsync(new RetirarPerfilCommand(Admin, "Intranet-Admin"));

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal($"perfil-retirar:{Admin}:Intranet-Admin");
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoPerfilRetirar);
	}

	[Fact]
	public async Task RetirarPerfil_ContaOsAdminsAtivosEmTodasAsPaginas()
	{
		// O único outro admin ativo está na terceira página da API: contar só a primeira daria falso "último".
		var gestao = Cenario().ComUsuario(Guid.NewGuid(), "d1@x.com", SituacaoDoUsuario.Desativado, "intranet-admin")
			.ComUsuario(Guid.NewGuid(), "d2@x.com", SituacaoDoUsuario.Desativado, "intranet-admin")
			.ComUsuario(OutroAdmin, "outro@x.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		gestao.TamanhoDaPagina = 1;

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Guid.NewGuid()))
			.HandleAsync(new RetirarPerfilCommand(Admin, "intranet-admin"));

		resultado.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task RetirarPerfil_PerfilQueNaoEIntranetAdmin_NaoAplicaARegraDoUltimo()
	{
		var gestao = Cenario().ComUsuario(Guid.NewGuid(), "z@x.com", SituacaoDoUsuario.Ativo, "gerente-de-compras");

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Admin))
			.HandleAsync(new RetirarPerfilCommand(Admin, "gerente-de-compras"));

		resultado.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task RetirarPerfil_SemAtorIdentificado_UsaSoARegraDoUltimo()
	{
		// Modo aberto de DEV: não há ator. A regra do último admin continua valendo.
		var gestao = Cenario();

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(null))
			.HandleAsync(new RetirarPerfilCommand(Admin, "intranet-admin"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.UltimoIntranetAdmin);
	}

	// --- Desativar / Reativar / Encerrar sessões ---

	[Fact]
	public async Task DesativarUsuario_ASiMesmo_Recusa()
	{
		var gestao = Cenario();

		var resultado = await new DesativarUsuarioHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Admin))
			.HandleAsync(Admin);

		resultado.Error.Should().Be(IntranetErrors.Acesso.AutoDesativacao);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task DesativarUsuario_UltimoIntranetAdminAtivo_Recusa()
	{
		var gestao = Cenario();

		var resultado = await new DesativarUsuarioHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Guid.NewGuid()))
			.HandleAsync(Admin);

		resultado.Error.Should().Be(IntranetErrors.Acesso.UltimoIntranetAdmin);
	}

	[Fact]
	public async Task DesativarUsuario_Comum_DesativaEAudita()
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new DesativarUsuarioHandler(gestao, trilha, new AtorDeAcessoFalso(Admin)).HandleAsync(Ana);

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal($"usuario-desativar:{Ana}");
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoUsuarioDesativar);
		trilha.Registros.Single().Metadata.Should().Contain("ana@x.com");
	}

	[Fact]
	public async Task ReativarUsuario_ReativaEAudita()
	{
		var gestao = Cenario().ComUsuario(Guid.NewGuid(), "z@x.com");
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new ReativarUsuarioHandler(gestao, trilha).HandleAsync(Ana);

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal($"usuario-reativar:{Ana}");
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoUsuarioReativar);
	}

	[Fact]
	public async Task EncerrarSessoes_EncerraEAudita_MesmoDeSiMesmo()
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new EncerrarSessoesHandler(gestao, trilha).HandleAsync(Admin);

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal($"sessoes-encerrar:{Admin}");
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoSessoesEncerrar);
	}

	[Fact]
	public async Task EncerrarSessoes_UsuarioInexistente_DevolveNotFound()
	{
		var resultado = await new EncerrarSessoesHandler(Cenario(), new TrilhaDeAcessoFalsa()).HandleAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}
}
```

Run: `dotnet test --filter EscritaDeAcessoHandlersTests` → falha de compilação.

- [ ] **Step 2: Suporte compartilhado — auditoria e regra do último admin**

```csharp
// src/Secco.Intranet.Application/Acesso/AuditoriaDeAcesso.cs
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Registro na trilha das ações da gestão de acesso — só depois que a plataforma aceitou a ação.</summary>
internal static class AuditoriaDeAcesso
{
	/// <summary>Registra uma ação sobre um perfil.</summary>
	public static Task PerfilAsync(
		ITrilhaDeAuditoria trilha, string verbo, string perfil, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Acesso, perfil, JsonSerializer.Serialize(new { perfil })),
			cancellationToken);

	/// <summary>Registra uma ação sobre um usuário, com o e-mail em cache (o SecureGate não guarda nome).</summary>
	public static Task UsuarioAsync(
		ITrilhaDeAuditoria trilha, string verbo, Guid usuarioId, string? email, string? perfil, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(
				verbo,
				RecursosDeAuditoria.Acesso,
				usuarioId.ToString(),
				JsonSerializer.Serialize(new { perfil, usuarioId, email })),
			cancellationToken);
}
```

```csharp
// src/Secco.Intranet.Application/Acesso/RegrasDeIntranetAdmin.cs
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>
/// A plataforma não conhece <c>intranet-admin</c> e deixa tirar o último — o que trancaria
/// todo mundo para fora da própria área administrativa. Esta regra é da Intranet.
/// </summary>
internal static class RegrasDeIntranetAdmin
{
	// Teto de segurança do laço: 50 páginas de 100 são 5 mil intranet-admins. Se chegar lá,
	// assume que sobra alguém — errar para o lado de não bloquear.
	private const int LimiteDePaginas = 50;

	/// <summary>Indica se, tirando <paramref name="usuarioId"/>, ainda sobra algum <c>intranet-admin</c> ativo.</summary>
	/// <param name="gestao">Porta de gestão de acesso.</param>
	/// <param name="usuarioId">Usuário que deixaria de ser intranet-admin (ou ativo).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<Result<bool>> RestamOutrosAtivosAsync(
		IGestaoDeAcesso gestao, Guid usuarioId, CancellationToken cancellationToken)
	{
		for (var pagina = 1; pagina <= LimiteDePaginas; pagina++)
		{
			var lida = await gestao
				.ListarMembrosAsync(ClassificacaoDePerfil.IntranetAdmin, pagina, cancellationToken)
				.ConfigureAwait(false);

			if (lida.IsFailure)
			{
				return Result.Failure<bool>(lida.Error);
			}

			if (lida.Value.Itens.Any(m => m.UsuarioId != usuarioId && m.Situacao == SituacaoDoUsuario.Ativo))
			{
				return true;
			}

			if (pagina >= lida.Value.TotalDePaginas)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>Indica se o ator identificado é o próprio usuário-alvo.</summary>
	/// <param name="ator">Ator atual (pode ser <c>null</c> no modo aberto de DEV).</param>
	/// <param name="usuarioId">Usuário-alvo.</param>
	public static bool EhOProprio(Secco.Intranet.Application.Auditoria.IAtorAtual ator, Guid usuarioId) =>
		ator.Atual() is { } atual && Guid.TryParse(atual.Id, out var id) && id == usuarioId;
}
```

- [ ] **Step 3: Handlers de perfil**

```csharp
// src/Secco.Intranet.Application/Acesso/CriarPerfilHandler.cs
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Cria um perfil no SecureGate.</summary>
public sealed class CriarPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="nome">Nome do perfil (a tela mostra o formato aceito).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(string nome, CancellationToken cancellationToken = default)
	{
		var perfil = nome?.Trim() ?? string.Empty;

		if (!ClassificacaoDePerfil.NomeValido(perfil))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilNomeInvalido);
		}

		if (ClassificacaoDePerfil.EhReservado(perfil))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		var criado = await gestao.CriarPerfilAsync(perfil, cancellationToken).ConfigureAwait(false);

		if (criado.IsFailure)
		{
			return criado;
		}

		await AuditoriaDeAcesso.PerfilAsync(trilha, VerbosDeAuditoria.AcessoPerfilCriar, perfil, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
```

```csharp
// src/Secco.Intranet.Application/Acesso/ExcluirPerfilHandler.cs
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Exclui um perfil comum sem membros.</summary>
public sealed class ExcluirPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(string nome, CancellationToken cancellationToken = default)
	{
		var perfil = nome?.Trim() ?? string.Empty;

		if (perfil.Length == 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (ClassificacaoDePerfil.EhReservado(perfil))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		// Apagar o perfil de um setor quebraria o vínculo dele com o setor (ADR-0001); o do
		// produto é o que sustenta a autorização. Vale por convenção de nome: um perfil comum
		// que termine em -admin/-user também fica protegido, e isso é o lado seguro do erro.
		if (ClassificacaoDePerfil.Tipo(perfil) != TipoDePerfil.Comum)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilProtegido);
		}

		var excluido = await gestao.ExcluirPerfilAsync(perfil, cancellationToken).ConfigureAwait(false);

		if (excluido.IsFailure)
		{
			return excluido;
		}

		await AuditoriaDeAcesso.PerfilAsync(trilha, VerbosDeAuditoria.AcessoPerfilExcluir, perfil, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
```

```csharp
// src/Secco.Intranet.Application/Acesso/AtribuirPerfilHandler.cs
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Pedido de atribuição de um perfil a um usuário.</summary>
/// <param name="UsuarioId">Usuário que recebe o perfil.</param>
/// <param name="Perfil">Nome do perfil.</param>
public sealed record AtribuirPerfilCommand(Guid UsuarioId, string Perfil);

/// <summary>Atribui um perfil a um usuário existente. Perfil reservado nunca é atribuível.</summary>
public sealed class AtribuirPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Usuário e perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(AtribuirPerfilCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var perfil = command.Perfil?.Trim() ?? string.Empty;

		if (perfil.Length == 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (ClassificacaoDePerfil.EhReservado(perfil))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		// A plataforma aceita atribuir installation-operator a um usuário; a decisão definitiva
		// sobre "reservado" é dela, e vem no IsReserved do detalhe.
		var detalhe = await gestao.ObterPerfilAsync(perfil, cancellationToken).ConfigureAwait(false);

		if (detalhe.IsFailure)
		{
			return Result.Failure(detalhe.Error);
		}

		if (detalhe.Value.Reservado)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		var usuario = await gestao.ObterUsuarioAsync(command.UsuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		var atribuido = await gestao.AtribuirPerfilAsync(command.UsuarioId, detalhe.Value.Nome, cancellationToken).ConfigureAwait(false);

		if (atribuido.IsFailure)
		{
			return atribuido;
		}

		await AuditoriaDeAcesso
			.UsuarioAsync(trilha, VerbosDeAuditoria.AcessoPerfilAtribuir, command.UsuarioId, usuario.Value.Email, detalhe.Value.Nome, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
```

Atenção: o teste `AtribuirPerfil_Valido...` espera a chamada `perfil-atribuir:{Ana}:gerente-de-compras`; o handler passa `detalhe.Value.Nome` (o nome canônico devolvido pela plataforma). No `GestaoDeAcessoFalsa` isso é o nome como cadastrado (`gerente-de-compras`) — coerente com o teste.

```csharp
// src/Secco.Intranet.Application/Acesso/RetirarPerfilHandler.cs
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Pedido de retirada de um perfil de um usuário.</summary>
/// <param name="UsuarioId">Usuário que perde o perfil.</param>
/// <param name="Perfil">Nome do perfil.</param>
public sealed record RetirarPerfilCommand(Guid UsuarioId, string Perfil);

/// <summary>
/// Retira um perfil. <c>intranet-admin</c> tem duas travas que a plataforma não tem: ninguém
/// tira o próprio, e nunca sobra a instalação sem um ativo.
/// </summary>
public sealed class RetirarPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha, IAtorAtual ator)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Usuário e perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(RetirarPerfilCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var perfil = command.Perfil?.Trim() ?? string.Empty;

		if (perfil.Length == 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		var ehIntranetAdmin = string.Equals(perfil, ClassificacaoDePerfil.IntranetAdmin, StringComparison.OrdinalIgnoreCase);

		if (ehIntranetAdmin)
		{
			if (RegrasDeIntranetAdmin.EhOProprio(ator, command.UsuarioId))
			{
				return Result.Failure(IntranetErrors.Acesso.AutoRemocaoDeIntranetAdmin);
			}

			var restam = await RegrasDeIntranetAdmin
				.RestamOutrosAtivosAsync(gestao, command.UsuarioId, cancellationToken)
				.ConfigureAwait(false);

			if (restam.IsFailure)
			{
				return Result.Failure(restam.Error);
			}

			if (!restam.Value)
			{
				return Result.Failure(IntranetErrors.Acesso.UltimoIntranetAdmin);
			}
		}

		var usuario = await gestao.ObterUsuarioAsync(command.UsuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		var retirado = await gestao.RetirarPerfilAsync(command.UsuarioId, perfil, cancellationToken).ConfigureAwait(false);

		if (retirado.IsFailure)
		{
			return retirado;
		}

		await AuditoriaDeAcesso
			.UsuarioAsync(trilha, VerbosDeAuditoria.AcessoPerfilRetirar, command.UsuarioId, usuario.Value.Email, perfil, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
```

```csharp
// src/Secco.Intranet.Application/Acesso/UsuarioHandlers.cs
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Desativa a conta de um usuário — nunca a própria, nunca a do último <c>intranet-admin</c> ativo.</summary>
public sealed class DesativarUsuarioHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha, IAtorAtual ator)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Usuário a desativar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		// A plataforma já responde 409 à autodesativação; a Intranet checa antes para a mensagem ser dela.
		if (RegrasDeIntranetAdmin.EhOProprio(ator, usuarioId))
		{
			return Result.Failure(IntranetErrors.Acesso.AutoDesativacao);
		}

		var usuario = await gestao.ObterUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		if (usuario.Value.Perfis.Any(p => string.Equals(p, ClassificacaoDePerfil.IntranetAdmin, StringComparison.OrdinalIgnoreCase)))
		{
			var restam = await RegrasDeIntranetAdmin
				.RestamOutrosAtivosAsync(gestao, usuarioId, cancellationToken)
				.ConfigureAwait(false);

			if (restam.IsFailure)
			{
				return Result.Failure(restam.Error);
			}

			if (!restam.Value)
			{
				return Result.Failure(IntranetErrors.Acesso.UltimoIntranetAdmin);
			}
		}

		var desativado = await gestao.DesativarUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (desativado.IsFailure)
		{
			return desativado;
		}

		await AuditoriaDeAcesso
			.UsuarioAsync(trilha, VerbosDeAuditoria.AcessoUsuarioDesativar, usuarioId, usuario.Value.Email, null, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}

/// <summary>Reativa a conta de um usuário.</summary>
public sealed class ReativarUsuarioHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Usuário a reativar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = await gestao.ObterUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		var reativado = await gestao.ReativarUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (reativado.IsFailure)
		{
			return reativado;
		}

		await AuditoriaDeAcesso
			.UsuarioAsync(trilha, VerbosDeAuditoria.AcessoUsuarioReativar, usuarioId, usuario.Value.Email, null, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}

/// <summary>Encerra as sessões de um usuário. Permitido até para o próprio admin (ele só sai e entra de novo).</summary>
public sealed class EncerrarSessoesHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Usuário cujas sessões serão encerradas.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = await gestao.ObterUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		var encerrado = await gestao.EncerrarSessoesAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (encerrado.IsFailure)
		{
			return encerrado;
		}

		await AuditoriaDeAcesso
			.UsuarioAsync(trilha, VerbosDeAuditoria.AcessoSessoesEncerrar, usuarioId, usuario.Value.Email, null, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
```

Se o analisador reclamar de mais de um tipo por arquivo em `UsuarioHandlers.cs`, separe em três arquivos, um por handler — sem mudar o código.

- [ ] **Step 4: Registrar no DI**

Em `IntranetApplicationExtensions.cs`, antes do `return services;`:

```csharp
		services.AddScoped<CriarPerfilHandler>();
		services.AddScoped<ExcluirPerfilHandler>();
		services.AddScoped<AtribuirPerfilHandler>();
		services.AddScoped<RetirarPerfilHandler>();
		services.AddScoped<DesativarUsuarioHandler>();
		services.AddScoped<ReativarUsuarioHandler>();
		services.AddScoped<EncerrarSessoesHandler>();
```

`IAtorAtual` já está registrado no Web (`AtorDoHttpContext`); confira com `grep -rn "IAtorAtual" src/Secco.Intranet.Web/Program.cs src/Secco.Intranet.Infrastructure` e, se a resolução falhar em runtime, registre onde os demais handlers de auditoria o esperam.

- [ ] **Step 5: Rodar**

Run: `dotnet test --filter "EscritaDeAcessoHandlersTests|LeituraDeAcessoHandlersTests"` → PASS, 0 avisos.

- [ ] **Step 6: Commit**

```bash
git add src/Secco.Intranet.Application/Acesso src/Secco.Intranet.Application/IntranetApplicationExtensions.cs tests/Secco.Intranet.Tests/Unit/EscritaDeAcessoHandlersTests.cs
git commit -m "feat(acesso): handlers de escrita com as regras que a plataforma nao protege

Reservado nunca atribuivel, intranet-admin nunca fica sem membro ativo,
ninguem tira o proprio perfil nem se desativa, perfil de produto/setor nao se exclui.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 7: Infrastructure — adaptador do SecureGate, no-op e composição

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcesso.cs`
- Create: `src/Secco.Intranet.Infrastructure/Access/GestaoDeAcessoIndisponivel.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/SecureGateGestaoDeAcessoTests.cs`

**Interfaces:**
- Consumes: `IGestaoDeAcesso` e DTOs (Task 4); `ISecureGateClient` 0.11.0 — `ListRolesAsync(Guid, ct)`, `GetRoleAsync(Guid, string, ct)`, `ListRoleMembersAsync(Guid, string, int?, int?, ct)`, `ListUsersAsync(Guid, ct)`, `GetUserAsync(Guid, Guid, ct)`, `CreateRoleAsync(Guid, CreateRoleRequest, ct)`, `DeleteRoleAsync(Guid, string, ct)`, `AddUserRoleAsync(Guid, Guid, string, ct)`, `RemoveUserRoleAsync(Guid, Guid, string, ct)`, `DeactivateUserAsync(Guid, Guid, ct)`, `ActivateUserAsync(Guid, Guid, ct)`, `RevokeUserSessionsAsync(Guid, Guid, ct)`; DTOs `RoleDto`, `RoleDetailDto`, `PagedResultOfRoleMemberDto`, `RoleMemberDto`, `UserDto`, `UserDetailDto` (`Status` é `string`: `Active`/`Deactivated`/`LockedOut`).
- Produces: `SecureGateGestaoDeAcesso(ISecureGateClient, ITenantContext, ILogger<SecureGateGestaoDeAcesso>)` e `GestaoDeAcessoIndisponivel` (sem dependências), ambos `IGestaoDeAcesso`; `IGestaoDeAcesso` registrado como scoped, escolhido por `SecureGateClientCredentialsOptions.IsConfigured`.

- [ ] **Step 1: Testes (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/SecureGateGestaoDeAcessoTests.cs
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// O adaptador da gestão de acesso. As garantias são as dos demais adaptadores de borda — o
/// timeout do HttpClient não pode escapar como exceção —, mais o mapeamento de 404/409 para os
/// erros de negócio e a confirmação de que cada chamada leva o tenant e os ids certos.
/// </summary>
public class SecureGateGestaoDeAcessoTests
{
	private static readonly Guid Tenant = Guid.NewGuid();

	private sealed class TenantContextFalso(Guid? tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => tenantId is not null;
	}

	/// <summary>
	/// Herda do client gerado em vez de implementar <c>ISecureGateClient</c> à mão: a interface
	/// ganha métodos a cada release da plataforma, e um fake que a implementa inteira quebra a
	/// compilação a cada bump de pacote.
	/// </summary>
	private sealed class ClientFalso() : SecureGateClient(new HttpClient())
	{
		public List<string> Chamadas { get; } = [];

		public Exception? Falha { get; set; }

		public RoleDetailDto? Papel { get; set; }

		public PagedResultOfRoleMemberDto? Membros { get; set; }

		public ICollection<UserDto> Usuarios { get; set; } = [];

		public UserDetailDto? Usuario { get; set; }

		private Task Registrar(string chamada)
		{
			Chamadas.Add(chamada);

			return Falha is null ? Task.CompletedTask : Task.FromException(Falha);
		}

		public override async Task<ICollection<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken)
		{
			await Registrar($"ListRoles:{tenantId}");

			return [new RoleDto { Name = "financeiro-admin", Permissions = ["a:read", "a:write"] }];
		}

		public override async Task<RoleDetailDto> GetRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken)
		{
			await Registrar($"GetRole:{tenantId}:{role}");

			return Papel!;
		}

		public override async Task<PagedResultOfRoleMemberDto> ListRoleMembersAsync(
			Guid tenantId, string role, int? page, int? size, CancellationToken cancellationToken)
		{
			await Registrar($"ListRoleMembers:{tenantId}:{role}:{page}:{size}");

			return Membros!;
		}

		public override async Task<ICollection<UserDto>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken)
		{
			await Registrar($"ListUsers:{tenantId}");

			return Usuarios;
		}

		public override async Task<UserDetailDto> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
		{
			await Registrar($"GetUser:{tenantId}:{userId}");

			return Usuario!;
		}

		public override async Task<RoleDto> CreateRoleAsync(Guid tenantId, CreateRoleRequest body, CancellationToken cancellationToken)
		{
			await Registrar($"CreateRole:{tenantId}:{body.Name}");

			return new RoleDto { Name = body.Name };
		}

		public override Task DeleteRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken) =>
			Registrar($"DeleteRole:{tenantId}:{role}");

		public override Task AddUserRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken) =>
			Registrar($"AddUserRole:{tenantId}:{userId}:{role}");

		public override Task RemoveUserRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken) =>
			Registrar($"RemoveUserRole:{tenantId}:{userId}:{role}");

		public override Task DeactivateUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
			Registrar($"DeactivateUser:{tenantId}:{userId}");

		public override Task ActivateUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
			Registrar($"ActivateUser:{tenantId}:{userId}");

		public override Task RevokeUserSessionsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
			Registrar($"RevokeUserSessions:{tenantId}:{userId}");
	}

	private static SecureGateGestaoDeAcesso Montar(ClientFalso client, Guid? tenant = null) =>
		new(client, new TenantContextFalso(tenant ?? Tenant), NullLogger<SecureGateGestaoDeAcesso>.Instance);

	private static ApiException Api(int status) => new("erro", status, string.Empty, new Dictionary<string, IEnumerable<string>>(), null!);

	[Fact]
	public async Task ListarPerfis_MapeiaNomeTipoEPermissoes()
	{
		var client = new ClientFalso();

		var resultado = await Montar(client).ListarPerfisAsync();

		var perfil = resultado.Value.Should().ContainSingle().Subject;
		perfil.Should().Be(new PerfilDto("financeiro-admin", 2, TipoDePerfil.Setor, false));
		client.Chamadas.Should().Equal($"ListRoles:{Tenant}");
	}

	[Fact]
	public async Task ObterPerfil_MapeiaDetalhe()
	{
		var client = new ClientFalso { Papel = new RoleDetailDto { Name = "x", Permissions = ["a:read"], IsReserved = true, MemberCount = 3 } };

		var resultado = await Montar(client).ObterPerfilAsync("x");

		resultado.Value.Should().Be(new PerfilDetalheDto("x", ["a:read"], true, 3, TipoDePerfil.Comum), options => options.ComparingByMembers<PerfilDetalheDto>());
		client.Chamadas.Should().Equal($"GetRole:{Tenant}:x");
	}

	[Fact]
	public async Task ObterPerfil_404_VirasPerfilNaoEncontrado()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(404) }).ObterPerfilAsync("x");

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado);
	}

	[Fact]
	public async Task ListarMembros_PedePaginaDe100_EMapeiaSituacao()
	{
		var usuario = Guid.NewGuid();
		var client = new ClientFalso
		{
			Membros = new PagedResultOfRoleMemberDto
			{
				Items = [new RoleMemberDto { UserId = usuario, Email = "a@x.com", Status = "LockedOut" }],
				Page = 2,
				Size = 100,
				TotalCount = 101,
				TotalPages = 2,
			},
		};

		var resultado = await Montar(client).ListarMembrosAsync("x", 2);

		client.Chamadas.Should().Equal($"ListRoleMembers:{Tenant}:x:2:100");
		resultado.Value.Total.Should().Be(101);
		resultado.Value.TotalDePaginas.Should().Be(2);
		resultado.Value.Itens.Should().ContainSingle().Which.Should().Be(new MembroDoPerfilDto(usuario, "a@x.com", SituacaoDoUsuario.Bloqueado));
	}

	[Theory]
	[InlineData("Active", SituacaoDoUsuario.Ativo)]
	[InlineData("active", SituacaoDoUsuario.Ativo)]
	[InlineData("Deactivated", SituacaoDoUsuario.Desativado)]
	[InlineData("LockedOut", SituacaoDoUsuario.Bloqueado)]
	[InlineData("Qualquer", SituacaoDoUsuario.Desconhecida)]
	[InlineData(null, SituacaoDoUsuario.Desconhecida)]
	public async Task ListarUsuarios_MapeiaSituacao(string? status, SituacaoDoUsuario esperada)
	{
		var client = new ClientFalso { Usuarios = [new UserDto { Id = Guid.NewGuid(), Email = "a@x.com", Status = status!, Roles = ["r"] }] };

		var resultado = await Montar(client).ListarUsuariosAsync();

		resultado.Value.Single().Situacao.Should().Be(esperada);
	}

	[Fact]
	public async Task ObterUsuario_MapeiaDetalhe()
	{
		var id = Guid.NewGuid();
		var client = new ClientFalso
		{
			Usuario = new UserDetailDto
			{
				Id = id,
				Email = "a@x.com",
				Status = "Active",
				Roles = ["r1"],
				EffectivePermissions = ["a:read"],
				ExternalLogins = ["EntraId"],
				TwoFactorEnabled = true,
			},
		};

		var resultado = await Montar(client).ObterUsuarioAsync(id);

		var dto = resultado.Value;
		dto.Perfis.Should().Equal("r1");
		dto.Permissoes.Should().Equal("a:read");
		dto.LoginsExternos.Should().Equal("EntraId");
		dto.DoisFatoresAtivo.Should().BeTrue();
		dto.Situacao.Should().Be(SituacaoDoUsuario.Ativo);
	}

	[Fact]
	public async Task ObterUsuario_404_VirasUsuarioNaoEncontrado()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(404) }).ObterUsuarioAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}

	[Fact]
	public async Task Escritas_LevamOTenantEOsIdsCertos()
	{
		var client = new ClientFalso();
		var gestao = Montar(client);
		var usuario = Guid.NewGuid();

		(await gestao.CriarPerfilAsync("p")).IsSuccess.Should().BeTrue();
		(await gestao.ExcluirPerfilAsync("p")).IsSuccess.Should().BeTrue();
		(await gestao.AtribuirPerfilAsync(usuario, "p")).IsSuccess.Should().BeTrue();
		(await gestao.RetirarPerfilAsync(usuario, "p")).IsSuccess.Should().BeTrue();
		(await gestao.DesativarUsuarioAsync(usuario)).IsSuccess.Should().BeTrue();
		(await gestao.ReativarUsuarioAsync(usuario)).IsSuccess.Should().BeTrue();
		(await gestao.EncerrarSessoesAsync(usuario)).IsSuccess.Should().BeTrue();

		client.Chamadas.Should().Equal(
			$"CreateRole:{Tenant}:p",
			$"DeleteRole:{Tenant}:p",
			$"AddUserRole:{Tenant}:{usuario}:p",
			$"RemoveUserRole:{Tenant}:{usuario}:p",
			$"DeactivateUser:{Tenant}:{usuario}",
			$"ActivateUser:{Tenant}:{usuario}",
			$"RevokeUserSessions:{Tenant}:{usuario}");
	}

	[Fact]
	public async Task CriarPerfil_409_VirasPerfilJaExiste()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(409) }).CriarPerfilAsync("p");

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilJaExiste);
	}

	[Fact]
	public async Task ExcluirPerfil_409_VirasPerfilComMembros()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(409) }).ExcluirPerfilAsync("p");

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilComMembros);
	}

	[Fact]
	public async Task DesativarUsuario_409_VirasDesativacaoRecusada()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(409) }).DesativarUsuarioAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Acesso.DesativacaoRecusada);
	}

	[Fact]
	public async Task AtribuirPerfil_404_VirasUsuarioNaoEncontrado()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(404) }).AtribuirPerfilAsync(Guid.NewGuid(), "p");

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}

	[Fact]
	public async Task ErroInesperadoDaPlataforma_ViraIndisponivel_SemVazarDetalhe()
	{
		var resultado = await Montar(new ClientFalso { Falha = Api(500) }).ListarPerfisAsync();

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task FalhaDeRede_ViraIndisponivel()
	{
		var resultado = await Montar(new ClientFalso { Falha = new HttpRequestException("caiu") }).ListarUsuariosAsync();

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task Timeout_ViraIndisponivel_NaoEscapaComoExcecao()
	{
		var resultado = await Montar(new ClientFalso { Falha = new TaskCanceledException("Timeout do HttpClient.") })
			.AtribuirPerfilAsync(Guid.NewGuid(), "p");

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task CancelamentoDoChamador_Relanca()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		var client = new ClientFalso { Falha = new OperationCanceledException(cts.Token) };

		var acao = async () => await Montar(client).ListarPerfisAsync(cts.Token);

		await acao.Should().ThrowAsync<OperationCanceledException>(
			"cancelamento pedido pelo chamador não é falha de infraestrutura");
	}

	[Fact]
	public async Task TenantNaoResolvido_ViraIndisponivel_SemChamarAPlataforma()
	{
		var client = new ClientFalso();
		var gestao = new SecureGateGestaoDeAcesso(client, new TenantContextFalso(null), NullLogger<SecureGateGestaoDeAcesso>.Instance);

		var resultado = await gestao.ListarPerfisAsync();

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		client.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task GestaoIndisponivel_ResponderNaoConfigurado_EmTudo()
	{
		var gestao = new GestaoDeAcessoIndisponivel();

		(await gestao.ListarPerfisAsync()).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ObterPerfilAsync("x")).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ListarMembrosAsync("x", 1)).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ListarUsuariosAsync()).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ObterUsuarioAsync(Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.CriarPerfilAsync("x")).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ExcluirPerfilAsync("x")).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.AtribuirPerfilAsync(Guid.NewGuid(), "x")).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.RetirarPerfilAsync(Guid.NewGuid(), "x")).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.DesativarUsuarioAsync(Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.ReativarUsuarioAsync(Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
		(await gestao.EncerrarSessoesAsync(Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
	}
}
```

Notas para quem executa: se `ObterPerfil_MapeiaDetalhe` reclamar da comparação de `IReadOnlyList` dentro do record, troque a asserção por comparar campo a campo (`Nome`, `Permissoes`, `Reservado`, `TotalDeMembros`, `Tipo`). Se a assinatura de override de algum método do client gerado divergir (ex.: sem `CancellationToken` no override), ajuste o `ClientFalso` à assinatura real do `SecureGateClient` 0.11.0 — o adaptador chama sempre a sobrecarga **com** `CancellationToken`.

Run: `dotnet test --filter SecureGateGestaoDeAcessoTests` → falha de compilação.

- [ ] **Step 2: Implementar o adaptador**

```csharp
// src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcesso.cs
using System.Net;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Adapter real de <see cref="IGestaoDeAcesso"/>, sobre o client administrativo do SecureGate
/// (mesma credencial de <see cref="SecureGateSetorAccessProvisioner"/>). Só é resolvido com a
/// seção <c>Secco:SecureGate</c> configurada — ver <see cref="GestaoDeAcessoIndisponivel"/>.
/// Falha de rede, de status e timeout viram <see cref="Result"/>: quem administra precisa saber
/// que a ação não aconteceu. Cancelamento pedido pelo chamador é relançado.
/// </summary>
/// <param name="client">Client administrativo do SecureGate.</param>
/// <param name="tenantContext">Tenant da requisição atual (ADR-0005).</param>
/// <param name="logger">Log de falhas — só operação e status, nunca corpo ou credencial (ADR-0020).</param>
public sealed class SecureGateGestaoDeAcesso(
	ISecureGateClient client,
	ITenantContext tenantContext,
	ILogger<SecureGateGestaoDeAcesso> logger) : IGestaoDeAcesso
{
	private const int TamanhoDaPaginaDeMembros = 100;

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDto>>> ListarPerfisAsync(CancellationToken cancellationToken = default) =>
		LerAsync<IReadOnlyList<PerfilDto>>(
			"listar perfis",
			async (tenantId, token) =>
			{
				var perfis = await client.ListRolesAsync(tenantId, token).ConfigureAwait(false);
				IReadOnlyList<PerfilDto> lista =
					[.. perfis.Select(p => new PerfilDto(
						p.Name,
						p.Permissions?.Count ?? 0,
						ClassificacaoDePerfil.Tipo(p.Name),
						ClassificacaoDePerfil.EhReservado(p.Name)))];

				return lista;
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<PerfilDetalheDto>> ObterPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		LerAsync(
			"obter perfil",
			async (tenantId, token) =>
			{
				var papel = await client.GetRoleAsync(tenantId, nome, token).ConfigureAwait(false);

				return new PerfilDetalheDto(
					papel.Name,
					[.. papel.Permissions ?? []],
					papel.IsReserved,
					papel.MemberCount,
					ClassificacaoDePerfil.Tipo(papel.Name));
			},
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(
		string nome, int pagina, CancellationToken cancellationToken = default) =>
		LerAsync(
			"listar membros",
			async (tenantId, token) =>
			{
				var lida = await client
					.ListRoleMembersAsync(tenantId, nome, pagina, TamanhoDaPaginaDeMembros, token)
					.ConfigureAwait(false);

				IReadOnlyList<MembroDoPerfilDto> itens =
					[.. (lida.Items ?? []).Select(m => new MembroDoPerfilDto(m.UserId, m.Email, Situacao(m.Status)))];

				return new PaginaDeMembros(itens, lida.Page, lida.TotalPages, lida.TotalCount);
			},
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(CancellationToken cancellationToken = default) =>
		LerAsync<IReadOnlyList<UsuarioDto>>(
			"listar usuarios",
			async (tenantId, token) =>
			{
				var usuarios = await client.ListUsersAsync(tenantId, token).ConfigureAwait(false);
				IReadOnlyList<UsuarioDto> lista =
					[.. usuarios.Select(u => new UsuarioDto(u.Id, u.Email, Situacao(u.Status), [.. u.Roles ?? []]))];

				return lista;
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<UsuarioDetalheDto>> ObterUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		LerAsync(
			"obter usuario",
			async (tenantId, token) =>
			{
				var u = await client.GetUserAsync(tenantId, usuarioId, token).ConfigureAwait(false);

				return new UsuarioDetalheDto(
					u.Id,
					u.Email,
					Situacao(u.Status),
					u.LockoutEnd,
					[.. u.Roles ?? []],
					[.. u.EffectivePermissions ?? []],
					[.. u.ExternalLogins ?? []],
					u.TwoFactorEnabled);
			},
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"criar perfil",
			(tenantId, token) => client.CreateRoleAsync(tenantId, new CreateRoleRequest { Name = nome }, token),
			null,
			IntranetErrors.Acesso.PerfilJaExiste,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"excluir perfil",
			(tenantId, token) => client.DeleteRoleAsync(tenantId, nome, token),
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			IntranetErrors.Acesso.PerfilComMembros,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"atribuir perfil",
			(tenantId, token) => client.AddUserRoleAsync(tenantId, usuarioId, perfil, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"retirar perfil",
			(tenantId, token) => client.RemoveUserRoleAsync(tenantId, usuarioId, perfil, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"desativar usuario",
			(tenantId, token) => client.DeactivateUserAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			IntranetErrors.Acesso.DesativacaoRecusada,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"reativar usuario",
			(tenantId, token) => client.ActivateUserAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"encerrar sessoes",
			(tenantId, token) => client.RevokeUserSessionsAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			null,
			cancellationToken);

	private static SituacaoDoUsuario Situacao(string? status) => status?.ToLowerInvariant() switch
	{
		"active" => SituacaoDoUsuario.Ativo,
		"deactivated" => SituacaoDoUsuario.Desativado,
		"lockedout" => SituacaoDoUsuario.Bloqueado,
		_ => SituacaoDoUsuario.Desconhecida,
	};

	private async Task<Result<T>> LerAsync<T>(
		string operacao,
		Func<Guid, CancellationToken, Task<T>> chamada,
		Error? naoEncontrado,
		CancellationToken cancellationToken)
	{
		if (!tenantContext.IsResolved)
		{
			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}

		try
		{
			return Result.Success(await chamada(tenantContext.TenantId!.Value, cancellationToken).ConfigureAwait(false));
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.NotFound && naoEncontrado is not null)
		{
			return Result.Failure<T>(naoEncontrado);
		}
		catch (ApiException apiException)
		{
			logger.LogWarning("Falha ao {Operacao} no SecureGate (status {StatusCode}).", operacao, apiException.StatusCode);

			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao {Operacao} no SecureGate.", operacao);

			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}
		catch (OperationCanceledException operationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Timeout do HttpClient, não cancelamento do chamador: o SecureGate aceitou a
			// conexão e não respondeu a tempo. Escapar como exceção viraria um 500.
			logger.LogWarning(operationCanceledException, "Timeout ao {Operacao} no SecureGate.", operacao);

			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}
	}

	private async Task<Result> EscreverAsync(
		string operacao,
		Func<Guid, CancellationToken, Task> chamada,
		Error? naoEncontrado,
		Error? conflito,
		CancellationToken cancellationToken)
	{
		if (!tenantContext.IsResolved)
		{
			return Result.Failure(IntranetErrors.Acesso.Indisponivel);
		}

		try
		{
			await chamada(tenantContext.TenantId!.Value, cancellationToken).ConfigureAwait(false);

			return Result.Success();
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.NotFound && naoEncontrado is not null)
		{
			return Result.Failure(naoEncontrado);
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.Conflict && conflito is not null)
		{
			return Result.Failure(conflito);
		}
		catch (ApiException apiException)
		{
			logger.LogWarning("Falha ao {Operacao} no SecureGate (status {StatusCode}).", operacao, apiException.StatusCode);

			return Result.Failure(IntranetErrors.Acesso.Indisponivel);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao {Operacao} no SecureGate.", operacao);

			return Result.Failure(IntranetErrors.Acesso.Indisponivel);
		}
		catch (OperationCanceledException operationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			logger.LogWarning(operationCanceledException, "Timeout ao {Operacao} no SecureGate.", operacao);

			return Result.Failure(IntranetErrors.Acesso.Indisponivel);
		}
	}
}
```

- [ ] **Step 3: O adaptador no-op**

```csharp
// src/Secco.Intranet.Infrastructure/Access/GestaoDeAcessoIndisponivel.cs
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Adapter no-op de <see cref="IGestaoDeAcesso"/> para DEV e Testing (seção
/// <c>Secco:SecureGate</c> ausente). Diferente dos outros no-ops, que fingem sucesso, este
/// responde "não configurado": fingir que um perfil foi atribuído seria mentir para quem administra.
/// </summary>
public sealed class GestaoDeAcessoIndisponivel : IGestaoDeAcesso
{
	private static readonly Error Erro = IntranetErrors.Acesso.NaoConfigurado;

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDto>>> ListarPerfisAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<PerfilDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<PerfilDetalheDto>> ObterPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<PerfilDetalheDto>(Erro));

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(string nome, int pagina, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<PaginaDeMembros>(Erro));

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<UsuarioDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<UsuarioDetalheDto>> ObterUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<UsuarioDetalheDto>(Erro));

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));
}
```

- [ ] **Step 4: Composição por configuração**

Em `IntranetInfrastructureExtensions.cs`, acrescente `using Secco.Intranet.Application.Acesso;` (se ainda não houver o `using` de `Secco.Intranet.Infrastructure.Access`, ele já existe porque o provisioner mora lá) e, logo depois do bloco `services.AddScoped<IDiretorioDeUsuarios>(...)`:

```csharp
		services.AddScoped<IGestaoDeAcesso>(serviceProvider =>
		{
			var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

			return credenciais.IsConfigured
				? ActivatorUtilities.CreateInstance<SecureGateGestaoDeAcesso>(serviceProvider)
				: ActivatorUtilities.CreateInstance<GestaoDeAcessoIndisponivel>(serviceProvider);
		});
```

- [ ] **Step 5: Rodar e commitar**

Run: `dotnet test --filter "SecureGateGestaoDeAcessoTests"` → PASS; depois `dotnet build` com 0 avisos.

```bash
git add src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcesso.cs src/Secco.Intranet.Infrastructure/Access/GestaoDeAcessoIndisponivel.cs src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs tests/Secco.Intranet.Tests/Unit/SecureGateGestaoDeAcessoTests.cs
git commit -m "feat(acesso): adaptador do SecureGate para a gestao de acesso

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 8: Web — `/Acesso`: abas Perfis e Usuários, criar perfil, perfis do produto

Primeira fatia vertical do Web: controller, ViewModels, views, teste HTTP com a gestão substituída por dublê. Cada tarefa 8–10 estende o mesmo `AcessoController`.

**Files:**
- Create: `src/Secco.Intranet.Web/Controllers/AcessoController.cs`
- Create: `src/Secco.Intranet.Web/Models/Acesso/AcessoViewModels.cs`
- Create: `src/Secco.Intranet.Web/Views/Acesso/Index.cshtml`
- Create: `src/Secco.Intranet.Web/Views/Acesso/Indisponivel.cshtml`
- Modify: `src/Secco.Intranet.Web/Views/_ViewImports.cshtml`
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs`
- Create: `tests/Secco.Intranet.Tests/Integration/AcessoTelasTests.cs`
- Modify: `tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs`

**Interfaces:**
- Consumes: handlers e DTOs das tarefas 5–6; `[SomenteIntranetAdmin]` (Task 1); `FeedbackViewComponent.ChaveDaMensagem`/`ChaveDaMensagemDeErro` (Task 3).
- Produces: `AcessoController` (`[SomenteIntranetAdmin]`, rotas convencionais `/Acesso/...`); `IntranetWebFactory.GestaoDeAcesso` (`IGestaoDeAcesso?`, settable) — quando nulo, o host usa `GestaoDeAcessoIndisponivel`; `AbaDoAcesso { Perfis, Usuarios }`; `AcessoIndexViewModel`; `IndisponivelViewModel(string Mensagem)`; item de menu "Acesso" (`/acesso`, ícone `bi-shield-lock`) no grupo Administração, logo abaixo de "Setores".

- [ ] **Step 1: A fábrica de teste aceita uma gestão substituta**

Em `IntranetWebFactory.cs`, acrescente `using Secco.Intranet.Application.Acesso;` e `using Secco.Intranet.Infrastructure.Access;`, a propriedade e o registro:

```csharp
	/// <summary>
	/// Gestão de acesso que o host devolve. Nula, vale o comportamento real do ambiente Testing
	/// (SecureGate não configurado → <see cref="GestaoDeAcessoIndisponivel"/>). Os testes de tela
	/// atribuem um dublê aqui; a fábrica é por classe de teste, e os testes de uma classe rodam em série.
	/// </summary>
	public IGestaoDeAcesso? GestaoDeAcesso { get; set; }
```

e troque `ConfigureTestServices` por:

```csharp
	/// <inheritdoc />
	protected override void ConfigureTestServices(IServiceCollection services)
	{
		services.AddSingleton<IStartupFilter, RolesDeTesteStartupFilter>();
		services.AddScoped<IGestaoDeAcesso>(serviceProvider =>
			GestaoDeAcesso ?? ActivatorUtilities.CreateInstance<GestaoDeAcessoIndisponivel>(serviceProvider));
	}
```

- [ ] **Step 2: ViewModels e imports**

```csharp
// src/Secco.Intranet.Web/Models/Acesso/AcessoViewModels.cs
using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Models.Acesso;

/// <summary>Aba da tela inicial de acesso.</summary>
public enum AbaDoAcesso
{
	/// <summary>Perfis.</summary>
	Perfis = 0,

	/// <summary>Usuários.</summary>
	Usuarios = 1,
}

/// <summary>Modelo de <c>/Acesso</c>: uma das duas abas preenchida.</summary>
/// <param name="Aba">Aba ativa.</param>
/// <param name="Perfis">Conteúdo da aba Perfis (nulo na aba Usuários).</param>
/// <param name="Usuarios">Conteúdo da aba Usuários (nulo na aba Perfis).</param>
/// <param name="Busca">Filtro de e-mail aplicado, para repopular a busca.</param>
public sealed record AcessoIndexViewModel(
	AbaDoAcesso Aba, PerfisDaTelaDto? Perfis, PagedResult<UsuarioDto>? Usuarios, string? Busca);

/// <summary>Modelo da página que explica por que a gestão de acesso não abriu.</summary>
/// <param name="Mensagem">Texto do erro, já sem detalhe interno.</param>
public sealed record IndisponivelViewModel(string Mensagem);
```

Em `Views/_ViewImports.cshtml`, acrescente as linhas:

```cshtml
@using Secco.Intranet.Web.Models.Acesso
@using Secco.Intranet.Application.Acesso
```

- [ ] **Step 3: Testes de tela (falham)**

```csharp
// tests/Secco.Intranet.Tests/Integration/AcessoTelasTests.cs
using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// As telas da Área de acesso, com a gestão substituída por um dublê em memória — o SecureGate
/// real não existe no ambiente Testing. Cada teste monta o cenário que quer.
/// </summary>
public class AcessoTelasTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.GestaoDeAcesso = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarClienteAdmin(GestaoDeAcessoFalsa? gestao = null)
	{
		factory.GestaoDeAcesso = gestao;
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		return client;
	}

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private static FormUrlEncodedContent Form(params (string Chave, string Valor)[] campos) =>
		new(campos.Select(c => new KeyValuePair<string, string>(c.Chave, c.Valor)));

	[Fact]
	public async Task Index_SemSecureGateConfigurado_AbreEExplica()
	{
		var resposta = await CriarClienteAdmin().GetAsync("/Acesso");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
	}

	[Fact]
	public async Task Index_AbaUsuarios_SemSecureGateConfigurado_AbreEExplica()
	{
		var resposta = await CriarClienteAdmin().GetAsync("/Acesso?aba=usuarios");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
	}

	[Fact]
	public async Task Index_ListaPerfisComClassificacao_EOferecerCriarOsDoProdutoQueFaltam()
	{
		var gestao = new GestaoDeAcessoFalsa().ComPerfil("marketing-admin").ComPerfil("gerente-de-compras").ComPerfil("intranet-admin");

		var html = await CriarClienteAdmin(gestao).GetStringAsync("/Acesso");

		html.Should().Contain("marketing-admin").And.Contain("gerente-de-compras").And.Contain("intranet-admin");
		html.Should().Contain("inventario-admin", "é o perfil do produto que ainda não existe e pode ser criado");
	}

	[Fact]
	public async Task Index_AbaUsuarios_BuscaPorEmail()
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Ana, "ana@x.com").ComUsuario(Bruno, "bruno@y.com");

		var html = await CriarClienteAdmin(gestao).GetStringAsync("/Acesso?aba=usuarios&busca=bruno");

		html.Should().Contain("bruno@y.com").And.NotContain("ana@x.com");
	}

	[Fact]
	public async Task Index_AbaUsuarios_PaginaAlemDoFim_AbreSemQuebrar()
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Ana, "ana@x.com");

		var resposta = await CriarClienteAdmin(gestao).GetAsync("/Acesso?aba=usuarios&page=99");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Index_AbaUsuarios_UsuarioSemEmail_AbreSemQuebrar()
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Ana, string.Empty).ComUsuario(Bruno, "bruno@x.com");

		var resposta = await CriarClienteAdmin(gestao).GetAsync("/Acesso?aba=usuarios");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task CriarPerfil_Valido_CriaEConfirmaNaPaginaDeDestino()
	{
		var gestao = new GestaoDeAcessoFalsa();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso");

		var resposta = await client.PostAsync("/Acesso/CriarPerfil", Form(("nome", "equipe-financeiro"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "a criação redireciona de volta para /Acesso");
		gestao.Chamadas.Should().Equal("perfil-criar:equipe-financeiro");
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("equipe-financeiro");
	}

	[Fact]
	public async Task CriarPerfil_NomeInvalido_MostraErroNoToast_ENaoCria()
	{
		var gestao = new GestaoDeAcessoFalsa();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso");

		var resposta = await client.PostAsync("/Acesso/CriarPerfil", Form(("nome", "gerente de compras"), ("__RequestVerificationToken", token)));

		var html = await resposta.Content.ReadAsStringAsync();
		html.Should().Contain("sc-toast--erro").And.Contain("sem espaços");
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task CriarPerfil_SemSecureGateConfigurado_MostraErroNoToast_NaoDa500()
	{
		var client = CriarClienteAdmin();

		// Sem SecureGate a página /Acesso é a que explica e não tem formulário; o token vem de
		// outra página com formulário que o admin abre (o token antifalsificação vale para o host).
		var token = await TokenAsync(client, "/Setores/Create");

		var resposta = await client.PostAsync("/Acesso/CriarPerfil", Form(("nome", "equipe-financeiro"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("sc-toast--erro");
	}
}
```

Confira no `Feedback/Default.cshtml` do tema Vertical que a classe de erro é mesmo `sc-toast--erro`.

Run: `dotnet test --filter AcessoTelasTests` → FAIL (rota inexistente).

- [ ] **Step 4: O controller (aba Perfis/Usuários + criar perfil)**

```csharp
// src/Secco.Intranet.Web/Controllers/AcessoController.cs
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Acesso;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Gestão de acesso: perfis (Roles) e usuários do tenant, no SecureGate. Exclusivo do
/// <c>intranet-admin</c> (ADR-0008) — o atributo na classe cobre toda action, presente e futura.
/// Controller fino (ADR-0002): só orquestra handlers.
/// </summary>
[SomenteIntranetAdmin]
public sealed class AcessoController(
	ListarPerfisHandler listarPerfis,
	ListarUsuariosHandler listarUsuarios,
	CriarPerfilHandler criarPerfil) : Controller
{
	/// <summary>Tela inicial, com as abas Perfis e Usuários.</summary>
	/// <param name="aba"><c>usuarios</c> abre a aba de usuários; qualquer outro valor, a de perfis.</param>
	/// <param name="busca">Trecho de e-mail (aba Usuários).</param>
	/// <param name="page">Página da aba Usuários.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Index(string? aba, string? busca, int page = 1, CancellationToken cancellationToken = default)
	{
		if (string.Equals(aba, "usuarios", StringComparison.OrdinalIgnoreCase))
		{
			var usuarios = await listarUsuarios.HandleAsync(new ListarUsuariosQuery(busca, page), cancellationToken);

			return usuarios.IsFailure
				? Falha(usuarios.Error)
				: View(new AcessoIndexViewModel(AbaDoAcesso.Usuarios, null, usuarios.Value, busca));
		}

		var perfis = await listarPerfis.HandleAsync(cancellationToken);

		return perfis.IsFailure
			? Falha(perfis.Error)
			: View(new AcessoIndexViewModel(AbaDoAcesso.Perfis, perfis.Value, null, null));
	}

	/// <summary>Cria um perfil (livre, ou um dos perfis do produto que ainda faltam).</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> CriarPerfil(string? nome, CancellationToken cancellationToken = default)
	{
		var resultado = await criarPerfil.HandleAsync(nome ?? string.Empty, cancellationToken);

		return Concluir(resultado, $"Perfil \"{nome?.Trim()}\" criado.", nameof(Index));
	}

	/// <summary>
	/// Erro de leitura: perfil/usuário inexistente é 404; o resto (SecureGate ausente ou fora do
	/// ar) vira a página que explica, com 200 — não é falha da Intranet.
	/// </summary>
	private IActionResult Falha(Error erro) =>
		erro.Type == ErrorType.NotFound
			? NotFound()
			: View("Indisponivel", new IndisponivelViewModel(erro.Description));

	/// <summary>Grava o resultado da ação no toast certo e volta para a tela de destino.</summary>
	private IActionResult Concluir(Result resultado, string sucesso, string action, object? rota = null)
	{
		if (resultado.IsSuccess)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = sucesso;
		}
		else
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}

		return RedirectToAction(action, rota);
	}
}
```

Confira o `using` de `ErrorType` (`Secco.SharedKernel.Results`). Nas tarefas 9 e 10 o construtor ganha mais handlers.

- [ ] **Step 5: Views**

```cshtml
@* src/Secco.Intranet.Web/Views/Acesso/Indisponivel.cshtml *@
@model IndisponivelViewModel
@{
    ViewData["Title"] = "Acesso";

    var cabecalho = new PageHeaderModel("Acesso", "Perfis e usuários deste tenant — exclusivo do intranet-admin.");

    var vazio = new EmptyStateModel(
        "bi-plug",
        "Gestão de acesso indisponível",
        Model.Mensagem);
}

<partial name="_PageHeader" model="cabecalho" />
<partial name="_EmptyState" model="vazio" />
```

```cshtml
@* src/Secco.Intranet.Web/Views/Acesso/Index.cshtml *@
@model AcessoIndexViewModel
@{
    ViewData["Title"] = "Acesso";

    var cabecalho = new PageHeaderModel("Acesso", "Perfis e usuários deste tenant — exclusivo do intranet-admin.");
    var nosPerfis = Model.Aba == AbaDoAcesso.Perfis;

    static BadgeModel BadgeDoPerfil(PerfilDto perfil) =>
        perfil.Reservado ? new BadgeModel("Reservado", BadgeVariante.Perigo)
        : perfil.Tipo == TipoDePerfil.Produto ? new BadgeModel("Do produto", BadgeVariante.Sucesso)
        : perfil.Tipo == TipoDePerfil.Setor ? new BadgeModel("Do setor", BadgeVariante.Neutro)
        : new BadgeModel("Perfil", BadgeVariante.Aviso);

    static BadgeModel BadgeDaSituacao(SituacaoDoUsuario situacao) => situacao switch
    {
        SituacaoDoUsuario.Ativo => new BadgeModel("Ativo", BadgeVariante.Sucesso),
        SituacaoDoUsuario.Desativado => new BadgeModel("Desativado", BadgeVariante.Perigo),
        SituacaoDoUsuario.Bloqueado => new BadgeModel("Bloqueado", BadgeVariante.Aviso),
        _ => new BadgeModel("Desconhecida", BadgeVariante.Neutro),
    };
}

<partial name="_PageHeader" model="cabecalho" />

<ul class="nav nav-tabs mb-3">
    <li class="nav-item">
        <a class="nav-link @(nosPerfis ? "active" : null)" asp-action="Index" asp-route-aba="perfis">Perfis</a>
    </li>
    <li class="nav-item">
        <a class="nav-link @(!nosPerfis ? "active" : null)" asp-action="Index" asp-route-aba="usuarios">Usuários</a>
    </li>
</ul>

@if (nosPerfis && Model.Perfis is { } perfis)
{
    @if (perfis.PerfisDoProdutoFaltando.Count > 0)
    {
        <div class="sc-panel mb-3">
            <p class="mb-2"><strong>Perfis do produto que ainda não existem neste tenant</strong></p>
            <div class="d-flex flex-wrap gap-2">
                @foreach (var faltando in perfis.PerfisDoProdutoFaltando)
                {
                    <form method="post" asp-action="CriarPerfil" class="d-inline">
                        <input type="hidden" name="nome" value="@faltando" />
                        <button class="btn btn-outline-primary" type="submit">Criar @faltando</button>
                    </form>
                }
            </div>
        </div>
    }

    @if (perfis.Perfis.Count == 0)
    {
        <partial name="_EmptyState" model="@(new EmptyStateModel("bi-shield-lock", "Nenhum perfil ainda", "Crie o primeiro perfil abaixo."))" />
    }
    else
    {
        <ul class="sc-list">
            @foreach (var perfil in perfis.Perfis)
            {
                <li class="sc-list__item">
                    <span class="sc-list__icon" aria-hidden="true"><i class="bi bi-shield-lock"></i></span>
                    <div class="sc-list__text">
                        <p class="sc-list__title">
                            <a class="text-reset text-decoration-none" asp-action="Perfil" asp-route-nome="@perfil.Nome">@perfil.Nome</a>
                        </p>
                        <span class="sc-list__sub">
                            <partial name="_Badge" model="BadgeDoPerfil(perfil)" />
                            <span class="sc-meta">@perfil.TotalDePermissoes permissões</span>
                        </span>
                    </div>
                </li>
            }
        </ul>
    }

    <div class="sc-panel mt-3">
        <form method="post" asp-action="CriarPerfil" class="d-flex flex-wrap gap-2 align-items-end">
            <div class="sc-form__field mb-0">
                <label class="form-label" for="nome">Novo perfil</label>
                <input class="form-control" id="nome" name="nome" placeholder="equipe-financeiro" maxlength="100" required />
                <div class="form-text">Letras, dígitos, ponto, sublinhado e hífen; sem espaços.</div>
            </div>
            <button class="btn btn-primary" type="submit">Criar perfil</button>
        </form>
    </div>
}
else if (Model.Usuarios is { } usuarios)
{
    string? PaginaUrl(int pagina) => Url.Action("Index", new { aba = "usuarios", busca = Model.Busca, page = pagina });

    var paginacao = new PaginationModel(
        usuarios.Page,
        usuarios.TotalPages,
        usuarios.HasPreviousPage ? PaginaUrl(usuarios.Page - 1) : null,
        usuarios.HasNextPage ? PaginaUrl(usuarios.Page + 1) : null);

    <form method="get" asp-action="Index" class="row g-2 mb-3" role="search">
        <input type="hidden" name="aba" value="usuarios" />
        <div class="col-sm-auto flex-grow-1">
            <label class="visually-hidden" for="busca">Buscar por e-mail</label>
            <input class="form-control" type="search" id="busca" name="busca" value="@Model.Busca" placeholder="Buscar por e-mail" />
        </div>
        <div class="col-sm-auto">
            <button class="btn btn-outline-secondary w-100" type="submit">Buscar</button>
        </div>
    </form>

    @if (usuarios.Items.Count == 0)
    {
        <partial name="_EmptyState" model="@(new EmptyStateModel("bi-people", "Nenhum usuário encontrado", "Ajuste a busca."))" />
    }
    else
    {
        <ul class="sc-list">
            @foreach (var usuario in usuarios.Items)
            {
                <li class="sc-list__item">
                    <span class="sc-list__icon" aria-hidden="true"><i class="bi bi-person"></i></span>
                    <div class="sc-list__text">
                        <p class="sc-list__title">
                            <a class="text-reset text-decoration-none" asp-action="Usuario" asp-route-id="@usuario.Id">@(string.IsNullOrWhiteSpace(usuario.Email) ? "(sem e-mail)" : usuario.Email)</a>
                        </p>
                        <span class="sc-list__sub">
                            <partial name="_Badge" model="BadgeDaSituacao(usuario.Situacao)" />
                            <span class="sc-meta">@string.Join(", ", usuario.Perfis)</span>
                        </span>
                    </div>
                </li>
            }
        </ul>

        <partial name="_Pagination" model="paginacao" />
    }
}
```

As views de `Perfil` e `Usuario` só existem nas tarefas 9 e 10; os links `asp-action="Perfil"` e `asp-action="Usuario"` gerarão URL para actions que ainda não existem — o `AnchorTagHelper` não falha, ele omite o `href`. Esta tarefa não os testa por clique.

- [ ] **Step 6: Item de menu "Acesso"**

Em `IntranetNavigation.cs`, no grupo "Administração", acrescente o item logo depois de "Setores":

```csharp
			grupos.Add(new NavigationGroupModel("Administração",
			[
				new NavigationItemModel("Setores", "bi-sliders", "/setores", Corresponde(caminho, "/setores")),
				new NavigationItemModel("Acesso", "bi-shield-lock", "/acesso", Corresponde(caminho, "/acesso")),
			]));
```

E o teste em `NavegacaoETemaTests.cs`:

```csharp
	[Fact]
	public void Build_ComAdministracao_OfereceSetoresEAcesso()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/acesso/perfil", MostrarAdministracao: true, DemoHabilitado: false, MostrarInventario: false));

		var itens = menu.Grupos.SelectMany(grupo => grupo.Itens).ToList();

		itens.Should().Contain(item => item.Texto == "Setores");
		itens.Single(item => item.Texto == "Acesso").Ativo.Should().BeTrue("/acesso/perfil está sob /acesso");
	}
```

- [ ] **Step 7: Rodar**

Run: `dotnet test --filter "AcessoTelasTests|NavegacaoETemaTests"` → PASS; `dotnet build` sem avisos.

- [ ] **Step 8: Commit**

```bash
git add src/Secco.Intranet.Web/Controllers/AcessoController.cs src/Secco.Intranet.Web/Models/Acesso src/Secco.Intranet.Web/Views/Acesso src/Secco.Intranet.Web/Views/_ViewImports.cshtml src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs tests/Secco.Intranet.Tests/Integration/AcessoTelasTests.cs tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs
git commit -m "feat(acesso): tela /Acesso com abas de perfis e usuarios e criacao de perfil

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 9: Web — detalhe do perfil: membros, adicionar, retirar, excluir

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/AcessoController.cs`
- Modify: `src/Secco.Intranet.Web/Models/Acesso/AcessoViewModels.cs`
- Create: `src/Secco.Intranet.Web/Views/Acesso/Perfil.cshtml`
- Modify: `tests/Secco.Intranet.Tests/Integration/AcessoTelasTests.cs`

**Interfaces:**
- Consumes: `ObterPerfilHandler`/`ObterPerfilQuery`/`PerfilTelaDto`, `AtribuirPerfilHandler`/`AtribuirPerfilCommand`, `RetirarPerfilHandler`/`RetirarPerfilCommand`, `ExcluirPerfilHandler`.
- Produces: `GET /Acesso/Perfil?nome=&page=`; `POST /Acesso/AtribuirPerfil` (`usuarioId`, `perfil`, `origem`); `POST /Acesso/RetirarPerfil` (`usuarioId`, `perfil`, `origem`); `POST /Acesso/ExcluirPerfil` (`nome`); `PerfilViewModel(PerfilTelaDto Tela)`. `origem` é `"perfil"` ou `"usuario"` e só escolhe **para onde** voltar (sempre por `RedirectToAction` — nunca redireciona para URL vinda do formulário).

- [ ] **Step 1: Testes (falham)**

Acrescente a `AcessoTelasTests`:

```csharp
	private static GestaoDeAcessoFalsa CenarioDePerfil() => new GestaoDeAcessoFalsa()
		.ComPerfil("gerente-de-compras", reservado: false, "compras:read", "compras:write")
		.ComPerfil("intranet-admin")
		.ComUsuario(Ana, "ana@x.com", SituacaoDoUsuario.Ativo, "gerente-de-compras")
		.ComUsuario(Bruno, "bruno@x.com");

	[Fact]
	public async Task Perfil_MostraPermissoesSoParaLeituraMembrosECandidatos()
	{
		var html = await CriarClienteAdmin(CenarioDePerfil()).GetStringAsync("/Acesso/Perfil?nome=gerente-de-compras");

		html.Should().Contain("compras:read").And.Contain("compras:write");
		html.Should().Contain("ana@x.com", "é membro");
		html.Should().Contain("bruno@x.com", "é candidato a membro, aparece no seletor de adicionar");
	}

	[Fact]
	public async Task Perfil_Inexistente_404()
	{
		var resposta = await CriarClienteAdmin(CenarioDePerfil()).GetAsync("/Acesso/Perfil?nome=nao-existe");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Perfil_SemSecureGateConfigurado_AbreEExplica()
	{
		var resposta = await CriarClienteAdmin().GetAsync("/Acesso/Perfil?nome=qualquer");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
	}

	[Fact]
	public async Task AdicionarMembro_AtribuiEVoltaParaOPerfil()
	{
		var gestao = CenarioDePerfil();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso/Perfil?nome=gerente-de-compras");

		var resposta = await client.PostAsync("/Acesso/AtribuirPerfil", Form(
			("usuarioId", Bruno.ToString()), ("perfil", "gerente-de-compras"), ("origem", "perfil"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/Acesso/Perfil");
		gestao.Chamadas.Should().Equal($"perfil-atribuir:{Bruno}:gerente-de-compras");
	}

	[Fact]
	public async Task RetirarMembro_Retira()
	{
		var gestao = CenarioDePerfil();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso/Perfil?nome=gerente-de-compras");

		await client.PostAsync("/Acesso/RetirarPerfil", Form(
			("usuarioId", Ana.ToString()), ("perfil", "gerente-de-compras"), ("origem", "perfil"), ("__RequestVerificationToken", token)));

		gestao.Chamadas.Should().Equal($"perfil-retirar:{Ana}:gerente-de-compras");
	}

	[Fact]
	public async Task RetirarUltimoIntranetAdmin_RecusadoComToastDeErro()
	{
		var gestao = CenarioDePerfil().ComUsuario(Guid.NewGuid(), "admin@x.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso/Perfil?nome=intranet-admin");
		var adminId = gestao.Usuarios.Last().Id;

		var resposta = await client.PostAsync("/Acesso/RetirarPerfil", Form(
			("usuarioId", adminId.ToString()), ("perfil", "intranet-admin"), ("origem", "perfil"), ("__RequestVerificationToken", token)));

		(await resposta.Content.ReadAsStringAsync()).Should().Contain("sc-toast--erro").And.Contain("último intranet-admin");
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task OrigemDesconhecida_NaoVirRedirecionamentoAberto()
	{
		var gestao = CenarioDePerfil();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso/Perfil?nome=gerente-de-compras");

		var resposta = await client.PostAsync("/Acesso/AtribuirPerfil", Form(
			("usuarioId", Bruno.ToString()), ("perfil", "gerente-de-compras"), ("origem", "https://mal.example/x"), ("__RequestVerificationToken", token)));

		resposta.RequestMessage!.RequestUri!.Host.Should().NotBe("mal.example");
	}

	[Fact]
	public async Task ExcluirPerfil_Comum_ExcluiEVoltaParaAsListagem()
	{
		var gestao = CenarioDePerfil();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso/Perfil?nome=gerente-de-compras");

		var resposta = await client.PostAsync("/Acesso/ExcluirPerfil", Form(("nome", "gerente-de-compras"), ("__RequestVerificationToken", token)));

		gestao.Chamadas.Should().Equal("perfil-excluir:gerente-de-compras");
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/Acesso");
	}

	[Fact]
	public async Task Perfil_DoProdutoOuDeSetor_NaoOfereceExcluir()
	{
		var html = await CriarClienteAdmin(CenarioDePerfil()).GetStringAsync("/Acesso/Perfil?nome=intranet-admin");

		html.Should().NotContain("Excluir perfil");
	}
```

(usar `using Secco.Intranet.Application.Acesso;` já está no arquivo.) Run → FAIL.

- [ ] **Step 2: ViewModel**

Acrescente a `AcessoViewModels.cs`:

```csharp
/// <summary>Modelo do detalhe de um perfil.</summary>
/// <param name="Tela">Perfil, membros da página e candidatos a membro.</param>
public sealed record PerfilViewModel(PerfilTelaDto Tela);
```

- [ ] **Step 3: Estender o controller**

Construtor e novas actions em `AcessoController.cs`:

```csharp
public sealed class AcessoController(
	ListarPerfisHandler listarPerfis,
	ListarUsuariosHandler listarUsuarios,
	ObterPerfilHandler obterPerfil,
	CriarPerfilHandler criarPerfil,
	ExcluirPerfilHandler excluirPerfil,
	AtribuirPerfilHandler atribuirPerfil,
	RetirarPerfilHandler retirarPerfil) : Controller
```

```csharp
	/// <summary>Detalhe de um perfil: permissões (só leitura), membros e adicionar membro.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="page">Página de membros.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Perfil(string? nome, int page = 1, CancellationToken cancellationToken = default)
	{
		var tela = await obterPerfil.HandleAsync(new ObterPerfilQuery(nome ?? string.Empty, page), cancellationToken);

		return tela.IsFailure ? Falha(tela.Error) : View(new PerfilViewModel(tela.Value));
	}

	/// <summary>Exclui um perfil comum sem membros.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ExcluirPerfil(string? nome, CancellationToken cancellationToken = default)
	{
		var resultado = await excluirPerfil.HandleAsync(nome ?? string.Empty, cancellationToken);

		return resultado.IsSuccess
			? Concluir(resultado, $"Perfil \"{nome?.Trim()}\" excluído.", nameof(Index))
			: Concluir(resultado, string.Empty, nameof(Perfil), new { nome });
	}

	/// <summary>Atribui um perfil a um usuário; volta para a tela de origem.</summary>
	/// <param name="usuarioId">Usuário que recebe o perfil.</param>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="origem"><c>perfil</c> ou <c>usuario</c> — só escolhe para onde voltar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AtribuirPerfil(Guid usuarioId, string? perfil, string? origem, CancellationToken cancellationToken = default)
	{
		var resultado = await atribuirPerfil.HandleAsync(new AtribuirPerfilCommand(usuarioId, perfil ?? string.Empty), cancellationToken);

		return ConcluirComOrigem(resultado, $"Perfil \"{perfil?.Trim()}\" atribuído.", origem, usuarioId, perfil);
	}

	/// <summary>Retira um perfil de um usuário; volta para a tela de origem.</summary>
	/// <param name="usuarioId">Usuário que perde o perfil.</param>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="origem"><c>perfil</c> ou <c>usuario</c> — só escolhe para onde voltar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> RetirarPerfil(Guid usuarioId, string? perfil, string? origem, CancellationToken cancellationToken = default)
	{
		var resultado = await retirarPerfil.HandleAsync(new RetirarPerfilCommand(usuarioId, perfil ?? string.Empty), cancellationToken);

		return ConcluirComOrigem(resultado, $"Perfil \"{perfil?.Trim()}\" retirado.", origem, usuarioId, perfil);
	}
```

e o helper (perto do `Concluir`):

```csharp
	/// <summary>
	/// Volta para a tela de onde o formulário saiu. <paramref name="origem"/> é só uma escolha entre
	/// duas telas — o destino é sempre montado aqui, nunca lido do formulário (sem redirecionamento aberto).
	/// </summary>
	private IActionResult ConcluirComOrigem(Result resultado, string sucesso, string? origem, Guid usuarioId, string? perfil) =>
		string.Equals(origem, "usuario", StringComparison.OrdinalIgnoreCase)
			? Concluir(resultado, sucesso, "Usuario", new { id = usuarioId })
			: Concluir(resultado, sucesso, nameof(Perfil), new { nome = perfil?.Trim() });
```

Nota: `Usuario` (action) só existe na Task 10; o `RedirectToAction("Usuario")` com origem `usuario` fica sem teste aqui e é exercitado na Task 10. Como a action ainda não existe, o `origem=usuario` vira 404 até lá — sem teste nesta tarefa.

Para `ExcluirPerfil`: em caso de falha, o `Concluir(resultado, string.Empty, nameof(Perfil), ...)` grava só o erro (o texto de sucesso não é usado quando `IsFailure`).

- [ ] **Step 4: View do perfil**

```cshtml
@* src/Secco.Intranet.Web/Views/Acesso/Perfil.cshtml *@
@model PerfilViewModel
@{
    var tela = Model.Tela;
    var perfil = tela.Perfil;

    ViewData["Title"] = perfil.Nome;

    var cabecalho = new PageHeaderModel(
        perfil.Nome,
        "Permissões são somente leitura neste corte.",
        Acoes: new[] { new PageActionModel("Voltar", Url.Action("Index")!, "bi-arrow-left") });

    string? PaginaUrl(int pagina) => Url.Action("Perfil", new { nome = perfil.Nome, page = pagina });

    var paginacao = new PaginationModel(
        tela.Membros.Pagina,
        tela.Membros.TotalDePaginas,
        tela.Membros.Pagina > 1 ? PaginaUrl(tela.Membros.Pagina - 1) : null,
        tela.Membros.Pagina < tela.Membros.TotalDePaginas ? PaginaUrl(tela.Membros.Pagina + 1) : null);

    static BadgeModel BadgeDaSituacao(SituacaoDoUsuario situacao) => situacao switch
    {
        SituacaoDoUsuario.Ativo => new BadgeModel("Ativo", BadgeVariante.Sucesso),
        SituacaoDoUsuario.Desativado => new BadgeModel("Desativado", BadgeVariante.Perigo),
        SituacaoDoUsuario.Bloqueado => new BadgeModel("Bloqueado", BadgeVariante.Aviso),
        _ => new BadgeModel("Desconhecida", BadgeVariante.Neutro),
    };

    var podeExcluir = perfil.Tipo == TipoDePerfil.Comum && !perfil.Reservado;
}

<partial name="_PageHeader" model="cabecalho" />

<div class="sc-panel">
    <h2 class="h6">Permissões</h2>
    @if (perfil.Permissoes.Count == 0)
    {
        <p class="text-muted mb-0">Este perfil não carrega permissões.</p>
    }
    else
    {
        <ul class="mb-0">
            @foreach (var permissao in perfil.Permissoes)
            {
                <li><span class="sc-meta">@permissao</span></li>
            }
        </ul>
    }
</div>

<div class="sc-panel mt-3">
    <h2 class="h6">Membros (@perfil.TotalDeMembros)</h2>

    @if (tela.Membros.Itens.Count == 0)
    {
        <partial name="_EmptyState" model="@(new EmptyStateModel("bi-people", "Ninguém tem este perfil ainda"))" />
    }
    else
    {
        <ul class="sc-list">
            @foreach (var membro in tela.Membros.Itens)
            {
                <li class="sc-list__item">
                    <div class="sc-list__text">
                        <p class="sc-list__title">
                            <a class="text-reset text-decoration-none" asp-action="Usuario" asp-route-id="@membro.UsuarioId">@(string.IsNullOrWhiteSpace(membro.Email) ? "(sem e-mail)" : membro.Email)</a>
                        </p>
                        <span class="sc-list__sub"><partial name="_Badge" model="BadgeDaSituacao(membro.Situacao)" /></span>
                    </div>
                    <form method="post" asp-action="RetirarPerfil" class="d-inline">
                        <input type="hidden" name="usuarioId" value="@membro.UsuarioId" />
                        <input type="hidden" name="perfil" value="@perfil.Nome" />
                        <input type="hidden" name="origem" value="perfil" />
                        <button class="btn btn-sm btn-outline-danger" type="submit">Retirar</button>
                    </form>
                </li>
            }
        </ul>

        <partial name="_Pagination" model="paginacao" />
    }
</div>

@if (!perfil.Reservado)
{
    <div class="sc-panel mt-3">
        @if (tela.Candidatos.Count == 0)
        {
            <p class="text-muted mb-0">Todo usuário ativo já tem este perfil.</p>
        }
        else
        {
            <form method="post" asp-action="AtribuirPerfil" class="d-flex flex-wrap gap-2 align-items-end">
                <input type="hidden" name="perfil" value="@perfil.Nome" />
                <input type="hidden" name="origem" value="perfil" />
                <div class="sc-form__field mb-0">
                    <label class="form-label" for="usuarioId">Adicionar membro</label>
                    <select class="form-select" id="usuarioId" name="usuarioId" required>
                        <option value="">Escolha um usuário</option>
                        @foreach (var candidato in tela.Candidatos)
                        {
                            <option value="@candidato.Id">@(string.IsNullOrWhiteSpace(candidato.Email) ? candidato.Id.ToString() : candidato.Email)</option>
                        }
                    </select>
                </div>
                <button class="btn btn-outline-primary" type="submit">Adicionar</button>
            </form>
        }
    </div>
}

@if (podeExcluir)
{
    <div class="sc-panel mt-3">
        <form method="post" asp-action="ExcluirPerfil">
            <input type="hidden" name="nome" value="@perfil.Nome" />
            <button class="btn btn-outline-danger" type="submit">Excluir perfil</button>
            <div class="form-text">Só é possível quando o perfil não tem membros.</div>
        </form>
    </div>
}
```

O texto "Excluir perfil" só aparece quando `podeExcluir` — o teste `Perfil_DoProdutoOuDeSetor_NaoOfereceExcluir` depende disso. `Usuarios sem e-mail` no seletor usam o id como rótulo, para o `<option>` nunca ficar em branco.

- [ ] **Step 5: Rodar e commitar**

Run: `dotnet test --filter AcessoTelasTests` → PASS; `dotnet build` sem avisos.

```bash
git add src/Secco.Intranet.Web/Controllers/AcessoController.cs src/Secco.Intranet.Web/Models/Acesso/AcessoViewModels.cs src/Secco.Intranet.Web/Views/Acesso/Perfil.cshtml tests/Secco.Intranet.Tests/Integration/AcessoTelasTests.cs
git commit -m "feat(acesso): detalhe do perfil com membros, adicionar, retirar e excluir

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 10: Web — detalhe do usuário: perfis por setor, adicionar/retirar, desativar, reativar, encerrar sessões

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/AcessoController.cs`
- Modify: `src/Secco.Intranet.Web/Models/Acesso/AcessoViewModels.cs`
- Create: `src/Secco.Intranet.Web/Views/Acesso/Usuario.cshtml`
- Modify: `tests/Secco.Intranet.Tests/Integration/AcessoTelasTests.cs`

**Interfaces:**
- Consumes: `ObterUsuarioHandler`/`UsuarioTelaDto`, `DesativarUsuarioHandler`, `ReativarUsuarioHandler`, `EncerrarSessoesHandler`; `AtribuirPerfil`/`RetirarPerfil` já existem (Task 9).
- Produces: `GET /Acesso/Usuario/{id}`; `POST /Acesso/AtribuirPerfilDeSetor` (`usuarioId`, `setor`, `papel` = `admin`|`user`) — compõe `{setor}-{papel}` e delega ao `AtribuirPerfilHandler`; `POST /Acesso/DesativarUsuario/{id}`, `ReativarUsuario/{id}`, `EncerrarSessoes/{id}`; `UsuarioViewModel(UsuarioTelaDto Tela)`.

- [ ] **Step 1: Testes (falham)**

Acrescente a `AcessoTelasTests`:

```csharp
	private static GestaoDeAcessoFalsa CenarioDeUsuario() => new GestaoDeAcessoFalsa()
		.ComPerfil("marketing-admin").ComPerfil("marketing-user").ComPerfil("rh-user").ComPerfil("ti-admin")
		.ComPerfil("gerente-de-compras").ComPerfil("all-users")
		.ComUsuario(Ana, "ana@x.com", SituacaoDoUsuario.Ativo, "marketing-admin", "rh-user", "gerente-de-compras")
		.ComUsuario(Bruno, "bruno@x.com", SituacaoDoUsuario.Desativado);

	[Fact]
	public async Task Usuario_AgrupaOsPerfisPorSetorEMostraOsAvulsos()
	{
		var html = await CriarClienteAdmin(CenarioDeUsuario()).GetStringAsync($"/Acesso/Usuario/{Ana}");

		html.Should().Contain("marketing").And.Contain("rh").And.Contain("gerente-de-compras");
		html.Should().Contain("Administrador").And.Contain("Usuário");
	}

	[Fact]
	public async Task Usuario_Inexistente_404()
	{
		var resposta = await CriarClienteAdmin(CenarioDeUsuario()).GetAsync($"/Acesso/Usuario/{Guid.NewGuid()}");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Usuario_UsuarioDesativado_OfereceReativar_ENaoDesativar()
	{
		var html = await CriarClienteAdmin(CenarioDeUsuario()).GetStringAsync($"/Acesso/Usuario/{Bruno}");

		html.Should().Contain("Reativar").And.NotContain("Desativar conta");
	}

	[Fact]
	public async Task AtribuirPerfilDeSetor_ComponheSlugEPapel()
	{
		var gestao = CenarioDeUsuario();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, $"/Acesso/Usuario/{Ana}");

		var resposta = await client.PostAsync("/Acesso/AtribuirPerfilDeSetor", Form(
			("usuarioId", Ana.ToString()), ("setor", "ti"), ("papel", "admin"), ("__RequestVerificationToken", token)));

		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/Acesso/Usuario/{Ana}");
		gestao.Chamadas.Should().Equal($"perfil-atribuir:{Ana}:ti-admin");
	}

	[Theory]
	[InlineData("superadmin")]
	[InlineData("")]
	[InlineData("admin-x")]
	public async Task AtribuirPerfilDeSetor_PapelInvalido_NaoAtribuiNada(string papel)
	{
		var gestao = CenarioDeUsuario();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, $"/Acesso/Usuario/{Ana}");

		var resposta = await client.PostAsync("/Acesso/AtribuirPerfilDeSetor", Form(
			("usuarioId", Ana.ToString()), ("setor", "ti"), ("papel", papel), ("__RequestVerificationToken", token)));

		gestao.Chamadas.Should().BeEmpty();
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("sc-toast--erro");
	}

	[Fact]
	public async Task AtribuirPerfilAvulso_VoltaParaOUsuario()
	{
		var gestao = CenarioDeUsuario();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, $"/Acesso/Usuario/{Ana}");

		var resposta = await client.PostAsync("/Acesso/AtribuirPerfil", Form(
			("usuarioId", Ana.ToString()), ("perfil", "all-users"), ("origem", "usuario"), ("__RequestVerificationToken", token)));

		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/Acesso/Usuario/{Ana}");
		gestao.Chamadas.Should().Equal($"perfil-atribuir:{Ana}:all-users");
	}

	[Fact]
	public async Task DesativarReativarEEncerrarSessoes_ChamamAPlataforma()
	{
		var gestao = CenarioDeUsuario();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, $"/Acesso/Usuario/{Ana}");

		await client.PostAsync($"/Acesso/DesativarUsuario/{Ana}", Form(("__RequestVerificationToken", token)));
		await client.PostAsync($"/Acesso/ReativarUsuario/{Ana}", Form(("__RequestVerificationToken", token)));
		await client.PostAsync($"/Acesso/EncerrarSessoes/{Ana}", Form(("__RequestVerificationToken", token)));

		gestao.Chamadas.Should().Equal($"usuario-desativar:{Ana}", $"usuario-reativar:{Ana}", $"sessoes-encerrar:{Ana}");
	}

	[Fact]
	public async Task DesativarUltimoIntranetAdmin_RecusadoComToastDeErro()
	{
		var gestao = CenarioDeUsuario().ComUsuario(Guid.NewGuid(), "admin@x.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		var adminId = gestao.Usuarios.Last().Id;
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, $"/Acesso/Usuario/{adminId}");

		var resposta = await client.PostAsync($"/Acesso/DesativarUsuario/{adminId}", Form(("__RequestVerificationToken", token)));

		(await resposta.Content.ReadAsStringAsync()).Should().Contain("sc-toast--erro");
		gestao.Chamadas.Should().BeEmpty();
	}
```

Run → FAIL.

- [ ] **Step 2: ViewModel**

```csharp
/// <summary>Modelo do detalhe de um usuário.</summary>
/// <param name="Tela">Usuário, perfis agrupados por setor e o que ainda pode ser atribuído.</param>
public sealed record UsuarioViewModel(UsuarioTelaDto Tela);
```

- [ ] **Step 3: Estender o controller**

Construtor completo:

```csharp
public sealed class AcessoController(
	ListarPerfisHandler listarPerfis,
	ListarUsuariosHandler listarUsuarios,
	ObterPerfilHandler obterPerfil,
	ObterUsuarioHandler obterUsuario,
	CriarPerfilHandler criarPerfil,
	ExcluirPerfilHandler excluirPerfil,
	AtribuirPerfilHandler atribuirPerfil,
	RetirarPerfilHandler retirarPerfil,
	DesativarUsuarioHandler desativarUsuario,
	ReativarUsuarioHandler reativarUsuario,
	EncerrarSessoesHandler encerrarSessoes) : Controller
```

Actions:

```csharp
	/// <summary>Detalhe de um usuário: perfis por setor, adicionar/retirar, situação e sessões.</summary>
	/// <param name="id">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Usuario(Guid id, CancellationToken cancellationToken = default)
	{
		var tela = await obterUsuario.HandleAsync(id, cancellationToken);

		return tela.IsFailure ? Falha(tela.Error) : View(new UsuarioViewModel(tela.Value));
	}

	/// <summary>
	/// Atribui a Role de um setor. Setor e papel chegam separados e o nome do perfil é composto
	/// aqui — só <c>admin</c> e <c>user</c> são papéis aceitos, então o formulário não consegue
	/// pedir um perfil arbitrário por esta rota.
	/// </summary>
	/// <param name="usuarioId">Usuário que recebe o perfil.</param>
	/// <param name="setor">Slug do setor.</param>
	/// <param name="papel"><c>admin</c> ou <c>user</c>.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AtribuirPerfilDeSetor(Guid usuarioId, string? setor, string? papel, CancellationToken cancellationToken = default)
	{
		var sufixo = papel?.Trim().ToLowerInvariant() switch
		{
			"admin" => ClassificacaoDePerfil.SufixoAdmin,
			"user" => ClassificacaoDePerfil.SufixoUsuario,
			_ => null,
		};

		if (sufixo is null || string.IsNullOrWhiteSpace(setor))
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = "Escolha o setor e o papel.";

			return RedirectToAction(nameof(Usuario), new { id = usuarioId });
		}

		var perfil = setor.Trim() + sufixo;
		var resultado = await atribuirPerfil.HandleAsync(new AtribuirPerfilCommand(usuarioId, perfil), cancellationToken);

		return Concluir(resultado, $"Perfil \"{perfil}\" atribuído.", nameof(Usuario), new { id = usuarioId });
	}

	/// <summary>Desativa a conta de um usuário.</summary>
	/// <param name="id">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> DesativarUsuario(Guid id, CancellationToken cancellationToken = default) =>
		Concluir(await desativarUsuario.HandleAsync(id, cancellationToken), "Conta desativada.", nameof(Usuario), new { id });

	/// <summary>Reativa a conta de um usuário.</summary>
	/// <param name="id">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ReativarUsuario(Guid id, CancellationToken cancellationToken = default) =>
		Concluir(await reativarUsuario.HandleAsync(id, cancellationToken), "Conta reativada.", nameof(Usuario), new { id });

	/// <summary>Encerra as sessões de um usuário.</summary>
	/// <param name="id">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EncerrarSessoes(Guid id, CancellationToken cancellationToken = default) =>
		Concluir(await encerrarSessoes.HandleAsync(id, cancellationToken), "Sessões encerradas.", nameof(Usuario), new { id });
```

Remova a nota da Task 9 sobre `origem=usuario` — agora a action existe.

- [ ] **Step 4: View do usuário**

```cshtml
@* src/Secco.Intranet.Web/Views/Acesso/Usuario.cshtml *@
@model UsuarioViewModel
@{
    var tela = Model.Tela;
    var usuario = tela.Usuario;
    var rotulo = string.IsNullOrWhiteSpace(usuario.Email) ? usuario.Id.ToString() : usuario.Email;

    ViewData["Title"] = rotulo;

    var cabecalho = new PageHeaderModel(
        rotulo,
        Acoes: new[] { new PageActionModel("Voltar", Url.Action("Index", new { aba = "usuarios" })!, "bi-arrow-left") });

    var situacao = usuario.Situacao switch
    {
        SituacaoDoUsuario.Ativo => new BadgeModel("Ativo", BadgeVariante.Sucesso),
        SituacaoDoUsuario.Desativado => new BadgeModel("Desativado", BadgeVariante.Perigo),
        SituacaoDoUsuario.Bloqueado => new BadgeModel("Bloqueado", BadgeVariante.Aviso),
        _ => new BadgeModel("Desconhecida", BadgeVariante.Neutro),
    };
}

<partial name="_PageHeader" model="cabecalho" />

<div class="sc-panel">
    <dl class="row mb-0">
        <dt class="col-sm-3">Situação</dt>
        <dd class="col-sm-9"><partial name="_Badge" model="situacao" /></dd>

        @if (usuario.BloqueadoAte is { } ate)
        {
            <dt class="col-sm-3">Bloqueado até</dt>
            <dd class="col-sm-9">@ate.ToLocalTime().ToString("g")</dd>
        }

        <dt class="col-sm-3">Segundo fator</dt>
        <dd class="col-sm-9">@(usuario.DoisFatoresAtivo ? "Ligado" : "Desligado")</dd>

        <dt class="col-sm-3">Logins externos</dt>
        <dd class="col-sm-9 mb-0">@(usuario.LoginsExternos.Count == 0 ? "Nenhum" : string.Join(", ", usuario.LoginsExternos))</dd>
    </dl>
</div>

<div class="sc-panel mt-3">
    <h2 class="h6">Perfis de setor</h2>

    @if (tela.Setores.Count == 0)
    {
        <p class="text-muted">Nenhum perfil de setor.</p>
    }
    else
    {
        <ul class="sc-list">
            @foreach (var setor in tela.Setores)
            {
                <li class="sc-list__item">
                    <div class="sc-list__text">
                        <p class="sc-list__title">@setor.Slug</p>
                        <span class="sc-list__sub">
                            @foreach (var papel in setor.Papeis)
                            {
                                var nomeDoPerfil = setor.Slug + (papel == PapelNoSetor.Administrador ? "-admin" : "-user");

                                <form method="post" asp-action="RetirarPerfil" class="d-inline">
                                    <input type="hidden" name="usuarioId" value="@usuario.Id" />
                                    <input type="hidden" name="perfil" value="@nomeDoPerfil" />
                                    <input type="hidden" name="origem" value="usuario" />
                                    <span class="sc-meta">@(papel == PapelNoSetor.Administrador ? "Administrador" : "Usuário")</span>
                                    <button class="btn btn-sm btn-outline-danger" type="submit">Retirar</button>
                                </form>
                            }
                        </span>
                    </div>
                </li>
            }
        </ul>
    }

    @if (tela.SlugsDeSetorDisponiveis.Count > 0)
    {
        <form method="post" asp-action="AtribuirPerfilDeSetor" class="d-flex flex-wrap gap-2 align-items-end mt-2">
            <input type="hidden" name="usuarioId" value="@usuario.Id" />
            <div class="sc-form__field mb-0">
                <label class="form-label" for="setor">Setor</label>
                <select class="form-select" id="setor" name="setor" required>
                    <option value="">Escolha um setor</option>
                    @foreach (var slug in tela.SlugsDeSetorDisponiveis)
                    {
                        <option value="@slug">@slug</option>
                    }
                </select>
            </div>
            <div class="sc-form__field mb-0">
                <label class="form-label" for="papel">Papel</label>
                <select class="form-select" id="papel" name="papel" required>
                    <option value="user">Usuário (só lê)</option>
                    <option value="admin">Administrador (altera o setor)</option>
                </select>
            </div>
            <button class="btn btn-outline-primary" type="submit">Adicionar perfil de setor</button>
        </form>
    }
</div>

<div class="sc-panel mt-3">
    <h2 class="h6">Outros perfis</h2>

    @if (tela.OutrosPerfis.Count == 0)
    {
        <p class="text-muted">Nenhum outro perfil.</p>
    }
    else
    {
        <ul class="sc-list">
            @foreach (var nome in tela.OutrosPerfis)
            {
                <li class="sc-list__item">
                    <div class="sc-list__text"><p class="sc-list__title">@nome</p></div>
                    <form method="post" asp-action="RetirarPerfil" class="d-inline">
                        <input type="hidden" name="usuarioId" value="@usuario.Id" />
                        <input type="hidden" name="perfil" value="@nome" />
                        <input type="hidden" name="origem" value="usuario" />
                        <button class="btn btn-sm btn-outline-danger" type="submit">Retirar</button>
                    </form>
                </li>
            }
        </ul>
    }

    @if (tela.OutrosPerfisAtribuiveis.Count > 0)
    {
        <form method="post" asp-action="AtribuirPerfil" class="d-flex flex-wrap gap-2 align-items-end mt-2">
            <input type="hidden" name="usuarioId" value="@usuario.Id" />
            <input type="hidden" name="origem" value="usuario" />
            <div class="sc-form__field mb-0">
                <label class="form-label" for="perfil">Adicionar perfil</label>
                <select class="form-select" id="perfil" name="perfil" required>
                    <option value="">Escolha um perfil</option>
                    @foreach (var nome in tela.OutrosPerfisAtribuiveis)
                    {
                        <option value="@nome">@nome</option>
                    }
                </select>
            </div>
            <button class="btn btn-outline-primary" type="submit">Adicionar</button>
        </form>
    }
</div>

<div class="sc-panel mt-3 d-flex flex-wrap gap-2">
    @if (usuario.Situacao == SituacaoDoUsuario.Desativado)
    {
        <form method="post" asp-action="ReativarUsuario" asp-route-id="@usuario.Id" class="d-inline">
            <button class="btn btn-outline-primary" type="submit">Reativar conta</button>
        </form>
    }
    else
    {
        <form method="post" asp-action="DesativarUsuario" asp-route-id="@usuario.Id" class="d-inline">
            <button class="btn btn-outline-danger" type="submit">Desativar conta</button>
        </form>
    }
    <form method="post" asp-action="EncerrarSessoes" asp-route-id="@usuario.Id" class="d-inline">
        <button class="btn btn-outline-secondary" type="submit">Encerrar sessões</button>
    </form>
</div>
```

O teste `Usuario_UsuarioDesativado_OfereceReativar_ENaoDesativar` espera o texto "Desativar conta" ausente para o usuário desativado e "Reativar" presente — a view acima faz isso. O `DesativarReativarEEncerrarSessoes` reaproveita o mesmo token para as três posts: o token antifalsificação é por sessão de cookie, não por formulário — se o teste falhar por token, obtenha um token novo antes de cada `POST`.

- [ ] **Step 5: Rodar e commitar**

Run: `dotnet test --filter AcessoTelasTests` → PASS; `dotnet build` sem avisos.

```bash
git add src/Secco.Intranet.Web/Controllers/AcessoController.cs src/Secco.Intranet.Web/Models/Acesso/AcessoViewModels.cs src/Secco.Intranet.Web/Views/Acesso/Usuario.cshtml tests/Secco.Intranet.Tests/Integration/AcessoTelasTests.cs
git commit -m "feat(acesso): detalhe do usuario com perfis por setor, desativar e encerrar sessoes

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 11: Matriz de autorização de toda rota da Área de acesso

O critério de aceite da ADR-0008 vira teste: usuário comum, `{slug}-admin` (de um ou de todos os setores) e `inventario-admin` bloqueados; `intranet-admin` liberado — em **toda** rota, `GET` e `POST`, e com um teste que impede uma action nova de nascer sem o gate.

**Files:**
- Create: `tests/Secco.Intranet.Tests/Integration/AcessoAutorizacaoTests.cs`

**Interfaces:**
- Consumes: todas as rotas das tarefas 8–10; `SomenteIntranetAdminAttribute` (Task 1).

- [ ] **Step 1: Escrever os testes**

```csharp
// tests/Secco.Intranet.Tests/Integration/AcessoAutorizacaoTests.cs
using System.Net;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Controllers;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Critério de aceite da ADR-0008: só o <c>intranet-admin</c> vê e acessa a Área de acesso —
/// usuário comum, <c>{slug}-admin</c> (de um ou de todos os setores) e <c>inventario-admin</c>
/// são bloqueados, em toda rota, inclusive nos <c>POST</c> e sem token antifalsificação.
/// </summary>
public class AcessoAutorizacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Id = Guid.NewGuid();

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente(params string[] roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	public static TheoryData<string[]> UsuariosSemAcesso => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-user" },
		new[] { "financeiro-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin", "marketing-admin" },
		new[] { "inventario-admin" },
		new[] { "gerente-de-compras" },
	};

	public static IEnumerable<string> RotasGet =>
	[
		"/Acesso",
		"/Acesso?aba=usuarios",
		"/Acesso/Perfil?nome=qualquer",
		$"/Acesso/Usuario/{Id}",
	];

	public static IEnumerable<string> RotasPost =>
	[
		"/Acesso/CriarPerfil",
		"/Acesso/ExcluirPerfil",
		"/Acesso/AtribuirPerfil",
		"/Acesso/AtribuirPerfilDeSetor",
		"/Acesso/RetirarPerfil",
		$"/Acesso/DesativarUsuario/{Id}",
		$"/Acesso/ReativarUsuario/{Id}",
		$"/Acesso/EncerrarSessoes/{Id}",
	];

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Get_SemIntranetAdmin_Bloqueado(string[] roles)
	{
		var client = CriarCliente(roles);

		foreach (var rota in RotasGet)
		{
			var resposta = await client.GetAsync(rota);

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota} é exclusivo do intranet-admin");
		}
	}

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Post_SemIntranetAdmin_Bloqueado403_NaoBarradoPeloAntifalsificacao(string[] roles)
	{
		var client = CriarCliente(roles);

		foreach (var rota in RotasPost)
		{
			var resposta = await client.PostAsync(rota, new FormUrlEncodedContent([]));

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden,
				$"POST {rota}: 403 do gate, e não 400 do filtro antifalsificação — o gate roda primeiro");
		}
	}

	[Fact]
	public async Task Get_ComIntranetAdmin_Liberado()
	{
		var client = CriarCliente("intranet-admin");

		foreach (var rota in RotasGet)
		{
			var resposta = await client.GetAsync(rota);

			resposta.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {rota} abre para o intranet-admin (com SecureGate ausente, explica)");
		}
	}

	[Fact]
	public async Task Post_ComIntranetAdminSemToken_PassaDoGateEBateNoAntifalsificacao()
	{
		var client = CriarCliente("intranet-admin");

		foreach (var rota in RotasPost)
		{
			var resposta = await client.PostAsync(rota, new FormUrlEncodedContent([]));

			resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"POST {rota}: o gate liberou, o token barrou");
		}
	}

	[Fact]
	public async Task Menu_QuemNaoEIntranetAdmin_NaoVeOItemAcesso()
	{
		foreach (var roles in new string[][] { [], ["financeiro-admin"], ["inventario-admin"] })
		{
			var html = await CriarCliente(roles).GetStringAsync("/");

			html.Should().NotContain("href=\"/acesso\"", $"roles: {string.Join(",", roles)}");
		}
	}

	[Fact]
	public async Task Menu_IntranetAdmin_VeOItemAcesso()
	{
		var html = await CriarCliente("intranet-admin").GetStringAsync("/");

		html.Should().Contain("href=\"/acesso\"");
	}

	[Fact]
	public void TodaActionDoControllerEstaSobOGate_EACobertaPelasListasDeRotas()
	{
		typeof(AcessoController).GetCustomAttribute<SomenteIntranetAdminAttribute>().Should().NotBeNull(
			"o atributo na classe é o que impede uma action nova de nascer desprotegida");

		var actions = typeof(AcessoController)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.Where(metodo => metodo.GetCustomAttributes().Any(a => a is HttpGetAttribute or HttpPostAttribute))
			.Select(metodo => metodo.Name)
			.ToList();

		var cobertas = RotasGet.Concat(RotasPost)
			.Select(rota => rota.Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries))
			.Select(partes => partes.Length > 1 ? partes[1] : "Index")
			.Distinct()
			.ToList();

		actions.Should().BeSubsetOf(cobertas,
			"toda action do AcessoController precisa aparecer nas listas de rotas deste teste — uma action nova sem teste de bloqueio é o que este teste existe para impedir");
	}
}
```

- [ ] **Step 2: Rodar**

Run: `dotnet test --filter AcessoAutorizacaoTests` → PASS. Se `Menu_...` falhar por diferença de markup do `href`, ajuste como na Task 2 (`href="/acesso"` é o valor de `NavigationItemModel.Url`; confira no `Navigation/Default.cshtml` do tema).

- [ ] **Step 3: Rodar a suíte inteira**

Run: `dotnet test` → PASS, 0 avisos.

- [ ] **Step 4: Commit**

```bash
git add tests/Secco.Intranet.Tests/Integration/AcessoAutorizacaoTests.cs
git commit -m "test(acesso): matriz de autorizacao em toda rota da area de acesso

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 12: Documentação — README, roadmap e ajustes da spec

**Files:**
- Modify: `README.md`
- Modify: `docs/roadmap.md`
- Modify: `docs/specs/2026-09-23-area-administrativa-acesso-design.md`
- Modify: `docs/specs/2026-09-13-inventario-design.md` (só se sobrar menção desatualizada que a Task de docs anterior não cobriu)

**Interfaces:**
- Consumes: o que foi entregue nas tarefas 1–11.

- [ ] **Step 1: README — primeiro `intranet-admin`**

Leia o `README.md` e ache a seção de instalação/configuração do SecureGate. Acrescente, na seção mais próxima do que fala de usuários e Roles, este passo (ajuste o nível de título ao do arquivo):

```markdown
### Primeiro `intranet-admin`

A Área administrativa (`/Acesso`, `/Setores`) só abre para quem tem a Role `intranet-admin` —
e por isso alguém precisa recebê-la **fora** da Intranet, na primeira vez: crie a Role
`intranet-admin` no tenant da Intranet e atribua-a ao primeiro usuário pelo AdminPortal ou
pela API do SecureGate. Dali em diante, esse usuário gerencia os demais pelo `/Acesso`
(a tela também oferece criar a Role `intranet-admin` e a `inventario-admin` quando faltam).

A Intranet não deixa o tenant ficar sem `intranet-admin` ativo: retirar o perfil ou desativar
a conta do último é recusado, e ninguém retira o próprio perfil nem se desativa.
```

- [ ] **Step 2: Roadmap**

Em `docs/roadmap.md`, marque como entregue o item "Área administrativa de acesso, primeiro corte" (troque `- [ ]` por `- [x]`) e acrescente no fim do texto do item: `Entregue com: só o intranet-admin acessa (filtro declarativo na classe do controller); listagem de perfis e usuários, atribuir/retirar perfil, criar/excluir perfil, desativar/reativar usuário e encerrar sessões; o cadastro de setores passou a exigir o mesmo perfil.` Marque também o item "Tela de administração de setores" apenas no que foi feito — se ele continua com o "toggle de recursos" pendente, deixe `[ ]` e mantenha a frase sobre a proteção.

- [ ] **Step 3: Ajustar a spec ao que foi implementado**

Em `docs/specs/2026-09-23-area-administrativa-acesso-design.md`, corrija os três pontos em que a implementação decidiu diferente do texto:

1. Em "Telas", aba Perfis: onde diz "lista com nome, nº de membros, badge…", troque por "lista com nome, nº de **permissões**, badge…" — `ListRoles` da plataforma não traz a contagem de membros, e uma chamada por perfil seria N+1; a contagem de membros fica no detalhe do perfil.
2. Em "Regras dos handlers", regra 4: onde diz "os `{slug}-admin`/`{slug}-user` de setores existentes", troque por "qualquer perfil de nome `{x}-admin`/`{x}-user`, por convenção de sufixo — não consulta a tabela de setores: é o lado seguro do erro, e um perfil comum que termine assim também fica protegido".
3. Em "Autorização": onde diz "Toda action... começa pelo mesmo guarda do `InventarioController` (`PodeAdministrar()` → `StatusCode(403)`)", troque por "Toda action é coberta por `[SomenteIntranetAdmin]` na classe — um filtro de autorização com `Order` menor que o do antiforgery, que devolve 403; o bypass de 'modo aberto' segue a mesma condição do Inventário. O filtro substitui o `PodeAdministrar()` por action: nenhuma action nova nasce desprotegida, e o `POST` de quem não é admin dá 403 mesmo sem token".

Acrescente ao fim da seção "Setores" da spec: "Entregue: `SetoresController` sob `[SomenteIntranetAdmin]`; o grupo 'Administração' do menu só aparece para o `intranet-admin`."

- [ ] **Step 4: Verificação final**

Run: `dotnet build` (0 avisos) e `dotnet test` (suíte inteira verde).

- [ ] **Step 5: Commit**

```bash
git add README.md docs/roadmap.md docs/specs/2026-09-23-area-administrativa-acesso-design.md
git commit -m "docs(acesso): README do primeiro intranet-admin, roadmap e spec alinhada a implementacao

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
