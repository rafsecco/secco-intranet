using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Infrastructure;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Processo controlado de migrations (ADR-0038 da plataforma): um tenant que falha não para os outros.</summary>
public class IntranetTenantMigratorTests
{
	private sealed class CatalogoFixo(params TenantInfo[] tenants) : ITenantCatalog
	{
		public ValueTask<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(tenants.FirstOrDefault(tenant => tenant.TenantId == tenantId));

		public ValueTask<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<TenantInfo>>(tenants);
	}

	// Porta 1 em loopback: recusa a conexão na hora, sem esperar timeout de rede.
	private static string Inalcancavel(string banco) =>
		$"Server=tcp:127.0.0.1,1;Database={banco};User Id=x;Password=y;Connect Timeout=2;Encrypt=false";

	[Fact]
	public async Task MigrateAsync_TenantsInalcancaveis_DevolveTodosOsIds_SemLancar()
	{
		var primeiro = Guid.NewGuid();
		var segundo = Guid.NewGuid();
		var migrator = new IntranetTenantMigrator(
			new CatalogoFixo(new TenantInfo(primeiro, Inalcancavel("a")), new TenantInfo(segundo, Inalcancavel("b"))),
			new IntranetDatabaseOptions(),
			NullLogger<IntranetTenantMigrator>.Instance);

		var falhas = await migrator.MigrateAsync();

		falhas.Should().BeEquivalentTo(
			[primeiro.ToString(), segundo.ToString()],
			"a falha do primeiro tenant não pode impedir a tentativa no segundo");
	}

	[Fact]
	public async Task MigrateAsync_Falha_NaoExpoeConnectionString()
	{
		var migrator = new IntranetTenantMigrator(
			new CatalogoFixo(new TenantInfo(Guid.NewGuid(), Inalcancavel("segredo"))),
			new IntranetDatabaseOptions(),
			NullLogger<IntranetTenantMigrator>.Instance);

		var falhas = await migrator.MigrateAsync();

		falhas.Should().ContainSingle().Which.Should().NotContain("Password");
	}
}
