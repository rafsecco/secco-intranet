using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Exclui um link do menu principal e a posição dele.</summary>
/// <param name="repository">Persistência dos links.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class ExcluirLinkDoMenuPrincipalHandler(ILinkDoMenuPrincipalRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Link a excluir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var link = await repository.ObterAsync(id, cancellationToken).ConfigureAwait(false);

		if (link is null)
		{
			return Result.Failure(IntranetErrors.LinksDoMenu.NotFound);
		}

		var dados = new { rotulo = link.Rotulo, url = link.Url, icone = link.Icone, novaAba = link.NovaAba };

		await repository.RemoverAsync(link, cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.LinkAsync(trilha, VerbosDeAuditoria.MenuLinkExcluir, id, dados, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
