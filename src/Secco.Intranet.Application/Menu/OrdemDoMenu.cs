namespace Secco.Intranet.Application.Menu;

/// <summary>Regra única de ordenação do menu: posição salva primeiro, o resto na ordem padrão.</summary>
public static class OrdemDoMenu
{
	/// <summary>
	/// Ordena os itens pelas posições salvas. Quem tem posição vem primeiro, por posição; quem não tem vem depois,
	/// na ordem de entrada (ordenação estável). Posição sem item correspondente não tem efeito.
	/// </summary>
	/// <typeparam name="T">Tipo do item.</typeparam>
	/// <param name="naOrdemPadrao">Itens na ordem padrão.</param>
	/// <param name="chave">Extrai a chave de posição do item (comparada sem diferenciar caixa).</param>
	/// <param name="posicoes">Posições salvas por chave; <c>null</c> mantém a ordem padrão.</param>
	public static IReadOnlyList<T> Ordenar<T>(
		IReadOnlyList<T> naOrdemPadrao, Func<T, string> chave, IReadOnlyDictionary<string, int>? posicoes)
	{
		if (posicoes is null || posicoes.Count == 0)
		{
			return naOrdemPadrao;
		}

		// Reindexa sem diferenciar caixa, qualquer que seja o comparador do dicionário recebido.
		var porChave = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		foreach (var (k, v) in posicoes)
		{
			porChave.TryAdd(k, v);
		}

		return naOrdemPadrao
			.Select((item, indice) => (Item: item, Indice: indice, Tem: porChave.TryGetValue(chave(item), out var pos), Pos: pos))
			.OrderBy(x => x.Tem ? 0 : 1)
			.ThenBy(x => x.Tem ? x.Pos : 0)
			.ThenBy(x => x.Indice)
			.Select(x => x.Item)
			.ToList();
	}
}
