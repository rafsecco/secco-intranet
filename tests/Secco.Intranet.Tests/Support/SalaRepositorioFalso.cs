using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;

namespace Secco.Intranet.Tests.Support;

/// <summary>Repositório de <see cref="Sala"/> em memória, para os testes de handler.</summary>
public sealed class SalaRepositorioFalso : ISalaRepository
{
	/// <summary>Salas existentes — os testes montam o cenário mexendo aqui direto.</summary>
	public List<Sala> Salas { get; } = [];

	/// <summary>Fim das reservas por sala; basta para decidir se há reserva futura.</summary>
	public List<(Guid SalaId, DateTimeOffset Fim)> Reservas { get; } = [];

	public Task AddAsync(Sala sala, CancellationToken ct = default)
	{
		Salas.Add(sala);

		return Task.CompletedTask;
	}

	public Task<Sala?> GetAsync(Guid id, CancellationToken ct = default) =>
		Task.FromResult(Salas.FirstOrDefault(sala => sala.Id == id));

	public Task<IReadOnlyList<Sala>> ListarAsync(bool somenteAtivas, CancellationToken ct = default) =>
		Task.FromResult<IReadOnlyList<Sala>>([
			.. Salas
				.Where(sala => !somenteAtivas || sala.Ativa)
				.OrderBy(sala => sala.Ordem)
				.ThenBy(sala => sala.Nome)]);

	public Task<bool> NomeEmUsoAsync(string nome, Guid? ignorarId, CancellationToken ct = default) =>
		Task.FromResult(Salas.Any(sala =>
			string.Equals(sala.Nome, nome, StringComparison.OrdinalIgnoreCase) && (ignorarId is null || sala.Id != ignorarId)));

	public Task<bool> TemReservaFuturaAsync(Guid salaId, DateTimeOffset agora, CancellationToken ct = default) =>
		Task.FromResult(Reservas.Any(reserva => reserva.SalaId == salaId && reserva.Fim > agora));

	public Task RemoverReservasDaSalaAsync(Guid salaId, CancellationToken ct = default)
	{
		Reservas.RemoveAll(reserva => reserva.SalaId == salaId);

		return Task.CompletedTask;
	}

	public void Remover(Sala sala) => Salas.Remove(sala);

	public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
