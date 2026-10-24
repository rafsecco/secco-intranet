using AwesomeAssertions;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Secco.Intranet.Tests.Ui;

/// <summary>
/// O organograma nos dois temas, num navegador de verdade: recolher e expandir por clique e por
/// teclado, a lista recuada no celular e a árvore aberta sem JavaScript. O cenário (Chefe → Ana →
/// Caio, e Bruno ao lado de Ana) vem da <see cref="TelaFixture"/>; os testes se agarram só nos
/// ganchos do contrato (<c>sc-org__no</c>, <c>data-nivel</c>, <c>data-equipe</c>), não no layout.
/// </summary>
public class OrganogramaTelaTests(TelaFixture f) : IClassFixture<TelaFixture>
{
	private const string Raiz = ".sc-org__no[data-nivel='0']";

	private async Task<IPage> AbrirOrganogramaAsync(string tema, int largura = 1280, int altura = 800, bool javaScript = true)
	{
		var pagina = await f.AbrirAsync(tema, largura, altura, javaScript, diretorio: true);
		await pagina.GotoAsync("/diretorio/organograma");

		return pagina;
	}

	private ILocator Nome(IPage pagina, string quem) =>
		pagina.Locator($"a.sc-org__nome:has-text('{quem} {f.SufixoDoOrganograma}')");

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Markup_TemOsGanchosDoContrato(string tema)
	{
		var pagina = await AbrirOrganogramaAsync(tema);

		await Expect(pagina.Locator($"{Raiz}[data-equipe='2']")).ToHaveCountAsync(1);
		await Expect(pagina.Locator(".sc-org__no[data-nivel='1'][data-equipe='1']")).ToHaveCountAsync(1);
		await Expect(pagina.Locator(".sc-org__no[data-nivel='2'][data-equipe='0']")).ToHaveCountAsync(1);
		await Expect(pagina.Locator(".sc-org[aria-label='Organograma']")).ToHaveCountAsync(1);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Clique_RecolheEExpandeAEquipe(string tema)
	{
		var pagina = await AbrirOrganogramaAsync(tema);
		var contagem = pagina.Locator($"{Raiz} > details > summary .sc-org__contagem");
		await Expect(Nome(pagina, "Ana")).ToBeVisibleAsync();

		await contagem.ClickAsync();

		await Expect(pagina.Locator($"{Raiz} > details[open]")).ToHaveCountAsync(0);
		await Expect(Nome(pagina, "Ana")).ToBeHiddenAsync();

		await contagem.ClickAsync();

		await Expect(pagina.Locator($"{Raiz} > details[open]")).ToHaveCountAsync(1);
		await Expect(Nome(pagina, "Ana")).ToBeVisibleAsync();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Clique_NoNome_NavegaParaOPerfil(string tema)
	{
		var pagina = await AbrirOrganogramaAsync(tema);

		await Nome(pagina, "Bruno").ClickAsync();

		await pagina.WaitForURLAsync(url => url.Contains("/diretorio/") && !url.Contains("organograma"));
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Teclado_EnterEEspacoRecolhemEExpandem(string tema)
	{
		var pagina = await AbrirOrganogramaAsync(tema);
		await pagina.Locator($"{Raiz} > details > summary").FocusAsync();

		await pagina.Keyboard.PressAsync("Enter");
		await Expect(pagina.Locator($"{Raiz} > details[open]")).ToHaveCountAsync(0);

		await pagina.Keyboard.PressAsync("Space");
		await Expect(pagina.Locator($"{Raiz} > details[open]")).ToHaveCountAsync(1);

		await pagina.Keyboard.PressAsync("Enter");
		await Expect(pagina.Locator($"{Raiz} > details[open]")).ToHaveCountAsync(0);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Celular_ViraListaRecuada_SemRolagemHorizontalDaPagina(string tema)
	{
		var pagina = await AbrirOrganogramaAsync(tema, largura: 375, altura: 700);
		var equipe = pagina.Locator(".sc-org__equipe").First;

		await Expect(Nome(pagina, "Caio")).ToBeVisibleAsync();
		(await equipe.EvaluateAsync<string>("e => getComputedStyle(e).display")).Should().Be("block", "no celular é lista, não árvore em linha");
		(await equipe.EvaluateAsync<string>("e => getComputedStyle(e).borderLeftWidth")).Should().Be("1px", "a linha vertical da lista recuada");
		(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth"))
			.Should().BeTrue("a página nunca rola na horizontal");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Desktop_ArvoreEmLinha_SemRolagemHorizontalDaPagina(string tema)
	{
		var pagina = await AbrirOrganogramaAsync(tema);

		await Expect(Nome(pagina, "Caio")).ToBeVisibleAsync();
		(await pagina.Locator(".sc-org__equipe").First.EvaluateAsync<string>("e => getComputedStyle(e).display"))
			.Should().Be("flex", "a partir de 768px a equipe fica lado a lado");
		(await pagina.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth"))
			.Should().BeTrue();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task SemJavaScript_AArvoreApareceAbertaComOsLinks(string tema)
	{
		var pagina = await AbrirOrganogramaAsync(tema, javaScript: false);

		await Expect(pagina.Locator(".sc-org details[open]")).ToHaveCountAsync(2);

		foreach (var quem in new[] { "Chefe", "Ana", "Bruno", "Caio" })
		{
			await Expect(Nome(pagina, quem)).ToBeVisibleAsync();
		}
	}
}
