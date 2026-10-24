namespace Secco.Intranet.Application.Vencimentos;

/// <summary>
/// Configuração do aviso de vencimento, seção <c>Intranet:Vencimentos</c>. Bind lazy feito
/// pela Infrastructure — a Application não conhece configuração.
/// </summary>
public sealed class VencimentosOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	public const string SectionKey = "Intranet:Vencimentos";

	/// <summary>Liga ou desliga o aviso automático (default <c>true</c>).</summary>
	public bool Habilitado { get; set; } = true;

	/// <summary>Intervalo entre varreduras do aviso, em minutos (default 60).</summary>
	public int IntervaloEmMinutos { get; set; } = 60;

	/// <summary>
	/// Fuso horário que define o "hoje" do produto, como identificador IANA ou do Windows
	/// (default <c>America/Sao_Paulo</c>).
	/// </summary>
	public string FusoHorario { get; set; } = "America/Sao_Paulo";
}
