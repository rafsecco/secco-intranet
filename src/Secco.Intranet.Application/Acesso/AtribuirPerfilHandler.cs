using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Pedido de atribuição de um perfil a um usuário.</summary>
/// <param name="UsuarioId">Usuário que recebe o perfil.</param>
/// <param name="Perfil">Nome do perfil.</param>
public sealed record AtribuirPerfilCommand(Guid UsuarioId, string Perfil);

/// <summary>Atribui um perfil a um usuário existente. Perfil reservado nunca é atribuível.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class AtribuirPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Usuário e perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(AtribuirPerfilCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var perfil = command.Perfil?.Trim() ?? string.Empty;

		if (perfil.Length == 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (ClassificacaoDePerfil.EhReservado(perfil))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		// A plataforma aceita atribuir installation-operator a um usuário; a decisão definitiva
		// sobre "reservado" é dela, e vem no IsReserved do detalhe.
		var detalhe = await gestao.ObterPerfilAsync(perfil, cancellationToken).ConfigureAwait(false);

		if (detalhe.IsFailure)
		{
			return Result.Failure(detalhe.Error);
		}

		if (detalhe.Value.Reservado)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		var usuario = await gestao.ObterUsuarioAsync(command.UsuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		var atribuido = await gestao
			.AtribuirPerfilAsync(command.UsuarioId, detalhe.Value.Nome, cancellationToken)
			.ConfigureAwait(false);

		if (atribuido.IsFailure)
		{
			return atribuido;
		}

		await AuditoriaDeAcesso
			.UsuarioAsync(trilha, VerbosDeAuditoria.AcessoPerfilAtribuir, command.UsuarioId, usuario.Value.Email, detalhe.Value.Nome, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
