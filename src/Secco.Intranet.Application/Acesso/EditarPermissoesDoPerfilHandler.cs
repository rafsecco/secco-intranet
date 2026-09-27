using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Edita as permissões de um perfil (ADR-0021) — substitui a lista inteira.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarPermissoesDoPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="permissoes">Lista final de permissões, no formato canônico.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(
		string perfil, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default)
	{
		if (permissoes.Any(permissao => !SeccoPermissions.IsValid(permissao)))
		{
			return Result.Failure(IntranetErrors.Acesso.PermissaoInvalida);
		}

		var definido = await gestao.DefinirPermissoesDoPerfilAsync(perfil, permissoes, cancellationToken).ConfigureAwait(false);

		if (definido.IsFailure)
		{
			return definido;
		}

		await AuditoriaDeAcesso.PermissoesAsync(trilha, perfil, permissoes, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
