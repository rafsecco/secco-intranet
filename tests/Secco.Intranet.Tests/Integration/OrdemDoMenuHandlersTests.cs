using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class OrdemDoMenuHandlersTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	/// <summary>Escopo no tenant Alfa com os dois grupos limpos. Quem chama descarta o escopo.</summary>
	private async Task<(AsyncServiceScope Escopo, IPosicaoMenuRepository Repo, TrilhaDeAcessoFalsa Trilha)> CriarAsync()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var repo = escopo.ServiceProvider.GetRequiredService<IPosicaoMenuRepository>();
		await repo.RemoverGrupoAsync(GrupoDoMenu.Principal);
		await repo.RemoverGrupoAsync(GrupoDoMenu.Setores);

		return (escopo, repo, new TrilhaDeAcessoFalsa());
	}

	[Fact]
	public async Task Mover_ParaCima_TrocaComOAnterior_EGravaOGrupoInteiro()
	{
		var (escopo, repo, trilha) = await CriarAsync();
		await using var _ = escopo;

		var resultado = await new MoverPosicaoNoMenuHandler(repo, trilha)
			.HandleAsync(GrupoDoMenu.Principal, ["mural", "diretorio", "x"], "x", paraCima: true);

		resultado.IsSuccess.Should().BeTrue();
		var posicoes = await new ObterPosicoesDoMenuHandler(repo).HandleAsync();
		posicoes.Principal.Should().BeEquivalentTo(new Dictionary<string, int> { ["mural"] = 0, ["x"] = 1, ["diretorio"] = 2 });
		posicoes.Setores.Should().BeEmpty();
	}

	[Fact]
	public async Task Mover_PrimeiroParaCima_NaoFazNada_EDaSucesso()
	{
		var (escopo, repo, trilha) = await CriarAsync();
		await using var _ = escopo;

		var resultado = await new MoverPosicaoNoMenuHandler(repo, trilha)
			.HandleAsync(GrupoDoMenu.Principal, ["mural", "diretorio"], "mural", paraCima: true);

		resultado.IsSuccess.Should().BeTrue();
		(await repo.ListarAsync()).Should().BeEmpty();
		trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Mover_ChaveForaDaLista_FalhaComPosicaoNotFound()
	{
		var (escopo, repo, trilha) = await CriarAsync();
		await using var _ = escopo;

		var resultado = await new MoverPosicaoNoMenuHandler(repo, trilha)
			.HandleAsync(GrupoDoMenu.Principal, ["mural", "diretorio"], "fantasma", paraCima: true);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.PosicaoNotFound);
		(await repo.ListarAsync()).Should().BeEmpty();
	}

	[Fact]
	public async Task Mover_ChavesRepetidas_SaoDescartadasAntesDeGravar()
	{
		var (escopo, repo, trilha) = await CriarAsync();
		await using var _ = escopo;

		var resultado = await new MoverPosicaoNoMenuHandler(repo, trilha)
			.HandleAsync(GrupoDoMenu.Principal, ["mural", "Mural", "diretorio"], "diretorio", paraCima: true);

		resultado.IsSuccess.Should().BeTrue();
		var posicoes = await new ObterPosicoesDoMenuHandler(repo).HandleAsync();
		posicoes.Principal.Should().BeEquivalentTo(new Dictionary<string, int> { ["diretorio"] = 0, ["mural"] = 1 });
	}

	[Fact]
	public async Task Mover_RegistraNaTrilha()
	{
		var (escopo, repo, trilha) = await CriarAsync();
		await using var _ = escopo;

		await new MoverPosicaoNoMenuHandler(repo, trilha)
			.HandleAsync(GrupoDoMenu.Setores, ["s1", "s2", "s3"], "s3", paraCima: true);

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuOrdemMover);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Menu);
		registro.RecursoId.Should().Be("s3");
		using var json = JsonDocument.Parse(registro.Metadata!);
		json.RootElement.GetProperty("grupo").GetString().Should().Be("Setores");
		json.RootElement.GetProperty("chave").GetString().Should().Be("s3");
		json.RootElement.GetProperty("de").GetInt32().Should().Be(2);
		json.RootElement.GetProperty("para").GetInt32().Should().Be(1);
	}

	[Fact]
	public async Task Restaurar_ApagaSoOGrupoPedido_ERegistraNaTrilha()
	{
		var (escopo, repo, trilha) = await CriarAsync();
		await using var _ = escopo;
		await repo.SubstituirGrupoAsync(GrupoDoMenu.Principal, ["mural"]);
		await repo.SubstituirGrupoAsync(GrupoDoMenu.Setores, ["s1"]);

		var resultado = await new RestaurarOrdemDoMenuHandler(repo, trilha).HandleAsync(GrupoDoMenu.Principal);

		resultado.IsSuccess.Should().BeTrue();
		(await repo.ListarAsync()).Should().ContainSingle().Which.Grupo.Should().Be(GrupoDoMenu.Setores);
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuOrdemRestaurar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Menu);
		using var json = JsonDocument.Parse(registro.Metadata!);
		json.RootElement.GetProperty("grupo").GetString().Should().Be("Principal");
	}
}
