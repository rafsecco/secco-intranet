using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain.Bibliotecas;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Tabela de bibliotecas e dono exclusivo do documento (setor xor biblioteca), contra o banco
/// de verdade: as garantias que só a constraint dá.
/// </summary>
public class BibliotecaPersistenciaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private AsyncServiceScope NovoEscopo()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		return escopo;
	}

	private static string SlugUnico() => $"bib-{Guid.NewGuid():N}"[..24];

	private static Task InserirDocumentoCruAsync(IntranetDbContext contexto, Guid? setorId, Guid? bibliotecaId) =>
		contexto.Database.ExecuteSqlInterpolatedAsync(
			$"""
			INSERT INTO tb_documentos
				(id_pk_documento, fl_ativo, dt_created_at, ds_criado_por, nr_ordem, id_fk_setor, id_fk_biblioteca, ie_tipo, ds_titulo, ie_visibilidade)
			VALUES
				({Guid.NewGuid()}, {true}, {DateTimeOffset.UtcNow}, {"quem"}, {0}, {setorId}, {bibliotecaId}, {0}, {"Doc"}, {0})
			""");

	[Fact]
	public async Task Slug_repetido_viola_uk_bibliotecas_ds_slug()
	{
		await using var escopo = NovoEscopo();
		var repositorio = escopo.ServiceProvider.GetRequiredService<IBibliotecaRepository>();
		var slug = SlugUnico();
		await repositorio.AddAsync(new Biblioteca("Primeira", slug));

		var acao = () => repositorio.AddAsync(new Biblioteca("Segunda", slug));

		var excecao = await acao.Should().ThrowAsync<DbUpdateException>();
		excecao.Which.ToString().Should().Contain("uk_bibliotecas_ds_slug");
	}

	[Fact]
	public async Task Documento_com_setor_e_biblioteca_viola_ck_documentos_dono()
	{
		await using var escopo = NovoEscopo();
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
		var setor = new Setor("Setor dono", $"s-{Guid.NewGuid():N}"[..20]);
		var biblioteca = new Biblioteca("Biblioteca dona", SlugUnico());
		contexto.Setores.Add(setor);
		contexto.Bibliotecas.Add(biblioteca);
		await contexto.SaveChangesAsync();

		var acao = () => InserirDocumentoCruAsync(contexto, setor.Id, biblioteca.Id);

		var excecao = await acao.Should().ThrowAsync<Exception>();
		excecao.Which.ToString().Should().Contain("ck_documentos_dono");
	}

	[Fact]
	public async Task Documento_sem_dono_viola_ck_documentos_dono()
	{
		await using var escopo = NovoEscopo();
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

		var acao = () => InserirDocumentoCruAsync(contexto, null, null);

		var excecao = await acao.Should().ThrowAsync<Exception>();
		excecao.Which.ToString().Should().Contain("ck_documentos_dono");
	}

	[Fact]
	public async Task Documento_de_biblioteca_e_lido_de_volta()
	{
		await using var escopo = NovoEscopo();
		var bibliotecas = escopo.ServiceProvider.GetRequiredService<IBibliotecaRepository>();
		var documentos = escopo.ServiceProvider.GetRequiredService<IDocumentoRepository>();
		var biblioteca = new Biblioteca("Biblioteca de leitura", SlugUnico());
		await bibliotecas.AddAsync(biblioteca);
		var grupo = Documento.CriarGrupo(DonoDeDocumento.DaBiblioteca(biblioteca.Id), "Pasta", null, "quem");
		await documentos.AddAsync(grupo);

		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
		var lido = await contexto.Documentos.AsNoTracking().SingleAsync(documento => documento.Id == grupo.Id);

		lido.BibliotecaId.Should().Be(biblioteca.Id);
		lido.SetorId.Should().BeNull();
	}

	[Fact]
	public async Task Repositorio_busca_por_slug_sem_diferenciar_caixa_e_lista_por_nome()
	{
		await using var escopo = NovoEscopo();
		var repositorio = escopo.ServiceProvider.GetRequiredService<IBibliotecaRepository>();
		var slug = SlugUnico();
		var ativa = new Biblioteca("Zeta ativa", slug);
		var inativa = new Biblioteca("Alfa inativa", SlugUnico());
		inativa.Desativar();
		await repositorio.AddAsync(ativa);
		await repositorio.AddAsync(inativa);

		(await repositorio.GetBySlugAsync(slug.ToUpperInvariant()))!.Id.Should().Be(ativa.Id);
		(await repositorio.ExistsBySlugAsync(slug.ToUpperInvariant())).Should().BeTrue();
		(await repositorio.GetByIdAsync(inativa.Id))!.Slug.Should().Be(inativa.Slug);

		var todas = (await repositorio.ListarAsync(somenteAtivas: false)).Select(b => b.Id).ToList();
		todas.Should().Contain([ativa.Id, inativa.Id]);
		todas.IndexOf(inativa.Id).Should().BeLessThan(todas.IndexOf(ativa.Id));

		var ativas = (await repositorio.ListarAsync(somenteAtivas: true)).Select(b => b.Id);
		ativas.Should().Contain(ativa.Id).And.NotContain(inativa.Id);
	}
}
