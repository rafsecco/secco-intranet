using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Resultado de resolver um caminho na árvore de um setor.</summary>
/// <param name="No">O nó resolvido.</param>
/// <param name="Irmaos">Irmãos ativos do nó (inclusive ele), ordenados — para a barra de abas.</param>
/// <param name="CaminhoCompleto">Os slugs percorridos até o nó.</param>
/// <param name="PrimeiroFilhoAtivo">
/// O primeiro filho ativo do nó, por <c>Ordem</c> — nulo se o nó não tem nenhum filho ativo
/// (é folha, ou tem só filhos desativados). Quem chama usa isto para decidir entre
/// redirecionar para o filho (nó com filhos) ou renderizar o próprio nó (folha).
/// </param>
public sealed record ResultadoDaResolucao(
	ItemMenuDto No, IReadOnlyList<ItemMenuDto> Irmaos, IReadOnlyList<string> CaminhoCompleto, ItemMenuDto? PrimeiroFilhoAtivo);

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

		foreach (var segmento in caminho)
		{
			var proximo = todos.FirstOrDefault(item =>
				item.ParentId == atual.Id && item.Ativo && string.Equals(item.Slug, segmento, StringComparison.Ordinal));

			if (proximo is null)
			{
				return Result.Failure<ResultadoDaResolucao>(IntranetErrors.Menu.NotFound);
			}

			atual = proximo;
		}

		var irmaos = todos
			.Where(item => item.ParentId == atual.ParentId && item.Ativo)
			.OrderBy(item => item.Ordem)
			.Select(ItemMenuDto.FromEntity)
			.ToList();

		var primeiroFilhoAtivo = todos
			.Where(item => item.ParentId == atual.Id && item.Ativo)
			.OrderBy(item => item.Ordem)
			.Select(ItemMenuDto.FromEntity)
			.FirstOrDefault();

		return new ResultadoDaResolucao(ItemMenuDto.FromEntity(atual), irmaos, caminho, primeiroFilhoAtivo);
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
