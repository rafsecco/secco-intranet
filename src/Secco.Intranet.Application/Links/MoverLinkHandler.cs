using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Links;

/// <summary>Troca a posição de um link ou grupo com o irmão adjacente.</summary>
/// <param name="repository">Persistência da árvore.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class MoverLinkHandler(IItemMenuRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso. Mover o primeiro para cima (ou o último para baixo) não faz nada — sucesso, sem efeito.</summary>
	/// <param name="setorId">Setor da tela.</param>
	/// <param name="id">Item a mover.</param>
	/// <param name="paraCima"><c>true</c> troca com o irmão anterior; <c>false</c>, com o próximo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid setorId, Guid id, bool paraCima, CancellationToken cancellationToken = default)
	{
		var arvore = await repository.ListarPorSetorAsync(setorId, cancellationToken).ConfigureAwait(false);
		var trecho = TrechoDeLinks.De(arvore);
		var item = trecho?.Item(id);

		if (trecho is null || item is null)
		{
			return Result.Failure(IntranetErrors.Links.NotFound);
		}

		var irmaos = trecho.FilhosDe(item.ParentId!.Value).ToList();
		var indice = irmaos.FindIndex(irmao => irmao.Id == id);
		var indiceDoVizinho = paraCima ? indice - 1 : indice + 1;

		if (indiceDoVizinho < 0 || indiceDoVizinho >= irmaos.Count)
		{
			return Result.Success();
		}

		// Mesma técnica do menu: troca na lista e renumera TODOS os irmãos 0..n-1 (a Ordem pode ter buracos).
		(irmaos[indice], irmaos[indiceDoVizinho]) = (irmaos[indiceDoVizinho], irmaos[indice]);

		for (var posicao = 0; posicao < irmaos.Count; posicao++)
		{
			var rastreado = await repository.GetParaEdicaoAsync(irmaos[posicao].Id, cancellationToken).ConfigureAwait(false);
			rastreado!.DefinirOrdem(posicao);
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.ItemAsync(
			trilha, VerbosDeAuditoria.MenuItemMover, item,
			new { setorId, nome = item.Nome, de = indice, para = indiceDoVizinho },
			cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
