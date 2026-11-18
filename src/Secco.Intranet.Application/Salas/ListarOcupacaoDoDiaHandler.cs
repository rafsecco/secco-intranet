namespace Secco.Intranet.Application.Salas;

/// <summary>Lista as salas ativas e as reservas delas num dia local.</summary>
public sealed class ListarOcupacaoDoDiaHandler(IReservaRepository reservas, ISalaRepository salas, RelogioDeSalas relogio)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="dia">Dia local.</param>
	/// <param name="quem">Quem pede, para decidir <c>PodeAlterar</c>.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<OcupacaoDoDiaDto> HandleAsync(DateOnly dia, QuemPede quem, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(quem);

		var ativas = await salas.ListarAsync(true, cancellationToken).ConfigureAwait(false);
		var (de, ate) = relogio.LimitesDoDia(dia);
		var doDia = await reservas.ListarNoIntervaloAsync(de, ate, cancellationToken).ConfigureAwait(false);
		var nomes = ativas.ToDictionary(sala => sala.Id, sala => sala.Nome);

		return new OcupacaoDoDiaDto(
			dia,
			[.. ativas.Select(SalaDto.De)],
			[
				.. doDia
					.Where(reserva => nomes.ContainsKey(reserva.SalaId))
					.Select(reserva => ApoioDeReservas.Mapear(reserva, nomes[reserva.SalaId], relogio, quem)),
			]);
	}
}
