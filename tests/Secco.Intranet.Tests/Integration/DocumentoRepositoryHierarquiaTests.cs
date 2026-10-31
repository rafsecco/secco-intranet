using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class DocumentoRepositoryHierarquiaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	/// <summary>Escopo do tenant Alfa, repositório real e um setor de verdade (a FK é Restrict).</summary>
	private async Task<(AsyncServiceScope Escopo, IDocumentoRepository Repositorio, Setor Setor)> CriarAsync()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var slug = $"doc-{Guid.NewGuid():N}"[..20];
		var setor = new Setor($"Setor {slug}", slug);
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
		contexto.Setores.Add(setor);
		await contexto.SaveChangesAsync();

		return (escopo, escopo.ServiceProvider.GetRequiredService<IDocumentoRepository>(), setor);
	}

	private static Documento Arquivo(Guid setorId, string titulo, Guid? parentId, int ordem) =>
		new(setorId, titulo, null, "a.pdf", "application/pdf", 10, Visibilidade.Setor, $"t/{Guid.NewGuid():N}", "secco-enc:v1:x", "quem", parentId, ordem);

	[Fact]
	public async Task ListarIrmaos_SoAtivosDoPaiNaOrdem()
	{
		var (escopo, repositorio, setor) = await CriarAsync();
		await using var _ = escopo;
		var pai = Documento.CriarGrupo(setor.Id, "Pai", null, "quem");
		var outroPai = Documento.CriarGrupo(setor.Id, "Outro", null, "quem");
		await repositorio.AddAsync(pai);
		await repositorio.AddAsync(outroPai);
		var segundo = Arquivo(setor.Id, "Segundo", pai.Id, 1);
		var primeiro = Arquivo(setor.Id, "Primeiro", pai.Id, 0);
		var arquivado = Arquivo(setor.Id, "Arquivado", pai.Id, 2);
		arquivado.Arquivar();
		var deOutro = Arquivo(setor.Id, "DeOutro", outroPai.Id, 0);
		foreach (var documento in new[] { segundo, primeiro, arquivado, deOutro })
		{
			await repositorio.AddAsync(documento);
		}

		var irmaos = await repositorio.ListarIrmaosAsync(setor.Id, pai.Id);

		irmaos.Select(d => d.Titulo).Should().Equal("Primeiro", "Segundo");
	}

	[Fact]
	public async Task TemFilhosAtivos_IgnoraArquivados()
	{
		var (escopo, repositorio, setor) = await CriarAsync();
		await using var _ = escopo;
		var pai = Documento.CriarGrupo(setor.Id, "Pai", null, "quem");
		await repositorio.AddAsync(pai);
		var filho = Arquivo(setor.Id, "Filho", pai.Id, 0);
		await repositorio.AddAsync(filho);

		(await repositorio.TemFilhosAtivosAsync(pai.Id)).Should().BeTrue();

		filho.Arquivar();
		await repositorio.SaveChangesAsync();

		(await repositorio.TemFilhosAtivosAsync(pai.Id)).Should().BeFalse();
	}

	[Fact]
	public async Task ListarPorSetor_OrdenaPorOrdem()
	{
		var (escopo, repositorio, setor) = await CriarAsync();
		await using var _ = escopo;
		await repositorio.AddAsync(Arquivo(setor.Id, "Terceiro", null, 2));
		await repositorio.AddAsync(Arquivo(setor.Id, "Primeiro", null, 0));
		await repositorio.AddAsync(Arquivo(setor.Id, "Segundo", null, 1));

		var lista = await repositorio.ListarPorSetorAsync(setor.Slug);

		lista.Select(d => d.Titulo).Should().Equal("Primeiro", "Segundo", "Terceiro");
		lista.Should().OnlyContain(d => d.Tipo == TipoDeDocumento.Arquivo);
	}
}
