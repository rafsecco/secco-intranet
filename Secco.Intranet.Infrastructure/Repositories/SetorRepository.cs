using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de setores no banco do tenant atual.</summary>
internal sealed class SetorRepository(IntranetDbContext context) : ISetorRepository
{
	public async Task AddAsync(Setor setor, CancellationToken cancellationToken = default)
	{
		context.Setores.Add(setor);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.Setores
			.AsNoTracking()
			.FirstOrDefaultAsync(setor => setor.Id == id, cancellationToken)
			.ConfigureAwait(false);

	public async Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
		await context.Setores
			.AsNoTracking()
			.FirstOrDefaultAsync(setor => setor.Slug == slug, cancellationToken)
			.ConfigureAwait(false);

	public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
		await context.Setores
			.AsNoTracking()
			.AnyAsync(setor => setor.Slug == slug, cancellationToken)
			.ConfigureAwait(false);

	public async Task<PagedResult<Setor>> SearchAsync(
		SetorSearchCriteria criteria,
		CancellationToken cancellationToken = default)
	{
		var query = context.Setores.AsNoTracking();

		if (!string.IsNullOrWhiteSpace(criteria.NomeContains))
		{
			query = query.Where(setor => setor.Nome.Contains(criteria.NomeContains));
		}

		if (criteria.ApenasAtivos)
		{
			query = query.Where(setor => setor.Ativo);
		}

		var page = criteria.EffectivePage;
		var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

		var items = await query
			.OrderBy(setor => setor.Nome)
			.ThenByDescending(setor => setor.Id)
			.Skip(page.Skip)
			.Take(page.Size)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return PagedResult.Create(items, page, totalCount);
	}
}
