using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Lista os links do menu principal, por rótulo.</summary>
/// <param name="repository">Persistência dos links.</param>
public sealed class ListarLinksDoMenuPrincipalHandler(ILinkDoMenuPrincipalRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<LinkDoMenuPrincipalDto>>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var links = await repository.ListarAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<LinkDoMenuPrincipalDto> lista =
		[
			.. links.OrderBy(link => link.Rotulo, StringComparer.OrdinalIgnoreCase).Select(LinkDoMenuPrincipalDto.FromEntity)
		];

		return Result.Success(lista);
	}
}
