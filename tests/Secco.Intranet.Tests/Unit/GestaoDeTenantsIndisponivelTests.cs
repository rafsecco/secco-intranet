using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Infrastructure.Tenants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class GestaoDeTenantsIndisponivelTests
{
	[Fact]
	public async Task TodaOperacao_RespondeNaoConfigurado_NuncaFingeSucesso()
	{
		var gestao = new GestaoDeTenantsIndisponivel();
		var id = Guid.NewGuid();

		(await gestao.ListarTenantsAsync()).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.ObterTenantAsync(id)).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.ObterStatusDosBancosAsync(id)).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.CriarTenantAsync("n", "s")).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.ProvisionarAsync(id, "logstream")).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.AtivarAsync(id)).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.DesativarAsync(id)).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
	}
}
