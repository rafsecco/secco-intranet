using Secco.Intranet.Domain.Salas;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Devolve à fila do espelhamento uma reserva que falhou; só a administração pode pedir.</summary>
public sealed class TentarEspelharDeNovoHandler(IReservaRepository reservas)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Reserva cujo espelhamento falhou.</param>
	/// <param name="quem">Quem pede.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, QuemPede quem, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(quem);

		if (!quem.EhAdministrador)
		{
			return Result.Failure(IntranetErrors.Salas.ReservaNotFound);
		}

		var reserva = await reservas.GetAsync(id, cancellationToken).ConfigureAwait(false);

		if (reserva is null)
		{
			return Result.Failure(IntranetErrors.Salas.ReservaNotFound);
		}

		if (reserva.Espelhamento != EstadoDoEspelhamento.Falhou)
		{
			return Result.Failure(IntranetErrors.Salas.EspelhamentoNaoFalhou);
		}

		reserva.TentarDeNovo();

		await reservas.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
