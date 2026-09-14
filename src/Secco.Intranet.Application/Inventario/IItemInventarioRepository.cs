using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Filtros da busca de itens de inventário. Todos opcionais; combinados com AND.</summary>
/// <param name="NomeContains">Trecho contido no nome.</param>
/// <param name="Status">Quando informado, restringe a esse status.</param>
/// <param name="ApenasNaoBaixados">
/// Quando <c>true</c> e <see cref="Status"/> não for informado, exclui itens Baixados — é o
/// padrão da listagem (mesmo comportamento de Setor inativo/Documento arquivado).
/// </param>
/// <param name="SetorId">Quando informado, restringe ao setor.</param>
/// <param name="Page">Paginação (1-based).</param>
public sealed record ItemInventarioSearchCriteria(
	string? NomeContains = null,
	StatusDoItem? Status = null,
	bool ApenasNaoBaixados = false,
	Guid? SetorId = null,
	PageRequest? Page = null)
{
	/// <summary>Paginação efetiva (default da plataforma quando não informada).</summary>
	public PageRequest EffectivePage => Page ?? PageRequest.Default;
}

/// <summary>Porta de persistência de itens de inventário — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IItemInventarioRepository
{
	/// <summary>Persiste um item novo.</summary>
	Task AddAsync(ItemInventario item, CancellationToken cancellationToken = default);

	/// <summary>Busca um item desrastreado — caminho de leitura.</summary>
	Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Busca um item <b>rastreado</b>, para alteração. Diferente de <see cref="GetByIdAsync"/>:
	/// alterar aquele resultado e chamar <see cref="SaveChangesAsync"/> não gravaria nada.
	/// </summary>
	Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Busca paginada, mais recentes primeiro.</summary>
	Task<PagedResult<ItemInventario>> SearchAsync(
		ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default);

	/// <summary>Grava alterações pendentes de um item já rastreado.</summary>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
