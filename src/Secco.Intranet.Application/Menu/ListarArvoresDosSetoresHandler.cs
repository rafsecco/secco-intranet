namespace Secco.Intranet.Application.Menu;

/// <summary>
/// Itens ativos das árvores de vários setores, agrupados por setor — a matéria-prima do menu
/// principal. Inativos ficam de fora aqui; quem monta o menu ainda poda o filho de um inativo,
/// porque o filho pode estar ativo debaixo de um pai desligado.
/// </summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ListarArvoresDosSetoresHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorIds">Setores visíveis para o usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>> HandleAsync(
		IReadOnlyCollection<Guid> setorIds, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(setorIds);

		var itens = await repository.ListarPorSetoresAsync(setorIds, cancellationToken).ConfigureAwait(false);

		return itens
			.Where(item => item.Ativo)
			.GroupBy(item => item.SetorId)
			.ToDictionary(
				grupo => grupo.Key,
				grupo => (IReadOnlyList<ItemMenuDto>)[.. grupo.Select(ItemMenuDto.FromEntity)]);
	}
}
