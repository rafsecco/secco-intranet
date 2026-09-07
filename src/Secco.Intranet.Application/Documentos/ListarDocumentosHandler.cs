using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Consulta dos documentos de um setor.</summary>
/// <param name="SetorSlug">Slug do setor.</param>
public sealed record ListarDocumentosQuery(string? SetorSlug);

/// <summary>Lista os documentos ativos de um setor, do mais recente para o mais antigo.</summary>
/// <param name="repository">Persistência de documentos.</param>
public sealed class ListarDocumentosHandler(IDocumentoRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Consulta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<DocumentoDto>>> HandleAsync(
		ListarDocumentosQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		if (string.IsNullOrWhiteSpace(query.SetorSlug))
		{
			return IntranetErrors.Setores.NotFound;
		}

		var documentos = await repository
			.ListarPorSetorAsync(query.SetorSlug, cancellationToken)
			.ConfigureAwait(false);

		return Result<IReadOnlyList<DocumentoDto>>.Success(documentos);
	}
}
