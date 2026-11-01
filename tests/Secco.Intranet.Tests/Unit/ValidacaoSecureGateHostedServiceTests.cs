using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Secco.Intranet.Web.Authentication;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Em que ambiente a guarda do SecureGate vale. O startup de verdade está em
/// <c>StartupSecureGateTests</c>; aqui é só a regra: Development é o único sem trava, e nenhum outro
/// nome (Staging, Testing, um "Homologacao" inventado) abre o modo sem autenticação.
/// </summary>
public class ValidacaoSecureGateHostedServiceTests
{
	private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = environmentName;
		public string ApplicationName { get; set; } = "Secco.Intranet.Web";
		public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private static Task Iniciar(string ambiente) =>
		new ValidacaoSecureGateHostedService(new ConfigurationBuilder().Build(), new FakeHostEnvironment(ambiente))
			.StartAsync(CancellationToken.None);

	[Theory]
	[InlineData("Production")]
	[InlineData("Staging")]
	[InlineData("Testing")]
	[InlineData("Homologacao")]
	public async Task StartAsync_SemSecureGateForaDeDevelopment_Recusa(string ambiente)
	{
		var iniciar = () => Iniciar(ambiente);

		await iniciar.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*Secco:SecureGate:Authority*");
	}

	[Fact]
	public async Task StartAsync_SemSecureGateEmDevelopment_Sobe()
	{
		var iniciar = () => Iniciar("Development");

		await iniciar.Should().NotThrowAsync("Development local sem SecureGate é o modo aberto previsto");
	}
}
