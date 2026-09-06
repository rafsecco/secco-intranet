using Markdig;
using Microsoft.AspNetCore.Html;

namespace Secco.Intranet.Web.Conteudo;

/// <summary>
/// Renderização com o Markdig, <b>com HTML bruto desabilitado</b>. É a diferença entre um
/// sanitizador — que precisa acertar todas as vezes e envelhece a cada CVE — e um pipeline
/// que nunca produz a tag perigosa: <c>&lt;script&gt;</c> no texto sai escapado, como texto.
/// </summary>
public sealed class RenderizadorMarkdown : IRenderizadorMarkdown
{
	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
		.DisableHtml()
		.UseAutoLinks()
		.Build();

	/// <inheritdoc />
	public IHtmlContent Renderizar(string? markdown) =>
		string.IsNullOrWhiteSpace(markdown)
			? HtmlString.Empty
			: new HtmlString(Markdown.ToHtml(markdown, Pipeline));
}
