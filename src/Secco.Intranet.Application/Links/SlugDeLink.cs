using System.Globalization;
using System.Text;

namespace Secco.Intranet.Application.Links;

/// <summary>Gera o slug de um link ou grupo a partir do nome, no formato do menu.</summary>
internal static class SlugDeLink
{
	private const int Limite = 120;

	/// <summary>Minúsculas, sem acento, só <c>[a-z0-9]</c> separados por hífen; vazio vira <c>link</c>; repetido ganha <c>-2</c>, <c>-3</c>...</summary>
	/// <param name="nome">Nome informado.</param>
	/// <param name="usadosEntreIrmaos">Slugs já usados pelos irmãos do destino.</param>
	public static string Gerar(string nome, IEnumerable<string> usadosEntreIrmaos)
	{
		ArgumentNullException.ThrowIfNull(nome);
		ArgumentNullException.ThrowIfNull(usadosEntreIrmaos);

		var baseDoSlug = Normalizar(nome);
		var usados = usadosEntreIrmaos.ToHashSet(StringComparer.Ordinal);

		if (!usados.Contains(baseDoSlug))
		{
			return baseDoSlug;
		}

		for (var n = 2; ; n++)
		{
			var candidato = $"{baseDoSlug}-{n}";

			if (!usados.Contains(candidato))
			{
				return candidato;
			}
		}
	}

	private static string Normalizar(string nome)
	{
		var sb = new StringBuilder();
		var hifenPendente = false;

		foreach (var c in nome.Normalize(NormalizationForm.FormD))
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
			{
				continue;
			}

			var minuscula = char.ToLowerInvariant(c);

			if (minuscula is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
			{
				if (hifenPendente && sb.Length > 0)
				{
					sb.Append('-');
				}

				hifenPendente = false;
				sb.Append(minuscula);
			}
			else
			{
				hifenPendente = true;
			}
		}

		var slug = sb.Length > Limite ? sb.ToString(0, Limite).TrimEnd('-') : sb.ToString();

		return slug.Length == 0 ? "link" : slug;
	}
}
