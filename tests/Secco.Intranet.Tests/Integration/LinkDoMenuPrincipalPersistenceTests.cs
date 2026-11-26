using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class LinkDoMenuPrincipalPersistenceTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task AddEListar_DevolveOLinkGravado()
	{
		var rotulo = $"Suporte {Guid.NewGuid():N}";
		Guid id;

		await using (var scope = factory.Services.CreateAsyncScope())
		{
			scope.ServiceProvider.SetTenant(factory.TenantAlfa);
			var repositorio = scope.ServiceProvider.GetRequiredService<ILinkDoMenuPrincipalRepository>();
			var link = new LinkDoMenuPrincipal(rotulo, "https://suporte.exemplo.com.br", "bi-headset", true);
			await repositorio.AddAsync(link);
			id = link.Id;
		}

		await using var outroScope = factory.Services.CreateAsyncScope();
		outroScope.ServiceProvider.SetTenant(factory.TenantAlfa);
		var lidos = await outroScope.ServiceProvider.GetRequiredService<ILinkDoMenuPrincipalRepository>().ListarAsync();

		var lido = lidos.Single(l => l.Id == id);
		lido.Rotulo.Should().Be(rotulo);
		lido.Url.Should().Be("https://suporte.exemplo.com.br");
		lido.Icone.Should().Be("bi-headset");
		lido.NovaAba.Should().BeTrue();
	}

	[Fact]
	public async Task Remover_ApagaOLinkESuaPosicaoNoGrupoPrincipal()
	{
		Guid id;
		string chave;

		await using (var scope = factory.Services.CreateAsyncScope())
		{
			scope.ServiceProvider.SetTenant(factory.TenantAlfa);
			var context = scope.ServiceProvider.GetRequiredService<IntranetDbContext>();
			var repositorio = scope.ServiceProvider.GetRequiredService<ILinkDoMenuPrincipalRepository>();
			var posicoes = scope.ServiceProvider.GetRequiredService<IPosicaoMenuRepository>();

			var link = new LinkDoMenuPrincipal("Suporte", "https://suporte.exemplo.com.br", null, false);
			await repositorio.AddAsync(link);
			id = link.Id;
			chave = link.Chave;
			await posicoes.SubstituirGrupoAsync(GrupoDoMenu.Principal, [chave, "mural"]);
			context.ChangeTracker.Clear();
		}

		await using (var scope = factory.Services.CreateAsyncScope())
		{
			scope.ServiceProvider.SetTenant(factory.TenantAlfa);
			var repositorio = scope.ServiceProvider.GetRequiredService<ILinkDoMenuPrincipalRepository>();
			var link = await repositorio.ObterAsync(id);
			link.Should().NotBeNull();
			await repositorio.RemoverAsync(link!);
		}

		await using var verificacao = factory.Services.CreateAsyncScope();
		verificacao.ServiceProvider.SetTenant(factory.TenantAlfa);
		var contexto = verificacao.ServiceProvider.GetRequiredService<IntranetDbContext>();

		(await contexto.LinksDoMenuPrincipal.AnyAsync(l => l.Id == id)).Should().BeFalse();
		var restantes = await contexto.PosicoesMenu.Where(p => p.Grupo == GrupoDoMenu.Principal).Select(p => p.Chave).ToListAsync();
		restantes.Should().Equal("mural");
	}

	[Fact]
	public async Task Listar_NaoVeLinkDoOutroTenant()
	{
		var rotulo = $"Suporte {Guid.NewGuid():N}";

		await using (var scope = factory.Services.CreateAsyncScope())
		{
			scope.ServiceProvider.SetTenant(factory.TenantAlfa);
			await scope.ServiceProvider.GetRequiredService<ILinkDoMenuPrincipalRepository>()
				.AddAsync(new LinkDoMenuPrincipal(rotulo, "/suporte", null, false));
		}

		await using var outroScope = factory.Services.CreateAsyncScope();
		outroScope.ServiceProvider.SetTenant(factory.TenantBeta);
		var lidos = await outroScope.ServiceProvider.GetRequiredService<ILinkDoMenuPrincipalRepository>().ListarAsync();

		lidos.Should().NotContain(l => l.Rotulo == rotulo);
	}
}
