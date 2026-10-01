using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class ItemMenuRepositoryTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	/// <summary>
	/// Escopo com tenant resolvido, o repositório real do DI e um setor de verdade — a FK para o
	/// setor é Restrict, então um SetorId inventado falharia no insert. Quem chama descarta o escopo.
	/// </summary>
	private async Task<(AsyncServiceScope Escopo, IItemMenuRepository Repositorio, Guid SetorId)> CriarAsync()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var slug = $"repo-{Guid.NewGuid():N}"[..20];
		var setor = new Setor($"Setor {slug}", slug);
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
		contexto.Setores.Add(setor);
		await contexto.SaveChangesAsync();

		return (escopo, escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>(), setor.Id);
	}

	[Fact]
	public async Task ListarPorSetor_DevolveTodaAArvore_InclusiveInativos()
	{
		var (escopo, repositorio, setorId) = await CriarAsync();
		await using var _ = escopo;
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var documentos = new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		var avisos = new ItemMenu(setorId, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 1);
		avisos.Desativar();
		await repositorio.AddAsync(raiz);
		await repositorio.AddAsync(documentos);
		await repositorio.AddAsync(avisos);

		var lista = await repositorio.ListarPorSetorAsync(setorId);

		lista.Should().HaveCount(3);
		lista.Should().Contain(item => item.Id == avisos.Id && !item.Ativo);
	}

	[Fact]
	public async Task ExisteTipo_DetectaDocumentosJaCriado_MasNaoAvisos()
	{
		var (escopo, repositorio, setorId) = await CriarAsync();
		await using var _ = escopo;
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var documentos = new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		await repositorio.AddAsync(raiz);
		await repositorio.AddAsync(documentos);

		(await repositorio.ExisteTipoAsync(setorId, TipoDeItemMenu.Documentos)).Should().BeTrue();
		(await repositorio.ExisteTipoAsync(setorId, TipoDeItemMenu.Avisos)).Should().BeFalse();
	}

	[Fact]
	public async Task GetParaEdicao_AlterarESalvar_Persiste()
	{
		var (escopo, repositorio, setorId) = await CriarAsync();
		await using var _ = escopo;
		var item = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		await repositorio.AddAsync(item);

		var rastreado = await repositorio.GetParaEdicaoAsync(item.Id);
		rastreado!.Desativar();
		await repositorio.SaveChangesAsync();

		var relido = await repositorio.GetByIdAsync(item.Id);
		relido!.Ativo.Should().BeFalse();
	}

	[Fact]
	public async Task ListarPorSetores_TrazSoOsSetoresPedidos()
	{
		var (escopoA, repositorio, setorA) = await CriarAsync();
		await using var _ = escopoA;
		var (escopoB, _, setorB) = await CriarAsync();
		await using var __ = escopoB;
		var (escopoC, _, setorC) = await CriarAsync();
		await using var ___ = escopoC;

		foreach (var setorId in new[] { setorA, setorB, setorC })
		{
			await repositorio.AddAsync(new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0));
		}

		var lista = await repositorio.ListarPorSetoresAsync([setorA, setorB]);

		lista.Select(item => item.SetorId).Should().BeEquivalentTo([setorA, setorB]);
	}

	[Fact]
	public async Task ListarPorSetores_ListaVazia_NaoConsulta()
	{
		var (escopo, repositorio, _) = await CriarAsync();
		await using var _ = escopo;

		(await repositorio.ListarPorSetoresAsync([])).Should().BeEmpty();
	}
}
