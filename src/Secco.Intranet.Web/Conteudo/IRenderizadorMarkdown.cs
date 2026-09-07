using Microsoft.AspNetCore.Html;

namespace Secco.Intranet.Web.Conteudo;

/// <summary>Converte o Markdown guardado em HTML para exibição.</summary>
public interface IRenderizadorMarkdown
{
	/// <summary>Renderiza o Markdown informado.</summary>
	/// <param name="markdown">Texto em Markdown; nulo ou vazio devolve conteúdo vazio.</param>
	IHtmlContent Renderizar(string? markdown);
}
