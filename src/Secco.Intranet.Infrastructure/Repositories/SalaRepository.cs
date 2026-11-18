using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de salas no banco do tenant atual.</summary>
internal sealed class SalaRepository(IntranetDbContext context) : ISalaRepository
{
	public async Task AddAsync(Sala sala, CancellationToken ct = default)
	{
		context.Salas.Add(sala);
		await context.SaveChangesAsync(ct).ConfigureAwait(false);
	}

	public async Task<Sala?> GetAsync(Guid id, CancellationToken ct = default) =>
		await context.Salas
			.FirstOrDefaultAsync(sala => sala.Id == id, ct)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<Sala>> ListarAsync(bool somenteAtivas, CancellationToken ct = default)
	{
		var query = context.Salas.AsNoTracking();

		if (somenteAtivas)
		{
			query = query.Where(sala => sala.Ativa);
		}

		return await query
			.OrderBy(sala => sala.Ordem)
			.ThenBy(sala => sala.Nome)
			.ToListAsync(ct)
			.ConfigureAwait(false);
	}

	public async Task<bool> NomeEmUsoAsync(string nome, Guid? ignorarId, CancellationToken ct = default) =>
		await context.Salas
			.AsNoTracking()
			.AnyAsync(sala => sala.Nome.ToLower() == nome.ToLower() && (ignorarId == null || sala.Id != ignorarId), ct)
			.ConfigureAwait(false);

	public async Task<bool> TemReservaFuturaAsync(Guid salaId, DateTimeOffset agora, CancellationToken ct = default) =>
		await context.Reservas
			.AsNoTracking()
			.AnyAsync(reserva => reserva.SalaId == salaId && reserva.Fim > agora, ct)
			.ConfigureAwait(false);

	public async Task RemoverReservasDaSalaAsync(Guid salaId, CancellationToken ct = default) =>
		await context.Reservas
			.Where(reserva => reserva.SalaId == salaId)
			.ExecuteDeleteAsync(ct)
			.ConfigureAwait(false);

	public void Remover(Sala sala) => context.Salas.Remove(sala);

	public async Task SaveChangesAsync(CancellationToken ct = default) =>
		await context.SaveChangesAsync(ct).ConfigureAwait(false);
}
