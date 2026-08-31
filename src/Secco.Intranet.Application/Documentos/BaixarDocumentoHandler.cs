using Secco.Intranet.Domain.Documentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Pedido de download de um documento.</summary>
/// <param name="DocumentoId">Identificador do documento.</param>
/// <param name="SlugsDoUsuario">Setores aos quais o usuário está vinculado (ADR-0001).</param>
/// <param name="ExigirVinculo">
/// Quando <c>false</c>, dispensa a checagem de vínculo — é o modo aberto de DEV, sem
/// SecureGate para emitir roles. Em produção é sempre <c>true</c>.
/// </param>
public sealed record BaixarDocumentoQuery(
	Guid DocumentoId,
	IReadOnlySet<string> SlugsDoUsuario,
	bool ExigirVinculo);

/// <summary>Documento liberado para entrega.</summary>
/// <param name="NomeArquivo">Nome original, usado no <c>Content-Disposition</c>.</param>
/// <param name="ContentType">Tipo apurado na publicação.</param>
/// <param name="Tamanho">Tamanho em bytes do conteúdo original.</param>
/// <param name="EscreverConteudo">Escreve o conteúdo decifrado no fluxo informado.</param>
public sealed record DocumentoParaDownload(
	string NomeArquivo,
	string ContentType,
	long Tamanho,
	Func<Stream, CancellationToken, Task> EscreverConteudo);

/// <summary>
/// Autoriza e prepara a entrega de um documento. Toda leitura passa por aqui: os arquivos
/// não são servidos como conteúdo estático nem têm endereço adivinhável, porque a regra de
/// visibilidade precisa ser avaliada a cada download.
/// </summary>
/// <param name="repository">Persistência de documentos.</param>
/// <param name="store">Armazenamento de arquivos.</param>
public sealed class BaixarDocumentoHandler(IDocumentoRepository repository, IArquivoStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Pedido de download.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<DocumentoParaDownload>> HandleAsync(
		BaixarDocumentoQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var encontrado = await repository.GetByIdAsync(query.DocumentoId, cancellationToken).ConfigureAwait(false);

		// Documento inexistente, arquivado ou fora do alcance do usuário devolvem o MESMO erro:
		// distinguir "não existe" de "existe mas você não pode ver" entrega a quem sonda a
		// informação de que o documento existe.
		if (encontrado is null || !encontrado.Documento.Ativo || !PodeLer(encontrado, query))
		{
			return IntranetErrors.Documentos.NotFound;
		}

		var documento = encontrado.Documento;

		return new DocumentoParaDownload(
			documento.NomeArquivo,
			documento.ContentType,
			documento.Tamanho,
			(destino, token) => store.EscreverEmAsync(
				documento.CaminhoRelativo, documento.ChaveEmbrulhada, destino, token));
	}

	private static bool PodeLer(DocumentoComSetor encontrado, BaixarDocumentoQuery query) =>
		encontrado.Documento.Visibilidade == VisibilidadeDocumento.Empresa
		|| !query.ExigirVinculo
		|| query.SlugsDoUsuario.Contains(encontrado.SetorSlug);
}
