using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
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
	public void Publicar_DependeDaTrilha()
	{
		var dependencias = typeof(PublicarDocumentoHandler)
			.GetConstructors()
			.Single()
			.GetParameters()
			.Select(parametro => parametro.ParameterType);

		dependencias.Should().Contain(
			typeof(ITrilhaDeAuditoria),
			"montar este handler num teste exigiria IArquivoStore, Stream e validação de magic "
			+ "bytes; a asserção de dependência prova a fiação sem duplicar esse setup, e o "
			+ "conteúdo do registro é conferido na verificação de navegador da Task 6");
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
