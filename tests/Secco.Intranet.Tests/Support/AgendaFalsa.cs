using Secco.Intranet.Application.Salas.Agenda;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// Agenda externa roteirizada: cada chamada consome o próximo resultado de <see cref="Roteiro"/>;
/// sem roteiro, cria com um id novo e atualiza/apaga com sucesso.
/// </summary>
public sealed class AgendaFalsa : IAgendaExterna
{
	private int _contador;

	/// <summary>Resultados a devolver, na ordem das chamadas.</summary>
	public Queue<ResultadoDaAgenda> Roteiro { get; } = new();

	/// <summary>Chamadas recebidas, com a operação ("criar", "atualizar", "apagar") e o id do evento quando houve.</summary>
	public List<(string Operacao, string? EventoId, EventoDeAgenda? Evento)> Chamadas { get; } = [];

	/// <summary>Executado no início de cada chamada, antes de devolver o resultado — simula o que acontece durante o envio.</summary>
	public Func<string, Task>? Durante { get; set; }

	/// <summary>Quando definido, toda chamada lança esta exceção.</summary>
	public Exception? Lancar { get; set; }

	public async Task<ResultadoDaAgenda> CriarAsync(EventoDeAgenda evento, CancellationToken ct = default)
	{
		Chamadas.Add(("criar", null, evento));
		await AntesAsync("criar");

		return Roteiro.Count > 0
			? Roteiro.Dequeue()
			: new ResultadoDaAgenda(DesfechoDaAgenda.Feito, $"evento-{Interlocked.Increment(ref _contador)}-{Guid.NewGuid():N}");
	}

	public async Task<ResultadoDaAgenda> AtualizarAsync(string eventoId, EventoDeAgenda evento, CancellationToken ct = default)
	{
		Chamadas.Add(("atualizar", eventoId, evento));
		await AntesAsync("atualizar");

		return Roteiro.Count > 0 ? Roteiro.Dequeue() : new ResultadoDaAgenda(DesfechoDaAgenda.Feito, eventoId);
	}

	public async Task<ResultadoDaAgenda> ApagarAsync(string eventoId, CancellationToken ct = default)
	{
		Chamadas.Add(("apagar", eventoId, null));
		await AntesAsync("apagar");

		return Roteiro.Count > 0 ? Roteiro.Dequeue() : new ResultadoDaAgenda(DesfechoDaAgenda.Feito);
	}

	private async Task AntesAsync(string operacao)
	{
		if (Lancar is not null)
		{
			throw Lancar;
		}

		if (Durante is not null)
		{
			await Durante(operacao);
		}
	}
}
