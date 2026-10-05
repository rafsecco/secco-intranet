using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Integration;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Secco.Intranet.Tests.Ui;

/// <summary>
/// Teste de tela num navegador de verdade. Opt-in: precisa de <c>SECCO_UI_TESTS=1</c> e do Chromium
/// do Playwright instalado (<c>playwright.ps1 install chromium</c>, na pasta de saída dos testes) —
/// sem isso fica ignorado, como a fumaça contra o SecureGate.
/// </summary>
public sealed class UiTheoryAttribute : TheoryAttribute
{
	/// <summary>Variável que liga os testes de tela.</summary>
	public const string Variavel = "SECCO_UI_TESTS";

	/// <summary>Se os testes de tela estão ligados neste processo.</summary>
	public static bool Ligados => Environment.GetEnvironmentVariable(Variavel) == "1";

	/// <summary>Ignora o teste quando a variável não está ligada.</summary>
	public UiTheoryAttribute()
	{
		if (!Ligados)
		{
			Skip = $"Teste de tela: defina {Variavel}=1 e instale o Chromium do Playwright (ver docs/temas.md).";
		}
	}
}

/// <summary>
/// Sobe a aplicação de verdade (Kestrel), uma vez por tema, com um setor que tem um nível a mais
/// (Relatórios → Vendas), e um Chromium headless.
/// </summary>
public sealed class TelaFixture : IAsyncLifetime
{
	private readonly IntranetWebFactory _fabrica = new();
	private readonly Dictionary<string, WebApplicationFactory<Program>> _apps = [];
	private IPlaywright? _playwright;

	/// <summary>Navegador compartilhado pelos testes.</summary>
	public IBrowser Navegador { get; private set; } = null!;

	/// <summary>Endereço da aplicação por tema.</summary>
	public Dictionary<string, string> Enderecos { get; } = [];

	/// <summary>Slug do setor do cenário.</summary>
	public string Slug { get; } = $"ui-{Guid.NewGuid():N}"[..12];

	/// <summary>Nome do setor do cenário.</summary>
	public string NomeDoSetor => $"Setor {Slug}";

	private readonly System.Collections.Concurrent.ConcurrentDictionary<IPage, List<string>> _errosDeScript = new();

	/// <summary>Erros de JavaScript de uma página aberta por <see cref="AbrirAsync"/>.</summary>
	public IReadOnlyList<string> ErrosDeScript(IPage pagina) => _errosDeScript.GetValueOrDefault(pagina) ?? [];

	/// <summary>Tenant das requisições do navegador.</summary>
	public Guid Tenant => _fabrica.TenantAlfa;

	/// <inheritdoc />
	public async Task InitializeAsync()
	{
		if (!UiTheoryAttribute.Ligados)
		{
			return;
		}

		// Como fixture de classe, o xUnit chamaria isto sozinho; aqui a fábrica é composta à mão.
		await ((IAsyncLifetime)_fabrica).InitializeAsync();
		await _fabrica.EnsureDatabaseMigratedAsync();
		await CriarCenarioAsync();

		foreach (var tema in new[] { "Vertical", "Horizontal" })
		{
			// Os assets dos temas (CSS e JS das RCLs) só são servidos sozinhos em Development; o
			// ambiente de testes é Testing, então liga-se aqui — sem isso a tela sobe sem estilo nem script.
			var app = _fabrica.WithWebHostBuilder(builder => builder
				.UseSetting("Intranet:Theme:Nome", tema)
				.UseStaticWebAssets());
			// Porta livre escolhida pelo sistema: as duas aplicações (uma por tema) sobem juntas.
			app.UseKestrel(kestrel => kestrel.Listen(System.Net.IPAddress.Loopback, 0));
			app.StartServer();
			_apps[tema] = app;
			Enderecos[tema] = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		}

		_playwright = await Playwright.CreateAsync();
		Navegador = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
	}

