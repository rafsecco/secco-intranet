using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Consulta dos documentos de um setor.</summary>
/// <param name="SetorSlug">Slug do setor.</param>
public sealed record ListarDocumentosQuery(string? SetorSlug);

/// <summary>Item da raiz com os documentos abaixo dele.</summary>
/// <param name="Documento">Item da raiz: arquivo ou grupo.</param>
/// <param name="Filhos">Documentos ativos abaixo do item, por <c>Ordem</c>; vazio se não houver.</param>
public sealed record NoDeDocumentoDto(DocumentoDto Documento, IReadOnlyList<DocumentoDto> Filhos);

/// <summary>
/// Lista os documentos ativos de um setor como árvore de dois níveis, por <c>Ordem</c>. Grupo
/// vazio permanece no resultado; esconder é decisão da página de leitura.
/// </summary>
/// <param name="repository">Persistência de documentos.</param>
public sealed class ListarDocumentosHandler(IDocumentoRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Consulta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<NoDeDocumentoDto>>> HandleAsync(
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

		var filhosPorPai = documentos
			.Where(documento => documento.ParentId is not null)
			.GroupBy(documento => documento.ParentId!.Value)
			.ToDictionary(grupo => grupo.Key, grupo => (IReadOnlyList<DocumentoDto>)[.. grupo.OrderBy(filho => filho.Ordem)]);

		// Filho cujo pai não está ativo não tem raiz onde aparecer e fica de fora.
		IReadOnlyList<NoDeDocumentoDto> arvore =
		[
			.. documentos
				.Where(documento => documento.ParentId is null)
				.OrderBy(documento => documento.Ordem)
				.Select(raiz => new NoDeDocumentoDto(
					raiz,
					filhosPorPai.TryGetValue(raiz.Id, out var filhos) ? filhos : [])),
		];

		return Result<IReadOnlyList<NoDeDocumentoDto>>.Success(arvore);
	}
}
