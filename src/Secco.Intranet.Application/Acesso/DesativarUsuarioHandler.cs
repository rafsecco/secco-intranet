using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Desativa a conta de um usuário — nunca a própria, nunca a do último <c>intranet-admin</c> ativo.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="ator">Usuário que está agindo.</param>
public sealed class DesativarUsuarioHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha, IAtorAtual ator)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Usuário a desativar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		// A plataforma já responde 409 à autodesativação; a Intranet checa antes para a mensagem ser dela.
		if (RegrasDeIntranetAdmin.EhOProprio(ator, usuarioId))
		{
			return Result.Failure(IntranetErrors.Acesso.AutoDesativacao);
		}

		var usuario = await gestao.ObterUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		if (usuario.Value.Perfis.Any(p => string.Equals(p, ClassificacaoDePerfil.IntranetAdmin, StringComparison.OrdinalIgnoreCase)))
		{
			var restam = await RegrasDeIntranetAdmin
				.RestamOutrosAtivosAsync(gestao, usuarioId, cancellationToken)
				.ConfigureAwait(false);

			if (restam.IsFailure)
			{
				return Result.Failure(restam.Error);
			}

			if (!restam.Value)
			{
				return Result.Failure(IntranetErrors.Acesso.UltimoIntranetAdmin);
			}
		}

		var desativado = await gestao.DesativarUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (desativado.IsFailure)
		{
			return desativado;
		}

		await AuditoriaDeAcesso
			.UsuarioAsync(trilha, VerbosDeAuditoria.AcessoUsuarioDesativar, usuarioId, usuario.Value.Email, null, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
