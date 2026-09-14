using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Busca paginada de itens de inventário. Sem caminho de falha de negócio hoje.</summary>
public sealed class SearchItensInventarioHandler(IItemInventarioRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<PagedResult<ItemInventarioDto>>> HandleAsync(
		ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(criteria);

		var pagina = await repository.SearchAsync(criteria, cancellationToken).ConfigureAwait(false);

		return PagedResult.Create(
			[.. pagina.Items.Select(ItemInventarioDto.FromEntity)], new PageRequest(pagina.Page, pagina.Size), pagina.TotalCount);
	}
}
