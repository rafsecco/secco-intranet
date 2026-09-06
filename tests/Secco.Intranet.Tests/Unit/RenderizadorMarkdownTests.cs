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

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Renderizar_SemConteudo_DevolveVazio(string? markdown) =>
		Renderizar(markdown).Should().BeEmpty();
}
