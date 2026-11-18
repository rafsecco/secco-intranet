using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Obtém uma reserva; a agenda é compartilhada, então qualquer pessoa autenticada a enxerga.</summary>
public sealed class ObterReservaHandler(IReservaRepository reservas, ISalaRepository salas, RelogioDeSalas relogio)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Reserva.</param>
	/// <param name="quem">Quem pede, para decidir <c>PodeAlterar</c>.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ReservaDto>> HandleAsync(Guid id, QuemPede quem, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(quem);

		var reserva = await reservas.GetAsync(id, cancellationToken).ConfigureAwait(false);

		if (reserva is null)
		{
			return IntranetErrors.Salas.ReservaNotFound;
		}

		var sala = await salas.GetAsync(reserva.SalaId, cancellationToken).ConfigureAwait(false);

		return ApoioDeReservas.Mapear(reserva, sala?.Nome ?? string.Empty, relogio, quem);
	}
}
