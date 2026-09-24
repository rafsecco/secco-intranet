using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Encerra as sessões de um usuário. Permitido até para o próprio admin (ele só sai e entra de novo).</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EncerrarSessoesHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Usuário cujas sessões serão encerradas.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = await gestao.ObterUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		var encerrado = await gestao.EncerrarSessoesAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (encerrado.IsFailure)
		{
			return encerrado;
		}

		await AuditoriaDeAcesso
			.UsuarioAsync(trilha, VerbosDeAuditoria.AcessoSessoesEncerrar, usuarioId, usuario.Value.Email, null, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
