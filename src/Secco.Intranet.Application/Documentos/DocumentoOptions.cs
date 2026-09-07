namespace Secco.Intranet.Application.Documentos;

/// <summary>
/// Limites de upload (seção <c>Intranet:Documentos</c>). Bind lazy feito pela Infrastructure,
/// como o restante da configuração do produto.
/// </summary>
public sealed class DocumentoOptions
{
	/// <summary>Seção de configuração de onde estas opções são lidas.</summary>
	public const string SectionKey = "Intranet:Documentos";

	/// <summary>Tamanho máximo aceito por arquivo, em bytes (default 25 MB).</summary>
	public long TamanhoMaximoBytes { get; set; } = 25L * 1024 * 1024;

	/// <summary>Extensões aceitas, com ponto e em minúsculas.</summary>
	public IList<string> ExtensoesPermitidas { get; set; } =
		[".pdf", ".png", ".jpg", ".jpeg", ".docx", ".xlsx", ".pptx", ".txt", ".csv", ".md", ".zip"];
}
