using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Guarda de configuração da autenticação (ADR-0020). O que importa aqui não é
/// <c>IntranetAuthenticationExtensions.IsConfigured</c> — isso já é trivial —, e sim a
/// <b>aplicação não subir</b>: sem esta guarda, um deploy de Production sem
/// <c>Secco:SecureGate:Authority</c> ficaria saudável e abriria Mural, Documentos e Setor para
/// qualquer visitante, sem autenticação nem autorização (o mesmo bypass que só deveria valer em
/// Development local).
/// </summary>
public class StartupSecureGateTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	// 32 bytes em base64 — satisfaz a guarda da chave mestra (StartupChaveMestraTests), isolando
	// aqui só a guarda do SecureGate.
	private static readonly string ChaveMestraValida = Convert.ToBase64String(new byte[32]);

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
	public void Startup_EmProductionSemSecureGate_DerrubaAAplicacao()
	{
		using var producao = factory.WithWebHostBuilder(builder =>
		{
			builder.UseEnvironment("Production");
			builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Intranet:Documentos:Chave:ChaveAtiva"] = ChaveMestraValida,
				}));
		});

		var subir = () => producao.CreateClient();

		var excecao = subir.Should().Throw<Exception>(
			"Production sem Secco:SecureGate:Authority precisa impedir a aplicação de subir").Which;

		Mensagens(excecao).Should().Contain(
			mensagem => mensagem.Contains("SecureGate", StringComparison.OrdinalIgnoreCase),
			"a falha precisa apontar o SecureGate, e não um sintoma distante dele");
	}

	[Fact]
	public void Startup_EmProductionComSecureGate_Sobe()
	{
		using var producao = factory.WithWebHostBuilder(builder =>
		{
			builder.UseEnvironment("Production");
			builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Intranet:Documentos:Chave:ChaveAtiva"] = ChaveMestraValida,
					["Secco:SecureGate:Authority"] = "https://securegate.exemplo.invalido",
				}));
		});

		var subir = () => producao.CreateClient();

		subir.Should().NotThrow("com Authority presente, nada mais nesta guarda impede o startup");
	}
}
