using System.Globalization;
using System.Text;
using Secco.Intranet.Application.Setores;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>
/// Casa o departamento do Entra com um setor da intranet pelo nome. Função pura: ignora caixa,
/// acentos e espaços nas pontas; só setor ativo conta e só um casamento único vale.
/// </summary>
public static class CasadorDeDepartamento
{
	/// <summary>Normaliza um nome: aparado, sem acentos (marcas de combinação) e em minúsculas.</summary>
	public static string Normalizar(string nome)
	{
		ArgumentNullException.ThrowIfNull(nome);

		var decomposto = nome.Trim().Normalize(NormalizationForm.FormD);
		var sb = new StringBuilder(decomposto.Length);

		foreach (var c in decomposto)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				sb.Append(c);
			}
		}

		return sb.ToString().ToLowerInvariant();
	}

	/// <summary>O único setor ativo cujo nome normalizado é igual ao departamento; nenhum ou mais de um, nulo.</summary>
	public static SetorDto? Casar(string? departamento, IReadOnlyList<SetorDto> setores)
	{
		var candidatos = Candidatos(departamento, setores);

		return candidatos.Count == 1 ? candidatos[0] : null;
	}

	/// <summary>Se mais de um setor ativo casa com o departamento.</summary>
	public static bool EhAmbiguo(string departamento, IReadOnlyList<SetorDto> setores) =>
		Candidatos(departamento, setores).Count > 1;

	private static List<SetorDto> Candidatos(string? departamento, IReadOnlyList<SetorDto> setores)
	{
		ArgumentNullException.ThrowIfNull(setores);

		if (string.IsNullOrWhiteSpace(departamento))
		{
			return [];
		}

		var alvo = Normalizar(departamento);

		return [.. setores.Where(setor => setor.Ativo && string.Equals(Normalizar(setor.Nome), alvo, StringComparison.Ordinal))];
	}
}
