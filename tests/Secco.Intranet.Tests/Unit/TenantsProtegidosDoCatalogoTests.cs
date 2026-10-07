using AwesomeAssertions;
using Secco.Intranet.Infrastructure.Tenants;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class TenantsProtegidosDoCatalogoTests
{
	[Fact]
	public async Task Protege_Instalacao_TodaIntranetDoCatalogo_EOProprio()
	{
		var filialA = Guid.NewGuid();
		var filialB = Guid.NewGuid();
		var proprio = Guid.NewGuid();
		var catalogo = new CatalogoFixo(filialA, filialB);

		var protegidos = await new TenantsProtegidosDoCatalogo(catalogo, new TenantDaRequisicao(proprio)).ListarAsync();

		protegidos.Should().BeEquivalentTo([TenantsProtegidosDoCatalogo.TenantDeInstalacao, filialA, filialB, proprio]);
	}

	[Fact]
	public void TenantDeInstalacao_EspelhaOGuidFixoDaPlataforma() =>
		TenantsProtegidosDoCatalogo.TenantDeInstalacao.Should().Be(Guid.Parse("018f0000-0000-7000-8000-0000000000ff"));

	[Fact]
	public async Task SemTenantResolvido_AindaProtegeOCatalogo()
	{
		var filial = Guid.NewGuid();

		var protegidos = await new TenantsProtegidosDoCatalogo(new CatalogoFixo(filial), new TenantDaRequisicao(null)).ListarAsync();

		protegidos.Should().Contain(filial).And.Contain(TenantsProtegidosDoCatalogo.TenantDeInstalacao);
	}

	private sealed class CatalogoFixo(params Guid[] tenants) : ITenantCatalog
	{
		public ValueTask<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(tenants.Contains(tenantId) ? new TenantInfo(tenantId, "x") : null);

		public ValueTask<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<TenantInfo>>([.. tenants.Select(t => new TenantInfo(t, "x"))]);
	}

	private sealed class TenantDaRequisicao(Guid? tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => tenantId is not null;
	}
}
