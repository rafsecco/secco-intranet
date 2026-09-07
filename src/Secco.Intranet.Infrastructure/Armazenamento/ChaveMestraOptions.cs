namespace Secco.Intranet.Infrastructure.Armazenamento;

/// <summary>
/// Chave mestra que embrulha as chaves dos arquivos (seção <c>Intranet:Documentos:Chave</c>).
/// Espelha o desenho já adotado pela plataforma para a cifragem do catálogo: chave ativa
/// para cifrar e decifrar, chaves aposentadas apenas para decifrar durante a rotação, e uma
/// chave de desenvolvimento embutida para não exigir configuração em DEV.
/// </summary>
/// <remarks>
/// Como a cifragem é em envelope, rotacionar esta chave reescreve apenas a coluna
/// <c>ds_chave_embrulhada</c> de cada documento — nenhum arquivo é reescrito. É também o que
/// torna viável guardá-la num KMS: o que passa pelo serviço são 32 bytes, não o arquivo.
/// </remarks>
public sealed class ChaveMestraOptions
{
	/// <summary>Seção de configuração de onde estas opções são lidas.</summary>
	public const string SectionKey = "Intranet:Documentos:Chave";

	/// <summary>Tamanho exigido da chave mestra, em bytes (AES-256).</summary>
	public const int TamanhoEmBytes = 32;

	/// <summary>
	/// Chave de desenvolvimento embutida (base64 de 32 bytes), usada quando
	/// <see cref="ChaveAtiva"/> está ausente <b>fora de Production</b>. Proibida em Production.
	/// </summary>
	public const string ChaveDesenvolvimento = "c2VjY28taW50cmFuZXQtZGV2LWRvY3VtZW50by1rZXk=";

	/// <summary>Chave mestra ativa (base64, 32 bytes). Obrigatória em Production.</summary>
	public string? ChaveAtiva { get; set; }

	/// <summary>
	/// Chaves aposentadas (base64, 32 bytes cada), mantidas <b>só para decifrar</b> enquanto a
	/// rotação não reembrulhou todos os documentos.
	/// </summary>
	public IList<string> ChavesAposentadas { get; set; } = [];
}
