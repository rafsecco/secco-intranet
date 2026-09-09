namespace Secco.Intranet.Application.Auditoria;

/// <summary>
/// Configuração da auditoria, seção <c>Intranet:Auditoria</c>. Bind lazy feito pela
/// Infrastructure — a Application não conhece configuração.
/// </summary>
public sealed class AuditoriaOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	public const string SectionKey = "Intranet:Auditoria";

	/// <summary>
	/// URL base do <c>Secco.LogStream</c>. Vazia desliga o registro — é o modo DEV/Testing, em
	/// que a operação acontece e nada é auditado, que é a verdade: não há para onde escrever.
	/// </summary>
	public string LogStreamUrl { get; set; } = string.Empty;
}
