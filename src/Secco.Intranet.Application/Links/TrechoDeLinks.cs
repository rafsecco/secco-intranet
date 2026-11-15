using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Links;

/// <summary>
/// A trava dos casos de uso de links: o recorte da árvore de um setor que fica debaixo do nó
/// <see cref="TipoDeItemMenu.Links"/>. Quem escreve no setor mexe só no que este recorte devolve;
/// qualquer item fora dele (outro tipo, outro setor, o próprio nó) é "não encontrado".
/// </summary>
internal sealed class TrechoDeLinks
{
	private readonly Dictionary<Guid, ItemMenu> _descendentes;
	private readonly ILookup<Guid, ItemMenu> _filhosPorPai;

	private TrechoDeLinks(ItemMenu no, IReadOnlyList<ItemMenu> arvore)
	{
		No = no;
		_filhosPorPai = arvore
			.Where(item => item.ParentId.HasValue)
			.OrderBy(item => item.Ordem)
			.ToLookup(item => item.ParentId!.Value);

		// Desce a partir do nó, nível a nível: só entra no mapa o que tem o nó como ancestral.
		_descendentes = [];
		var fila = new Queue<ItemMenu>(_filhosPorPai[no.Id]);

		while (fila.Count > 0)
		{
			var item = fila.Dequeue();

			if (_descendentes.TryAdd(item.Id, item))
			{
				foreach (var filho in _filhosPorPai[item.Id])
				{
					fila.Enqueue(filho);
				}
			}
		}

		Grupos = [.. _filhosPorPai[no.Id].Where(item => item.Rota is null)];
	}

	/// <summary>O nó de links do setor.</summary>
	public ItemMenu No { get; }

	/// <summary>Filhos diretos do nó que não têm rota, por ordem.</summary>
	public IReadOnlyList<ItemMenu> Grupos { get; }

	/// <summary>O trecho da árvore informada; nulo quando ela não tem nó de links ativo.</summary>
	/// <param name="arvoreDoSetor">Todos os itens do setor.</param>
	public static TrechoDeLinks? De(IReadOnlyList<ItemMenu> arvoreDoSetor)
	{
		ArgumentNullException.ThrowIfNull(arvoreDoSetor);

		var no = arvoreDoSetor.FirstOrDefault(item => item.Tipo == TipoDeItemMenu.Links && item.Ativo);

		return no is null ? null : new TrechoDeLinks(no, arvoreDoSetor);
	}

	/// <summary>Filhos diretos de um item, por ordem.</summary>
	/// <param name="parentId">Pai.</param>
	public IReadOnlyList<ItemMenu> FilhosDe(Guid parentId) => [.. _filhosPorPai[parentId]];

	/// <summary>O item, se for descendente do nó em qualquer profundidade; o próprio nó não conta.</summary>
	/// <param name="id">Item procurado.</param>
	public ItemMenu? Item(Guid id) => _descendentes.GetValueOrDefault(id);

	/// <summary>O grupo, se for filho direto do nó e não tiver rota.</summary>
	/// <param name="id">Grupo procurado.</param>
	public ItemMenu? Grupo(Guid id) => Grupos.FirstOrDefault(item => item.Id == id);
}
