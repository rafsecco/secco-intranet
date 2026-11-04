using Secco.Intranet.Web.Extensibility;
using Secco.SDK.Documents;

namespace Secco.Intranet.Web.Exportacao;

/// <summary>
/// Exportação Excel/PDF para RCLs (ADR-0013): o exportador do <c>Secco.SDK.Documents</c>, a trilha
/// de exportação e o limite por pessoa, todos resolvidos do contêiner do host. A RCL só injeta.
/// </summary>
public static class ExportacaoServiceCollectionExtensions
{
	/// <summary>
	/// Seção de configuração da exportação: as chaves do SDK (<c>MaxXlsxRows</c>, <c>MaxPdfRows</c>,
	/// <c>MaxConcurrentExports</c>, <c>TempDirectory</c>) e o limite por pessoa. Valor fora da faixa
	/// derruba a subida.
	/// </summary>
	public const string SectionKey = "Intranet:Exportacao";

	/// <summary>Registra o exportador, a trilha de exportação e a política de limite.</summary>
	/// <param name="services">Coleção de serviços.</param>
	public static IServiceCollection AddIntranetExportacao(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddSeccoDocuments(SectionKey);
		services.AddScoped<ITrilhaDeExportacao, TrilhaDeExportacao>();

		return services;
	}
}
