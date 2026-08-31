using AwesomeAssertions;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain.Documentos;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Autorização de leitura de documento (ADR-0001). É a regra que decide quem enxerga o quê,
/// então cada combinação de visibilidade e vínculo tem teste próprio.
/// </summary>
public class BaixarDocumentoHandlerTests
{
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

	private sealed class StoreFalso : IArquivoStore
	{
		public bool Leu { get; private set; }

		public Task<ArquivoGravado> GravarAsync(Stream conteudo, CancellationToken cancellationToken = default) =>
			Task.FromResult(new ArquivoGravado("t/ab/arquivo", "secco-enc:v1:x", 10));

		public Task EscreverEmAsync(
			string caminhoRelativo,
			string chaveEmbrulhada,
			Stream destino,
			CancellationToken cancellationToken = default)
		{
			Leu = true;

			return Task.CompletedTask;
		}

		public Task RemoverAsync(string caminhoRelativo, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private static DocumentoComSetor Documento(VisibilidadeDocumento visibilidade, bool ativo = true)
	{
		var documento = new Documento(
			Guid.NewGuid(),
			"Política interna",
			descricao: null,
			"politica.pdf",
			"application/pdf",
			tamanho: 128,
			visibilidade,
			"tenant/ab/arquivo",
			"secco-enc:v1:x",
			"quem.publicou");

		if (!ativo)
		{
			documento.Arquivar();
		}

		return new DocumentoComSetor(documento, "Financeiro", "financeiro");
	}

	private static async Task<bool> LiberouAsync(
		DocumentoComSetor? encontrado,
		bool exigirVinculo,
		params string[] slugs)
	{
		var handler = new BaixarDocumentoHandler(new RepositorioFalso(encontrado), new StoreFalso());

		var resultado = await handler.HandleAsync(new BaixarDocumentoQuery(
			Guid.NewGuid(),
			new HashSet<string>(slugs, StringComparer.OrdinalIgnoreCase),
			exigirVinculo));

		return resultado.IsSuccess;
	}

	[Fact]
	public async Task DocumentoDeSetor_SemVinculoDoUsuario_Nega() =>
		(await LiberouAsync(Documento(VisibilidadeDocumento.Setor), exigirVinculo: true, "diretoria"))
			.Should().BeFalse();

	[Fact]
	public async Task DocumentoDeSetor_ComVinculoDoUsuario_Libera() =>
		(await LiberouAsync(Documento(VisibilidadeDocumento.Setor), exigirVinculo: true, "financeiro"))
			.Should().BeTrue();

	[Fact]
	public async Task DocumentoDaEmpresa_SemVinculoNenhum_Libera() =>
		(await LiberouAsync(Documento(VisibilidadeDocumento.Empresa), exigirVinculo: true))
			.Should().BeTrue("documento marcado para a empresa toda dispensa vínculo com o setor");

	[Fact]
	public async Task DocumentoDeSetor_NoModoAbertoDeDesenvolvimento_Libera() =>
		(await LiberouAsync(Documento(VisibilidadeDocumento.Setor), exigirVinculo: false))
			.Should().BeTrue();

	[Fact]
	public async Task DocumentoArquivado_MesmoComVinculo_Nega() =>
		(await LiberouAsync(Documento(VisibilidadeDocumento.Setor, ativo: false), exigirVinculo: true, "financeiro"))
			.Should().BeFalse();

	[Fact]
	public async Task DocumentoInexistente_Nega() =>
		(await LiberouAsync(encontrado: null, exigirVinculo: true, "financeiro")).Should().BeFalse();

	[Fact]
	public async Task DocumentoNegado_DevolveOMesmoErroDeInexistente()
	{
		var handler = new BaixarDocumentoHandler(
			new RepositorioFalso(Documento(VisibilidadeDocumento.Setor)), new StoreFalso());

		var negado = await handler.HandleAsync(new BaixarDocumentoQuery(
			Guid.NewGuid(), new HashSet<string>(StringComparer.OrdinalIgnoreCase), ExigirVinculo: true));

		var inexistente = await new BaixarDocumentoHandler(new RepositorioFalso(null), new StoreFalso())
			.HandleAsync(new BaixarDocumentoQuery(
				Guid.NewGuid(), new HashSet<string>(StringComparer.OrdinalIgnoreCase), ExigirVinculo: true));

		negado.Error.Code.Should().Be(
			inexistente.Error.Code,
			"distinguir os dois casos revelaria a existência do documento a quem não pode vê-lo");
	}
}
