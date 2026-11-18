using Secco.Intranet.Application.Salas;

namespace Secco.Intranet.Web.Models.Salas;

/// <summary>Um passo de 15 minutos de uma sala; quando abre uma reserva, ocupa <paramref name="Linhas"/> linhas.</summary>
/// <param name="Hora">Início do passo.</param>
/// <param name="Reserva">Reserva que começa aqui, se houver.</param>
/// <param name="Linhas">Duração em passos de 15 minutos quando abre uma reserva; senão 1.</param>
/// <param name="Livre">Se dá para reservar a partir daqui (livre e ainda não passou).</param>
public sealed record CelulaDaGrade(TimeOnly Hora, ReservaDto? Reserva, int Linhas, bool Livre);

/// <summary>Coluna de uma sala; as células cobertas por uma reserva não entram.</summary>
/// <param name="Sala">Sala da coluna.</param>
/// <param name="Celulas">Células na ordem do dia.</param>
public sealed record ColunaDaGrade(SalaDto Sala, IReadOnlyList<CelulaDaGrade> Celulas);

/// <summary>Grade do dia: uma coluna por sala ativa, uma linha por passo de 15 minutos.</summary>
public sealed class GradeDoDiaViewModel
{
	/// <summary>Minutos de cada linha da grade.</summary>
	public const int Passo = 15;

	private const int AberturaPadrao = 7 * 60;

	private const int FechamentoPadrao = 20 * 60;

	private const int FimDoDia = 24 * 60;

	/// <summary>Dia mostrado.</summary>
	public DateOnly Dia { get; init; }

	/// <summary>Primeira hora mostrada.</summary>
	public TimeOnly Abre { get; init; }

	/// <summary>Hora em que a grade termina (23:59 quando vai até a meia-noite).</summary>
	public TimeOnly Fecha { get; init; }

	/// <summary>Colunas, na ordem das salas.</summary>
	public IReadOnlyList<ColunaDaGrade> Colunas { get; init; } = [];

	/// <summary>Sala cuja coluna aparece em telas estreitas.</summary>
	public Guid? SalaEmFoco { get; init; }

	/// <summary>Se quem vê é administrador, para oferecer o atalho do cadastro no estado vazio.</summary>
	public bool PodeGerenciarSalas { get; set; }

	/// <summary>Monta a grade a partir da ocupação do dia.</summary>
	/// <param name="ocupacao">Salas ativas e reservas do dia.</param>
	/// <param name="hoje">Data corrente local.</param>
	/// <param name="agora">Hora corrente local.</param>
	/// <param name="salaEmFoco">Sala pedida na URL; inexistente ou nula cai na primeira.</param>
	public static GradeDoDiaViewModel Montar(OcupacaoDoDiaDto ocupacao, DateOnly hoje, TimeOnly agora, Guid? salaEmFoco)
	{
		ArgumentNullException.ThrowIfNull(ocupacao);

		var abre = AberturaPadrao;
		var fecha = FechamentoPadrao;

		if (ocupacao.Reservas.Count > 0)
		{
			abre = Math.Min(abre, Minutos(ocupacao.Reservas.Min(reserva => reserva.Inicio)) / 60 * 60);
			fecha = Math.Max(fecha, (Minutos(ocupacao.Reservas.Max(reserva => reserva.Fim)) + 59) / 60 * 60);
		}

		var colunas = new List<ColunaDaGrade>();

		foreach (var sala in ocupacao.Salas)
		{
			var daSala = ocupacao.Reservas.Where(reserva => reserva.SalaId == sala.Id).ToList();
			var celulas = new List<CelulaDaGrade>();

			for (var minuto = abre; minuto < fecha;)
			{
				var hora = Hora(minuto);
				var reserva = daSala.FirstOrDefault(r => Minutos(r.Inicio) <= minuto && minuto < Minutos(r.Fim));

				if (reserva is null)
				{
					var livre = ocupacao.Dia > hoje || (ocupacao.Dia == hoje && hora >= agora);
					celulas.Add(new CelulaDaGrade(hora, null, 1, livre));
					minuto += Passo;

					continue;
				}

				var linhas = Math.Max(1, (Minutos(reserva.Fim) - minuto + Passo - 1) / Passo);
				celulas.Add(new CelulaDaGrade(hora, reserva, linhas, false));
				minuto += linhas * Passo;
			}

			colunas.Add(new ColunaDaGrade(sala, celulas));
		}

		var foco = colunas.Any(coluna => coluna.Sala.Id == salaEmFoco) ? salaEmFoco : colunas.FirstOrDefault()?.Sala.Id;

		return new GradeDoDiaViewModel
		{
			Dia = ocupacao.Dia,
			Abre = Hora(abre),
			Fecha = Hora(fecha),
			Colunas = colunas,
			SalaEmFoco = foco,
		};
	}

	private static int Minutos(TimeOnly hora) => hora.Hour * 60 + hora.Minute;

	private static TimeOnly Hora(int minutos) =>
		minutos >= FimDoDia ? new TimeOnly(23, 59) : new TimeOnly(minutos / 60, minutos % 60);
}
