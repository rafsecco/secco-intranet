using Secco.Intranet.Domain.Salas;

namespace Secco.Intranet.Application.Salas.Agenda;

/// <summary>Transforma uma reserva no evento da agenda externa.</summary>
public static class MontadorDeEvento
{
	/// <summary>Monta o evento da reserva; a chave de idempotência é o id dela, a mesma em toda tentativa.</summary>
	/// <param name="reserva">Reserva, com os convidados.</param>
	/// <param name="sala">Sala da reserva.</param>
	public static EventoDeAgenda Montar(Reserva reserva, Sala sala)
	{
		ArgumentNullException.ThrowIfNull(reserva);
		ArgumentNullException.ThrowIfNull(sala);

		var corpo = $"Reservado por {reserva.ReservadoPorNome}";

		if (!string.IsNullOrWhiteSpace(reserva.Descricao))
		{
			corpo += $"\n\n{reserva.Descricao}";
		}

		return new EventoDeAgenda(
			reserva.Id.ToString(),
			reserva.Titulo,
			corpo,
			sala.Nome,
			reserva.Inicio,
			reserva.Fim,
			Participantes(reserva),
			sala.EmailNaAgenda);
	}

	// O dono vem primeiro; quem não tem e-mail não recebe convite e e-mail repetido entra uma vez só.
	private static List<ParticipanteDeAgenda> Participantes(Reserva reserva)
	{
		var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var participantes = new List<ParticipanteDeAgenda>();

		void Incluir(string nome, string? email)
		{
			if (!string.IsNullOrWhiteSpace(email) && vistos.Add(email.Trim()))
			{
				participantes.Add(new ParticipanteDeAgenda(nome, email.Trim()));
			}
		}

		Incluir(reserva.ReservadoPorNome, reserva.ReservadoPorEmail);

		foreach (var convidado in reserva.Convidados)
		{
			Incluir(convidado.Nome, convidado.Email);
		}

		return participantes;
	}
}
