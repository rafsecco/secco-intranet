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

	[Theory]
	[InlineData("Production")]
	[InlineData("Staging")]
	[InlineData("Testing")]
	public void Startup_ForaDeDevelopmentSemChaveMestra_DerrubaAAplicacao(string ambiente)
	{
		using var host = factory.WithWebHostBuilder(builder =>
		{
			builder.UseEnvironment(ambiente);
			builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					// A fábrica configura uma chave para todo host de teste; aqui ela some.
					["Intranet:Documentos:Chave:ChaveAtiva"] = "",
				}));
		});

		var subir = () => host.CreateClient();

		var excecao = subir.Should().Throw<Exception>(
			"fora de Development, sem chave mestra a aplicação não pode subir com a chave pública embutida").Which;

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
					// Satisfaz a guarda do ValidacaoSecureGateHostedService (StartupSecureGateTests):
					// aqui o que se testa é só a guarda da chave mestra, isolada da outra.
					["Secco:SecureGate:Authority"] = "https://securegate.exemplo.invalido",
					["Intranet:Login:ClientId"] = "secco-intranet-login",
					["Secco:SecureGate:TenantManagement:ClientId"] = "secco-intranet-gestao",
					["Secco:SecureGate:TenantManagement:ClientSecret"] = "segredo-de-teste",
				}));
		});

		var subir = () => producao.CreateClient();

		subir.Should().NotThrow("com a chave presente, nada mais nesta guarda impede o startup");
	}
}
