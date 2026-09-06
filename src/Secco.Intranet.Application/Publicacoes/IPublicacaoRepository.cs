using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Publicação com os dados do setor dono, para decidir acesso sem segunda consulta.</summary>
/// <param name="Publicacao">Entidade rastreada.</param>
/// <param name="SetorNome">Nome do setor dono.</param>
/// <param name="SetorSlug">Slug do setor dono.</param>
public sealed record PublicacaoComSetor(Publicacao Publicacao, string SetorNome, string SetorSlug);

/// <summary>Critérios da consulta do mural.</summary>
/// <param name="Agora">Instante que define o que está no ar.</param>
/// <param name="Tipo">Filtro por natureza; nulo traz todos.</param>
/// <param name="SetoresDoLeitor">Slugs dos setores aos quais o leitor pertence.</param>
/// <param name="ExigirVisibilidade">
/// Quando <c>false</c>, dispensa o filtro de visibilidade — é o modo aberto de DEV.
/// </param>
/// <param name="Page">Paginação.</param>
public sealed record MuralCriteria(
	DateTimeOffset Agora,
	TipoPublicacao? Tipo,
	IReadOnlyList<string> SetoresDoLeitor,
	bool ExigirVisibilidade,
	PageRequest Page);

/// <summary>Porta de persistência de publicações — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IPublicacaoRepository
{
	/// <summary>Persiste uma publicação.</summary>
	/// <param name="publicacao">Publicação a persistir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(Publicacao publicacao, CancellationToken cancellationToken = default);

	/// <summary>Grava alterações pendentes de uma publicação já rastreada.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);

	/// <summary>Busca uma publicação pelo identificador, junto do setor dono.</summary>
	/// <param name="id">Identificador.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PublicacaoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Publicações no ar e visíveis para o leitor, da mais recente para a mais antiga.</summary>
	/// <param name="criteria">Critérios.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PagedResult<PublicacaoDto>> ListarNoMuralAsync(
		MuralCriteria criteria,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Todas as publicações de um setor, inclusive fora do ar — é a aba de quem administra.
	/// </summary>
	/// <param name="setorSlug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<PublicacaoDto>> ListarDoSetorAsync(
		string setorSlug,
		CancellationToken cancellationToken = default);
}
