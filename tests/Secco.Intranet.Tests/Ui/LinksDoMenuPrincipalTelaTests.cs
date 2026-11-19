using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Secco.Intranet.Tests.Integration;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Secco.Intranet.Tests.Ui;

/// <summary>
/// Sobe a aplicação de verdade (Kestrel), uma vez por tema, com a CSP em Enforce, e um Chromium
/// headless. Quem navega é o <c>intranet-admin</c>; cada teste usa rótulos próprios para não
/// depender dos links dos outros.
/// </summary>
public sealed class LinksDoMenuPrincipalFixture : IAsyncLifetime
{
	private readonly IntranetWebFactory _fabrica = new();
	private readonly Dictionary<string, WebApplicationFactory<Program>> _apps = [];
	private readonly ConditionalWeakTable<IPage, List<string>> _violacoes = [];
	private readonly ConditionalWeakTable<IPage, List<string>> _errosDeScript = [];
	private IPlaywright? _playwright;

	/// <summary>Navegador compartilhado pelos testes.</summary>
	public IBrowser Navegador { get; private set; } = null!;

	/// <summary>Endereço da aplicação por tema (<c>http://127.0.0.1:porta</c>).</summary>
	public Dictionary<string, string> Enderecos { get; } = [];

	/// <inheritdoc />
	public async Task InitializeAsync()
	{
		if (!UiTheoryAttribute.Ligados)
		{
			return;
		}

		await ((IAsyncLifetime)_fabrica).InitializeAsync();
		await _fabrica.EnsureDatabaseMigratedAsync();

		foreach (var tema in new[] { "Vertical", "Horizontal" })
		{
			var app = _fabrica.WithWebHostBuilder(builder => builder
				.UseSetting("Intranet:Theme:Nome", tema)
				.UseStaticWebAssets());
			app.UseKestrel(kestrel => kestrel.Listen(System.Net.IPAddress.Loopback, 0));
			app.StartServer();
			_apps[tema] = app;
			Enderecos[tema] = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		}

		_playwright = await Playwright.CreateAsync();
		Navegador = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
	}

	/// <summary>Mensagens de console sobre a CSP vistas pela página desde que foi aberta.</summary>
	public List<string> ViolacoesDeCsp(IPage pagina) => _violacoes.TryGetValue(pagina, out var lista) ? lista : [];

	/// <summary>Erros de JavaScript da página.</summary>
	public List<string> ErrosDeScript(IPage pagina) => _errosDeScript.TryGetValue(pagina, out var lista) ? lista : [];

