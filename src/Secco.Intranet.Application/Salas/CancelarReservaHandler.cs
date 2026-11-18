using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Salas;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Cancela uma reserva que ainda não começou; se ela já tinha evento externo, deixa a exclusão dele pendente.</summary>
public sealed class CancelarReservaHandler(IReservaRepository reservas, RelogioDeSalas relogio, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Reserva a cancelar.</param>
	/// <param name="quem">Quem pede.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, QuemPede quem, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(quem);

		var reserva = await reservas.GetAsync(id, cancellationToken).ConfigureAwait(false);

		if (reserva is null || (reserva.ReservadoPorId != quem.UsuarioId && !quem.EhAdministrador))
		{
			return Result.Failure(IntranetErrors.Salas.ReservaNotFound);
		}

		if (reserva.JaComecou(relogio.Agora()))
		{
			return Result.Failure(IntranetErrors.Salas.JaComecou);
		}

		var metadata = ApoioDeReservas.Metadata(reserva);

		reservas.Remover(reserva);

		if (!string.IsNullOrEmpty(reserva.EventoExternoId))
		{
			await reservas
				.AdicionarExclusaoPendenteAsync(new ExclusaoDeEventoPendente(reserva.EventoExternoId), cancellationToken)
				.ConfigureAwait(false);
		}

		await reservas.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.ReservaCancelar, RecursosDeAuditoria.Reserva, reserva.Id.ToString(), metadata),
				cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
