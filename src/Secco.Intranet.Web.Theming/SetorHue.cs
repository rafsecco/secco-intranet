namespace Secco.Intranet.Web.Theming;

/// <summary>
/// Deriva um matiz estável a partir do slug de um setor. É o elemento de assinatura do
/// sistema de temas: como todo conteúdo da Intranet pertence a um setor (ADR-0001), a cor
/// que acompanha um card ou um item de menu carrega informação — de quem é o conteúdo — em
/// vez de decorar.
/// </summary>
/// <remarks>
/// Os matizes são curados e espaçados no círculo cromático; saturação e luminosidade ficam
/// a cargo do tema, que as fixa por modo claro/escuro para o contraste não escapar. O hash é
/// FNV-1a, e não <see cref="object.GetHashCode"/>, porque o do runtime é aleatorizado por
/// processo — a cor de um setor mudaria a cada reinício.
/// </remarks>
public static class SetorHue
{
	private const uint FnvOffsetBasis = 2_166_136_261;
	private const uint FnvPrime = 16_777_619;

	private static readonly int[] Hues = [152, 199, 221, 265, 291, 340, 18, 41];

	/// <summary>Matiz (0–360) do setor informado.</summary>
	/// <param name="slug">Slug do setor. Nulo ou vazio devolve o primeiro matiz.</param>
	public static int From(string? slug)
	{
		if (string.IsNullOrWhiteSpace(slug))
		{
			return Hues[0];
		}

		var hash = FnvOffsetBasis;

		foreach (var character in slug.Trim().ToLowerInvariant())
		{
			hash ^= character;
			hash *= FnvPrime;
		}

		return Hues[(int)(hash % (uint)Hues.Length)];
	}
}
