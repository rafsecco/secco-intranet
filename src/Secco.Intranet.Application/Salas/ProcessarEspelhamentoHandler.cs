using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Salas.Agenda;
using Secco.Intranet.Domain.Salas;

namespace Secco.Intranet.Application.Salas;

/// <summary>
/// Um ciclo do espelhamento das reservas na agenda externa: cria ou atualiza o evento das reservas
/// pendentes e apaga os eventos das reservas canceladas. Cada item é gravado logo depois de tratado e a
/// falha de um não interrompe os outros.
/// </summary>
/// <param name="reservas">Reservas e exclusões pendentes.</param>
/// <param name="salas">Salas, para o nome e o e-mail do evento.</param>
/// <param name="agenda">Agenda externa.</param>
/// <param name="relogio">Relógio.</param>
/// <param name="logger">Log.</param>
public sealed partial class ProcessarEspelhamentoHandler(
	IReservaRepository reservas,
	ISalaRepository salas,
	IAgendaExterna agenda,
	RelogioDeSalas relogio,
	ILogger<ProcessarEspelhamentoHandler> logger)
{
	/// <summary>Quantas reservas, e quantas exclusões, um ciclo trata.</summary>
	public const int TamanhoDoLote = 20;

	// Quantas vezes a gravação do evento criado é refeita quando a reserva muda de novo no meio.
	private const int MaximoDeConflitos = 5;

	private const string MotivoInesperado = "Falha inesperada ao falar com a agenda.";

	/// <summary>Executa um ciclo e devolve quantas pendências tratou (reservas e exclusões).</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<int> HandleAsync(CancellationToken cancellationToken = default)
	{
		var tratadas = 0;

		foreach (var reserva in await reservas.ListarParaEspelharAsync(relogio.Agora(), TamanhoDoLote, cancellationToken).ConfigureAwait(false))
		{
			await EspelharAsync(reserva, cancellationToken).ConfigureAwait(false);
			tratadas++;
		}

		foreach (var pendencia in await reservas.ListarExclusoesPendentesAsync(relogio.Agora(), TamanhoDoLote, cancellationToken).ConfigureAwait(false))
		{
			await ExcluirAsync(pendencia, cancellationToken).ConfigureAwait(false);
			tratadas++;
		}

		return tratadas;
	}

	private async Task EspelharAsync(Reserva reserva, CancellationToken ct)
	{
		var eventoAnterior = reserva.EventoExternoId;
		var sala = await salas.GetAsync(reserva.SalaId, ct).ConfigureAwait(false);
		string? eventoCriado = null;

		if (sala is null)
		{
			reserva.MarcarFalhaDefinitiva("A sala da reserva não existe mais.");
		}
		else
		{
			var evento = MontadorDeEvento.Montar(reserva, sala);
			var resultado = await ChamarAsync(
				token => eventoAnterior is null
					? agenda.CriarAsync(evento, token)
					: agenda.AtualizarAsync(eventoAnterior, evento, token),
				ct).ConfigureAwait(false);

			switch (resultado.Desfecho)
			{
				case DesfechoDaAgenda.Feito when !string.IsNullOrEmpty(resultado.EventoId ?? eventoAnterior):
					var eventoId = resultado.EventoId ?? eventoAnterior!;
					eventoCriado = eventoAnterior is null ? eventoId : null;
					reserva.MarcarEspelhada(eventoId);
					break;

				case DesfechoDaAgenda.Feito:
					reserva.MarcarFalhaDefinitiva("A agenda não devolveu o identificador do evento.");
					break;

				case DesfechoDaAgenda.FalhaPassageira:
					reserva.RegistrarFalhaPassageira(resultado.Motivo ?? MotivoInesperado, relogio.Agora());
					break;

				default:
					reserva.MarcarFalhaDefinitiva(resultado.Motivo ?? MotivoInesperado);
					break;
			}
		}

		if (!await reservas.TentarGravarAsync(ct).ConfigureAwait(false))
		{
			await ResolverConflitoAsync(reserva, eventoCriado, ct).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// A reserva foi alterada ou cancelada entre a leitura e a gravação (a versão mudou), então o resultado
	/// da tentativa não pode mexer no estado: a reserva segue como está no banco. Só o evento criado na
	/// agenda não pode se perder: se a reserva ainda existe, o id dele é registrado (o próximo ciclo
	/// atualiza em vez de criar outro); se ela sumiu, o evento vira uma exclusão pendente.
	/// </summary>
	private async Task ResolverConflitoAsync(Reserva reserva, string? eventoCriado, CancellationToken ct)
	{
		for (var tentativa = 0; tentativa < MaximoDeConflitos; tentativa++)
		{
			var existe = await reservas.RecarregarAsync(reserva, ct).ConfigureAwait(false);

			if (!existe)
			{
				if (eventoCriado is not null)
				{
					await EnfileirarExclusaoAsync(eventoCriado, ct).ConfigureAwait(false);
				}

				return;
			}

			if (eventoCriado is null || eventoCriado == reserva.EventoExternoId)
			{
				return;
			}

			if (!string.IsNullOrEmpty(reserva.EventoExternoId))
			{
				// Outro evento já foi registrado para esta reserva: o que acabou de ser criado sobra.
				await EnfileirarExclusaoAsync(eventoCriado, ct).ConfigureAwait(false);

				return;
			}

			reserva.RegistrarEventoExterno(eventoCriado);

			if (await reservas.TentarGravarAsync(ct).ConfigureAwait(false))
			{
				return;
			}
		}

		LogConflitosDemais(logger, reserva.Id);
	}

	private async Task EnfileirarExclusaoAsync(string eventoId, CancellationToken ct)
	{
		await reservas.AdicionarExclusaoPendenteAsync(new ExclusaoDeEventoPendente(eventoId), ct).ConfigureAwait(false);
		await reservas.SaveChangesAsync(ct).ConfigureAwait(false);
	}

	private async Task ExcluirAsync(ExclusaoDeEventoPendente pendencia, CancellationToken ct)
	{
		var resultado = await ChamarAsync(token => agenda.ApagarAsync(pendencia.EventoExternoId, token), ct).ConfigureAwait(false);

		switch (resultado.Desfecho)
		{
			case DesfechoDaAgenda.Feito:
				reservas.RemoverExclusaoPendente(pendencia);
				break;

			case DesfechoDaAgenda.FalhaPassageira:
				pendencia.RegistrarFalhaPassageira(resultado.Motivo ?? MotivoInesperado, relogio.Agora());
				break;

			default:
				pendencia.MarcarFalhaDefinitiva(resultado.Motivo ?? MotivoInesperado);
				break;
		}

		await reservas.SaveChangesAsync(ct).ConfigureAwait(false);
	}

	// Uma exceção da agenda vira falha passageira com texto genérico: a mensagem dela pode trazer
	// dado que não deve ir para a tela nem para o banco (o detalhe vai só para o log).
	private async Task<ResultadoDaAgenda> ChamarAsync(Func<CancellationToken, Task<ResultadoDaAgenda>> chamada, CancellationToken ct)
	{
		try
		{
			return await chamada(ct).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			LogFalhaDaAgenda(logger, exception);

			return new ResultadoDaAgenda(DesfechoDaAgenda.FalhaPassageira, Motivo: MotivoInesperado);
		}
	}

	[LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "A agenda externa lançou uma exceção; a tentativa conta como falha passageira.")]
	private static partial void LogFalhaDaAgenda(ILogger logger, Exception exception);

	[LoggerMessage(EventId = 2, Level = LogLevel.Warning,
		Message = "A reserva {ReservaId} mudou várias vezes durante o envio; o id do evento criado não foi registrado (a chave de idempotência evita o duplicado).")]
	private static partial void LogConflitosDemais(ILogger logger, Guid reservaId);
}
