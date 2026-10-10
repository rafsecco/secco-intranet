using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Composição do processo controlado (verbo <c>migrate</c>) e da migração do tenant novo no
/// primeiro uso (ADR-0038 da plataforma), contra o banco de verdade.
/// </summary>
public class MigracoesDosTenantsTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task RunSeccoMigrations_TenantsDoCatalogoJaMigrados_TerminaComSucesso()
	{
		// O mesmo caminho do `dotnet Secco.Intranet.Web.dll migrate`: idempotente sobre bancos em dia.
		(await factory.Services.RunSeccoMigrationsAsync()).Should().BeTrue();
	}

	[Fact]
	public void ContextoDoTenant_CarregaOInterceptorDePrimeiroUso()
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
		var interceptores = contexto.GetService<IDbContextOptions>()
			.FindExtension<CoreOptionsExtension>()!.Interceptors;

		interceptores.Should().ContainSingle(interceptor => interceptor is SeccoTenantMigrationInterceptor<IntranetDbContext>,
			"sem ele, um tenant criado depois do deploy fica com o banco vazio até alguém rodar o migrate");
	}
}
