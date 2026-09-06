using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Consulta do mural.</summary>
/// <param name="Tipo">Filtro por natureza; nulo traz todos.</param>
/// <param name="SetoresDoLeitor">Slugs dos setores aos quais o leitor pertence.</param>
/// <param name="ExigirVisibilidade">
/// Quando <c>false</c>, dispensa o filtro de visibilidade — modo aberto de DEV.
/// </param>
/// <param name="Pagina">Página desejada (1-based).</param>
public sealed record MuralQuery(
	TipoPublicacao? Tipo,
	IReadOnlyList<string> SetoresDoLeitor,
	bool ExigirVisibilidade,
	int Pagina);

/// <summary>
/// Lista o que está no ar e visível para o leitor. O instante de referência é resolvido aqui,
/// uma vez por consulta: se cada cláusula perguntasse a hora, duas publicações no limite da
/// expiração poderiam ser avaliadas contra relógios diferentes.
/// </summary>
/// <param name="repository">Persistência de publicações.</param>
public sealed class ListarMuralHandler(IPublicacaoRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Consulta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PagedResult<PublicacaoDto>>> HandleAsync(
		MuralQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var criteria = new MuralCriteria(
			DateTimeOffset.UtcNow,
			query.Tipo,
			query.SetoresDoLeitor,
			query.ExigirVisibilidade,
			new PageRequest(query.Pagina));

		var pagina = await repository.ListarNoMuralAsync(criteria, cancellationToken).ConfigureAwait(false);

		return Result<PagedResult<PublicacaoDto>>.Success(pagina);
	}
}
