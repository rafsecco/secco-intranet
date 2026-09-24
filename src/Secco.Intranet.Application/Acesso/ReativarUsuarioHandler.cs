using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Reativa a conta de um usuário.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class ReativarUsuarioHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Usuário a reativar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = await gestao.ObterUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		var reativado = await gestao.ReativarUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (reativado.IsFailure)
		{
			return reativado;
		}

		await AuditoriaDeAcesso
			.UsuarioAsync(trilha, VerbosDeAuditoria.AcessoUsuarioReativar, usuarioId, usuario.Value.Email, null, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
