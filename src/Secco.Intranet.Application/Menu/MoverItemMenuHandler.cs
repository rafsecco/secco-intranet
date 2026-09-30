using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Troca a posição de um item com o irmão adjacente.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class MoverItemMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso. Mover o primeiro para cima (ou o último para baixo) não faz nada — sucesso, sem efeito.</summary>
	/// <param name="id">Item a mover.</param>
	/// <param name="paraCima"><c>true</c> troca com o irmão anterior; <c>false</c>, com o próximo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, bool paraCima, CancellationToken cancellationToken = default)
	{
		var item = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return Result.Failure(IntranetErrors.Menu.NotFound);
		}

		var irmaos = (await repository.ListarPorSetorAsync(item.SetorId, cancellationToken).ConfigureAwait(false))
			.Where(i => i.ParentId == item.ParentId)
			.OrderBy(i => i.Ordem)
			.ToList();

		var indice = irmaos.FindIndex(i => i.Id == id);
		var indiceDoVizinho = paraCima ? indice - 1 : indice + 1;

		if (indiceDoVizinho < 0 || indiceDoVizinho >= irmaos.Count)
		{
			return Result.Success();
		}

		// Troca de posição na lista e renumera TODOS os irmãos 0..n-1. Trocar só os dois
		// valores de Ordem não basta: depois de uma exclusão as ordens ficam com buraco
		// (ex.: 0, 5, 6, 7), e dar ao par os índices novos quebraria a ordem dos vizinhos.
		// Também resolve empate de Ordem, se algum dia existir.
		(irmaos[indice], irmaos[indiceDoVizinho]) = (irmaos[indiceDoVizinho], irmaos[indice]);

		for (var posicao = 0; posicao < irmaos.Count; posicao++)
		{
			var rastreado = await repository.GetParaEdicaoAsync(irmaos[posicao].Id, cancellationToken).ConfigureAwait(false);
			rastreado!.DefinirOrdem(posicao);
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
