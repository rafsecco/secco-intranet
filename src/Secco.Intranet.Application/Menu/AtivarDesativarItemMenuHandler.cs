using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>(Des)ativa um item — nunca a linha raiz.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class AtivarDesativarItemMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Item a alterar.</param>
	/// <param name="ativar"><c>true</c> ativa, <c>false</c> desativa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, bool ativar, CancellationToken cancellationToken = default)
	{
		var item = await repository.GetParaEdicaoAsync(id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return Result.Failure(IntranetErrors.Menu.NotFound);
		}

		if (item.Tipo == Domain.Menu.TipoDeItemMenu.Setor)
		{
			return Result.Failure(IntranetErrors.Menu.RaizProtegida);
		}

		if (ativar)
		{
			item.Ativar();
		}
		else
		{
			item.Desativar();
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
