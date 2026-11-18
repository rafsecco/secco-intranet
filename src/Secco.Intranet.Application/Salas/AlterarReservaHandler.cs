using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Salas;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Altera horário, sala, título, descrição e convidados de uma reserva que ainda não começou.</summary>
public sealed class AlterarReservaHandler(
	IReservaRepository reservas,
	ISalaRepository salas,
	IUsuariosParaDiretorio diretorio,
	RelogioDeSalas relogio,
	IEspelhamentoLigado espelhamento,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de alteração.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(AlterarReservaCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var dados = ApoioDeReservas.Validar(relogio, command.Dia, command.Inicio, command.Fim, command.Titulo, command.Descricao);

		if (dados.IsFailure)
		{
			return Result.Failure(dados.Error);
		}

		var reserva = await reservas.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (reserva is null || (reserva.ReservadoPorId != command.Quem.UsuarioId && !command.Quem.EhAdministrador))
		{
			return Result.Failure(IntranetErrors.Salas.ReservaNotFound);
		}

		if (reserva.JaComecou(relogio.Agora()))
		{
			return Result.Failure(IntranetErrors.Salas.JaComecou);
		}

		// O dono continua sendo quem reservou, mesmo quando um administrador altera.
		var convidadosIds = ApoioDeReservas.NormalizarConvidados(command.Convidados, reserva.ReservadoPorId);

		if (convidadosIds.Count > Reserva.MaximoDeConvidados)
		{
			return Result.Failure(IntranetErrors.Salas.ConvidadosDemais(Reserva.MaximoDeConvidados));
		}

		var sala = await salas.GetAsync(command.SalaId, cancellationToken).ConfigureAwait(false);

		if (sala is not { Ativa: true })
		{
			return Result.Failure(IntranetErrors.Salas.NotFound);
		}

		var ativos = await ApoioDeReservas.CarregarAtivosAsync(diretorio, cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure(ativos.Error);
		}

		var convidados = ApoioDeReservas.MontarConvidados(convidadosIds, ativos.Value);

		if (convidados is null)
		{
			return Result.Failure(IntranetErrors.Salas.ConvidadoInvalido);
		}

		var resultado = await reservas
			.ComSalaTravadaAsync(
				sala.Id,
				async ct =>
				{
					var existente = await reservas
						.PrimeiraSobrepostaAsync(sala.Id, dados.Value.Inicio, dados.Value.Fim, reserva.Id, ct)
						.ConfigureAwait(false);

					if (existente is not null)
					{
						return Result.Failure(ApoioDeReservas.Conflito(existente, relogio));
					}

					reserva.Alterar(
						sala.Id,
						dados.Value.Inicio,
						dados.Value.Fim,
						dados.Value.Titulo,
						dados.Value.Descricao,
						convidados,
						espelhamento.Ligado);

					await reservas.SaveChangesAsync(ct).ConfigureAwait(false);

					return Result.Success();
				},
				cancellationToken)
			.ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			return resultado;
		}

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.ReservaAlterar, RecursosDeAuditoria.Reserva, reserva.Id.ToString(), ApoioDeReservas.Metadata(reserva)),
				cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
