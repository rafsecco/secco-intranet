using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Tests.Integration;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Secco.Intranet.Tests.Ui;

/// <summary>
/// Sobe a aplicação de verdade (Kestrel), uma vez por tema, com a CSP em Enforce, e um Chromium
/// headless. Cada teste cria o próprio setor (com o nó Links), para não depender da ordem dos outros.
/// </summary>
public sealed class LinksDoSetorFixture : IAsyncLifetime
{
	private readonly IntranetWebFactory _fabrica = new();
	private readonly Dictionary<string, WebApplicationFactory<Program>> _apps = [];
	private readonly ConditionalWeakTable<IPage, List<string>> _violacoes = [];
	private readonly ConditionalWeakTable<IPage, List<string>> _errosDeScript = [];
	private readonly PermissionResolverDeTeste _permissoes = new();
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
		_fabrica.ResolvedorDePermissoes = _permissoes;

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

	/// <summary>Perfil de teste de quem lê e escreve no setor (fora de Development a permissão vem do perfil).</summary>
	private static string PerfilDoEscritor(string slug) => $"escritor-{slug}";

	/// <summary>Cria um setor novo, já com o nó Links, e devolve o slug. Quem o abre pelo <see cref="AbrirAsync"/> lê e escreve nele.</summary>
	public async Task<string> CriarSetorAsync()
	{
		var slug = $"lnk-{Guid.NewGuid():N}"[..12];
		using var escopo = _fabrica.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(_fabrica.TenantAlfa);

		var setor = await escopo.ServiceProvider.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));
		setor.IsSuccess.Should().BeTrue();

		_permissoes
			.ComPermissao(PerfilDoEscritor(slug), IntranetPermissoes.Setor.Read(slug))
			.ComPermissao(PerfilDoEscritor(slug), IntranetPermissoes.Setor.Write(slug));

		return slug;
	}

	/// <summary>Mensagens de console sobre a CSP vistas pela página desde que foi aberta.</summary>
	public List<string> ViolacoesDeCsp(IPage pagina) => _violacoes.TryGetValue(pagina, out var lista) ? lista : [];

	/// <summary>Erros de JavaScript da página.</summary>
	public List<string> ErrosDeScript(IPage pagina) => _errosDeScript.TryGetValue(pagina, out var lista) ? lista : [];

	/// <summary>Abre um caminho do tema como alguém com escrita no setor.</summary>
	public async Task<IPage> AbrirAsync(string tema, string slug, string caminho, bool javaScript = true)
	{
		var contexto = await Navegador.NewContextAsync(new BrowserNewContextOptions
		{
			BaseURL = Enderecos[tema],
			JavaScriptEnabled = javaScript,
			ExtraHTTPHeaders = new Dictionary<string, string>
			{
				[SeccoHeaders.TenantId] = _fabrica.TenantAlfa.ToString(),
				[RolesDeTesteMiddleware.Header] = PerfilDoEscritor(slug),
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
/// A gestão dos links do setor e o resultado deles no menu, nos dois temas. Os cliques no menu usam
/// uma página sem JavaScript, onde a árvore aparece aberta: assim o teste não depende do tempo do hover.
/// </summary>
public class LinksDoSetorTelaTests(LinksDoSetorFixture f) : IClassFixture<LinksDoSetorFixture>
{
	private static Regex Termina(string sufixo) => new($"{Regex.Escape(sufixo)}$");

	private static async Task CriarGrupoAsync(IPage pagina, string slug, string nome)
	{
		await pagina.GotoAsync($"/{slug}/links");
		await pagina.Locator("#nome-do-grupo").FillAsync(nome);
		await pagina.GetByRole(AriaRole.Button, new() { Name = "Criar grupo" }).ClickAsync();
		await Expect(pagina.Locator(".sc-list__title", new() { HasTextString = nome })).ToBeVisibleAsync();
	}

	private static async Task CriarLinkAsync(IPage pagina, string slug, string nome, string endereco, string? grupo = null, bool novaAba = true)
	{
		await pagina.GotoAsync($"/{slug}/links");
		await pagina.GetByRole(AriaRole.Link, new() { Name = "Novo link", Exact = true }).ClickAsync();
		await Expect(pagina).ToHaveURLAsync(Termina($"/{slug}/links/novo"));

		await pagina.GetByLabel("Nome", new() { Exact = true }).FillAsync(nome);
		await pagina.GetByLabel("Endereço").FillAsync(endereco);

		if (grupo is not null)
		{
			await pagina.GetByLabel("Grupo", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = grupo });
		}

		var caixa = pagina.GetByLabel("Abrir em nova página");
		await Expect(caixa).ToBeCheckedAsync();

		if (!novaAba)
		{
			await caixa.UncheckAsync();
		}

		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar" }).ClickAsync();
		await Expect(pagina.Locator(".sc-list__title", new() { HasTextString = nome })).ToBeVisibleAsync();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task CriarGrupoELinkPelaTela_ApareceNoMenu(string tema)
	{
		var slug = await f.CriarSetorAsync();
		var endereco = $"{f.Enderecos[tema]}/{slug}/avisos";
		var pagina = await f.AbrirAsync(tema, slug, $"/{slug}/links");

		await CriarGrupoAsync(pagina, slug, "Sistemas internos");
		await CriarLinkAsync(pagina, slug, "ERP de teste", endereco, grupo: "Sistemas internos");

		// Sem JavaScript a árvore aparece aberta: o teste não depende do hover.
		pagina = await f.AbrirAsync(tema, slug, "/", javaScript: false);
		await Expect(pagina.Locator("button[data-sc-submenu]:has-text('Sistemas internos')")).ToBeVisibleAsync();
		var link = pagina.Locator($"a[href='{endereco}']:has-text('ERP de teste')");
		await Expect(link).ToBeVisibleAsync();
		await Expect(link).ToHaveAttributeAsync("target", "_blank");
		await Expect(pagina.Locator($"a[href='/{slug}/links']:has-text('Gerenciar links')")).ToBeVisibleAsync();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Link_NovaAba_AbreOutraPagina(string tema)
	{
		var slug = await f.CriarSetorAsync();
		var destino = $"{f.Enderecos[tema]}/{slug}/avisos";
		var pagina = await f.AbrirAsync(tema, slug, $"/{slug}/links");
		await CriarLinkAsync(pagina, slug, "ERP de teste", destino, novaAba: true);

		pagina = await f.AbrirAsync(tema, slug, "/", javaScript: false);
		var link = pagina.Locator($"a[href='{destino}']:has-text('ERP de teste')");
		await Expect(link).ToHaveAttributeAsync("rel", "noopener noreferrer");
		var origem = pagina.Url;

		var popup = await pagina.RunAndWaitForPopupAsync(async () => await link.ClickAsync());
		await Expect(popup).ToHaveURLAsync(Termina($"/{slug}/avisos"));

		pagina.Url.Should().Be(origem, "a página de origem não navega quando o link abre em nova aba");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Link_MesmaPagina_NavegaNaMesmaAba(string tema)
	{
		var slug = await f.CriarSetorAsync();
		var destino = $"{f.Enderecos[tema]}/{slug}/avisos";
		var pagina = await f.AbrirAsync(tema, slug, $"/{slug}/links");
		await CriarLinkAsync(pagina, slug, "ERP de teste", destino, novaAba: false);

		pagina = await f.AbrirAsync(tema, slug, "/", javaScript: false);
		var link = pagina.Locator($"a[href='{destino}']:has-text('ERP de teste')");
		await Expect(link).Not.ToHaveAttributeAsync("target", "_blank");

		var abriuPopup = false;
		pagina.Popup += (_, _) => abriuPopup = true;
		await link.ClickAsync();

		await Expect(pagina).ToHaveURLAsync(Termina($"/{slug}/avisos"));
		abriuPopup.Should().BeFalse("sem a marcação o link abre na própria aba");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Organizar_SubirEDescer_MudaAOrdemNoMenu(string tema)
	{
		var slug = await f.CriarSetorAsync();
		var pagina = await f.AbrirAsync(tema, slug, $"/{slug}/links");
		await CriarLinkAsync(pagina, slug, "Primeiro", $"{f.Enderecos[tema]}/{slug}/avisos?ordem=1");
		await CriarLinkAsync(pagina, slug, "Segundo", $"{f.Enderecos[tema]}/{slug}/avisos?ordem=2");

		async Task<string[]> NomesNoMenuAsync()
		{
			await pagina.GotoAsync("/");
			var nomes = await pagina.Locator("a[href*='/avisos?ordem=']")
				.EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");

			return nomes;
		}

		(await NomesNoMenuAsync()).Should().Equal("Primeiro", "Segundo");

		await pagina.GotoAsync($"/{slug}/links");
		await pagina.Locator("button[title='Subir Segundo']").ClickAsync();
		await Expect(pagina).ToHaveURLAsync(Termina($"/{slug}/links"));
		(await NomesNoMenuAsync()).Should().Equal("Segundo", "Primeiro");

		await pagina.GotoAsync($"/{slug}/links");
		await pagina.Locator("button[title='Descer Segundo']").ClickAsync();
		await Expect(pagina).ToHaveURLAsync(Termina($"/{slug}/links"));
		(await NomesNoMenuAsync()).Should().Equal("Primeiro", "Segundo");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Gerenciar_SemViolacaoDeCsp(string tema)
	{
		var slug = await f.CriarSetorAsync();
		var pagina = await f.AbrirAsync(tema, slug, $"/{slug}/links");

		await CriarGrupoAsync(pagina, slug, "Sistemas internos");
		await CriarLinkAsync(pagina, slug, "ERP de teste", $"{f.Enderecos[tema]}/{slug}/avisos", grupo: "Sistemas internos");
		await pagina.GotoAsync($"/{slug}/links");
		await Expect(pagina.Locator("button[title='Subir ERP de teste']")).ToBeVisibleAsync();

		f.ViolacoesDeCsp(pagina).Should().BeEmpty();
		f.ErrosDeScript(pagina).Should().BeEmpty();
	}
}
