using Microsoft.AspNetCore.Routing.Patterns;
using Secco.Intranet.Application.Bibliotecas;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Slugs de biblioteca reservados tirados das rotas registradas: o segundo segmento fixo de cada
/// endpoint cujo primeiro segmento é o literal <c>bibliotecas</c> (<c>/bibliotecas/nova</c>
/// reserva <c>nova</c>). Rota fixa nova sob <c>/bibliotecas</c> fica reservada sem editar lista
/// nenhuma. A leitura acontece na primeira consulta, quando as rotas já estão montadas, e fica em cache.
/// </summary>
public sealed class SlugsReservadosDeBibliotecaDasRotas : ISlugsReservadosDeBiblioteca
{
	private const string Prefixo = "bibliotecas";

	private readonly Lazy<IReadOnlySet<string>> _todos;

	/// <summary>Cria o serviço sobre as fontes de endpoint do app.</summary>
	/// <param name="fontes">Fontes de endpoint registradas.</param>
	public SlugsReservadosDeBibliotecaDasRotas(IEnumerable<EndpointDataSource> fontes)
	{
		ArgumentNullException.ThrowIfNull(fontes);

		_todos = new Lazy<IReadOnlySet<string>>(() => Calcular(fontes));
	}

	/// <summary>Todos os slugs reservados, sem diferenciar caixa.</summary>
	public IReadOnlySet<string> Todos => _todos.Value;

	/// <inheritdoc />
	public bool Contem(string? slug) => slug is not null && Todos.Contains(slug.Trim());

	/// <summary>Segundo segmento fixo da rota quando o primeiro é <c>bibliotecas</c>; nulo nos demais casos.</summary>
	/// <param name="padrao">Padrão da rota.</param>
	private static string? SegundoSegmentoFixo(RoutePattern padrao)
	{
		if (padrao.PathSegments.Count < 2
			|| padrao.PathSegments[0].Parts[0] is not RoutePatternLiteralPart primeiro
			|| !string.Equals(primeiro.Content, Prefixo, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return padrao.PathSegments[1].Parts[0] is RoutePatternLiteralPart segundo ? segundo.Content : null;
	}

	private static HashSet<string> Calcular(IEnumerable<EndpointDataSource> fontes)
	{
		var todos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var endpoint in fontes.SelectMany(fonte => fonte.Endpoints).OfType<RouteEndpoint>())
		{
			if (SegundoSegmentoFixo(endpoint.RoutePattern) is { } segmento)
			{
				todos.Add(segmento);
			}
		}

		return todos;
	}
}
