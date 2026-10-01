using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Resultado de resolver um caminho na árvore de um setor.</summary>
/// <param name="No">O nó resolvido.</param>
/// <param name="Ancestrais">Nós entre a raiz (exclusive) e o nó (exclusive), de cima para baixo — o trilho da página.</param>
/// <param name="CaminhoCompleto">Os slugs percorridos até o nó.</param>
public sealed record ResultadoDaResolucao(ItemMenuDto No, IReadOnlyList<ItemMenuDto> Ancestrais, IReadOnlyList<string> CaminhoCompleto);

/// <summary>Desce a árvore de um setor segmento a segmento, casando pelo <c>Slug</c> de um filho ativo.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ResolverCaminhoDeMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorId">Setor dono da árvore.</param>
	/// <param name="caminho">Segmentos do caminho, na ordem; vazio resolve a raiz.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ResultadoDaResolucao>> HandleAsync(
		Guid setorId, IReadOnlyList<string> caminho, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(caminho);

		var todos = await repository.ListarPorSetorAsync(setorId, cancellationToken).ConfigureAwait(false);
		var raiz = todos.FirstOrDefault(item => item.Tipo == TipoDeItemMenu.Setor);

		if (raiz is null)
		{
			return Result.Failure<ResultadoDaResolucao>(IntranetErrors.Menu.NotFound);
		}

		var atual = raiz;
		var ancestrais = new List<ItemMenuDto>();

		foreach (var segmento in caminho)
		{
			var proximo = todos.FirstOrDefault(item =>
				item.ParentId == atual.Id && item.Ativo && string.Equals(item.Slug, segmento, StringComparison.Ordinal));

			if (proximo is null)
			{
				return Result.Failure<ResultadoDaResolucao>(IntranetErrors.Menu.NotFound);
			}

			if (atual.Tipo != TipoDeItemMenu.Setor)
			{
				ancestrais.Add(ItemMenuDto.FromEntity(atual));
			}

			atual = proximo;
		}

		return new ResultadoDaResolucao(ItemMenuDto.FromEntity(atual), ancestrais, caminho);
	}

	/// <summary>
	/// Caminho (slugs a partir da raiz) do item de um tipo embutido — Documentos ou Avisos,
	/// no máximo um por setor. Nulo se o setor não tem esse item, ou se ele ou qualquer
	/// ancestral está desativado: nesse caso o recurso está desligado para o setor.
	/// </summary>
	/// <param name="setorId">Setor dono da árvore.</param>
	/// <param name="tipo">Tipo procurado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlyList<string>?> CaminhoDoTipoAsync(
		Guid setorId, TipoDeItemMenu tipo, CancellationToken cancellationToken = default)
	{
		var todos = await repository.ListarPorSetorAsync(setorId, cancellationToken).ConfigureAwait(false);
		var atual = todos.FirstOrDefault(item => item.Tipo == tipo);
		var caminho = new List<string>();

		while (atual is not null && atual.Tipo != TipoDeItemMenu.Setor)
		{
			if (!atual.Ativo)
			{
				return null;
			}

			caminho.Insert(0, atual.Slug);
			var paiId = atual.ParentId;
			atual = todos.FirstOrDefault(item => item.Id == paiId);
		}

		// Sem item do tipo, ou cadeia que não termina na raiz (dado corrompido): desligado.
		return atual is null ? null : caminho;
	}
}
