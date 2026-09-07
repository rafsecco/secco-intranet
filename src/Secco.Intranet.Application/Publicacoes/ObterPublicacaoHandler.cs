using Secco.Intranet.Domain;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Consulta de uma publicação pelo identificador, para exibição.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="SetoresDoLeitor">Slugs dos setores aos quais o leitor pertence.</param>
/// <param name="ExigirVisibilidade">Quando <c>false</c>, dispensa o filtro — modo aberto de DEV.</param>
public sealed record ObterPublicacaoQuery(
	Guid Id,
	IReadOnlyList<string> SetoresDoLeitor,
	bool ExigirVisibilidade);

/// <summary>
/// Devolve uma publicação se — e só se — ela está no ar e o leitor pode vê-la. Fora do ar e
/// fora do alcance devolvem o mesmo erro de inexistente: distinguir revelaria a existência.
/// </summary>
/// <param name="repository">Persistência de publicações.</param>
public sealed class ObterPublicacaoHandler(IPublicacaoRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Consulta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PublicacaoDto>> HandleAsync(
		ObterPublicacaoQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var encontrada = await repository.GetByIdAsync(query.Id, cancellationToken).ConfigureAwait(false);

		if (encontrada is null || !encontrada.Publicacao.EstaNoAr(DateTimeOffset.UtcNow))
		{
			return IntranetErrors.Publicacoes.NotFound;
		}

		var visivel = !query.ExigirVisibilidade
			|| encontrada.Publicacao.Visibilidade == Visibilidade.Empresa
			|| query.SetoresDoLeitor.Contains(encontrada.SetorSlug, StringComparer.OrdinalIgnoreCase);

		return visivel
			? Projecao.De(encontrada)
			: IntranetErrors.Publicacoes.NotFound;
	}
}
