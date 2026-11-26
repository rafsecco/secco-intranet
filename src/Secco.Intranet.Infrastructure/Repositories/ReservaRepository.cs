using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.Intranet.Infrastructure.Salas;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de reservas de sala e de exclusões de evento pendentes no banco do tenant atual.</summary>
internal sealed class ReservaRepository(IntranetDbContext context) : IReservaRepository
{
	public async Task<T> ComSalaTravadaAsync<T>(Guid salaId, Func<CancellationToken, Task<T>> acao, CancellationToken ct = default)
	{
		// A estratégia de execução repete o bloco inteiro quando o provedor reconhece uma falha
		// passageira; a transação precisa estar dentro dela, não em volta.
		var estrategia = context.Database.CreateExecutionStrategy();

		return await estrategia.ExecuteAsync(async token =>
		{
			await using var transacao = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);

			await TravaDeSala.AdquirirAsync(context, salaId, token).ConfigureAwait(false);

			var resultado = await acao(token).ConfigureAwait(false);

			await transacao.CommitAsync(token).ConfigureAwait(false);

			return resultado;
		}, ct).ConfigureAwait(false);
	}

	public async Task<Reserva?> PrimeiraSobrepostaAsync(
		Guid salaId, DateTimeOffset inicio, DateTimeOffset fim, Guid? ignorarId, CancellationToken ct = default) =>
		await context.Reservas
			.AsNoTracking()
			.Where(reserva => reserva.SalaId == salaId
				&& reserva.Inicio < fim
				&& reserva.Fim > inicio
				&& (ignorarId == null || reserva.Id != ignorarId))
			.OrderBy(reserva => reserva.Inicio)
			.FirstOrDefaultAsync(ct)
			.ConfigureAwait(false);

	public async Task AddAsync(Reserva reserva, CancellationToken ct = default)
	{
		context.Reservas.Add(reserva);
		await context.SaveChangesAsync(ct).ConfigureAwait(false);
	}

	public async Task<Reserva?> GetAsync(Guid id, CancellationToken ct = default) =>
		await context.Reservas
			.Include(reserva => reserva.Convidados)
			.FirstOrDefaultAsync(reserva => reserva.Id == id, ct)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<Reserva>> ListarNoIntervaloAsync(DateTimeOffset de, DateTimeOffset ate, CancellationToken ct = default) =>
		await context.Reservas
			.AsNoTracking()
			.Include(reserva => reserva.Convidados)
			.Where(reserva => reserva.Inicio < ate && reserva.Fim > de)
			.OrderBy(reserva => reserva.Inicio)
			.ToListAsync(ct)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<Reserva>> ListarFuturasDaPessoaAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken ct = default) =>
		await context.Reservas
			.AsNoTracking()
			.Include(reserva => reserva.Convidados)
			.Where(reserva => reserva.Fim > agora
				&& (reserva.ReservadoPorId == usuarioId
					|| reserva.Convidados.Any(convidado => convidado.UsuarioId == usuarioId)))
			.OrderBy(reserva => reserva.Inicio)
			.ToListAsync(ct)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<Reserva>> ListarParaEspelharAsync(DateTimeOffset agora, int limite, CancellationToken ct = default) =>
		await context.Reservas
			.Include(reserva => reserva.Convidados)
			.Where(reserva => reserva.Espelhamento == EstadoDoEspelhamento.Aguardando
				&& (reserva.ProximaTentativaEm == null || reserva.ProximaTentativaEm <= agora))
			.OrderBy(reserva => reserva.CreatedAt)
			.Take(limite)
			.ToListAsync(ct)
			.ConfigureAwait(false);

	public void Remover(Reserva reserva) => context.Reservas.Remove(reserva);

	public async Task AdicionarExclusaoPendenteAsync(ExclusaoDeEventoPendente pendencia, CancellationToken ct = default)
	{
		// Só registra: quem cancela a reserva grava a remoção e a pendência na mesma chamada a SaveChangesAsync.
		await context.ExclusoesDeEventoPendentes.AddAsync(pendencia, ct).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ExclusaoDeEventoPendente>> ListarExclusoesPendentesAsync(DateTimeOffset agora, int limite, CancellationToken ct = default) =>
		await context.ExclusoesDeEventoPendentes
			.Where(pendencia => !pendencia.Falhou
				&& (pendencia.ProximaTentativaEm == null || pendencia.ProximaTentativaEm <= agora))
			.OrderBy(pendencia => pendencia.CreatedAt)
			.Take(limite)
			.ToListAsync(ct)
			.ConfigureAwait(false);

	public void RemoverExclusaoPendente(ExclusaoDeEventoPendente pendencia) =>
		context.ExclusoesDeEventoPendentes.Remove(pendencia);

	public async Task<bool> TentarGravarAsync(CancellationToken ct = default)
	{
		try
		{
			await context.SaveChangesAsync(ct).ConfigureAwait(false);

			return true;
		}
		catch (DbUpdateConcurrencyException)
		{
			return false;
		}
	}

	public async Task<bool> RecarregarAsync(Reserva reserva, CancellationToken ct = default)
	{
		var entrada = context.Entry(reserva);

		// Recarregar uma linha apagada solta a entrada do rastreamento.
		await entrada.ReloadAsync(ct).ConfigureAwait(false);

		return entrada.State != EntityState.Detached;
	}

	public async Task SaveChangesAsync(CancellationToken ct = default) =>
		await context.SaveChangesAsync(ct).ConfigureAwait(false);
}
