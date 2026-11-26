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

	public Task<IReadOnlyList<Reserva>> ListarParaEspelharAsync(DateTimeOffset agora, int limite, CancellationToken ct = default)
	{
		var lote = Reservas
			.Where(r => r.Espelhamento == EstadoDoEspelhamento.Aguardando && (r.ProximaTentativaEm is null || r.ProximaTentativaEm <= agora))
			.Take(limite)
			.ToList();

		// Guarda o que o "banco" tem de cada uma, para o recarregamento desfazer o que o handler mudou em memória.
		foreach (var reserva in lote)
		{
			_noBanco[reserva] = CamposDoEspelhamento.Select(c => c.GetValue(reserva)).ToArray();
		}

		return Task.FromResult<IReadOnlyList<Reserva>>(lote);
	}

	public void Remover(Reserva reserva) => Reservas.Remove(reserva);

	public Task AdicionarExclusaoPendenteAsync(ExclusaoDeEventoPendente pendencia, CancellationToken ct = default)
	{
		Exclusoes.Add(pendencia);

		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<ExclusaoDeEventoPendente>> ListarExclusoesPendentesAsync(
		DateTimeOffset agora, int limite, CancellationToken ct = default) =>
		Task.FromResult<IReadOnlyList<ExclusaoDeEventoPendente>>([
			.. Exclusoes
				.Where(e => !e.Falhou && (e.ProximaTentativaEm is null || e.ProximaTentativaEm <= agora))
				.Take(limite)]);

	public void RemoverExclusaoPendente(ExclusaoDeEventoPendente pendencia) => Exclusoes.Remove(pendencia);

	/// <summary>Quantas gravações seguintes falham por conflito de versão (a alteração fica pendente até <see cref="RecarregarAsync"/>).</summary>
	public int ConflitosForcados { get; set; }

	/// <summary>Quantas vezes <see cref="RecarregarAsync"/> foi chamado.</summary>
	public int Recarregadas { get; private set; }

	private static readonly System.Reflection.PropertyInfo[] CamposDoEspelhamento =
	[
		.. new[] { nameof(Reserva.Espelhamento), nameof(Reserva.EventoExternoId), nameof(Reserva.MotivoDaFalha), nameof(Reserva.TentativasDeEspelhamento), nameof(Reserva.ProximaTentativaEm) }
			.Select(nome => typeof(Reserva).GetProperty(nome)!),
	];

	private readonly Dictionary<Reserva, object?[]> _noBanco = [];

	private bool _alteracaoPendente;

	public Task<bool> TentarGravarAsync(CancellationToken ct = default)
	{
		if (ConflitosForcados > 0)
		{
			ConflitosForcados--;
			_alteracaoPendente = true;
		}

		if (_alteracaoPendente)
		{
			return Task.FromResult(false);
		}

		Gravacoes++;

		return Task.FromResult(true);
	}

	public Task<bool> RecarregarAsync(Reserva reserva, CancellationToken ct = default)
	{
		Recarregadas++;
		_alteracaoPendente = false;

		if (_noBanco.TryGetValue(reserva, out var valores))
		{
			for (var i = 0; i < CamposDoEspelhamento.Length; i++)
			{
				CamposDoEspelhamento[i].SetValue(reserva, valores[i]);
			}
		}

		return Task.FromResult(Reservas.Contains(reserva));
	}

	public Task SaveChangesAsync(CancellationToken ct = default)
	{
		Gravacoes++;

		return Task.CompletedTask;
	}
}
