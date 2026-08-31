using Microsoft.AspNetCore.Mvc.Razor;

namespace Secco.Intranet.Web.Theming;

/// <summary>
/// Coloca as views do tema ativo <b>antes</b> das do core na resolução (ADR-0003): o tema
/// pode sobrescrever qualquer view, mas só precisa entregar o layout e os parciais do
/// contrato — o que ele não define cai nas views do core.
/// </summary>
/// <remarks>
/// O padrão <c>Shared</c> também atende view components: o MVC os procura pelo nome de view
/// <c>Components/{Componente}/{View}</c>, que entra no token <c>{0}</c>.
/// </remarks>
/// <param name="options">Seleção do tema ativo.</param>
public sealed class ThemeViewLocationExpander(ThemeOptions options) : IViewLocationExpander
{
	private const string ThemeKey = "secco-theme";

	/// <inheritdoc />
	public void PopulateValues(ViewLocationExpanderContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		// Entra na chave de cache das views: trocar o tema invalida o cache automaticamente.
		context.Values[ThemeKey] = options.Nome;
	}

	/// <inheritdoc />
	public IEnumerable<string> ExpandViewLocations(
		ViewLocationExpanderContext context,
		IEnumerable<string> viewLocations)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(viewLocations);

		if (!context.Values.TryGetValue(ThemeKey, out var theme) || string.IsNullOrWhiteSpace(theme))
		{
			return viewLocations;
		}

		return
		[
			$"/Themes/{theme}/Views/{{1}}/{{0}}.cshtml",
			$"/Themes/{theme}/Views/Shared/{{0}}.cshtml",
			.. viewLocations,
		];
	}
}
