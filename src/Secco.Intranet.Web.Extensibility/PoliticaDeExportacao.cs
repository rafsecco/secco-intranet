namespace Secco.Intranet.Web.Extensibility;

/// <summary>
/// Limite de taxa das exportações, por pessoa (<c>sub</c>), registrado pelo host (ADR-0058 da
/// plataforma). Toda ação que exporta aplica a política pelo nome:
/// <c>[EnableRateLimiting(PoliticaDeExportacao.Nome)]</c>. Acima do limite, a resposta é <c>429</c>.
/// </summary>
public static class PoliticaDeExportacao
{
	/// <summary>Nome da política de rate limiting das exportações.</summary>
	public const string Nome = "exportacao";
}
