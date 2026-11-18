using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;

namespace Secco.Intranet.Tests.Support;

/// <summary>Repositório de <see cref="Reserva"/> em memória, para os testes de handler.</summary>
public sealed class ReservaRepositorioFalso : IReservaRepository
{
	private bool _dentroDaTrava;

	/// <summary>Reservas existentes — os testes montam o cenário mexendo aqui direto.</summary>
	public List<Reserva> Reservas { get; } = [];

	/// <summary>Exclusões de evento pendentes gravadas.</summary>
	public List<ExclusaoDeEventoPendente> Exclusoes { get; } = [];

	/// <summary>Salas travadas, na ordem.</summary>
	public List<Guid> Travas { get; } = [];

	/// <summary>Quantas vezes a verificação de sobreposição foi pedida fora da trava.</summary>
	public int SobreposicoesForaDaTrava { get; private set; }

	/// <summary>Quantas vezes <see cref="SaveChangesAsync"/> foi chamado.</summary>
	public int Gravacoes { get; private set; }

	/// <summary>Todos os instantes recebidos, para conferir que estão em UTC.</summary>
	public List<DateTimeOffset> InstantesRecebidos { get; } = [];

	public async Task<T> ComSalaTravadaAsync<T>(Guid salaId, Func<CancellationToken, Task<T>> acao, CancellationToken ct = default)
	{
		Travas.Add(salaId);
		_dentroDaTrava = true;

		try
		{
			return await acao(ct);
		}
		finally
		{
			_dentroDaTrava = false;
		}
	}

	public Task<Reserva?> PrimeiraSobrepostaAsync(
		Guid salaId, DateTimeOffset inicio, DateTimeOffset fim, Guid? ignorarId, CancellationToken ct = default)
	{
		if (!_dentroDaTrava)
		{
			SobreposicoesForaDaTrava++;
		}

		InstantesRecebidos.Add(inicio);
		InstantesRecebidos.Add(fim);

		return Task.FromResult(Reservas
			.Where(r => r.SalaId == salaId && r.Id != ignorarId && r.Sobrepoe(inicio, fim))
			.OrderBy(r => r.Inicio)
			.FirstOrDefault());
	}

	public Task AddAsync(Reserva reserva, CancellationToken ct = default)
	{
		InstantesRecebidos.Add(reserva.Inicio);
		InstantesRecebidos.Add(reserva.Fim);
		Reservas.Add(reserva);

		return Task.CompletedTask;
	}

	public Task<Reserva?> GetAsync(Guid id, CancellationToken ct = default) =>
		Task.FromResult(Reservas.FirstOrDefault(r => r.Id == id));

	public Task<IReadOnlyList<Reserva>> ListarNoIntervaloAsync(DateTimeOffset de, DateTimeOffset ate, CancellationToken ct = default) =>
		Task.FromResult<IReadOnlyList<Reserva>>([.. Reservas.Where(r => r.Sobrepoe(de, ate)).OrderBy(r => r.Inicio)]);

	public Task<IReadOnlyList<Reserva>> ListarFuturasDaPessoaAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken ct = default) =>
		Task.FromResult<IReadOnlyList<Reserva>>([
			.. Reservas
				.Where(r => r.Fim > agora && (r.ReservadoPorId == usuarioId || r.Convidados.Any(c => c.UsuarioId == usuarioId)))
				.OrderBy(r => r.Inicio)]);

	public Task<IReadOnlyList<Reserva>> ListarParaEspelharAsync(DateTimeOffset agora, int limite, CancellationToken ct = default) =>
		Task.FromResult<IReadOnlyList<Reserva>>([]);

	public void Remover(Reserva reserva) => Reservas.Remove(reserva);

	public Task AdicionarExclusaoPendenteAsync(ExclusaoDeEventoPendente pendencia, CancellationToken ct = default)
	{
		Exclusoes.Add(pendencia);

		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<ExclusaoDeEventoPendente>> ListarExclusoesPendentesAsync(
		DateTimeOffset agora, int limite, CancellationToken ct = default) =>
		Task.FromResult<IReadOnlyList<ExclusaoDeEventoPendente>>([.. Exclusoes]);

	public void RemoverExclusaoPendente(ExclusaoDeEventoPendente pendencia) => Exclusoes.Remove(pendencia);

	public Task SaveChangesAsync(CancellationToken ct = default)
	{
		Gravacoes++;

		return Task.CompletedTask;
	}
}
