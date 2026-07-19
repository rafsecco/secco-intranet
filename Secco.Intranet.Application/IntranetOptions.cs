namespace Secco.Intranet.Application;

/// <summary>
/// Limites de entrada do produto (ADR-0020), configuráveis na seção
/// <c>Intranet:Limits</c> — bind lazy feito pela Infrastructure.
/// </summary>
public sealed class IntranetOptions
{
	/// <summary>Tamanho máximo do nome de um setor (default 256).</summary>
	public int MaxNameLength { get; set; } = 256;

	/// <summary>Tamanho máximo da descrição (default 4096).</summary>
	public int MaxDescriptionLength { get; set; } = 4_096;
}