	/// <summary>Abre um caminho do tema como <c>intranet-admin</c>.</summary>
	public async Task<IPage> AbrirAsync(string tema, string caminho, bool javaScript = true)
	{
		var contexto = await Navegador.NewContextAsync(new BrowserNewContextOptions
		{
			BaseURL = Enderecos[tema],
			JavaScriptEnabled = javaScript,
			ExtraHTTPHeaders = new Dictionary<string, string>
			{
				[SeccoHeaders.TenantId] = _fabrica.TenantAlfa.ToString(),
				[RolesDeTesteMiddleware.Header] = "intranet-admin",
			},
		});

		var pagina = await contexto.NewPageAsync();
		var violacoes = new List<string>();
		var erros = new List<string>();
		_violacoes.Add(pagina, violacoes);
		_errosDeScript.Add(pagina, erros);
		pagina.Console += (_, mensagem) =>
		{
			if (mensagem.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
			{
				lock (violacoes)
				{
					violacoes.Add(mensagem.Text);
				}
			}
		};
		pagina.PageError += (_, erro) => erros.Add(erro);
		await pagina.GotoAsync(caminho);

		return pagina;
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
/// A gestão dos links do menu principal em <c>/menu</c> e o resultado deles no menu, nos dois temas.
/// Os cliques no menu usam uma página sem JavaScript, onde a árvore aparece aberta.
/// </summary>
public class LinksDoMenuPrincipalTelaTests(LinksDoMenuPrincipalFixture f) : IClassFixture<LinksDoMenuPrincipalFixture>
{
	private static Regex Termina(string sufixo) => new($"{Regex.Escape(sufixo)}$");

	private static string Sufixo() => Guid.NewGuid().ToString("N")[..8];

	private static async Task CriarLinkAsync(IPage pagina, string rotulo, string url)
	{
		await pagina.GotoAsync("/menu");
		await pagina.GetByRole(AriaRole.Link, new() { Name = "+ Link", Exact = true }).ClickAsync();
		await Expect(pagina).ToHaveURLAsync(new Regex("/menu/links/novo$", RegexOptions.IgnoreCase));

		await pagina.GetByLabel("Rótulo", new() { Exact = true }).FillAsync(rotulo);
		await pagina.GetByLabel("URL", new() { Exact = true }).FillAsync(url);
		await Expect(pagina.GetByLabel("Abrir em nova aba")).ToBeCheckedAsync();

		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar" }).ClickAsync();
		await Expect(pagina).ToHaveURLAsync(new Regex("/menu/?$", RegexOptions.IgnoreCase));
		await Expect(pagina.Locator(".sc-list__title", new() { HasTextString = rotulo }).First).ToBeVisibleAsync();
	}

	private static async Task ExcluirLinkAsync(IPage pagina, string rotulo)
	{
		EventHandler<IDialog> aceitar = async (_, dialogo) => await dialogo.AcceptAsync();
		pagina.Dialog += aceitar;
		await pagina.GotoAsync("/menu");
		await pagina.Locator($"button[title='Excluir {rotulo}']").ClickAsync();
		await Expect(pagina).ToHaveURLAsync(new Regex("/menu/?$", RegexOptions.IgnoreCase));
		await Expect(pagina.Locator($"button[title='Excluir {rotulo}']")).ToHaveCountAsync(0);
		pagina.Dialog -= aceitar;
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task CriarLinkPelaTela_ApareceNoMenu_AbreNovaPagina_SobeEExclui(string tema)
	{
		var sufixo = Sufixo();
		var rotulo = $"Abrir um chamado {sufixo}";
		var outro = $"Webmail {sufixo}";
		var destino = $"{f.Enderecos[tema]}/?origem={sufixo}";
		var pagina = await f.AbrirAsync(tema, "/menu");

		await CriarLinkAsync(pagina, rotulo, destino);
		await CriarLinkAsync(pagina, outro, $"{destino}-b");

		// Sem JavaScript a árvore aparece aberta: o teste não depende do hover.
		var semScript = await f.AbrirAsync(tema, "/", javaScript: false);
		var link = semScript.Locator($"a[href='{destino}']:has-text('{rotulo}')");
		await Expect(link).ToBeVisibleAsync();
		await Expect(link).ToHaveAttributeAsync("target", "_blank");
		await Expect(link).ToHaveAttributeAsync("rel", "noopener noreferrer");

		var origem = semScript.Url;
		var esperaPagina = semScript.Context.WaitForPageAsync(new() { Predicate = p => p.Url.Contains($"origem={sufixo}") });
		await link.ClickAsync();
		var aberta = await esperaPagina;
		await Expect(aberta).ToHaveURLAsync(new Regex($"origem={sufixo}$"));
		semScript.Url.Should().Be(origem, "a página de origem não navega quando o link abre em nova aba");

		// Ordem padrão: por rótulo; subir o segundo inverte.
		async Task<string[]> NomesNoMenuAsync()
		{
			await semScript.GotoAsync("/");

			return await semScript.Locator($"a[href*='origem={sufixo}']")
				.EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
		}

		(await NomesNoMenuAsync()).Should().Equal(rotulo, outro);

		await pagina.GotoAsync("/menu");
		await pagina.GetByRole(AriaRole.Button, new() { Name = $"Subir {outro}" }).ClickAsync();
		await Expect(pagina).ToHaveURLAsync(new Regex("/menu/?$", RegexOptions.IgnoreCase));
		(await NomesNoMenuAsync()).Should().Equal(outro, rotulo);

		await ExcluirLinkAsync(pagina, rotulo);
		await ExcluirLinkAsync(pagina, outro);

		await semScript.GotoAsync("/");
		await Expect(semScript.Locator($"a[href*='origem={sufixo}']")).ToHaveCountAsync(0);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Menu_SemViolacaoDeCsp(string tema)
	{
		var sufixo = Sufixo();
		var rotulo = $"Abrir um chamado {sufixo}";
		var pagina = await f.AbrirAsync(tema, "/menu");

		await CriarLinkAsync(pagina, rotulo, $"{f.Enderecos[tema]}/?origem={sufixo}");
		await pagina.GotoAsync("/");
		await pagina.GotoAsync("/menu");
		await Expect(pagina.Locator($"button[title='Excluir {rotulo}']")).ToBeVisibleAsync();

		f.ViolacoesDeCsp(pagina).Should().BeEmpty();
		f.ErrosDeScript(pagina).Should().BeEmpty();

		await ExcluirLinkAsync(pagina, rotulo);
	}
}
