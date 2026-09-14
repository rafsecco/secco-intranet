using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de itens de inventário no banco do tenant atual.</summary>
internal sealed class ItemInventarioRepository(IntranetDbContext context) : IItemInventarioRepository
{
	public async Task AddAsync(ItemInventario item, CancellationToken cancellationToken = default)
	{
		context.ItensInventario.Add(item);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.ItensInventario
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
			.ConfigureAwait(false);

	public async Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.ItensInventario
			.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
			.ConfigureAwait(false);

	public async Task<PagedResult<ItemInventario>> SearchAsync(
		ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default)
	{
		var query = context.ItensInventario.AsNoTracking();

		if (!string.IsNullOrWhiteSpace(criteria.NomeContains))
		{
			query = query.Where(item => item.Nome.Contains(criteria.NomeContains));
		}

		if (criteria.Status.HasValue)
		{
			query = query.Where(item => item.Status == criteria.Status.Value);
		}
		else if (criteria.ApenasNaoBaixados)
		{
			query = query.Where(item => item.Status != StatusDoItem.Baixado);
		}

		if (criteria.SetorId.HasValue)
		{
			query = query.Where(item => item.SetorId == criteria.SetorId.Value);
		}

		var page = criteria.EffectivePage;
		var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

		var items = await query
			.OrderByDescending(item => item.CreatedAt)
			.ThenByDescending(item => item.Id)
			.Skip(page.Skip)
			.Take(page.Size)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return PagedResult.Create(items, page, totalCount);
	}

	public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
