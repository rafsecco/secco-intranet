using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Setores;

namespace Secco.Intranet.Application;

/// <summary>Composição de DI da camada de aplicação.</summary>
public static class IntranetApplicationExtensions
{
	/// <summary>
	/// Registra os casos de uso. As options são registradas pela Infrastructure
	/// (bind lazy da configuração) — a Application não conhece configuração.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddIntranetApplication(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddScoped<CreateSetorHandler>();
		services.AddScoped<GetSetorByIdHandler>();
		services.AddScoped<SearchSetoresHandler>();

		return services;
	}
}
