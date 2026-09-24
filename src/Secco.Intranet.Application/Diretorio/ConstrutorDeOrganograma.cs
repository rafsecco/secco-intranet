namespace Secco.Intranet.Application.Diretorio;

/// <summary>Um nó do organograma: uma pessoa e a equipe direta dela.</summary>
/// <param name="Pessoa">A pessoa.</param>
/// <param name="Equipe">Quem reporta diretamente, por nome.</param>
public sealed record NoDoOrganograma(PessoaDto Pessoa, IReadOnlyList<NoDoOrganograma> Equipe);

/// <summary>O organograma: as árvores e quem ficou fora delas.</summary>
/// <param name="Raizes">Quem tem equipe e não tem gestor ativo.</param>
/// <param name="SemPosicao">Quem não tem gestor ativo nem equipe — e qualquer um que a árvore não alcançou.</param>
public sealed record OrganogramaDto(IReadOnlyList<NoDoOrganograma> Raizes, IReadOnlyList<PessoaDto> SemPosicao);

/// <summary>
/// Monta a árvore por gestor. Função pura. Duas defesas para dado corrompido no banco: teto de
/// profundidade e uma rede de segurança que coloca em "sem posição" qualquer pessoa que a árvore
/// não alcançou (o caso típico é um ciclo gravado à mão, em que ninguém seria raiz).
/// </summary>
public static class ConstrutorDeOrganograma
{
	/// <summary>Monta o organograma a partir das pessoas ativas.</summary>
	/// <param name="pessoas">Pessoas ativas do diretório.</param>
	public static OrganogramaDto Construir(IReadOnlyList<PessoaDto> pessoas)
	{
		ArgumentNullException.ThrowIfNull(pessoas);

		// Só conta como equipe quem tem um gestor ATIVO (GestorInativo = gestor definido que saiu).
		var equipePorGestor = pessoas
			.Where(pessoa => pessoa.GestorUsuarioId is not null && !pessoa.GestorInativo)
			.GroupBy(pessoa => pessoa.GestorUsuarioId!.Value)
			.ToDictionary(grupo => grupo.Key, grupo => grupo.OrderBy(Nome, StringComparer.OrdinalIgnoreCase).ToList());

		var semGestorAtivo = pessoas.Where(pessoa => pessoa.GestorUsuarioId is null || pessoa.GestorInativo).ToList();

		var visitados = new HashSet<Guid>();

		IReadOnlyList<NoDoOrganograma> raizes =
		[
			.. semGestorAtivo
				.Where(pessoa => equipePorGestor.ContainsKey(pessoa.UsuarioId))
				.OrderBy(Nome, StringComparer.OrdinalIgnoreCase)
				.Select(pessoa => No(pessoa, 0)),
		];

		var semPosicao = semGestorAtivo
			.Where(pessoa => !equipePorGestor.ContainsKey(pessoa.UsuarioId))
			.ToList();

		foreach (var pessoa in semPosicao)
		{
			visitados.Add(pessoa.UsuarioId);
		}

		// Rede de segurança: quem não entrou em lugar nenhum (ciclo corrompido, ou cortado pelo teto).
		semPosicao.AddRange(pessoas.Where(pessoa => !visitados.Contains(pessoa.UsuarioId)));

		return new OrganogramaDto(raizes, [.. semPosicao.OrderBy(Nome, StringComparer.OrdinalIgnoreCase)]);

		NoDoOrganograma No(PessoaDto pessoa, int profundidade)
		{
			visitados.Add(pessoa.UsuarioId);

			var filhos = new List<NoDoOrganograma>();

			if (profundidade < RegrasDeGestor.ProfundidadeMaxima
				&& equipePorGestor.TryGetValue(pessoa.UsuarioId, out var equipe))
			{
				foreach (var membro in equipe.Where(membro => !visitados.Contains(membro.UsuarioId)))
				{
					filhos.Add(No(membro, profundidade + 1));
				}
			}

			return new NoDoOrganograma(pessoa, filhos);
		}
	}

	private static string Nome(PessoaDto pessoa) => pessoa.Nome;
}