	/// <summary>Abre uma página com o tenant no cabeçalho.</summary>
	public async Task<IPage> AbrirAsync(string tema, int largura = 1280, int altura = 800, bool javaScript = true)
	{
		var contexto = await Navegador.NewContextAsync(new BrowserNewContextOptions
		{
			BaseURL = Enderecos[tema],
			ViewportSize = new ViewportSize { Width = largura, Height = altura },
			JavaScriptEnabled = javaScript,
			ExtraHTTPHeaders = new Dictionary<string, string> { [SeccoHeaders.TenantId] = Tenant.ToString() },
		});

		var pagina = await contexto.NewPageAsync();
		var erros = _errosDeScript.GetOrAdd(pagina, _ => []);
		pagina.PageError += (_, erro) => erros.Add(erro);
		await pagina.GotoAsync("/");

		return pagina;
	}

	private async Task CriarCenarioAsync()
	{
		using var escopo = _fabrica.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(_fabrica.TenantAlfa);

		var setor = await escopo.ServiceProvider.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand(NomeDoSetor, Slug));
		setor.IsSuccess.Should().BeTrue();

		var arvore = await escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>().ListarPorSetorAsync(setor.Value.Id);
		var criar = escopo.ServiceProvider.GetRequiredService<CriarItemMenuHandler>();

