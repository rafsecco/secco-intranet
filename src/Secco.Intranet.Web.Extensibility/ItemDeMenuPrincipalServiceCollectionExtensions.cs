using Microsoft.Extensions.DependencyInjection;

namespace Secco.Intranet.Web.Extensibility;

/// <summary>Registro explícito de itens do menu principal no DI.</summary>
public static class ItemDeMenuPrincipalServiceCollectionExtensions
{
	/// <summary>
	/// Registra <typeparamref name="T"/> como item do menu principal, além da descoberta pelas
	/// application parts. O tipo é instanciado a cada requisição, não resolvido do contêiner.
	/// </summary>
	/// <typeparam name="T">Implementação do item.</typeparam>
	/// <param name="services">Coleção de serviços.</param>
	/// <returns>A mesma coleção, para encadeamento.</returns>
	public static IServiceCollection AddItemDeMenuPrincipal<T>(this IServiceCollection services)
		where T : class, IItemDeMenuPrincipal
	{
		ArgumentNullException.ThrowIfNull(services);
		return services.AddSingleton(new RegistroDeItemDeMenuPrincipal(typeof(T)));
	}
}
