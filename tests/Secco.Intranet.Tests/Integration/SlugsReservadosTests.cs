using AwesomeAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Setores;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O setor mora na raiz (/{slug}/…). Todo endpoint cujo primeiro segmento é fixo — rota de
/// atributo, rota convencional, health check — precisa estar reservado, senão um setor com esse
/// slug ficaria inalcançável. Um controller novo que esqueça de reservar o nome quebra aqui.
/// </summary>
public class SlugsReservadosTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>
{
	[Fact]
	public void TodoPrimeiroSegmentoFixo_EstaReservado()
	{
		var fontes = factory.Services.GetServices<EndpointDataSource>();

		var segmentos = fontes
			.SelectMany(fonte => fonte.Endpoints)
			.OfType<RouteEndpoint>()
			.Select(endpoint => PrimeiroSegmentoFixo(endpoint.RoutePattern))
			.OfType<string>()
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		segmentos.Should().NotBeEmpty("o app tem rotas fixas — lista vazia quer dizer que a varredura quebrou");
		segmentos.Should().Contain(s => s.Equals("setores", StringComparison.OrdinalIgnoreCase));
		segmentos.Where(s => !SlugsReservados.Contem(s)).Should().BeEmpty(
			"cada um destes caminhos colidiria com um setor de mesmo slug");
	}

	private static string? PrimeiroSegmentoFixo(RoutePattern padrao)
	{
		if (padrao.PathSegments.Count == 0)
		{
			return null;
		}

		var parte = padrao.PathSegments[0].Parts[0];

		return parte switch
		{
			RoutePatternLiteralPart literal => literal.Content,
			// Rota convencional: {controller} vem preenchido pelo endpoint (RequiredValues).
			RoutePatternParameterPart parametro
				when padrao.RequiredValues.TryGetValue(parametro.Name, out var valor) && valor is string texto
				=> texto,
			_ => null,
		};
	}
}
