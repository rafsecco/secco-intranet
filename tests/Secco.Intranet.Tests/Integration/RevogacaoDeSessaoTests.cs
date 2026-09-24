using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.Authentication;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Adoção da ADR-0032 da plataforma: revogar sessão no SecureGate precisa alcançar o cookie da
/// Intranet. O ambiente Testing nunca configura o SecureGate, então as duas linhas de
/// composição nunca executariam sozinhas — e o risco delas é justamente o startup: o SDK
/// derruba a aplicação se a validação de sessão for ligada sem resolvedor registrado.
/// </summary>
public class RevogacaoDeSessaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>
{
	private static readonly Dictionary<string, string?> SecureGateConfigurado = new()
	{
		["Secco:SecureGate:Authority"] = "https://securegate.exemplo.com",
		["Secco:SecureGate:BaseUrl"] = "https://securegate.exemplo.com",
		["Secco:SecureGate:ClientId"] = "secco-intranet",
		["Secco:SecureGate:ClientSecret"] = "segredo-de-teste",
	};

	[Fact]
	public void ComSecureGateConfigurado_SobeERegistraOResolvedorDeVersaoDeSessao()
	{
		// UseSetting, e não ConfigureAppConfiguration: a decisão de registrar a autenticação é
		// tomada durante o Program.cs (IsConfigured lê builder.Configuration), antes de as fontes
		// acrescentadas pelo teste serem aplicadas.
		using var host = factory.WithWebHostBuilder(builder =>
		{
			foreach (var (chave, valor) in SecureGateConfigurado)
			{
				builder.UseSetting(chave, valor);
			}
		});

		// Acessar Services constrói o host e roda os IStartupFilter — é onde o SDK falharia.
		using var escopo = host.Services.CreateScope();

		escopo.ServiceProvider.GetService<ISessionVersionResolver>().Should().NotBeNull(
			"sem o resolvedor a validação de sessão do cookie derruba o startup, e sem a validação um usuário revogado segue logado");
	}

	[Fact]
	public void SemSecureGate_NaoRegistraValidacaoDeSessao()
	{
		using var escopo = factory.Services.CreateScope();

		escopo.ServiceProvider.GetService<ISessionVersionResolver>().Should().BeNull(
			"o modo aberto de DEV e o ambiente Testing não têm sessão de SecureGate a revogar");
	}
}
