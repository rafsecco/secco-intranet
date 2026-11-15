using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Links;

/// <summary>Exclui um link, ou um grupo sem links.</summary>
/// <param name="repository">Persistência da árvore.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class ExcluirLinkHandler(IItemMenuRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorId">Setor da tela.</param>
	/// <param name="id">Item a excluir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid setorId, Guid id, CancellationToken cancellationToken = default)
	{
		var arvore = await repository.ListarPorSetorAsync(setorId, cancellationToken).ConfigureAwait(false);
		var trecho = TrechoDeLinks.De(arvore);
		var item = trecho?.Item(id);

		if (trecho is null || item is null)
		{
			return Result.Failure(IntranetErrors.Links.NotFound);
		}

		// Com filhos, a FK de ParentId (Restrict) recusaria no banco — aqui vira mensagem, não 500.
		if (trecho.FilhosDe(item.Id).Count > 0)
		{
			return Result.Failure(IntranetErrors.Menu.ItemComFilhos);
		}

		await repository.ExcluirAsync(id, cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.ItemAsync(
			trilha, VerbosDeAuditoria.MenuItemExcluir, item,
			new { setorId = item.SetorId, nome = item.Nome, slug = item.Slug, rota = item.Rota },
			cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
