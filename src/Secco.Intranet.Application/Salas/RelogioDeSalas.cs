namespace Secco.Intranet.Application.Salas;

/// <summary>
/// Relógio das reservas: os horários que a pessoa digita são do fuso configurado, e o banco
/// só recebe instantes em UTC (offset zero).
/// </summary>
/// <param name="relogio">Fonte de tempo (substituível em teste).</param>
/// <param name="opcoes">Configuração, de onde sai o fuso.</param>
public sealed class RelogioDeSalas(TimeProvider relogio, SalasOptions opcoes)
{
	private readonly TimeProvider _relogio = relogio ?? throw new ArgumentNullException(nameof(relogio));

	// Resolvido uma vez: fuso inválido é erro de configuração e deve falhar na partida.
	private readonly TimeZoneInfo _fuso = TimeZoneInfo.FindSystemTimeZoneById(
		(opcoes ?? throw new ArgumentNullException(nameof(opcoes))).FusoHorario);

	/// <summary>Instante corrente, em UTC.</summary>
	public DateTimeOffset Agora() => _relogio.GetUtcNow();

	/// <summary>Data corrente no fuso configurado.</summary>
	public DateOnly Hoje() => ParaLocal(Agora()).Dia;

	/// <summary>
	/// Converte data e hora locais em instante UTC (offset zero); <c>null</c> quando a hora local
	/// não existe (salto do horário de verão) ou é ambígua (repetida na volta).
	/// </summary>
	public DateTimeOffset? ParaUtc(DateOnly dia, TimeOnly hora)
	{
		var local = dia.ToDateTime(hora, DateTimeKind.Unspecified);

		if (_fuso.IsInvalidTime(local) || _fuso.IsAmbiguousTime(local))
		{
			return null;
		}

		return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _fuso), TimeSpan.Zero);
	}

	/// <summary>Data e hora locais de um instante.</summary>
	public (DateOnly Dia, TimeOnly Hora) ParaLocal(DateTimeOffset instante)
	{
		var local = TimeZoneInfo.ConvertTime(instante, _fuso).DateTime;

		return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
	}

	/// <summary>Início (inclusive) e fim (exclusive) do dia local, em UTC.</summary>
	public (DateTimeOffset De, DateTimeOffset Ate) LimitesDoDia(DateOnly dia) =>
		(InicioDoDia(dia), InicioDoDia(dia.AddDays(1)));

	private DateTimeOffset InicioDoDia(DateOnly dia)
	{
		var local = dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

		// Há fusos em que a meia-noite não existe no salto do horário de verão: vale a primeira hora que existe.
		while (_fuso.IsInvalidTime(local))
		{
			local = local.AddMinutes(15);
		}

		return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _fuso), TimeSpan.Zero);
	}
}
