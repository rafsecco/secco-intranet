namespace Secco.Intranet.Application.Diretorio;

/// <summary>Regras da relação "reporta a" que não cabem na entidade (dependem do conjunto todo).</summary>
public static class RegrasDeGestor
{
	/// <summary>Teto de profundidade do organograma — defesa contra dado corrompido, não regra de negócio.</summary>
	public const int ProfundidadeMaxima = 20;

	/// <summary>
	/// Indica se fazer <paramref name="usuarioId"/> reportar a <paramref name="novoGestorId"/> fecharia
	/// um ciclo: sobe a cadeia de gestores a partir do novo gestor e vê se chega ao próprio usuário.
	/// Um ciclo já gravado que <b>não</b> envolve o usuário não trava a regra — a subida termina ao
	/// repetir um nó.
	/// </summary>
	/// <param name="gestorPorUsuario">Mapa atual usuário → gestor (só quem tem gestor).</param>
	/// <param name="usuarioId">Quem está mudando de gestor.</param>
	/// <param name="novoGestorId">O gestor proposto.</param>
	public static bool CriariaCiclo(IReadOnlyDictionary<Guid, Guid> gestorPorUsuario, Guid usuarioId, Guid novoGestorId)
	{
		ArgumentNullException.ThrowIfNull(gestorPorUsuario);

		var visitados = new HashSet<Guid>();
		var atual = novoGestorId;

		while (visitados.Add(atual))
		{
			if (!gestorPorUsuario.TryGetValue(atual, out var proximo))
			{
				return false;
			}

			if (proximo == usuarioId)
			{
				return true;
			}

			atual = proximo;
		}

		return false;
	}
}
