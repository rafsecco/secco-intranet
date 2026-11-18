namespace Secco.Intranet.Application.Salas;

/// <summary>Lista as reservas futuras em que a pessoa é dona ou convidada.</summary>
public sealed class ListarMinhasReservasHandler(IReservaRepository reservas, ISalaRepository salas, RelogioDeSalas relogio)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="quem">Quem pede.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlyList<ReservaDto>> HandleAsync(QuemPede quem, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(quem);

		var minhas = await reservas
			.ListarFuturasDaPessoaAsync(quem.UsuarioId, relogio.Agora(), cancellationToken)
			.ConfigureAwait(false);
		var todas = await salas.ListarAsync(false, cancellationToken).ConfigureAwait(false);
		var nomes = todas.ToDictionary(sala => sala.Id, sala => sala.Nome);

		return
		[
			.. minhas.Select(reserva =>
				ApoioDeReservas.Mapear(reserva, nomes.GetValueOrDefault(reserva.SalaId, string.Empty), relogio, quem)),
		];
	}
}
