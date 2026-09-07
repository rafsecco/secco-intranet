namespace Secco.Intranet.Application.Documentos;

/// <summary>
/// Apura o tipo real de um arquivo enviado. O <c>Content-Type</c> declarado pelo cliente
/// nunca é aceito: quem envia controla esse cabeçalho, então ele diz o que o remetente quer
/// que acreditemos, não o que o arquivo é. O que vale é a extensão cruzada com a assinatura
/// dos primeiros bytes.
/// </summary>
public static class TiposDeArquivo
{
	/// <summary>Quantidade de bytes iniciais suficiente para reconhecer as assinaturas conhecidas.</summary>
	public const int BytesDeAssinatura = 8;

	private static readonly Dictionary<string, (string ContentType, byte[][] Assinaturas)> PorExtensao =
		new(StringComparer.OrdinalIgnoreCase)
		{
			[".pdf"] = ("application/pdf", [[0x25, 0x50, 0x44, 0x46]]),
			[".png"] = ("image/png", [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]]),
			[".jpg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
			[".jpeg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
			[".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ZipAssinaturas),
			[".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ZipAssinaturas),
			[".pptx"] = ("application/vnd.openxmlformats-officedocument.presentationml.presentation", ZipAssinaturas),
			[".zip"] = ("application/zip", ZipAssinaturas),

			// Formatos de texto não têm assinatura: a lista vazia significa aceitar sem conferir.
			[".txt"] = ("text/plain", []),
			[".csv"] = ("text/csv", []),
			[".md"] = ("text/markdown", []),
		};

	private static byte[][] ZipAssinaturas =>
	[
		[0x50, 0x4B, 0x03, 0x04],
		[0x50, 0x4B, 0x05, 0x06],
		[0x50, 0x4B, 0x07, 0x08],
	];

	/// <summary>
	/// Devolve o tipo apurado, ou <c>null</c> quando a extensão não é aceita ou o conteúdo não
	/// corresponde a ela.
	/// </summary>
	/// <param name="nomeArquivo">Nome original do arquivo.</param>
	/// <param name="prefixo">Primeiros bytes do conteúdo.</param>
	/// <param name="extensoesPermitidas">Extensões aceitas pela configuração.</param>
	public static string? Apurar(
		string? nomeArquivo,
		ReadOnlySpan<byte> prefixo,
		IEnumerable<string> extensoesPermitidas)
	{
		ArgumentNullException.ThrowIfNull(extensoesPermitidas);

		if (string.IsNullOrWhiteSpace(nomeArquivo))
		{
			return null;
		}

		var extensao = Path.GetExtension(nomeArquivo);

		if (string.IsNullOrEmpty(extensao)
			|| !extensoesPermitidas.Contains(extensao, StringComparer.OrdinalIgnoreCase)
			|| !PorExtensao.TryGetValue(extensao, out var tipo))
		{
			return null;
		}

		if (tipo.Assinaturas.Length == 0)
		{
			return tipo.ContentType;
		}

		foreach (var assinatura in tipo.Assinaturas)
		{
			if (prefixo.Length >= assinatura.Length && prefixo[..assinatura.Length].SequenceEqual(assinatura))
			{
				return tipo.ContentType;
			}
		}

		return null;
	}

	/// <summary>Nome do ícone que representa a extensão na interface.</summary>
	/// <param name="nomeArquivo">Nome original do arquivo.</param>
	public static string Icone(string? nomeArquivo) =>
		Path.GetExtension(nomeArquivo ?? string.Empty).ToLowerInvariant() switch
		{
			".pdf" => "bi-file-earmark-pdf",
			".png" or ".jpg" or ".jpeg" => "bi-file-earmark-image",
			".docx" => "bi-file-earmark-word",
			".xlsx" or ".csv" => "bi-file-earmark-spreadsheet",
			".pptx" => "bi-file-earmark-slides",
			".zip" => "bi-file-earmark-zip",
			_ => "bi-file-earmark-text",
		};
}
