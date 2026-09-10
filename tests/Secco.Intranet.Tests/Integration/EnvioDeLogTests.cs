using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.SDK.Logging;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Composição do sink de log. O pacote traz <c>Enabled = true</c> por padrão, e nesse estado o
/// validador exige URL, credenciais e scope — chamar <c>AddLogStream()</c> sem a seção
/// configurada derrubaria o startup. Estes testes fixam o oposto: o envio é opcional de
/// verdade, e ligá-lo depende só de configurar a seção.
/// </summary>
public class EnvioDeLogTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>
{
	private LogStreamLoggerOptions Resolver(Dictionary<string, string?> configuracao)
	{
		using var escopo = factory
			.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
				config.AddInMemoryCollection(configuracao)))
			.Services
			.CreateScope();

		return escopo.ServiceProvider
			.GetRequiredService<IOptions<LogStreamLoggerOptions>>()
			.Value;
	}

	[Fact]
	public void SemASecao_SinkDesligadoEAplicacaoSobe()
	{
		// Chegar a resolver as options já prova que o host subiu: o validador do pacote roda na
		// primeira resolução e derrubaria aqui se o sink tivesse ficado habilitado sem URL.
		var opcoes = Resolver([]);

		opcoes.Enabled.Should().BeFalse(
			"sem a seção Secco:LogStream o ILogger local segue sozinho, em vez de a aplicação não subir");
	}

	[Fact]
	public void ComASecaoCompleta_SinkLigado()
	{
		var opcoes = Resolver(new Dictionary<string, string?>
		{
			["Secco:LogStream:BaseUrl"] = "https://logstream.exemplo.com",
			["Secco:LogStream:AuthorityUrl"] = "https://securegate.exemplo.com",
			["Secco:LogStream:ClientId"] = "secco-intranet",
			["Secco:LogStream:ClientSecret"] = "segredo-de-teste",
		});

		opcoes.Enabled.Should().BeTrue();
		opcoes.BaseUrl.Should().Be("https://logstream.exemplo.com");
	}

	[Fact]
	public void OProdutoSeIdentificaNoFluxo()
	{
		var opcoes = Resolver([]);

		opcoes.ServiceName.Should().Be(
			"secco-intranet", "sem isso o log chega ao LogStream sem dizer de qual produto veio");
	}
}
