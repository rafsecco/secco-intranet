using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Exclui um item — só <see cref="TipoDeItemMenu.Personalizado"/> se exclui de verdade.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ExcluirItemMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Item a excluir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var item = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return Result.Failure(IntranetErrors.Menu.NotFound);
		}

		if (item.Tipo == TipoDeItemMenu.Setor)
		{
			return Result.Failure(IntranetErrors.Menu.RaizProtegida);
		}

		if (item.Tipo != TipoDeItemMenu.Personalizado)
		{
			return Result.Failure(IntranetErrors.Menu.TipoEmbutidoNaoExclui);
		}

		await repository.ExcluirAsync(id, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
