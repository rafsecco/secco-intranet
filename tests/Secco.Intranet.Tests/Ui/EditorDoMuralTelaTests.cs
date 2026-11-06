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

	/// <summary>Chave do app do tema Vertical com a CSP em ReportOnly, para a CSP não mascarar o resultado dos ataques.</summary>
	public const string ReportOnly = "Vertical-ReportOnly";

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

		foreach (var chave in new[] { "Vertical", "Horizontal", ReportOnly })
		{
			var tema = chave == ReportOnly ? "Vertical" : chave;
			var modoDaCsp = chave == ReportOnly ? "ReportOnly" : null;
			var app = _fabrica.WithWebHostBuilder(builder => builder
				.UseSetting("Intranet:Theme:Nome", tema)
				.UseSetting("Intranet:Csp:Modo", modoDaCsp)
				.UseStaticWebAssets());
			app.UseKestrel(kestrel => kestrel.Listen(System.Net.IPAddress.Loopback, 0));
			app.StartServer();
			_apps[chave] = app;
			Enderecos[chave] = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		}

		await CriarSetorAsync();

		_playwright = await Playwright.CreateAsync();
		Navegador = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
	}

	/// <summary>Abre a tela de avisos do setor, como administrador da intranet.</summary>
	public Task<IPage> AbrirAvisosAsync(string tema, bool javaScript = true) =>
		AbrirAsync(tema, $"/{Slug}/avisos", javaScript, null);

	/// <summary>Abre a tela de avisos do setor com a publicação em edição.</summary>
	public Task<IPage> AbrirEdicaoAsync(string tema, Guid publicacaoId, string? scriptInicial = null) =>
		AbrirAsync(tema, $"/{Slug}/avisos?editar={publicacaoId}", javaScript: true, scriptInicial);

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

	/// <summary>Abre um caminho do tema, como administrador da intranet.</summary>
	public Task<IPage> AbrirAsync(string tema, string caminho) => AbrirAsync(tema, caminho, javaScript: true, null);

	private async Task<IPage> AbrirAsync(string tema, string caminho, bool javaScript, string? scriptInicial)
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

		if (scriptInicial is not null)
		{
			await pagina.AddInitScriptAsync(scriptInicial);
		}

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

	private const string Avisos = ".sc-editor__avisos .alert";

	private const string AvisoMesclada = "Esta tabela tinha células mescladas, que não são suportadas. Confira o resultado ou envie a tabela como imagem.";

	private const string AvisoAninhada = "Esta tabela tinha outra tabela dentro dela, o que não é suportado. Confira o resultado ou envie a tabela como imagem.";

	private const string AvisoImagem = "A imagem não foi colada. Use o campo Imagem.";

	private static Task ColarAsync(IPage pagina, string html, string? texto = null) =>
		pagina.EvaluateAsync(
			"""
			([html, texto]) => {
			  const alvo = document.querySelector('.sc-editor .ProseMirror');
			  alvo.focus();
			  const dados = new DataTransfer();
			  if (html) { dados.setData('text/html', html); }
			  if (texto) { dados.setData('text/plain', texto); }
			  alvo.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dados, bubbles: true, cancelable: true }));
			}
			""",
			new string?[] { html, texto });

	/// <summary>Linhas da tabela do markdown (as que começam com barra), sem a linha de separação.</summary>
	private static string[] LinhasDaTabela(string markdown) => [.. markdown
		.Split('\n')
		.Select(l => l.Trim())
		.Where(l => l.StartsWith('|') && !Regex.IsMatch(l, @"^\|(\s*-+\s*\|)+$"))];

	private static int Colunas(string linha) => Regex.Matches(linha, @"(?<!\\)\|").Count - 1;

	/// <summary>Salva o aviso e devolve o corpo publicado, na página de detalhe.</summary>
	private async Task<ILocator> PublicarAsync(IPage pagina, string tema)
	{
		var titulo = $"Colado {Guid.NewGuid():N}"[..14];
		await pagina.Locator("#Form_Titulo").FillAsync(titulo);
		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar publicação" }).ClickAsync();

		var editar = pagina.Locator($"a[title='Editar {titulo}']");
		await Expect(editar).ToBeVisibleAsync();
		var id = Regex.Match((await editar.GetAttributeAsync("href"))!, "editar=([0-9a-f-]{36})").Groups[1].Value;
		await pagina.GotoAsync($"{f.Enderecos[tema]}/publicacoes/{id}");

		return pagina.Locator(".sc-card__body");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_TextoBasicoDoWord_PreservaTituloNegritoItalicoListaELink(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, """
			<html xmlns:o="urn:schemas-microsoft-com:office:office"><body><!--StartFragment-->
			<h1>Título do comunicado</h1>
			<p class="MsoNormal">Texto <b>forte</b> e <i>inclinado</i><o:p></o:p></p>
			<ul><li style="mso-list:l0 level1 lfo1">primeiro</li><li style="mso-list:l0 level1 lfo1">segundo</li></ul>
			<p class="MsoNormal">Veja <a href="https://exemplo.com">o site</a></p>
			<!--EndFragment--></body></html>
			""");

		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		markdown.Should().Contain("# ");
		markdown.Should().Contain("**forte**");
		markdown.Should().MatchRegex(@"(?<!\*)\*[^*]+\*(?!\*)");
		markdown.Should().Contain("- primeiro");
		markdown.Should().Contain("](https://exemplo.com)");
		await Expect(pagina.Locator(Avisos)).ToHaveCountAsync(0);
		f.ViolacoesDeCsp(pagina).Should().BeEmpty();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_TextoQuePareceDominio_NaoViraLink(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, "<p>Usamos ASP.NET aqui</p>");

		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		markdown.Should().Contain("ASP.NET");
		markdown.Should().NotContain("](");
		markdown.Should().NotContain("http");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_TabelaSemCabecalho_PrimeiraLinhaViraCabecalho(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, """
			<table class="MsoTableGrid"><tr><td>Sala</td><td>Dia</td></tr><tr><td>Azul</td><td>Terça</td></tr></table>
			""");

		var linhas = (await pagina.Locator(Corpo).InputValueAsync()).Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('|')).ToList();
		linhas[0].Should().Be("| Sala | Dia |");
		linhas[1].Should().Be("| --- | --- |");

		var publicado = await PublicarAsync(pagina, tema);
		await Expect(publicado.Locator("table")).ToHaveCountAsync(1);
		var textoPublicado = await publicado.InnerTextAsync();
		textoPublicado.Should().NotContain("<");
		textoPublicado.Should().NotContain("##");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_TituloEQuebraEListaEmCelula_FicamNaMesmaLinha(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, """
			<table><tr><td>Col</td><td>Col2</td><td>Col3</td></tr>
			<tr><td><h2>T</h2></td><td>a<br>b</td><td><ul><li>x</li><li>y</li></ul></td></tr></table>
			""");

		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		var celulas = string.Join('\n', LinhasDaTabela(markdown));
		celulas.Should().NotContain("##");
		celulas.Should().NotContain("<br>");
		celulas.Should().NotContain("- ");
		celulas.Should().Contain("**T**");
		celulas.Should().Contain("a b");
		celulas.Should().Contain("x y");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_CelulaMesclada_ExpandeEAvisa(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, """
			<table>
			<tr><td colspan="2">Topo</td><td>B</td></tr>
			<tr><td rowspan="2">Lado</td><td>D</td><td>E</td></tr>
			<tr><td>F</td><td>G</td></tr>
			</table>
			""");

		var linhas = LinhasDaTabela(await pagina.Locator(Corpo).InputValueAsync());
		linhas.Should().HaveCount(3);
		linhas.Select(Colunas).Should().OnlyContain(c => c == 3);
		linhas[2].Should().MatchRegex(@"^\|\s*\|\s*F\s*\|\s*G\s*\|$");
		await Expect(pagina.Locator(Avisos)).ToHaveCountAsync(1);
		await Expect(pagina.Locator(Avisos)).ToContainTextAsync(AvisoMesclada);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_TabelaDentroDeTabela_MantemOTextoEAvisa(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, """
			<table><tr><td>Externa</td><td>Fora</td></tr>
			<tr><td><table><tr><td>interno1</td><td>interno2</td></tr></table></td><td>Resto</td></tr></table>
			""");

		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		markdown.Should().Contain("Externa");
		markdown.Should().Contain("interno1 interno2");
		Regex.Matches(markdown, @"^\|\s*---", RegexOptions.Multiline).Should().HaveCount(1);
		await Expect(pagina.Locator(Avisos)).ToContainTextAsync(AvisoAninhada);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_LinhasDeTamanhosDiferentes_MantemATabelaValida(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, """
			<table><tr><td>a1</td><td>a2</td><td>a3</td></tr><tr><td>b1</td><td>b2</td></tr></table>
			""");

		var linhas = LinhasDaTabela(await pagina.Locator(Corpo).InputValueAsync());
		linhas.Should().HaveCount(2);
		linhas.Select(Colunas).Should().OnlyContain(c => c == 3);
		linhas[1].Should().MatchRegex(@"^\|\s*b1\s*\|\s*b2\s*\|\s*\|$");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_TabelaDentroDeCelulaExistente_MantemTabelaValida(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);
		await pagina.Locator(".sc-editor .ProseMirror").ClickAsync();
		await pagina.GetByLabel("Inserir tabela").ClickAsync();

		await ColarAsync(pagina, "<table><tr><td>colado</td><td>junto</td></tr></table>");

		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		Regex.Matches(markdown, @"^\|\s*---", RegexOptions.Multiline).Should().HaveCount(1);
		markdown.Should().Contain("colado");
		var linhas = LinhasDaTabela(markdown);
		linhas.Select(Colunas).Distinct().Should().HaveCount(1);
		linhas[0].Should().Contain("colado");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_Imagem_DescartaEAvisa(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, """<p>Antes</p><p><img src="file:///C:/x/clip_image001.png"></p>""");

		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		markdown.Should().NotContain("![");
		markdown.Should().NotContain("file:");
		await Expect(pagina.Locator(Avisos)).ToContainTextAsync(AvisoImagem);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_TextoPuro_InsereSemAviso(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, string.Empty, "apenas texto puro");

		(await pagina.Locator(Corpo).InputValueAsync()).Should().Contain("apenas texto puro");
		await Expect(pagina.Locator(Avisos)).ToHaveCountAsync(0);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_LinkComEsquemaProibido_SaiSemLink(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, """<p>Veja <a href="javascript:alert(1)">este texto</a></p>""");

		var publicado = await PublicarAsync(pagina, tema);
		await Expect(publicado).ToContainTextAsync("este texto");
		await Expect(publicado.Locator("a")).ToHaveCountAsync(0);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Aviso_Fechar_Remove(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);
		const string Html = """<p><img src="file:///C:/x/clip_image001.png"></p>""";

		await ColarAsync(pagina, Html);
		await ColarAsync(pagina, Html);
		await Expect(pagina.Locator(Avisos)).ToHaveCountAsync(1);

		await pagina.Locator($"{Avisos} .btn-close").ClickAsync();
		await Expect(pagina.Locator(Avisos)).ToHaveCountAsync(0);
		f.ViolacoesDeCsp(pagina).Should().BeEmpty();
	}

	private const string ListaFalsaComMarcador = """
		<p class="MsoListParagraphCxSpFirst" style="mso-list:l0 level1 lfo1"><![if !supportLists]><span style="font-family:Symbol">MID<span>&nbsp;&nbsp;</span></span><![endif]>Primeiro</p>
		<p class="MsoListParagraphCxSpLast" style="mso-list:l0 level1 lfo1"><![if !supportLists]><span style="font-family:Symbol">MID<span>&nbsp;&nbsp;</span></span><![endif]>Segundo</p>
		""";

	private const string ListaFalsaNumerada = """
		<p class="MsoListParagraphCxSpFirst" style="mso-list:l0 level1 lfo1"><!--[if !supportLists]--><span>1.<span>&nbsp;</span></span><!--[endif]-->Primeiro</p>
		<p class="MsoListParagraphCxSpLast" style="mso-list:l0 level1 lfo1"><!--[if !supportLists]--><span>2.<span>&nbsp;</span></span><!--[endif]-->Segundo</p>
		""";

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_TextoQueCitaStyle_PreservaOTexto(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, "<p>use style=foo aqui</p><p style=\"color:red\">x</p>");

		(await pagina.Locator(Corpo).InputValueAsync()).Should().Contain("style=foo");
		f.ViolacoesDeCsp(pagina).Should().BeEmpty();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_ListaFalsaDoWord_ViraListaComMarcadores(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, ListaFalsaComMarcador.Replace("MID", "·"));

		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		markdown.Should().Contain("- Primeiro");
		markdown.Should().Contain("- Segundo");
		markdown.Should().NotContain("·");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_ListaFalsaNumeradaDoWord_ViraListaNumerada(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, ListaFalsaNumerada);

		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		markdown.Should().Contain("1. Primeiro");
		markdown.Should().Contain("2. Segundo");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_ListaFalsaEmCelula_FicaNaMesmaLinha(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await ColarAsync(pagina, "<table><tr><td>Cab</td></tr><tr><td>" + ListaFalsaComMarcador.Replace("MID", "·") + "</td></tr></table>");

		var celulas = string.Join('\n', LinhasDaTabela(await pagina.Locator(Corpo).InputValueAsync()));
		celulas.Should().Contain("Primeiro Segundo");
		celulas.Should().NotContain("- ");
		celulas.Should().NotContain("·");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Colar_TabelaGiganteComMesclagem_ViraTextoEAvisa(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);
		var linhas = string.Concat(Enumerable.Range(0, 200).Select(i =>
			$"<tr><td colspan=\"100\">{(i == 0 ? "marcador" : "x")}</td><td colspan=\"100\">y</td></tr>"));

		await ColarAsync(pagina, $"<table>{linhas}</table>");

		await Expect(pagina.Locator(Avisos)).ToContainTextAsync("A tabela é grande demais: o limite é de 10000 células. Envie-a como imagem.");
		var markdown = await pagina.Locator(Corpo).InputValueAsync();
		markdown.Should().Contain("marcador");
		markdown.Should().NotContain("| --- |");
		(await pagina.EvaluateAsync<int>("() => 1 + 1")).Should().Be(2);
	}

	private const string ScriptXss = "window.__xss = [];";

	/// <summary>Cargas hostis; cada uma vai sozinha no corpo da publicação.</summary>
	public static TheoryData<string, string> CargasHostis => new()
	{
		{ "script", "<script>window.__xss.push('script')</script>" },
		{ "imgOnerror", "<img src=x onerror=\"window.__xss.push('imgOnerror')\">" },
		{ "svgOnload", "<svg onload=\"window.__xss.push('svgOnload')\"></svg>" },
		{ "iframe", "<iframe srcdoc=\"<script>parent.__xss.push('iframe')</script>\"></iframe>" },
		{ "detalhes", "<details open ontoggle=\"window.__xss.push('detalhes')\">x</details>" },
		{ "mxss", "<math><mtext><table><mglyph><style><img src=x onerror=\"window.__xss.push('mxss')\">" },
		{ "linkJs", "[clique](javascript:window.__xss.push('linkJs'))" },
		{ "linkData", "[clique](data:text/html,<script>window.__xss.push('linkData')</script>)" },
		{ "imgAtributo", "![a](x\" onerror=\"window.__xss.push('imgAtributo'))" },
		{ "imgJs", "![a](javascript:window.__xss.push('imgJs'))" },
		{ "celula", "| a |\n| --- |\n| <img src=x onerror=\"window.__xss.push('celula')\"> |" },
		{ "codigo", "```\n<img src=x onerror=\"window.__xss.push('codigo')\">\n```" },
	};

	[UiTheory]
	[MemberData(nameof(CargasHostis))]
	public async Task Editor_ConteudoHostil_NaoExecuta(string nome, string carga)
	{
		var id = await f.CriarPublicacaoAsync(carga);
		var pagina = await f.AbrirEdicaoAsync(EditorDoMuralFixture.ReportOnly, id, ScriptXss);

		await Expect(pagina.Locator(".sc-editor .ProseMirror")).ToBeVisibleAsync();
		await pagina.WaitForTimeoutAsync(500);

		(await pagina.EvaluateAsync<string[]>("() => window.__xss")).Should().BeEmpty($"a carga {nome} não pode executar");
		(await pagina.EvaluateAsync<int>(
			"() => [...document.querySelectorAll('.sc-editor *')].filter(e => [...e.attributes].some(a => a.name.startsWith('on'))).length"))
			.Should().Be(0);
		(await pagina.Locator(".sc-editor").Locator("script, iframe, svg, math, details, object, embed").CountAsync()).Should().Be(0);
		(await pagina.Locator(".sc-editor a[href^='javascript:' i], .sc-editor a[href^='data:' i]").CountAsync()).Should().Be(0);

		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar publicação" }).ClickAsync();
		await pagina.WaitForURLAsync(url => !url.Contains("editar=", StringComparison.Ordinal));
		await pagina.GotoAsync($"{f.Enderecos[EditorDoMuralFixture.ReportOnly]}/publicacoes/{id}");
		await pagina.WaitForLoadStateAsync(LoadState.Load);
		await pagina.WaitForTimeoutAsync(500);

		(await pagina.EvaluateAsync<string[]>("() => window.__xss")).Should().BeEmpty($"a carga {nome} não pode executar na página de leitura");
	}

	private const string CorpoDeIdaEVolta = """
		# Título 1 < 2 e a * b

		Texto com 1 < 2 e a * b no meio.

		- item 1 < 2
		- item a * b

		| Coluna | Valor |
		| --- | --- |
		| ab | 1 < 2 |
		| a * b | fim |
		""";

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Editor_SalvarEReabrir_MantemOTextoVisivel(string tema)
	{
		var id = await f.CriarPublicacaoAsync(CorpoDeIdaEVolta);

		var original = await TextoVisivelAsync(tema, id);
		var markdown1 = await SalvarSemMudarAsync(tema, id);
		var depoisDaPrimeira = await TextoVisivelAsync(tema, id);
		var markdown2 = await SalvarSemMudarAsync(tema, id);

		depoisDaPrimeira.Should().Be(original, "markdown1: " + markdown1);
		markdown2.Should().Be(markdown1, "uma segunda ida e volta não pode alterar mais o Markdown");
		original.Should().Contain("1 < 2");
		original.Should().Contain("a * b");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Editor_Tabela_BarraDigitadaNaCelula_FicaEscapada(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);

		await pagina.Locator(".sc-editor .ProseMirror").ClickAsync();
		await pagina.GetByLabel("Inserir tabela").ClickAsync();
		var colunas = Colunas(LinhasDaTabela(await pagina.Locator(Corpo).InputValueAsync())[0]);
		await pagina.Keyboard.TypeAsync("a|b");

		var linhas = LinhasDaTabela(await pagina.Locator(Corpo).InputValueAsync());
		linhas[0].Should().Contain(@"a\|b");
		linhas.Select(Colunas).Should().OnlyContain(c => c == colunas);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Editor_SalvarEReabrir_BarraEmCelula_MantemACelula(string tema)
	{
		var id = await f.CriarPublicacaoAsync("| Coluna | Valor |\n| --- | --- |\n| a\\|b | x |");

		var original = await TextoVisivelAsync(tema, id);
		await SalvarSemMudarAsync(tema, id);

		(await TextoVisivelAsync(tema, id)).Should().Be(original);
	}

	private async Task<string> TextoVisivelAsync(string tema, Guid id)
	{
		var pagina = await f.AbrirAsync(tema, $"/publicacoes/{id}");

		return await pagina.Locator(".sc-card__body").InnerTextAsync();
	}

	/// <summary>Abre a edição, provoca a serialização do editor sem mudar o texto, salva e devolve o Markdown enviado.</summary>
	private async Task<string> SalvarSemMudarAsync(string tema, Guid id)
	{
		var pagina = await f.AbrirEdicaoAsync(tema, id);
		await Expect(pagina.Locator(".sc-editor .ProseMirror")).ToBeVisibleAsync();
		await pagina.Locator(".sc-editor .ProseMirror").ClickAsync();
		await pagina.Keyboard.PressAsync("Control+End");
		await pagina.Keyboard.TypeAsync("x");
		await pagina.Keyboard.PressAsync("Backspace");
		var markdown = await pagina.Locator(Corpo).InputValueAsync();

		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar publicação" }).ClickAsync();
		await pagina.WaitForURLAsync(url => !url.Contains("editar=", StringComparison.Ordinal));

		return markdown;
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Publicar_CorpoAcimaDoLimite_MantemOTextoNoEditor(string tema)
	{
		var pagina = await f.AbrirAvisosAsync(tema);
		await pagina.Locator(".sc-editor .ProseMirror").ClickAsync();
		await pagina.Keyboard.InsertTextAsync(new string('a', 20_001));
		await pagina.Locator("#Form_Titulo").FillAsync("Corpo grande");

		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar publicação" }).ClickAsync();

		await Expect(pagina.GetByText("O corpo excede o limite de 20000 caracteres.").First).ToBeVisibleAsync();
		(await pagina.Locator(".sc-editor .ProseMirror").InnerTextAsync()).Length.Should().BeGreaterThanOrEqualTo(20_001);
	}
}
