using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>Mapeamento, índice único e rastreamento do cadastro de tenants, contra o banco de verdade.</summary>
public class TenantsAdministradosPersistenciaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private IServiceScope NovoEscopo()
	{
		var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		return escopo;
	}

	[Fact]
	public async Task Adicionar_EBuscar_GravaDeVerdade()
	{
		var tenantId = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			(await escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>()
				.TentarAdicionarAsync(new TenantAdministrado(tenantId, "Sistema de compras", "Ana", OrigemDoTenant.Criado, "admin")))
				.Should().BeTrue();
		}

		using var leitura = NovoEscopo();
		var lido = await leitura.ServiceProvider.GetRequiredService<ITenantsAdministrados>().ObterAsync(tenantId);

		lido.Should().NotBeNull();
		lido!.Sistema.Should().Be("Sistema de compras");
		lido.Origem.Should().Be(OrigemDoTenant.Criado);
	}

	[Fact]
	public async Task MesmoTenantDuasVezes_DevolveFalse_SemLancar()
	{
		var tenantId = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			await escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>()
				.TentarAdicionarAsync(new TenantAdministrado(tenantId, "A", "Ana", OrigemDoTenant.Criado, "admin"));
		}

		using var outro = NovoEscopo();
		var repetido = await outro.ServiceProvider.GetRequiredService<ITenantsAdministrados>()
			.TentarAdicionarAsync(new TenantAdministrado(tenantId, "B", "Bia", OrigemDoTenant.Adotado, "admin"));

		repetido.Should().BeFalse("o índice único barra o segundo registro, e a corrida não vira 500");
	}

	[Fact]
	public async Task HabilitarSecureGate_Rastreado_GravaDeVerdade()
	{
		var tenantId = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			await escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>()
				.TentarAdicionarAsync(new TenantAdministrado(tenantId, "A", "Ana", OrigemDoTenant.Criado, "admin"));
		}

		using (var escopo = NovoEscopo())
		{
			var repositorio = escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>();
			var tenant = await repositorio.ObterParaEdicaoAsync(tenantId);
			tenant!.HabilitarSecureGate();
			await repositorio.SalvarAsync();
		}

		using var leitura = NovoEscopo();
		(await leitura.ServiceProvider.GetRequiredService<ITenantsAdministrados>().ObterAsync(tenantId))!
			.SecureGateHabilitado.Should().BeTrue();
	}
}
