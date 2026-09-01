using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Documentos;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Isolamento de documentos entre tenants (ADR-0005), no mesmo padrão de
/// <see cref="SetorTenantIsolationTests"/>: handlers reais em escopos de DI com o tenant
/// fixado por <see cref="TenantScopeExtensions.SetTenant"/>.
/// </summary>
public class DocumentoTenantIsolationTests(IntranetWebFactory factory)
	: IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly byte[] ConteudoPdf =
		System.Text.Encoding.UTF8.GetBytes("%PDF-1.7\nconteudo do tenant alfa\n%%EOF");

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<Guid> PublicarNoAlfaAsync(string slug)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criarSetor = escopo.ServiceProvider.GetRequiredService<CreateSetorHandler>();
		var criado = await criarSetor.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));
		criado.IsSuccess.Should().BeTrue();

		using var conteudo = new MemoryStream(ConteudoPdf);
		var publicar = escopo.ServiceProvider.GetRequiredService<PublicarDocumentoHandler>();

		var publicado = await publicar.HandleAsync(new PublicarDocumentoCommand(
			slug,
			"Política do Alfa",
			Descricao: null,
			"politica.pdf",
			ConteudoPdf.Length,
			conteudo,
			// Visibilidade mais permissiva de propósito: assim o teste prova o isolamento do
			// BANCO, e não a regra de autorização, que já tem testes próprios.
			VisibilidadeDocumento.Empresa,
			"teste"));

		publicado.IsSuccess.Should().BeTrue();

		return publicado.Value.Id;
	}

	[Fact]
	public async Task DocumentoPublicadoEmUmTenant_NaoEhAlcancavelPeloOutro()
	{
		var slug = $"docs-alfa-{Guid.NewGuid():N}"[..20];
		var documentoId = await PublicarNoAlfaAsync(slug);

		using var escopoBeta = factory.Services.CreateScope();
		escopoBeta.ServiceProvider.SetTenant(factory.TenantBeta);
		var baixar = escopoBeta.ServiceProvider.GetRequiredService<BaixarDocumentoHandler>();

		var resultado = await baixar.HandleAsync(new BaixarDocumentoQuery(
			documentoId,
			new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			ExigirVinculo: false));

		resultado.IsFailure.Should().BeTrue(
			"cada tenant tem banco próprio: nem com a autorização aberta o documento atravessa");
		resultado.Error.Should().Be(IntranetErrors.Documentos.NotFound);
	}

	[Fact]
	public async Task DocumentoPublicadoEmUmTenant_ContinuaAlcancavelNoProprioTenant()
	{
		var slug = $"docs-alfa-{Guid.NewGuid():N}"[..20];
		var documentoId = await PublicarNoAlfaAsync(slug);

		using var escopoAlfa = factory.Services.CreateScope();
		escopoAlfa.ServiceProvider.SetTenant(factory.TenantAlfa);
		var baixar = escopoAlfa.ServiceProvider.GetRequiredService<BaixarDocumentoHandler>();

		var resultado = await baixar.HandleAsync(new BaixarDocumentoQuery(
			documentoId,
			new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			ExigirVinculo: false));

		resultado.IsSuccess.Should().BeTrue("sem esta metade, o teste acima passaria mesmo com o recurso quebrado");

		using var destino = new MemoryStream();
		await resultado.Value.EscreverConteudo(destino, CancellationToken.None);

		destino.ToArray().Should().Equal(ConteudoPdf);
	}

	[Fact]
	public async Task DocumentoArquivado_DeixaDeAparecerNaListagemDoSetor()
	{
		var slug = $"docs-alfa-{Guid.NewGuid():N}"[..20];
		var documentoId = await PublicarNoAlfaAsync(slug);

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var listar = escopo.ServiceProvider.GetRequiredService<ListarDocumentosHandler>();
		var arquivar = escopo.ServiceProvider.GetRequiredService<ArquivarDocumentoHandler>();

		(await listar.HandleAsync(new ListarDocumentosQuery(slug))).Value.Should().ContainSingle();

		var arquivado = await arquivar.HandleAsync(new ArquivarDocumentoCommand(
			documentoId, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ExigirVinculo: false));

		arquivado.IsSuccess.Should().BeTrue();
		(await listar.HandleAsync(new ListarDocumentosQuery(slug))).Value.Should().BeEmpty();
	}

	[Fact]
	public async Task DocumentoArquivado_NaoEhMaisBaixavel()
	{
		var slug = $"docs-alfa-{Guid.NewGuid():N}"[..20];
		var documentoId = await PublicarNoAlfaAsync(slug);

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var arquivar = escopo.ServiceProvider.GetRequiredService<ArquivarDocumentoHandler>();
		var baixar = escopo.ServiceProvider.GetRequiredService<BaixarDocumentoHandler>();

		await arquivar.HandleAsync(new ArquivarDocumentoCommand(
			documentoId, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ExigirVinculo: false));

		var resultado = await baixar.HandleAsync(new BaixarDocumentoQuery(
			documentoId, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ExigirVinculo: false));

		resultado.IsFailure.Should().BeTrue("arquivar tira o documento de circulação, não só da listagem");
	}
}
