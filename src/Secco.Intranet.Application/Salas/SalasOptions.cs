namespace Secco.Intranet.Application.Salas;

/// <summary>
/// Configuração da reserva de salas, seção <c>Intranet:Salas</c>. Bind lazy feito pela
/// Infrastructure — a Application não conhece configuração.
/// </summary>
public sealed class SalasOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	public const string SectionKey = "Intranet:Salas";

	/// <summary>
	/// Fuso horário em que os horários de reserva são entendidos, como identificador IANA ou do
	/// Windows (default <c>America/Sao_Paulo</c>).
	/// </summary>
	public string FusoHorario { get; set; } = "America/Sao_Paulo";
}
