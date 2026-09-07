using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Guarda de configuração da cifragem de documentos (ADR-0005/ADR-0020). O que importa aqui
/// não é o cifrador recusar a chave — isso já tem teste de unidade —, e sim a <b>aplicação
/// não subir</b>: sem esta guarda, um deploy de produção sem chave ficaria saudável e só
/// falharia quando alguém tentasse publicar o primeiro documento.
/// </summary>
public class StartupChaveMestraTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private static IEnumerable<string> Mensagens(Exception excecao)
	{
		for (var atual = excecao; atual is not null; atual = atual.InnerException)
		{
			yield return atual.Message;
		}
	}

	[Fact]
	public void Startup_EmProductionSemChaveMestra_DerrubaAAplicacao()
	{
		using var producao = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

		var subir = () => producao.CreateClient();

		var excecao = subir.Should().Throw<Exception>(
			"Production sem chave mestra precisa impedir a aplicação de subir").Which;

		Mensagens(excecao).Should().Contain(
			mensagem => mensagem.Contains("Chave mestra", StringComparison.OrdinalIgnoreCase),
			"a falha precisa apontar a chave, e não um sintoma distante dela");
	}

	[Fact]
	public void Startup_EmProductionComChaveMestra_Sobe()
	{
		using var producao = factory.WithWebHostBuilder(builder =>
		{
			builder.UseEnvironment("Production");
			builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					// 32 bytes em base64 — o tamanho que a chave mestra exige.
					["Intranet:Documentos:Chave:ChaveAtiva"] =
						Convert.ToBase64String(new byte[32]),
				}));
		});

		var subir = () => producao.CreateClient();

		subir.Should().NotThrow("com a chave presente, nada mais nesta guarda impede o startup");
	}
}
