using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Pedido de retirada de um perfil de um usuário.</summary>
/// <param name="UsuarioId">Usuário que perde o perfil.</param>
/// <param name="Perfil">Nome do perfil.</param>
public sealed record RetirarPerfilCommand(Guid UsuarioId, string Perfil);

/// <summary>
/// Retira um perfil. <c>intranet-admin</c> tem duas travas que a plataforma não tem: ninguém
/// tira o próprio, e nunca sobra a instalação sem um ativo.
/// </summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="ator">Usuário que está agindo.</param>
public sealed class RetirarPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha, IAtorAtual ator)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Usuário e perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(RetirarPerfilCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var perfil = command.Perfil?.Trim() ?? string.Empty;

		if (perfil.Length == 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (string.Equals(perfil, ClassificacaoDePerfil.IntranetAdmin, StringComparison.OrdinalIgnoreCase))
		{
			if (RegrasDeIntranetAdmin.EhOProprio(ator, command.UsuarioId))
			{
				return Result.Failure(IntranetErrors.Acesso.AutoRemocaoDeIntranetAdmin);
			}

			var restam = await RegrasDeIntranetAdmin
				.RestamOutrosAtivosAsync(gestao, command.UsuarioId, cancellationToken)
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

		var usuario = await gestao.ObterUsuarioAsync(command.UsuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		var retirado = await gestao.RetirarPerfilAsync(command.UsuarioId, perfil, cancellationToken).ConfigureAwait(false);

		if (retirado.IsFailure)
		{
			return retirado;
		}

		await AuditoriaDeAcesso
			.UsuarioAsync(trilha, VerbosDeAuditoria.AcessoPerfilRetirar, command.UsuarioId, usuario.Value.Email, perfil, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
