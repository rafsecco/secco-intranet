using Secco.Intranet.Application.Salas.Agenda;

namespace Secco.Intranet.Infrastructure.Salas;

/// <summary>
/// Agenda do Microsoft 365 provisória: ainda não fala com o serviço e recusa tudo como falha definitiva.
/// A integração real a substitui (ver a tarefa seguinte do plano).
/// </summary>
internal sealed class AgendaMicrosoftProvisoria : IAgendaExterna
{
	private static readonly ResultadoDaAgenda Indisponivel =
		new(DesfechoDaAgenda.FalhaDefinitiva, Motivo: "Provedor ainda não disponível.");

	/// <inheritdoc />
	public Task<ResultadoDaAgenda> CriarAsync(EventoDeAgenda evento, CancellationToken ct = default) => Task.FromResult(Indisponivel);

	/// <inheritdoc />
	public Task<ResultadoDaAgenda> AtualizarAsync(string eventoId, EventoDeAgenda evento, CancellationToken ct = default) => Task.FromResult(Indisponivel);

	/// <inheritdoc />
	public Task<ResultadoDaAgenda> ApagarAsync(string eventoId, CancellationToken ct = default) => Task.FromResult(Indisponivel);
}
