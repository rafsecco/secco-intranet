using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Cria um link do menu principal.</summary>
/// <param name="repository">Persistência dos links.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class CriarLinkDoMenuPrincipalHandler(ILinkDoMenuPrincipalRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Dados do novo link.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(CriarLinkDoMenuPrincipalCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var campos = CamposDeLinkDoMenuPrincipal.Validar(command.Rotulo, command.Url, command.Icone);

		if (campos.IsFailure)
		{
			return Result.Failure<Guid>(campos.Error);
		}

		var link = new LinkDoMenuPrincipal(campos.Value.Rotulo, campos.Value.Url, campos.Value.Icone, command.NovaAba);

		await repository.AddAsync(link, cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.LinkAsync(
			trilha, VerbosDeAuditoria.MenuLinkCriar, link.Id,
			new { rotulo = link.Rotulo, url = link.Url, icone = link.Icone, novaAba = link.NovaAba },
			cancellationToken).ConfigureAwait(false);

		return link.Id;
	}
}
