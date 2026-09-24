using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de perfis de colaborador no banco do tenant atual.</summary>
internal sealed class PerfilColaboradorRepository(IntranetDbContext context) : IPerfilColaboradorRepository
{
	public async Task<PerfilColaborador?> GetByUsuarioIdAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		await context.PerfisColaboradores
			.AsNoTracking()
			.FirstOrDefaultAsync(perfil => perfil.UsuarioId == usuarioId, cancellationToken)
			.ConfigureAwait(false);

	public async Task<PerfilColaborador?> GetParaEdicaoAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		await context.PerfisColaboradores
			.FirstOrDefaultAsync(perfil => perfil.UsuarioId == usuarioId, cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<PerfilColaborador>> ListarTodosAsync(CancellationToken cancellationToken = default) =>
		await context.PerfisColaboradores
			.AsNoTracking()
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<bool> TentarAdicionarAsync(PerfilColaborador perfil, CancellationToken cancellationToken = default)
	{
		context.PerfisColaboradores.Add(perfil);

		try
		{
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

			return true;
		}
		catch (DbUpdateException)
		{
			context.Entry(perfil).State = EntityState.Detached;

			var jaExiste = await context.PerfisColaboradores
				.AsNoTracking()
				.AnyAsync(outro => outro.UsuarioId == perfil.UsuarioId, cancellationToken)
				.ConfigureAwait(false);

			// Outro pedido criou o perfil um instante antes: não é falha, o chamador reaplica.
			// Qualquer outra causa de DbUpdateException continua subindo.
			if (jaExiste)
			{
				return false;
			}

			throw;
		}
	}

	public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
