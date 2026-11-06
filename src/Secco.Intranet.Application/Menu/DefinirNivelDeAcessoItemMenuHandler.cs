using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Troca quem vê um item no menu — nunca a linha raiz.</summary>
/// <param name="repository">Persistência da árvore.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class DefinirNivelDeAcessoItemMenuHandler(IItemMenuRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Item a alterar.</param>
	/// <param name="nivel">Novo nível.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, NivelDeAcessoItemMenu nivel, CancellationToken cancellationToken = default)
	{
		if (!Enum.IsDefined(nivel))
		{
			return Result.Failure(IntranetErrors.Menu.NivelDeAcessoInvalido);
		}

		var item = await repository.GetParaEdicaoAsync(id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return Result.Failure(IntranetErrors.Menu.NotFound);
		}

		if (item.Tipo == TipoDeItemMenu.Setor)
		{
			return Result.Failure(IntranetErrors.Menu.RaizProtegida);
		}

		var nivelAnterior = item.NivelDeAcesso;

		if (nivelAnterior == nivel)
		{
			return Result.Success();
		}

		item.DefinirNivelDeAcesso(nivel);

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.ItemAsync(
			trilha, VerbosDeAuditoria.MenuItemDefinirNivel, item,
			new { setorId = item.SetorId, nome = item.Nome, tipo = item.Tipo.ToString(), nivelAnterior = nivelAnterior.ToString(), nivel = nivel.ToString() },
			cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
