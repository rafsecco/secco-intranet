using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência da árvore de itens de menu no banco do tenant atual.</summary>
internal sealed class ItemMenuRepository(IntranetDbContext context) : IItemMenuRepository
{
	public async Task AddAsync(ItemMenu item, CancellationToken cancellationToken = default)
	{
		context.ItensMenu.Add(item);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<ItemMenu?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.ItensMenu
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
			.ConfigureAwait(false);

	public async Task<ItemMenu?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.ItensMenu
			.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<ItemMenu>> ListarPorSetorAsync(Guid setorId, CancellationToken cancellationToken = default) =>
		await context.ItensMenu
			.AsNoTracking()
			.Where(item => item.SetorId == setorId)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<bool> ExisteTipoAsync(Guid setorId, TipoDeItemMenu tipo, CancellationToken cancellationToken = default) =>
		await context.ItensMenu
			.AsNoTracking()
			.AnyAsync(item => item.SetorId == setorId && item.Tipo == tipo, cancellationToken)
			.ConfigureAwait(false);

	public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
