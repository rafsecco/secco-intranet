using Secco.Intranet.Application.Setores;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Restrição <c>{slug:setor}</c> da página do setor, que mora na raiz da URL. Rota de atributo tem
/// <c>Order</c> 0 e venceria as rotas convencionais (<c>/Setores/Create</c>, <c>/Acesso/…</c>) antes de
/// a precedência de segmento literal contar; recusar os <see cref="SlugsReservados"/> aqui devolve
/// esses caminhos a quem é dono deles. Subir o <c>Order</c> não serve: a geração de links passaria a
/// preferir a rota convencional e montaria <c>/Setor/Publicar?slug=…</c>.
/// </summary>
public sealed class SlugDeSetorRouteConstraint : IRouteConstraint
{
	/// <summary>Nome da restrição no template.</summary>
	public const string Nome = "setor";

	/// <inheritdoc />
	public bool Match(
		HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
	{
		ArgumentNullException.ThrowIfNull(values);

		return values.TryGetValue(routeKey, out var valor)
			&& valor?.ToString() is { Length: > 0 } slug
			&& !SlugsReservados.Contem(slug);
	}
}
