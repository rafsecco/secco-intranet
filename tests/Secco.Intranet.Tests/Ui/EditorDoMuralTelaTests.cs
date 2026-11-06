using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Tests.Integration;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Secco.Intranet.Tests.Ui;

/// <summary>
/// Sobe a aplicação de verdade (Kestrel), uma vez por tema, com um setor onde o administrador da
/// intranet publica avisos, e um Chromium headless — o necessário para exercitar o editor do mural.
/// </summary>
public sealed class EditorDoMuralFixture : IAsyncLifetime
{
	private readonly IntranetWebFactory _fabrica = new();
	private readonly Dictionary<string, WebApplicationFactory<Program>> _apps = [];
	private readonly ConditionalWeakTable<IPage, List<string>> _violacoes = [];
	private IPlaywright? _playwright;

	/// <summary>Navegador compartilhado pelos testes.</summary>
	public IBrowser Navegador { get; private set; } = null!;

	/// <summary>Endereço da aplicação por tema.</summary>
	public Dictionary<string, string> Enderecos { get; } = [];

	/// <summary>Slug do setor do cenário.</summary>
	public string Slug { get; } = $"edt-{Guid.NewGuid():N}"[..12];

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

		await CriarSetorAsync();

		_playwright = await Playwright.CreateAsync();
		Navegador = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
	}

	/// <summary>Abre a tela de avisos do setor, como administrador da intranet.</summary>
	public Task<IPage> AbrirAvisosAsync(string tema, bool javaScript = true) =>
		AbrirAsync(tema, $"/{Slug}/avisos", javaScript);

	/// <summary>Abre a tela de avisos do setor com a publicação em edição.</summary>
	public Task<IPage> AbrirEdicaoAsync(string tema, Guid publicacaoId) =>
		AbrirAsync(tema, $"/{Slug}/avisos?editar={publicacaoId}", javaScript: true);

	/// <summary>Grava uma publicação com o corpo dado, pelo caso de uso, sem passar pelo formulário.</summary>
	public async Task<Guid> CriarPublicacaoAsync(string corpo)
	{
		using var escopo = _fabrica.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(_fabrica.TenantAlfa);
		var resultado = await escopo.ServiceProvider.GetRequiredService<PublicarPublicacaoHandler>().HandleAsync(
			new PublicarPublicacaoCommand(
				Slug,
				$"Aviso {Guid.NewGuid():N}"[..14],
				corpo,
				TipoPublicacao.Aviso,
				Visibilidade.Empresa,
				PrioridadePublicacao.Normal,
				DateTimeOffset.UtcNow.AddMinutes(-1),
				null,
				"Teste"),
			CancellationToken.None);
		resultado.IsSuccess.Should().BeTrue();

		return resultado.Value.Publicacao.Id;
	}

	/// <summary>Mensagens de console sobre a CSP vistas pela página desde que foi aberta.</summary>
	public List<string> ViolacoesDeCsp(IPage pagina) => _violacoes.TryGetValue(pagina, out var lista) ? lista : [];

	private async Task<IPage> AbrirAsync(string tema, string caminho, bool javaScript)
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
		var lista = new List<string>();
		_violacoes.Add(pagina, lista);
		pagina.Console += (_, mensagem) =>
		{
			if (mensagem.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
			{
				lock (lista)
				{
					lista.Add(mensagem.Text);
				}
			}
		};

		await pagina.GotoAsync(caminho);

		return pagina;
	}

	private async Task CriarSetorAsync()
	{
		var admin = _apps["Vertical"].CreateClient();
		admin.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, _fabrica.TenantAlfa.ToString());
		admin.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		var html = await admin.GetStringAsync("/Setores/Create");
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
		token.Success.Should().BeTrue();

		(await admin.PostAsync("/Setores/Create", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", "Setor do Mural"),
			new KeyValuePair<string, string>("Slug", Slug),
			new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Value),
		]))).IsSuccessStatusCode.Should().BeTrue();
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

