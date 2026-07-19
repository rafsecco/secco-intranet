using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Application.Setores;

/// <summary>Filtros da busca de setores. Todos opcionais; combinados com AND.</summary>
/// <param name="NomeContains">Trecho contido no nome.</param>
/// <param name="ApenasAtivos">Quando <c>true</c>, retorna somente setores ativos.</param>
/// <param name="Page">Paginação (1-based).</param>
public sealed record SetorSearchCriteria(
	string? NomeContains = null,
	bool ApenasAtivos = false,
	PageRequest? Page = null)
{
	/// <summary>Paginação efetiva (default da plataforma quando não informada).</summary>
	public PageRequest EffectivePage => Page ?? PageRequest.Default;
}

/// <summary>Porta de persistência de setores — sempre no banco do tenant atual (ADR-0005).</summary>
public interface ISetorRepository
{
	/// <summary>Persiste um setor.</summary>
	/// <param name="setor">Setor a persistir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(Setor setor, CancellationToken cancellationToken = default);

	/// <summary>Busca um setor pelo identificador.</summary>
	/// <param name="id">Identificador do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Busca um setor pelo slug — usado ao resolver a Role do usuário logado.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

	/// <summary>Verifica se já existe um setor com o slug informado.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);

	/// <summary>Busca paginada, mais recentes primeiro.</summary>
	/// <param name="criteria">Filtros e paginação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PagedResult<Setor>> SearchAsync(SetorSearchCriteria criteria, CancellationToken cancellationToken = default);
}
