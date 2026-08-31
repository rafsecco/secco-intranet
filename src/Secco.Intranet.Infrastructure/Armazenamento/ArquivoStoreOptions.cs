namespace Secco.Intranet.Infrastructure.Armazenamento;

/// <summary>
/// Onde os arquivos ficam (seção <c>Intranet:Documentos:Armazenamento</c>). O conteúdo é
/// gravado cifrado, com nome opaco, e nunca é exposto como conteúdo estático: toda leitura
/// passa pelo endpoint de download, que avalia a visibilidade do documento.
/// </summary>
public sealed class ArquivoStoreOptions
{
	/// <summary>Seção de configuração de onde estas opções são lidas.</summary>
	public const string SectionKey = "Intranet:Documentos:Armazenamento";

	/// <summary>
	/// Raiz do armazenamento. Precisa apontar para fora da pasta pública da aplicação — em
	/// container, um volume dedicado.
	/// </summary>
	public string Raiz { get; set; } = Path.Combine(AppContext.BaseDirectory, "documentos");
}
