using Microsoft.AspNetCore.Routing.Patterns;
using Secco.Intranet.Application.Setores;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Slugs reservados tirados das rotas registradas: o primeiro segmento fixo de cada endpoint (rota
/// de atributo, rota convencional, health check), somado aos caminhos que não viram endpoint.
/// Controller novo — do core ou de uma RCL referenciada — fica reservado sem editar lista nenhuma.
/// A leitura acontece na primeira consulta, quando as rotas já estão montadas, e fica em cache.
/// </summary>
public sealed class SlugsReservadosDasRotas : ISlugsReservados
{
	/// <summary>Caminhos que um setor não pode usar e que não aparecem entre os endpoints.</summary>
	private static readonly string[] ForaDasRotas =
	[
		// Prefixo antigo da página do setor: reservado para nunca virar um setor que confunda links velhos.
		"setor",
		// Infraestrutura e arquivos estáticos, servidos antes do roteamento.
		"_content", "css", "js", "lib", "img", "favicon.ico", "api", ".well-known",
		// Callbacks do OpenID Connect: o middleware de autenticação responde antes do roteamento.
		"signin-oidc", "signout-callback-oidc",
	];

	private readonly Lazy<IReadOnlySet<string>> _todos;

	/// <summary>Cria o serviço sobre as fontes de endpoint do app.</summary>
	/// <param name="fontes">Fontes de endpoint registradas.</param>
	public SlugsReservadosDasRotas(IEnumerable<EndpointDataSource> fontes)
	{
		ArgumentNullException.ThrowIfNull(fontes);

		_todos = new Lazy<IReadOnlySet<string>>(() => Calcular(fontes));
	}

	/// <summary>Todos os slugs reservados, sem diferenciar caixa.</summary>
	public IReadOnlySet<string> Todos => _todos.Value;

	/// <inheritdoc />
	public bool Contem(string? slug) => slug is not null && Todos.Contains(slug.Trim());

	/// <summary>Primeiro segmento fixo da rota; nulo se ela começa por parâmetro livre ou é a raiz.</summary>
	/// <param name="padrao">Padrão da rota.</param>
	private static string? PrimeiroSegmentoFixo(RoutePattern padrao)
	{
		if (padrao.PathSegments.Count == 0)
		{
			return null;
		}

		return padrao.PathSegments[0].Parts[0] switch
		{
			RoutePatternLiteralPart literal => literal.Content,
			// Rota convencional: {controller} vem preenchido pelo endpoint (RequiredValues).
			RoutePatternParameterPart parametro
				when padrao.RequiredValues.TryGetValue(parametro.Name, out var valor) && valor is string texto
				=> texto,
			_ => null,
		};
	}

	private static HashSet<string> Calcular(IEnumerable<EndpointDataSource> fontes)
	{
		var todos = new HashSet<string>(ForaDasRotas, StringComparer.OrdinalIgnoreCase);

		foreach (var endpoint in fontes.SelectMany(fonte => fonte.Endpoints).OfType<RouteEndpoint>())
		{
			if (PrimeiroSegmentoFixo(endpoint.RoutePattern) is { } segmento)
			{
				todos.Add(segmento);
			}
		}

		return todos;
	}
}
