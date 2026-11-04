using AwesomeAssertions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.Intranet.Web.Exportacao;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>O limite por pessoa valida o valor na montagem das options: menor que 1 não sobe.</summary>
public class LimiteDeExportacaoTests
{
	private static ServiceProvider Montar(string? porMinuto)
	{
		var valores = new Dictionary<string, string?>();

		if (porMinuto is not null)
		{
			valores["Intranet:Exportacao:PorPessoaPorMinuto"] = porMinuto;
		}

		var services = new ServiceCollection()
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(valores).Build());
		services.AddLimiteDeExportacao();

		return services.BuildServiceProvider();
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-3")]
	public void PorMinuto_menorQueUm_derrubaAMontagem(string porMinuto)
	{
		using var provider = Montar(porMinuto);

		var ler = () => provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

		ler.Should().Throw<InvalidOperationException>().WithMessage("*Intranet:Exportacao:PorPessoaPorMinuto*");
	}

	[Fact]
	public void SemValor_montaComOPadrao()
	{
		using var provider = Montar(null);

		var ler = () => provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

		ler.Should().NotThrow();
		LimiteDeExportacao.PadraoPorMinuto.Should().Be(10);
	}
}
