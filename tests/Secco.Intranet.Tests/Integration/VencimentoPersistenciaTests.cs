using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Vencimentos;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Domain.Vencimentos;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Persistência de itens de vencimento contra o banco de verdade: antecedências em coluna texto,
/// índice único por marco do ciclo e a seleção dos itens elegíveis ao aviso.
/// </summary>
public class VencimentoPersistenciaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly DateOnly Data = new(2027, 3, 15);

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private AsyncServiceScope NovoEscopo()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		return escopo;
	}

	/// <summary>Cria um setor e, opcionalmente, a árvore raiz + Vencimentos (ativos).</summary>
	private async Task<(Setor Setor, ItemMenu? Recurso)> CriarSetorAsync(bool comRecurso = true)
	{
		await using var escopo = NovoEscopo();
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

		var slug = $"venc-{Guid.NewGuid():N}"[..20];
		var setor = new Setor($"Setor {slug}", slug);
		contexto.Setores.Add(setor);

		ItemMenu? recurso = null;

		if (comRecurso)
		{
			var raiz = new ItemMenu(setor.Id, null, setor.Nome, slug, TipoDeItemMenu.Setor, null, null, 0);
			recurso = new ItemMenu(setor.Id, raiz.Id, "Vencimentos", "vencimentos", TipoDeItemMenu.Vencimentos, null, null, 0);
			contexto.ItensMenu.AddRange(raiz, recurso);
		}

		await contexto.SaveChangesAsync();

		return (setor, recurso);
	}

	private async Task<ItemVencimento> CriarItemAsync(Guid setorId, IReadOnlyList<int>? antecedencias = null)
	{
		await using var escopo = NovoEscopo();
		var item = new ItemVencimento(setorId, "Certificado digital", "Renovar no portal", Data, Guid.NewGuid(), antecedencias);
		await escopo.ServiceProvider.GetRequiredService<IItemVencimentoRepository>().AddAsync(item);

		return item;
	}

	private static AvisoDeVencimentoEnviado NovoAviso(Guid itemId, int ciclo, int marco) =>
		new(itemId, ciclo, marco, ResultadoDoAviso.Enviado, 1, DateTimeOffset.UtcNow);

	[Fact]
	public async Task GravaERelêComAntecedencias()
	{
		var (setor, _) = await CriarSetorAsync();
		var item = await CriarItemAsync(setor.Id, [1, 30, 7]);

		// Escopo novo, DbContext novo: só sobrevive o que foi para o banco.
		await using var leitura = NovoEscopo();
		var repositorio = leitura.ServiceProvider.GetRequiredService<IItemVencimentoRepository>();

		var lido = await repositorio.GetParaEdicaoAsync(item.Id);

		lido.Should().NotBeNull();
		lido.Antecedencias.Should().Equal(30, 7, 1);
		lido.DataVencimento.Should().Be(Data);
		lido.Titulo.Should().Be("Certificado digital");
		lido.Ciclo.Should().Be(1);
		lido.ResponsavelId.Should().Be(item.ResponsavelId);

		var lista = await repositorio.ListarDoSetorAsync(setor.Id, incluirEncerrados: false);
		lista.Should().ContainSingle().Which.Id.Should().Be(item.Id);
	}

	[Fact]
	public async Task TentarRegistrarAviso_Duplicado_DevolveFalse()
	{
		var (setor, _) = await CriarSetorAsync();
		var item = await CriarItemAsync(setor.Id);

		await using var escopo = NovoEscopo();
		var repositorio = escopo.ServiceProvider.GetRequiredService<IItemVencimentoRepository>();

		(await repositorio.TentarRegistrarAvisoAsync(NovoAviso(item.Id, 1, 7))).Should().BeTrue();
		(await repositorio.TentarRegistrarAvisoAsync(NovoAviso(item.Id, 1, 7))).Should().BeFalse();
		(await repositorio.TentarRegistrarAvisoAsync(NovoAviso(item.Id, 1, 1))).Should().BeTrue();
		(await repositorio.TentarRegistrarAvisoAsync(NovoAviso(item.Id, 2, 7))).Should().BeTrue();

		var avisos = await repositorio.ListarAvisosAsync(item.Id);
		avisos.Should().HaveCount(3);
	}

	[Fact]
	public async Task TentarRegistrarAviso_Concorrente_SoUmVence()
	{
		var (setor, _) = await CriarSetorAsync();
		var item = await CriarItemAsync(setor.Id);

		await using var a = NovoEscopo();
		await using var b = NovoEscopo();
		var repositorioA = a.ServiceProvider.GetRequiredService<IItemVencimentoRepository>();
		var repositorioB = b.ServiceProvider.GetRequiredService<IItemVencimentoRepository>();

		var resultados = await Task.WhenAll(
			repositorioA.TentarRegistrarAvisoAsync(NovoAviso(item.Id, 1, 7)),
			repositorioB.TentarRegistrarAvisoAsync(NovoAviso(item.Id, 1, 7)));

		resultados.Count(vitoria => vitoria).Should().Be(1);

		await using var leitura = NovoEscopo();
		(await leitura.ServiceProvider.GetRequiredService<IItemVencimentoRepository>().ListarAvisosAsync(item.Id))
			.Should().ContainSingle();
	}

	[Fact]
	public async Task ListarAtivosParaAviso_IgnoraEncerrado_SetorInativo_ERecursoDesligado()
	{
		var (setorOk, _) = await CriarSetorAsync();
		var (setorInativo, _) = await CriarSetorAsync();
		var (setorDesligado, recursoDesligado) = await CriarSetorAsync();
		var (setorSemRecurso, _) = await CriarSetorAsync(comRecurso: false);

		var ativo = await CriarItemAsync(setorOk.Id);
		var encerrado = await CriarItemAsync(setorOk.Id);
		var doSetorInativo = await CriarItemAsync(setorInativo.Id);
		var doRecursoDesligado = await CriarItemAsync(setorDesligado.Id);
		var doSetorSemRecurso = await CriarItemAsync(setorSemRecurso.Id);

		await using (var escopo = NovoEscopo())
		{
			var repositorio = escopo.ServiceProvider.GetRequiredService<IItemVencimentoRepository>();
			var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

			(await repositorio.GetParaEdicaoAsync(encerrado.Id))!.Encerrar("ana@exemplo.local", DateTimeOffset.UtcNow);
			contexto.Setores.Single(s => s.Id == setorInativo.Id).Desativar();
			contexto.ItensMenu.Single(i => i.Id == recursoDesligado!.Id).Desativar();
			await contexto.SaveChangesAsync();
		}

		await using var leitura = NovoEscopo();
		var elegiveis = await leitura.ServiceProvider.GetRequiredService<IItemVencimentoRepository>()
			.ListarAtivosParaAvisoAsync();

		var ids = elegiveis.Select(e => e.Item.Id).ToList();
		ids.Should().Contain(ativo.Id);
		ids.Should().NotContain([encerrado.Id, doSetorInativo.Id, doRecursoDesligado.Id, doSetorSemRecurso.Id]);
		elegiveis.Single(e => e.Item.Id == ativo.Id).SetorSlug.Should().Be(setorOk.Slug);
		elegiveis.Single(e => e.Item.Id == ativo.Id).SetorNome.Should().Be(setorOk.Nome);
	}

	[Fact]
	public async Task ListarAtivosParaAviso_TrazSoOsMarcosDoCicloAtual()
	{
		var (setor, _) = await CriarSetorAsync();
		var item = await CriarItemAsync(setor.Id);

		await using (var escopo = NovoEscopo())
		{
			var repositorio = escopo.ServiceProvider.GetRequiredService<IItemVencimentoRepository>();
			await repositorio.TentarRegistrarAvisoAsync(NovoAviso(item.Id, 1, 30));
			await repositorio.TentarRegistrarAvisoAsync(NovoAviso(item.Id, 1, 7));

			// Mudar a data abre o ciclo 2: os marcos do ciclo 1 deixam de valer.
			var rastreado = (await repositorio.GetParaEdicaoAsync(item.Id))!;
			rastreado.Editar(rastreado.Titulo, rastreado.Descricao, Data.AddDays(60), rastreado.ResponsavelId, rastreado.Antecedencias)
				.Should().BeTrue();
			await repositorio.SaveChangesAsync();
			await repositorio.TentarRegistrarAvisoAsync(NovoAviso(item.Id, 2, 1));
		}

		await using var leitura = NovoEscopo();
		var elegiveis = await leitura.ServiceProvider.GetRequiredService<IItemVencimentoRepository>()
			.ListarAtivosParaAvisoAsync();

		var doItem = elegiveis.Single(e => e.Item.Id == item.Id);
		doItem.Item.Ciclo.Should().Be(2);
		doItem.MarcosTratados.Should().BeEquivalentTo([1]);
	}
}
