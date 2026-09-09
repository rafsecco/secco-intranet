using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Os verbos de Documentos. Escrita entra na trilha; leitura não — decisão registrada no
/// spec de 2026-09-08, com a consequência assumida de não haver como saber quem baixou.
/// </summary>
public class AuditoriaDeDocumentosTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public List<RegistroDeAuditoria> Registros { get; } = [];

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Registros.Add(registro);

			return Task.CompletedTask;
		}
	}

	private sealed class RepositorioFalso(DocumentoComSetor? resultado) : IDocumentoRepository
	{
		public Task AddAsync(Documento documento, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<DocumentoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(resultado);

		public Task<IReadOnlyList<DocumentoDto>> ListarPorSetorAsync(
			string setorSlug,
			CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<DocumentoDto>>([]);
	}

	private static DocumentoComSetor Existente()
	{
		var documento = new Documento(
			Guid.NewGuid(),
			"Política interna",
			descricao: null,
			"politica.pdf",
			"application/pdf",
			tamanho: 128,
			Visibilidade.Setor,
			"tenant/ab/arquivo",
			"secco-enc:v1:x",
			"quem.publicou");

		return new DocumentoComSetor(documento, "Financeiro", "financeiro");
	}

	private static HashSet<string> Setores(params string[] slugs) =>
		new(slugs, StringComparer.OrdinalIgnoreCase);

	private sealed class SetorRepositorioFalso(Setor setor) : ISetorRepository
	{
		public Task AddAsync(Setor novo, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(setor);

		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(setor);

		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(setor);

		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(false);

		public Task<PagedResult<Setor>> SearchAsync(
			SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<Setor>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private sealed class StoreFalso : IArquivoStore
	{
		public Task<ArquivoGravado> GravarAsync(Stream conteudo, CancellationToken cancellationToken = default) =>
			Task.FromResult(new ArquivoGravado("t/ab/arquivo", "secco-enc:v1:x", 10));

		public Task EscreverEmAsync(
			string caminhoRelativo,
			string chaveEmbrulhada,
			Stream destino,
			CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task RemoverAsync(string caminhoRelativo, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	[Fact]
	public async Task Arquivar_RegistraDocumentoArquivar()
	{
		var trilha = new TrilhaFalsa();
		var encontrado = Existente();
		var handler = new ArquivarDocumentoHandler(new RepositorioFalso(encontrado), trilha);

		await handler.HandleAsync(new ArquivarDocumentoCommand(
			encontrado.Documento.Id, Setores("financeiro"), ExigirVinculo: true));

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.DocumentoArquivar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Documento);
		registro.RecursoId.Should().Be(encontrado.Documento.Id.ToString());
		JsonDocument.Parse(registro.Metadata!).RootElement.GetProperty("setor").GetString()
			.Should().Be("financeiro");
	}

	[Fact]
	public async Task Arquivar_Negado_NaoRegistra()
	{
		var trilha = new TrilhaFalsa();
		var handler = new ArquivarDocumentoHandler(new RepositorioFalso(Existente()), trilha);

		var resultado = await handler.HandleAsync(new ArquivarDocumentoCommand(
			Guid.NewGuid(), Setores("diretoria"), ExigirVinculo: true));

		resultado.IsFailure.Should().BeTrue();
		trilha.Registros.Should().BeEmpty("o que não aconteceu não entra na trilha");
	}

	[Fact]
	public async Task Publicar_RegistraDocumentoPublicar()
	{
		var trilha = new TrilhaFalsa();
		var setor = new Setor("Financeiro", "financeiro");
		var handler = new PublicarDocumentoHandler(
			new RepositorioFalso(resultado: null),
			new SetorRepositorioFalso(setor),
			new StoreFalso(),
			new DocumentoOptions(),
			new IntranetOptions(),
			trilha);

		// "%PDF" mais bytes de preenchimento: é só a assinatura que TiposDeArquivo.Apurar
		// confere, então o resto do conteúdo do PDF de verdade não faz falta ao teste.
		using var conteudo = new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34]);

		var resultado = await handler.HandleAsync(new PublicarDocumentoCommand(
			"financeiro",
			"Política interna",
			null,
			"politica.pdf",
			conteudo.Length,
			conteudo,
			Visibilidade.Setor,
			"quem.publicou"));

		resultado.IsSuccess.Should().BeTrue();

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.DocumentoPublicar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Documento);
		registro.RecursoId.Should().Be(resultado.Value.Id.ToString());
		JsonDocument.Parse(registro.Metadata!).RootElement.GetProperty("arquivo").GetString()
			.Should().Be("politica.pdf");
	}

	[Fact]
	public void Baixar_NaoDependeDaTrilha()
	{
		var dependencias = typeof(BaixarDocumentoHandler)
			.GetConstructors()
			.Single()
			.GetParameters()
			.Select(parametro => parametro.ParameterType);

		dependencias.Should().NotContain(
			typeof(ITrilhaDeAuditoria),
			"leitura fica fora da trilha por decisão registrada no spec de 2026-09-08; "
			+ "este teste existe para que acrescentar auditoria de leitura seja deliberado — "
			+ "quem o fizer precisa apagar este teste, e portanto rever a decisão");
	}
}
