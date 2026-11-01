using System.Text.RegularExpressions;

namespace Secco.Intranet.Domain;

/// <summary>
/// Ícone do Bootstrap Icons informado por quem administra. O valor vai direto para o atributo
/// <c>class</c> do menu, então só sai daqui no formato <c>bi-nome</c>: sem a guarda, qualquer
/// texto viraria classe CSS arbitrária, e um <c>d-none</c> digitado sumiria com o próprio item.
/// Aceita as formas que o site do Bootstrap Icons mostra — o nome (<c>person-lines-fill</c>), a
/// classe (<c>bi bi-person-lines-fill</c>) e o trecho <c>&lt;i class="…"&gt;</c> — e devolve sempre
/// <c>bi-person-lines-fill</c>.
/// </summary>
public static partial class IconeBootstrap
{
	/// <summary>Tenta reduzir o texto informado ao formato <c>bi-nome</c>.</summary>
	/// <param name="texto">O que a pessoa digitou ou colou.</param>
	/// <param name="icone">O ícone normalizado; <c>null</c> quando o texto está em branco.</param>
	/// <returns><c>false</c> só quando o texto não está em branco e não é um ícone reconhecível.</returns>
	public static bool TentarNormalizar(string? texto, out string? icone)
	{
		icone = null;

		if (string.IsNullOrWhiteSpace(texto))
		{
			return true;
		}

		var valor = texto.Trim().ToLowerInvariant();

		// Trecho <i class="bi bi-nome"></i> copiado do site: vale o que está dentro do class.
		if (AtributoClass().Match(valor) is { Success: true } atributo)
		{
			valor = atributo.Groups["classes"].Value.Trim();
		}

		var partes = valor.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Where(parte => parte != "bi")
			.ToList();

		if (partes.Count != 1)
		{
			return false;
		}

		var nome = partes[0].StartsWith("bi-", StringComparison.Ordinal) ? partes[0] : "bi-" + partes[0];

		if (!Formato().IsMatch(nome))
		{
			return false;
		}

		icone = nome;

		return true;
	}

	[GeneratedRegex("""class\s*=\s*["'](?<classes>[^"']*)["']""", RegexOptions.CultureInvariant)]
	private static partial Regex AtributoClass();

	[GeneratedRegex("^bi-[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
	private static partial Regex Formato();
}
