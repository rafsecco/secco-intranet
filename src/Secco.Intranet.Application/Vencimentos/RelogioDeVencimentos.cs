namespace Secco.Intranet.Application.Vencimentos;

/// <summary>
/// Relógio do produto para vencimentos: "hoje" é a data no fuso configurado, não a data UTC —
/// às 23h de São Paulo já é dia seguinte em UTC, e o aviso do dia não pode antecipar.
/// </summary>
/// <param name="relogio">Fonte de tempo (substituível em teste).</param>
/// <param name="opcoes">Configuração, de onde sai o fuso.</param>
public sealed class RelogioDeVencimentos(TimeProvider relogio, VencimentosOptions opcoes)
{
	private readonly TimeProvider _relogio = relogio ?? throw new ArgumentNullException(nameof(relogio));

	// Resolvido uma vez: fuso inválido é erro de configuração e deve falhar na partida.
	private readonly TimeZoneInfo _fuso = TimeZoneInfo.FindSystemTimeZoneById(
		(opcoes ?? throw new ArgumentNullException(nameof(opcoes))).FusoHorario);

	/// <summary>Data corrente no fuso configurado.</summary>
	public DateOnly Hoje() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_relogio.GetUtcNow(), _fuso).DateTime);

	/// <summary>Instante corrente.</summary>
	public DateTimeOffset Agora() => _relogio.GetUtcNow();
}