/// <summary>O editor Tiptap do formulário de aviso, nos dois temas, com a CSP em Enforce.</summary>
public class EditorDoMuralTelaTests(EditorDoMuralFixture f) : IClassFixture<EditorDoMuralFixture>
{
	private const string Corpo = "#Form_Corpo";

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Editor_Abre_SemViolacaoDeCsp(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await Expect(pagina.Locator(".sc-editor .ProseMirror")).ToBeVisibleAsync();
		await Expect(pagina.Locator(Corpo)).ToBeHiddenAsync();
		f.ViolacoesDeCsp(pagina).Should().BeEmpty();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Editor_Digitar_PublicaOTextoDigitado(string tema)
	{
		var titulo = $"Digitado {Guid.NewGuid():N}"[..16];
		var pagina = await f.AbrirAvisosAsync(tema);

		await pagina.Locator(".sc-editor .ProseMirror").ClickAsync();
		await pagina.Keyboard.TypeAsync("Texto do aviso");
		await pagina.Locator("#Form_Titulo").FillAsync(titulo);
		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar publicação" }).ClickAsync();

		await Expect(pagina.Locator(".sc-list__title", new() { HasTextString = titulo })).ToBeVisibleAsync();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Editor_Barra_AplicaNegritoETabela(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await pagina.Locator(".sc-editor .ProseMirror").ClickAsync();
		await pagina.GetByLabel("Negrito", new() { Exact = true }).ClickAsync();
		await pagina.Keyboard.TypeAsync("forte");
		await pagina.GetByLabel("Inserir tabela").ClickAsync();

		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		markdown.Should().Contain("**");
		markdown.Should().Contain("| --- |");
		f.ViolacoesDeCsp(pagina).Should().BeEmpty("a barra e a tabela não escrevem style inline");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Editor_Tabela_EnterNaCelula_SalvaNaMesmaLinha(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await pagina.Locator(".sc-editor .ProseMirror").ClickAsync();
		await pagina.GetByLabel("Inserir tabela").ClickAsync();
		await pagina.Keyboard.TypeAsync("um");
		await pagina.Keyboard.PressAsync("Enter");
		await pagina.Keyboard.TypeAsync("dois");
		await pagina.Keyboard.PressAsync("Shift+Enter");
		await pagina.Keyboard.TypeAsync("três");

		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		markdown.Should().Contain("um dois três");
		markdown.Should().NotContain("<br>");
		f.ViolacoesDeCsp(pagina).Should().BeEmpty();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Editor_SemScript_PublicaPeloTextarea(string tema)
	{
		var titulo = $"SemJs {Guid.NewGuid():N}"[..12];
		var pagina = await f.AbrirAvisosAsync(tema, javaScript: false);

		await Expect(pagina.Locator(Corpo)).ToBeVisibleAsync();
		await pagina.Locator(Corpo).FillAsync("Texto sem script");
		await pagina.Locator("#Form_Titulo").FillAsync(titulo);
		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar publicação" }).ClickAsync();

		await Expect(pagina.Locator(".sc-list__title", new() { HasTextString = titulo })).ToBeVisibleAsync();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Editor_ModoEscuro_UsaOsTokensDoTema(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);
		await Expect(pagina.Locator(".sc-editor .ProseMirror")).ToBeVisibleAsync();

		const string Fundo = "() => getComputedStyle(document.querySelector('.sc-editor .ProseMirror')).backgroundColor";
		await pagina.EvaluateAsync("() => document.documentElement.setAttribute('data-bs-theme', 'dark')");
		var escuro = await pagina.EvaluateAsync<string>(Fundo);
		await pagina.EvaluateAsync("() => document.documentElement.setAttribute('data-bs-theme', 'light')");
		var claro = await pagina.EvaluateAsync<string>(Fundo);

		escuro.Should().NotBe(claro);
	}
}
