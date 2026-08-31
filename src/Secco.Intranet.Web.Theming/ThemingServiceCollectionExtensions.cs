using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Secco.Intranet.Web.Theming;

/// <summary>Composição de DI do sistema de temas.</summary>
public static class ThemingServiceCollectionExtensions
{
	/// <summary>
	/// Registra o tema ativo e o expander que dá precedência às views dele. O bind das
	/// options é lazy — o mesmo padrão do resto do produto, para respeitar fontes de
	/// configuração adicionadas por testes ou por hosting tardio.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddIntranetTheming(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddSingleton(serviceProvider =>
		{
			var options = new ThemeOptions();
			serviceProvider.GetRequiredService<IConfiguration>()
				.GetSection(ThemeOptions.SectionKey)
				.Bind(options);

			return options;
		});

		// Registrado por último para que a prefixação das localizações do tema seja a
		// transformação final — nenhum outro expander reordena o que ele decidiu.
		services.AddOptions<RazorViewEngineOptions>()
			.Configure<ThemeOptions>((razorOptions, themeOptions) =>
				razorOptions.ViewLocationExpanders.Add(new ThemeViewLocationExpander(themeOptions)));

		return services;
	}
}
