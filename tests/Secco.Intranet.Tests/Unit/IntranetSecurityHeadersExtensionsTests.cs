using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.Intranet.Web.Hosting;
using Secco.SDK.AspNetCore.SecurityHeaders;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Os ajustes da Intranet sobre a política de cabeçalhos do SDK, lidos da configuração.</summary>
public class IntranetSecurityHeadersExtensionsTests
{
	private static SeccoSecurityHeadersOptions Opcoes(params (string Chave, string Valor)[] valores)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(valores.ToDictionary(valor => valor.Chave, valor => (string?)valor.Valor))
			.Build();

		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddIntranetSecurityHeaders();

		using var provider = services.BuildServiceProvider();

		return provider.GetRequiredService<IOptions<SeccoSecurityHeadersOptions>>().Value;
	}

	[Fact]
	public void SemConfiguracao_ComecaEmReportOnlySemFormAction()
	{
		var opcoes = Opcoes();

		opcoes.Mode.Should().Be(SeccoCspMode.ReportOnly);
		opcoes.FormActions.Should().BeEmpty("sem SecureGate HTTPS não há origem para declarar");
		opcoes.ReportUri.Should().BeNull();
	}

	[Theory]
	[InlineData("Enforce", SeccoCspMode.Enforce)]
	[InlineData("enforce", SeccoCspMode.Enforce)]
	[InlineData("ReportOnly", SeccoCspMode.ReportOnly)]
	public void Modo_LidoDaConfiguracao(string valor, SeccoCspMode esperado) =>
		Opcoes((IntranetSecurityHeadersExtensions.ChaveDoModo, valor)).Mode.Should().Be(esperado);

	[Theory]
	[InlineData("Bloquear")]
	[InlineData("7")]
	public void Modo_Desconhecido_RecusaEmVezDeEscolherUm(string valor)
	{
		var ler = () => Opcoes((IntranetSecurityHeadersExtensions.ChaveDoModo, valor));

		ler.Should().Throw<InvalidOperationException>().WithMessage("*Intranet:Csp:Modo*");
	}

	[Fact]
	public void AuthorityHttps_FormActionComAPropriaOrigemEADoSecureGate() =>
		Opcoes(("Secco:SecureGate:Authority", "https://securegate.exemplo.com/realms/x"))
			.FormActions.Should().Equal("'self'", "https://securegate.exemplo.com");

	[Fact]
	public void AuthorityHttp_SemFormAction() =>
		Opcoes(("Secco:SecureGate:Authority", "http://localhost:5001")).FormActions.Should().BeEmpty();

	[Fact]
	public void ReportUri_LidoDaConfiguracao() =>
		Opcoes((IntranetSecurityHeadersExtensions.ChaveDoReportUri, "https://csp.exemplo.com/relatos"))
			.ReportUri.Should().Be(new Uri("https://csp.exemplo.com/relatos"));
}
