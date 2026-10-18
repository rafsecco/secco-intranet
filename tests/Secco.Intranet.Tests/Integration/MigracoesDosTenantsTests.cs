using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Secco.SecureGate.Client.Permissions;
using Secco.SharedKernel.Authorization;
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

	private sealed class PublicadorFalso : ISecureGatePermissionCatalogPublisher
	{
		public List<(string Produto, IReadOnlyList<SeccoPermissionDefinition> Permissoes)> Publicados { get; } = [];

		public Task PublishAsync(string product, IReadOnlyList<SeccoPermissionDefinition> permissions, CancellationToken cancellationToken = default)
		{
			Publicados.Add((product, permissions));

			return Task.CompletedTask;
		}
	}

	[Fact]
	public async Task Migrate_PublicaOCatalogoDaIntranet_DepoisDasMigrations()
	{
		// ADR-0039 da plataforma: sem o catálogo publicado, o SecureGate recusa as permissões novas.
		var publicador = new PublicadorFalso();
		using var app = factory.WithWebHostBuilder(builder =>
			builder.ConfigureTestServices(services => services.AddSingleton<ISecureGatePermissionCatalogPublisher>(publicador)));

		(await app.Services.RunSeccoMigrationsAsync()).Should().BeTrue();

		var publicado = publicador.Publicados.Should().ContainSingle().Subject;
		publicado.Produto.Should().Be("intranet");
		publicado.Permissoes.Should().BeEquivalentTo(IntranetPermissoes.Catalog);
	}
}
