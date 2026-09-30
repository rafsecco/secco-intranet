using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Um nó da árvore, com os filhos já aninhados.</summary>
/// <param name="Item">O nó.</param>
/// <param name="Filhos">Filhos diretos, cada um com os próprios filhos.</param>
public sealed record NoDaArvoreDto(ItemMenuDto Item, IReadOnlyList<NoDaArvoreDto> Filhos);

/// <summary>Monta a árvore inteira de um setor, aninhada, para a tela de administração.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ObterArvoreDeMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorId">Setor dono da árvore.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<NoDaArvoreDto>> HandleAsync(Guid setorId, CancellationToken cancellationToken = default)
	{
		var todos = await repository.ListarPorSetorAsync(setorId, cancellationToken).ConfigureAwait(false);
		var raiz = todos.FirstOrDefault(item => item.Tipo == Domain.Menu.TipoDeItemMenu.Setor);

		if (raiz is null)
		{
			return Result.Failure<NoDaArvoreDto>(IntranetErrors.Menu.NotFound);
		}

		NoDaArvoreDto Montar(Guid id)
		{
			var item = todos.First(i => i.Id == id);
			var filhos = todos.Where(i => i.ParentId == id).OrderBy(i => i.Ordem).Select(i => Montar(i.Id)).ToList();

			return new NoDaArvoreDto(ItemMenuDto.FromEntity(item), filhos);
		}

		return Montar(raiz.Id);
	}
}