		var relatorios = await criar.HandleAsync(new CriarItemMenuCommand(
			setor.Value.Id, arvore.Single(i => i.Tipo == TipoDeItemMenu.Setor).Id, "Relatórios", "relatorios",
			TipoDeItemMenu.Personalizado, null, null));
		(await criar.HandleAsync(new CriarItemMenuCommand(
			setor.Value.Id, relatorios.Value.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/diretorio", null)))
			.IsSuccess.Should().BeTrue();
	}

	/// <inheritdoc />
	public async Task DisposeAsync()
	{
		if (_playwright is not null)
		{
			await Navegador.DisposeAsync();
			_playwright.Dispose();
		}

		foreach (var app in _apps.Values)
		{
			await app.DisposeAsync();
		}

		await _fabrica.DisposeAsync();
	}
}

/// <summary>
/// Os submenus da árvore dos setores nos dois temas. Cada caso é um defeito que só apareceu no
/// navegador: painel atrás dos cards, clique fechando o que o hover abriu, gaveta do celular
/// quebrada por nome repetido no script, fundo escurecido engolindo cliques, rolagem fechando o
/// submenu no celular.
/// </summary>
public class MenuDaArvoreTelaTests(TelaFixture f) : IClassFixture<TelaFixture>
{
	private const string Grupo = ".sc-nav > li > button[data-sc-submenu]";

	private ILocator BotaoDoSetor(IPage pagina) =>
		pagina.Locator($"button[data-sc-submenu]:has-text('{f.NomeDoSetor}')");

	private static async Task<ILocator> PainelDeAsync(IPage pagina, ILocator gatilho) =>
		pagina.Locator($"#{await gatilho.GetAttributeAsync("aria-controls")}");

	/// <summary>No Horizontal o setor mora dentro do grupo "Setores": abre o grupo primeiro.</summary>
	private static async Task AbrirGrupoSeHorizontalAsync(IPage pagina, string tema, bool porToque = false)
	{
		if (tema != "Horizontal")
		{
			return;
		}

		var grupo = pagina.Locator($"{Grupo}:has-text('Setores')");

		if (porToque)
		{
			await grupo.ClickAsync();
		}
		else
		{
			await grupo.HoverAsync();
		}
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Hover_AbreOPainelPorCimaDoConteudo_EONivelSeguinte(string tema)
	{
		var pagina = await f.AbrirAsync(tema);
		// O banco de teste não tem publicações, então o Mural sai sem cards. Este bloco posicionado
		// faz o papel deles: conteúdo posicionado é o que cobria o painel quando a camada estava errada.
		await pagina.EvaluateAsync(
			"() => document.querySelector('#conteudo').insertAdjacentHTML('afterbegin', '<div style=\"position:relative;height:2000px;background:#fff\"></div>')");
		await AbrirGrupoSeHorizontalAsync(pagina, tema);
		var setor = BotaoDoSetor(pagina);

		await setor.HoverAsync();

		var painel = await PainelDeAsync(pagina, setor);
		await Expect(painel).ToBeVisibleAsync();
		var noTopo = await painel.EvaluateAsync<bool>(
			"p => { const r = p.getBoundingClientRect(); return document.elementFromPoint(r.x + r.width / 2, r.y + 12)?.closest('.sc-nav__sub') === p; }");
		noTopo.Should().BeTrue("o painel precisa ficar por cima do conteúdo da página, não atrás dos cards");

		var relatorios = painel.Locator("button[data-sc-submenu]:has-text('Relatórios')");
		await relatorios.HoverAsync();
		await Expect(painel.Locator("a:has-text('Vendas')")).ToBeVisibleAsync();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task HoverSeguidoDeClique_MantemAberto(string tema)
	{
		var pagina = await f.AbrirAsync(tema);
		await AbrirGrupoSeHorizontalAsync(pagina, tema);
		var setor = BotaoDoSetor(pagina);

		await setor.ClickAsync();

		await Expect(setor).ToHaveAttributeAsync("aria-expanded", "true");
		await Expect(await PainelDeAsync(pagina, setor)).ToBeVisibleAsync();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Teclado_EnterAbreEFoca_EscFechaEDevolveOFoco(string tema)
	{
		var pagina = await f.AbrirAsync(tema);

		if (tema == "Horizontal")
		{
			await pagina.Locator($"{Grupo}:has-text('Setores')").FocusAsync();
			await pagina.Keyboard.PressAsync("Enter");
		}

		var setor = BotaoDoSetor(pagina);
		await setor.FocusAsync();
		await pagina.Keyboard.PressAsync("Enter");

		var painel = await PainelDeAsync(pagina, setor);
		await Expect(painel).ToBeVisibleAsync();
		(await painel.EvaluateAsync<bool>("p => p.contains(document.activeElement)")).Should().BeTrue("Enter foca o primeiro item");

		await pagina.Keyboard.PressAsync("Escape");

		await Expect(painel).ToBeHiddenAsync();
		(await setor.EvaluateAsync<bool>("b => b === document.activeElement")).Should().BeTrue("Esc devolve o foco a quem abriu");
	}

	[UiTheory]
	[InlineData("Vertical", "[data-sc-sidebar-open]", "#sc-sidebar")]
	[InlineData("Horizontal", "[data-sc-nav-open]", "#sc-topnav")]
	public async Task Celular_AbreOMenu_OSubmenuRecuado_ResisteARolagem_EOLinkFunciona(string tema, string abrir, string conteiner)
	{
		var pagina = await f.AbrirAsync(tema, largura: 375, altura: 640);

		await pagina.Locator(abrir).ClickAsync();
		await Expect(pagina.Locator(abrir)).ToHaveAttributeAsync("aria-expanded", "true");
		await AbrirGrupoSeHorizontalAsync(pagina, tema, porToque: true);
		var setor = BotaoDoSetor(pagina);
		await setor.ClickAsync();

		var painel = await PainelDeAsync(pagina, setor);
		await Expect(painel).ToBeVisibleAsync();
		(await painel.EvaluateAsync<string>("p => getComputedStyle(p).position")).Should().Be("static", "no celular abre recuado, não flutua");

		await pagina.Locator(conteiner).EvaluateAsync("c => { c.scrollTop = 40; c.dispatchEvent(new Event('scroll')); }");
		await Expect(painel).ToBeVisibleAsync();

		await painel.Locator("a:has-text('Avisos')").ClickAsync();
		await pagina.WaitForURLAsync($"**/{f.Slug}/avisos");
		f.ErrosDeScript(pagina).Should().BeEmpty("nenhum erro de JavaScript — um nome repetido no script já quebrou a gaveta assim");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task SemJavaScript_AArvoreApareceAbertaComOsLinks(string tema)
	{
		var pagina = await f.AbrirAsync(tema, javaScript: false);

		await Expect(pagina.Locator($"a[href='/{f.Slug}/avisos']")).ToBeVisibleAsync();
		await Expect(pagina.Locator("a[href='/diretorio']:has-text('Vendas')")).ToBeVisibleAsync();
	}
}
