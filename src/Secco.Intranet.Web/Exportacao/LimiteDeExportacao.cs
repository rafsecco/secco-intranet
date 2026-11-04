using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Secco.Intranet.Web.Extensibility;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.Exportacao;

/// <summary>
/// Limite de taxa das exportações por pessoa (<c>sub</c>), além da concorrência do SDK (ADR-0058 da
/// plataforma): uma pessoa só não ocupa todas as vagas. Mesmo desenho do <c>ExportRateLimiting</c> do
/// LogStream. O contador é da instância: com N réplicas, o teto efetivo é N vezes o configurado, e
/// perder o contador é tolerável.
/// </summary>
internal static class LimiteDeExportacao
{
	/// <summary>Chave, dentro de <see cref="ExportacaoServiceCollectionExtensions.SectionKey"/>, do limite por minuto.</summary>
	public const string ChavePorPessoaPorMinuto = "PorPessoaPorMinuto";

	/// <summary>Exportações por pessoa por minuto quando a chave não é configurada.</summary>
	public const int PadraoPorMinuto = 10;

	/// <summary>Registra a política <see cref="PoliticaDeExportacao.Nome"/>; acima do limite, <c>429</c>.</summary>
	/// <param name="services">Coleção de serviços.</param>
	public static IServiceCollection AddLimiteDeExportacao(this IServiceCollection services)
	{
		var chave = $"{ExportacaoServiceCollectionExtensions.SectionKey}:{ChavePorPessoaPorMinuto}";

		services.AddRateLimiter(_ => { });

		// Lido na montagem das options, não no registro: fontes de configuração tardias (a dos testes
		// de integração, por exemplo) também valem.
		services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, configuration) =>
		{
			var porMinuto = configuration.GetValue(chave, PadraoPorMinuto);

			if (porMinuto < 1)
			{
				throw new InvalidOperationException($"'{chave}' deve ser maior que zero.");
			}

			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			options.AddPolicy(PoliticaDeExportacao.Nome, context => RateLimitPartition.GetFixedWindowLimiter(
				// Sem sub (modo aberto de DEV), todos caem numa partição só.
				context.User.FindFirst(SeccoClaims.Subject)?.Value ?? string.Empty,
				_ => new FixedWindowRateLimiterOptions
				{
					PermitLimit = porMinuto,
					Window = TimeSpan.FromMinutes(1),
					QueueLimit = 0,
				}));
		});

		return services;
	}
}
