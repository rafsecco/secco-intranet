using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class PosicaoMenuRepositoryTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	/// <summary>Escopo com tenant resolvido e o repositório real do DI, com os dois grupos limpos. Quem chama descarta o escopo.</summary>
	private async Task<(AsyncServiceScope Escopo, IPosicaoMenuRepository Repositorio)> CriarAsync()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var repositorio = escopo.ServiceProvider.GetRequiredService<IPosicaoMenuRepository>();
		await repositorio.RemoverGrupoAsync(GrupoDoMenu.Principal);
		await repositorio.RemoverGrupoAsync(GrupoDoMenu.Setores);
		return (escopo, repositorio);
	}

	[Fact]
	public async Task SubstituirGrupo_GravaNaOrdemDada()
	{
		var (escopo, repositorio) = await CriarAsync();
		await using var _ = escopo;

		await repositorio.SubstituirGrupoAsync(GrupoDoMenu.Principal, ["diretorio", "mural"]);

		var lista = await repositorio.ListarAsync();
		lista.Should().HaveCount(2);
		lista.Single(p => p.Chave == "diretorio").Ordem.Should().Be(0);
		lista.Single(p => p.Chave == "mural").Ordem.Should().Be(1);
	}

	[Fact]
	public async Task SubstituirGrupo_DeNovo_TiraAsChavesQueSairam()
	{
		var (escopo, repositorio) = await CriarAsync();
		await using var _ = escopo;

		await repositorio.SubstituirGrupoAsync(GrupoDoMenu.Principal, ["a", "b", "c"]);
		await repositorio.SubstituirGrupoAsync(GrupoDoMenu.Principal, ["c", "a"]);

		var lista = (await repositorio.ListarAsync()).Where(p => p.Grupo == GrupoDoMenu.Principal).ToList();
		lista.Should().HaveCount(2);
		lista.Single(p => p.Chave == "c").Ordem.Should().Be(0);
		lista.Single(p => p.Chave == "a").Ordem.Should().Be(1);
	}

	[Fact]
	public async Task SubstituirGrupo_NaoMexeNoOutroGrupo()
	{
		var (escopo, repositorio) = await CriarAsync();
		await using var _ = escopo;

		await repositorio.SubstituirGrupoAsync(GrupoDoMenu.Setores, ["s1", "s2"]);
		await repositorio.SubstituirGrupoAsync(GrupoDoMenu.Principal, ["mural"]);

		var lista = await repositorio.ListarAsync();
		lista.Where(p => p.Grupo == GrupoDoMenu.Setores).Select(p => p.Chave).Should().BeEquivalentTo(["s1", "s2"]);
	}

	[Fact]
	public async Task RemoverGrupo_ApagaSoAqueleGrupo()
	{
		var (escopo, repositorio) = await CriarAsync();
		await using var _ = escopo;
		await repositorio.SubstituirGrupoAsync(GrupoDoMenu.Setores, ["s1"]);
		await repositorio.SubstituirGrupoAsync(GrupoDoMenu.Principal, ["mural"]);

		await repositorio.RemoverGrupoAsync(GrupoDoMenu.Principal);

		var lista = await repositorio.ListarAsync();
		lista.Should().ContainSingle().Which.Grupo.Should().Be(GrupoDoMenu.Setores);
	}

	[Fact]
	public async Task MesmaChave_EmGruposDiferentes_Convive()
	{
		var (escopo, repositorio) = await CriarAsync();
		await using var _ = escopo;

		await repositorio.SubstituirGrupoAsync(GrupoDoMenu.Principal, ["x"]);
		await repositorio.SubstituirGrupoAsync(GrupoDoMenu.Setores, ["x"]);

		(await repositorio.ListarAsync()).Should().HaveCount(2);
	}
}
