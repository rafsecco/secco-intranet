using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Edita os quatro campos de um link do menu principal.</summary>
/// <param name="repository">Persistência dos links.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarLinkDoMenuPrincipalHandler(ILinkDoMenuPrincipalRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Novos dados.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(EditarLinkDoMenuPrincipalCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var link = await repository.ObterAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (link is null)
		{
			return Result.Failure(IntranetErrors.LinksDoMenu.NotFound);
		}

		var campos = CamposDeLinkDoMenuPrincipal.Validar(command.Rotulo, command.Url, command.Icone);

		if (campos.IsFailure)
		{
			return Result.Failure(campos.Error);
		}

		var antes = new { rotulo = link.Rotulo, url = link.Url, icone = link.Icone, novaAba = link.NovaAba };

		link.Editar(campos.Value.Rotulo, campos.Value.Url, campos.Value.Icone, command.NovaAba);

		await repository.SalvarAsync(cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.LinkAsync(
			trilha, VerbosDeAuditoria.MenuLinkEditar, link.Id,
			new
			{
				antes,
				depois = new { rotulo = link.Rotulo, url = link.Url, icone = link.Icone, novaAba = link.NovaAba },
			},
			cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
