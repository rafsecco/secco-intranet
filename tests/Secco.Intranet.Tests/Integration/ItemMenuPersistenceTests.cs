using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class ItemMenuPersistenceTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task PersisteEReleParentIdTipoEIcone()
	{
		Guid setorId;
		Guid raizId;
		Guid filhoId;

		// Banco por tenant (ADR-0005): o escopo manual precisa do tenant, senão o DbContext não
		// tem connection string.
		await using (var scope = factory.Services.CreateAsyncScope())
		{
			scope.ServiceProvider.SetTenant(factory.TenantAlfa);
			var context = scope.ServiceProvider.GetRequiredService<IntranetDbContext>();

			// A FK para o setor é de verdade (Restrict) — o setor precisa existir.
			var slug = $"pers-{Guid.NewGuid():N}"[..20];
			var setor = new Setor($"Setor {slug}", slug);
			context.Setores.Add(setor);
			await context.SaveChangesAsync();
			setorId = setor.Id;

			var raiz = new ItemMenu(setorId, null, setor.Nome, slug, TipoDeItemMenu.Setor, null, null, 0);
			context.ItensMenu.Add(raiz);
			await context.SaveChangesAsync();
			raizId = raiz.Id;

			var filho = new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, "bi-folder2", 0);
			context.ItensMenu.Add(filho);
			await context.SaveChangesAsync();
			filhoId = filho.Id;
		}

		await using var outroScope = factory.Services.CreateAsyncScope();
		outroScope.ServiceProvider.SetTenant(factory.TenantAlfa);
		var outroContexto = outroScope.ServiceProvider.GetRequiredService<IntranetDbContext>();

		var lido = await outroContexto.ItensMenu.AsNoTracking().FirstAsync(i => i.Id == filhoId);

		lido.ParentId.Should().Be(raizId);
		lido.SetorId.Should().Be(setorId);
		lido.Tipo.Should().Be(TipoDeItemMenu.Documentos);
		lido.Icone.Should().Be("bi-folder2");
	}

	[Fact]
	public async Task Salvar_ItemComNovaAba_LeDeVolta()
	{
		Guid comRotaId;
		Guid linksId;

		await using (var scope = factory.Services.CreateAsyncScope())
		{
			scope.ServiceProvider.SetTenant(factory.TenantAlfa);
			var context = scope.ServiceProvider.GetRequiredService<IntranetDbContext>();

			var slug = $"pers-{Guid.NewGuid():N}"[..20];
			var setor = new Setor($"Setor {slug}", slug);
			context.Setores.Add(setor);
			await context.SaveChangesAsync();

			var raiz = new ItemMenu(setor.Id, null, setor.Nome, slug, TipoDeItemMenu.Setor, null, null, 0);
			context.ItensMenu.Add(raiz);
			await context.SaveChangesAsync();

			var links = new ItemMenu(setor.Id, raiz.Id, "Links", "links", TipoDeItemMenu.Links, null, null, 0);
			context.ItensMenu.Add(links);
			await context.SaveChangesAsync();
			linksId = links.Id;

			var comRota = new ItemMenu(
				setor.Id, links.Id, "Portal", "portal", TipoDeItemMenu.Personalizado, "https://exemplo.com", null, 0, novaAba: true);
			context.ItensMenu.Add(comRota);
			await context.SaveChangesAsync();
			comRotaId = comRota.Id;
		}

		await using var outroScope = factory.Services.CreateAsyncScope();
		outroScope.ServiceProvider.SetTenant(factory.TenantAlfa);
		var outroContexto = outroScope.ServiceProvider.GetRequiredService<IntranetDbContext>();

		var lidoComRota = await outroContexto.ItensMenu.AsNoTracking().FirstAsync(i => i.Id == comRotaId);
		var lidoLinks = await outroContexto.ItensMenu.AsNoTracking().FirstAsync(i => i.Id == linksId);

		lidoComRota.NovaAba.Should().BeTrue();
		lidoComRota.Rota.Should().Be("https://exemplo.com");
		lidoLinks.Tipo.Should().Be(TipoDeItemMenu.Links);
		lidoLinks.NovaAba.Should().BeFalse();
	}
}
