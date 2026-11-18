using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Exclui uma sala sem reserva futura, junto com as reservas passadas dela.</summary>
public sealed class ExcluirSalaHandler(ISalaRepository repository, ITrilhaDeAuditoria trilha, TimeProvider relogio)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Sala a excluir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var sala = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);

		if (sala is null)
		{
			return Result.Failure(IntranetErrors.Salas.NotFound);
		}

		if (await repository.TemReservaFuturaAsync(sala.Id, relogio.GetUtcNow(), cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(IntranetErrors.Salas.TemReservaFutura);
		}

		var metadata = ApoioDeSalas.Metadata(sala);

		await repository.RemoverReservasDaSalaAsync(sala.Id, cancellationToken).ConfigureAwait(false);
		repository.Remover(sala);
		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(VerbosDeAuditoria.SalaExcluir, RecursosDeAuditoria.Sala, sala.Id.ToString(), metadata),
				cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
