using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Infrastructure;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.Intranet.Infrastructure.Diretorio;
using Secco.Intranet.Infrastructure.Seeding;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class DiretorioDesenvolvimentoSeederTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public async Task DisposeAsync() => await LimparAsync();

	private async Task<IntranetDbContext> AbrirAsync()
	{
		using var escopo = factory.Services.CreateScope();
		var catalogo = escopo.ServiceProvider.GetRequiredService<ITenantCatalog>();
		var opcoes = escopo.ServiceProvider.GetRequiredService<IntranetDatabaseOptions>();
		var tenant = (await catalogo.ListAsync()).First(t => t.TenantId == factory.TenantAlfa);

		return new IntranetDbContext(IntranetDatabaseProviderConfigurator.CreateOptions(opcoes.Provider, tenant.ConnectionString));
	}

	private async Task LimparAsync()
	{
		await using var contexto = await AbrirAsync();
		var ids = PessoasDeDesenvolvimento.Todas.Select(p => p.Id).ToList();
		await contexto.PerfisColaboradores.Where(perfil => ids.Contains(perfil.UsuarioId)).ExecuteDeleteAsync();
	}

	[Fact]
	public async Task Seed_GravaAsPessoasEEIdempotente()
	{
		using var escopo = factory.Services.CreateScope();
		var seeder = new DiretorioDesenvolvimentoSeeder(
			escopo.ServiceProvider.GetRequiredService<ITenantCatalog>(),
			escopo.ServiceProvider.GetRequiredService<IntranetDatabaseOptions>());

		await seeder.SeedAsync();
		await seeder.SeedAsync();

		await using var contexto = await AbrirAsync();
		var ids = PessoasDeDesenvolvimento.Todas.Select(p => p.Id).ToList();
		var gravados = await contexto.PerfisColaboradores.Where(perfil => ids.Contains(perfil.UsuarioId)).ToListAsync();

		gravados.Should().HaveCount(PessoasDeDesenvolvimento.Todas.Count, "rodar duas vezes não duplica");
		gravados.Single(perfil => perfil.UsuarioId == PessoasDeDesenvolvimento.AnaId).NomeExibicao.Should().Be("Ana Ribeiro");
		gravados.Single(perfil => perfil.UsuarioId == PessoasDeDesenvolvimento.CamilaId).GestorUsuarioId
			.Should().Be(PessoasDeDesenvolvimento.AnaId);
	}
}
