using System.Text.Json;

namespace Secco.Intranet.Application.Documentos;

/// <summary>
/// Monta o payload da trilha dos verbos de documento: o dono entra como <c>setor</c> ou como
/// <c>biblioteca</c> (nunca os dois), na posição em que o payload de setor sempre o levou.
/// </summary>
internal static class TrilhaDeDono
{
	/// <summary>Serializa os campos, com o slug do dono entre os de <paramref name="antes"/> e os de <paramref name="depois"/>.</summary>
	/// <param name="dono">Dono do documento.</param>
	/// <param name="antes">Campos que vêm antes do dono.</param>
	/// <param name="depois">Campos que vêm depois do dono.</param>
	public static string Serializar(
		ReferenciaDeDono dono,
		IReadOnlyList<(string Chave, object? Valor)> antes,
		IReadOnlyList<(string Chave, object? Valor)> depois)
	{
		var payload = new Dictionary<string, object?>();

		foreach (var (chave, valor) in antes)
		{
			payload[chave] = valor;
		}

		payload[dono.Tipo == TipoDeDono.Biblioteca ? "biblioteca" : "setor"] = dono.Slug;

		foreach (var (chave, valor) in depois)
		{
			payload[chave] = valor;
		}

		return JsonSerializer.Serialize(payload);
	}
}
