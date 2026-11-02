using System.Text.Encodings.Web;
using AwesomeAssertions;
using Secco.Intranet.Web.Conteudo;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Renderização de Markdown. O ponto que importa não é a formatação — é que HTML bruto no
/// texto nunca sobreviva, por construção do pipeline e não por filtro posterior.
/// </summary>
public class RenderizadorMarkdownTests
{
	private static string Renderizar(string? markdown)
	{
		using var escritor = new StringWriter();
		new RenderizadorMarkdown().Renderizar(markdown).WriteTo(escritor, HtmlEncoder.Default);

		return escritor.ToString();
	}

	[Fact]
	public void Renderizar_ComNegrito_GeraStrong() =>
		Renderizar("Atenção: **importante**.").Should().Contain("<strong>importante</strong>");

	[Fact]
	public void Renderizar_ComLink_GeraAnchor() =>
		Renderizar("Ver o [manual](/documentos/1).").Should().Contain("href=\"/documentos/1\"");

	[Fact]
	public void Renderizar_ComScript_EscapaEmVezDeExecutar()
	{
		var html = Renderizar("Antes <script>alert('x')</script> depois.");

		html.Should().NotContain("<script>", "HTML bruto é desabilitado no pipeline");
		html.Should().Contain("&lt;script&gt;", "o texto original continua legível, apenas escapado");
	}

	[Fact]
	public void Renderizar_ComAtributoDeEvento_NaoGeraHtmlExecutavel()
	{
		var html = Renderizar("<img src=x onerror=alert(1)>");

		// O que torna a entrada inofensiva e a tag nao existir. A string "onerror=alert"
		// continua na saida, e deve continuar: ela sobrevive como TEXTO escapado dentro do
		// paragrafo, que e o comportamento desejado. Afirmar a ausencia dela testaria a
		// coisa errada e passaria a exigir um sanitizador que o pipeline dispensa.
		html.Should().NotContain("<img", "sem tag, nao ha atributo de evento para o navegador executar");
		html.Trim().Should().Be("<p>&lt;img src=x onerror=alert(1)&gt;</p>");
	}

	// Desligar o HTML bruto não fecha o destino do link: o Markdig emite o href/src como veio, e um
	// `javascript:` vira script na sessão de quem clicar. Cada linha é uma forma de chegar lá.
	[Theory]
	[InlineData("[clique](javascript:alert(document.cookie))")]
	[InlineData("[clique](JaVaScRiPt:alert(1))")]
	[InlineData("[clique](jav&#x61;script:alert(1))")]
	[InlineData("[clique](java&#9;script:alert(1))")]
	[InlineData("[clique]( javascript:alert(1) )")]
	[InlineData("[clique](vbscript:msgbox(1))")]
	[InlineData("[clique](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)")]
	[InlineData("[clique][ref]\n\n[ref]: javascript:alert(1)")]
	[InlineData("![clique](javascript:alert(1))")]
	[InlineData("<javascript:alert(1)>")]
	public void Renderizar_ComDestinoDeEsquemaNaoPermitido_NaoGeraLinkNemImagem(string markdown)
	{
		var html = Renderizar(markdown);

		html.Should().NotContain("<a ", "um destino fora da lista de esquemas não pode virar link");
		html.Should().NotContain("<img", "nem imagem");
	}

	[Theory]
	[InlineData("[clique](javascript:alert(1))", "clique")]
	[InlineData("![cartaz da festa](javascript:alert(1))", "cartaz da festa")]
	public void Renderizar_ComDestinoDeEsquemaNaoPermitido_MantemOTextoVisivel(string markdown, string texto) =>
		Renderizar(markdown).Should().Contain(texto, "quem lê ainda vê o texto do link ou a descrição da imagem");

	[Theory]
	[InlineData("[site](https://exemplo.com/a)", "href=\"https://exemplo.com/a\"")]
	[InlineData("[site](http://exemplo.com/a)", "href=\"http://exemplo.com/a\"")]
	[InlineData("[e-mail](mailto:rh@exemplo.com)", "href=\"mailto:rh@exemplo.com\"")]
	[InlineData("[ramal](tel:+551130000000)", "href=\"tel:+551130000000\"")]
	[InlineData("[manual](/documentos/1)", "href=\"/documentos/1\"")]
	[InlineData("[abaixo](#prazos)", "href=\"#prazos\"")]
	[InlineData("[relativo](anexo/regras)", "href=\"anexo/regras\"")]
	[InlineData("Veja www.exemplo.com hoje.", "href=\"http://www.exemplo.com\"")]
	public void Renderizar_ComDestinoPermitido_MantemOLink(string markdown, string atributo) =>
		Renderizar(markdown).Should().Contain(atributo);

	[Theory]
	[InlineData("![logo](/publicacoes/1/imagem)", "src=\"/publicacoes/1/imagem\"")]
	[InlineData("![logo](anexo/logo.png)", "src=\"anexo/logo.png\"")]
	public void Renderizar_ImagemDaPropriaIntranet_MantemAImagem(string markdown, string atributo) =>
		Renderizar(markdown).Should().Contain("<img").And.Contain(atributo);

	// Imagem de fora não carrega: a CSP só aceita imagem da própria origem, e nenhum terceiro deve ver
	// quem lê o Mural. Vira link, com a descrição como texto, para quem quiser abrir por conta própria.
	[Theory]
	[InlineData("![cartaz da festa](https://exemplo.com/cartaz.png)", "href=\"https://exemplo.com/cartaz.png\"", "cartaz da festa")]
	[InlineData("![](http://exemplo.com/cartaz.png)", "href=\"http://exemplo.com/cartaz.png\"", "http://exemplo.com/cartaz.png")]
	[InlineData("![cartaz](//exemplo.com/cartaz.png)", "href=\"//exemplo.com/cartaz.png\"", "cartaz")]
	public void Renderizar_ImagemExterna_ViraLinkSemImagem(string markdown, string atributo, string texto)
	{
		var html = Renderizar(markdown);

		html.Should().NotContain("<img", "imagem externa não carrega dentro da Intranet");
		html.Should().Contain("<a ").And.Contain(atributo).And.Contain($">{texto}</a>");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Renderizar_SemConteudo_DevolveVazio(string? markdown) =>
		Renderizar(markdown).Should().BeEmpty();
}
