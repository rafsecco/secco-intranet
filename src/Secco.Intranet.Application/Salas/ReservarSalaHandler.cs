using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Salas;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Reserva uma sala, sem deixar dois horários sobrepostos na mesma sala.</summary>
public sealed class ReservarSalaHandler(
	IReservaRepository reservas,
	ISalaRepository salas,
	IUsuariosParaDiretorio diretorio,
	RelogioDeSalas relogio,
	IEspelhamentoLigado espelhamento,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de reserva.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>O id da reserva criada.</returns>
	public async Task<Result<Guid>> HandleAsync(ReservarSalaCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var dados = ApoioDeReservas.Validar(relogio, command.Dia, command.Inicio, command.Fim, command.Titulo, command.Descricao);

		if (dados.IsFailure)
		{
			return dados.Error;
		}

		var convidadosIds = ApoioDeReservas.NormalizarConvidados(command.Convidados, command.Quem.UsuarioId);

		if (convidadosIds.Count > Reserva.MaximoDeConvidados)
		{
			return IntranetErrors.Salas.ConvidadosDemais(Reserva.MaximoDeConvidados);
		}

		var sala = await salas.GetAsync(command.SalaId, cancellationToken).ConfigureAwait(false);

		if (sala is not { Ativa: true })
		{
			return IntranetErrors.Salas.NotFound;
		}

		var ativos = await ApoioDeReservas.CarregarAtivosAsync(diretorio, cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return ativos.Error;
		}

		var convidados = ApoioDeReservas.MontarConvidados(convidadosIds, ativos.Value);

		if (convidados is null)
		{
			return IntranetErrors.Salas.ConvidadoInvalido;
		}

		var dono = ativos.Value.GetValueOrDefault(command.Quem.UsuarioId);
		var reserva = new Reserva(
			sala.Id,
			dados.Value.Inicio,
			dados.Value.Fim,
			dados.Value.Titulo,
			dados.Value.Descricao,
			command.Quem.UsuarioId,
			dono is null ? ApoioDeReservas.NomeDoVisitante : ApoioDeReservas.Nome(dono),
			dono?.Email,
			convidados,
			espelhamento.Ligado);

		var resultado = await reservas
			.ComSalaTravadaAsync(
				sala.Id,
				async ct =>
				{
					var existente = await reservas
						.PrimeiraSobrepostaAsync(sala.Id, reserva.Inicio, reserva.Fim, null, ct)
						.ConfigureAwait(false);

					if (existente is not null)
					{
						return Result.Failure(ApoioDeReservas.Conflito(existente, relogio));
					}

					await reservas.AddAsync(reserva, ct).ConfigureAwait(false);

					return Result.Success();
				},
				cancellationToken)
			.ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			return resultado.Error;
		}

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.ReservaCriar, RecursosDeAuditoria.Reserva, reserva.Id.ToString(), ApoioDeReservas.Metadata(reserva)),
				cancellationToken)
			.ConfigureAwait(false);

		return reserva.Id;
	}
}
