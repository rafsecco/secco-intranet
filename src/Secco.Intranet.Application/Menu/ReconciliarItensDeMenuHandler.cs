using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Application.Menu;

/// <summary>
/// Garante que todo setor tenha, no mínimo, a linha raiz e os itens Documentos/Avisos —
/// mesmo padrão de "Reconciliar permissões" (2026-09-27), para setores criados antes desta
/// feature. Nunca remove nada já customizado.
/// </summary>
/// <param name="searchSetores">Busca dos setores do tenant.</param>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ReconciliarItensDeMenuHandler(SearchSetoresHandler searchSetores, IItemMenuRepository repository)
{
	// Mesmo tamanho de página de ReconciliarPermissoesHandler — e o mesmo laço: sem ele só a
	// primeira página de setores seria reconciliada, em silêncio.
	private const int TamanhoDaPagina = 100;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Quantos setores ganharam a raiz e/ou algum dos dois itens embutidos.</returns>
	public async Task<int> HandleAsync(CancellationToken cancellationToken = default)
	{
		var alterados = 0;
		var pagina = PageRequest.FirstPage;

		while (true)
		{
			var busca = await searchSetores
				.HandleAsync(new SetorSearchCriteria(Page: new PageRequest(pagina, TamanhoDaPagina)), cancellationToken)
				.ConfigureAwait(false);

			foreach (var setor in busca.Value.Items)
			{
				if (await ReconciliarSetorAsync(setor, cancellationToken).ConfigureAwait(false))
				{
					alterados++;
				}
			}

			if (!busca.Value.HasNextPage)
			{
				break;
			}

			pagina++;
		}

		return alterados;
	}

	private async Task<bool> ReconciliarSetorAsync(SetorDto setor, CancellationToken cancellationToken)
	{
		var itens = (await repository.ListarPorSetorAsync(setor.Id, cancellationToken).ConfigureAwait(false)).ToList();
		var raiz = itens.FirstOrDefault(item => item.Tipo == TipoDeItemMenu.Setor);
		var mudou = false;

		if (raiz is null)
		{
			raiz = new ItemMenu(setor.Id, null, setor.Nome, setor.Slug, TipoDeItemMenu.Setor, null, null, 0);
			await repository.AddAsync(raiz, cancellationToken).ConfigureAwait(false);
			itens.Add(raiz);
			mudou = true;
		}

		// Documentos antes de Avisos, de propósito (diferente da criação, que é alfabética):
		// estes setores já existem, e hoje /setor/{slug} abre em Documentos — reconciliar não
		// deve mudar a página de entrada de ninguém. O admin reordena depois, se quiser.
		foreach (var (tipo, nome, slugDesejado) in new[]
		{
			(TipoDeItemMenu.Documentos, "Documentos", "documentos"),
			(TipoDeItemMenu.Avisos, "Avisos", "avisos"),
		})
		{
			if (itens.Any(item => item.Tipo == tipo))
			{
				continue;
			}

			var irmaos = itens.Where(item => item.ParentId == raiz.Id).ToList();
			var ordem = irmaos.Select(item => item.Ordem).DefaultIfEmpty(-1).Max() + 1;
			var item = new ItemMenu(setor.Id, raiz.Id, nome, SlugLivre(slugDesejado, irmaos), tipo, null, null, ordem);

			await repository.AddAsync(item, cancellationToken).ConfigureAwait(false);
			itens.Add(item);
			mudou = true;
		}

		return mudou;
	}

	/// <summary>
	/// Um Personalizado criado pela tela pode já ter tomado o slug "documentos" debaixo da
	/// raiz; nesse caso sufixa (-2, -3…) em vez de gravar dois irmãos com o mesmo slug.
	/// </summary>
	private static string SlugLivre(string desejado, IReadOnlyCollection<ItemMenu> irmaos)
	{
		var candidato = desejado;
		var sufixo = 2;

		while (irmaos.Any(item => string.Equals(item.Slug, candidato, StringComparison.Ordinal)))
		{
			candidato = $"{desejado}-{sufixo++}";
		}

		return candidato;
	}
}
