using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Domain.Bibliotecas;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de bibliotecas de documentos no banco do tenant atual.</summary>
internal sealed class BibliotecaRepository(IntranetDbContext context) : IBibliotecaRepository
{
	public async Task AddAsync(Biblioteca biblioteca, CancellationToken cancellationToken = default)
	{
		context.Bibliotecas.Add(biblioteca);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

	public async Task<Biblioteca?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.Bibliotecas
			.FirstOrDefaultAsync(biblioteca => biblioteca.Id == id, cancellationToken)
			.ConfigureAwait(false);

	public async Task<Biblioteca?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
	{
		var normalizado = slug.Trim().ToLowerInvariant();

		return await context.Bibliotecas
			.FirstOrDefaultAsync(biblioteca => biblioteca.Slug == normalizado, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default)
	{
		var normalizado = slug.Trim().ToLowerInvariant();

		return await context.Bibliotecas
			.AsNoTracking()
			.AnyAsync(biblioteca => biblioteca.Slug == normalizado, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<Biblioteca>> ListarAsync(bool somenteAtivas, CancellationToken cancellationToken = default)
	{
		var consulta = context.Bibliotecas.AsNoTracking();

		if (somenteAtivas)
		{
			consulta = consulta.Where(biblioteca => biblioteca.Ativo);
		}

		return await consulta
			.OrderBy(biblioteca => biblioteca.Nome)
			.ThenBy(biblioteca => biblioteca.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
	}
}
