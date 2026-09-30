using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Menu;

/// <summary>Porta de persistência da árvore de itens de menu — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IItemMenuRepository
{
	/// <summary>Persiste um item novo.</summary>
	/// <param name="item">Item a persistir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(ItemMenu item, CancellationToken cancellationToken = default);

	/// <summary>Busca um item pelo identificador, desrastreado.</summary>
	/// <param name="id">Identificador do item.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<ItemMenu?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Busca um item <b>rastreado</b>, para alteração — alterar o resultado de
	/// <see cref="GetByIdAsync"/> e chamar <see cref="SaveChangesAsync"/> não gravaria nada.
	/// </summary>
	/// <param name="id">Identificador do item.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<ItemMenu?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Toda a árvore de um setor, achatada, inclusive itens inativos.</summary>
	/// <param name="setorId">Setor dono da árvore.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<ItemMenu>> ListarPorSetorAsync(Guid setorId, CancellationToken cancellationToken = default);

	/// <summary>Indica se o setor já tem um item do tipo informado, ativo ou não.</summary>
	/// <param name="setorId">Setor dono da árvore.</param>
	/// <param name="tipo">Tipo procurado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> ExisteTipoAsync(Guid setorId, TipoDeItemMenu tipo, CancellationToken cancellationToken = default);

	/// <summary>Grava alterações pendentes de itens já rastreados.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
