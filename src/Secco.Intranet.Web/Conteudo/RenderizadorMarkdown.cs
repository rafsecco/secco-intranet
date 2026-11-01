using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Html;

namespace Secco.Intranet.Web.Conteudo;

/// <summary>
/// Renderização com o Markdig, <b>com HTML bruto desabilitado</b>. É a diferença entre um
/// sanitizador — que precisa acertar todas as vezes e envelhece a cada CVE — e um pipeline
/// que nunca produz a tag perigosa: <c>&lt;script&gt;</c> no texto sai escapado, como texto.
/// </summary>
/// <remarks>
/// Desligar o HTML não fecha o destino de link e imagem: o Markdig emite o <c>href</c>/<c>src</c>
/// como veio, e <c>javascript:</c> roda na sessão de quem clicar. Por isso o destino passa por uma
/// lista de esquemas permitidos entre a análise e a renderização; fora dela, o link vira o próprio
/// texto e a imagem vira a descrição.
/// </remarks>
public sealed class RenderizadorMarkdown : IRenderizadorMarkdown
{
	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
		.DisableHtml()
		.UseAutoLinks()
		.Build();

	private static readonly HashSet<string> EsquemasPermitidos =
		new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto", "tel" };

	/// <inheritdoc />
	public IHtmlContent Renderizar(string? markdown)
	{
		if (string.IsNullOrWhiteSpace(markdown))
		{
			return HtmlString.Empty;
		}

		var documento = Markdown.Parse(markdown, Pipeline);

		NeutralizarDestinosNaoPermitidos(documento);

		using var escritor = new StringWriter();
		var renderizador = new HtmlRenderer(escritor);
		Pipeline.Setup(renderizador);
		renderizador.Render(documento);
		escritor.Flush();

		return new HtmlString(escritor.ToString());
	}

	private static void NeutralizarDestinosNaoPermitidos(MarkdownDocument documento)
	{
		// Materializado antes de mexer: trocar nós durante a enumeração invalida o percurso.
		foreach (var link in documento.Descendants<LinkInline>().ToList())
		{
			if (!DestinoPermitido(link.Url))
			{
				DesembrulharTexto(link);
			}
		}

		foreach (var autolink in documento.Descendants<AutolinkInline>().ToList())
		{
			if (!DestinoPermitido(autolink.Url))
			{
				autolink.ReplaceBy(new LiteralInline(autolink.Url));
			}
		}
	}

	// Os filhos do link são o texto dele; os da imagem, a descrição. Sobem para o lugar do nó.
	private static void DesembrulharTexto(LinkInline link)
	{
		while (link.FirstChild is { } filho)
		{
			filho.Remove();
			link.InsertBefore(filho);
		}

		link.Remove();
	}

	/// <summary>
	/// Relativo (sem esquema) ou com esquema da lista. O navegador ignora espaço e caractere de
	/// controle dentro do esquema (<c>java&#9;script:</c> executa), então eles saem antes de ler.
	/// </summary>
	private static bool DestinoPermitido(string? url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return true;
		}

		var normalizado = new string([.. url.Where(caractere => !char.IsWhiteSpace(caractere) && !char.IsControl(caractere))]);
		var fimDoEsquema = normalizado.IndexOfAny([':', '/', '?', '#']);

		return fimDoEsquema < 0
			|| normalizado[fimDoEsquema] != ':'
			|| EsquemasPermitidos.Contains(normalizado[..fimDoEsquema]);
	}
}
