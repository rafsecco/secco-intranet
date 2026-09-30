using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Pedido de alteração da federação do tenant atual.</summary>
/// <param name="DirectoryId">Tenant GUID do Entra da empresa, como texto vindo do formulário.</param>
/// <param name="Habilitada">Se o login por Entra deve ficar ativo.</param>
public sealed record DefinirFederacaoCommand(string? DirectoryId, bool Habilitada);

/// <summary>
/// Liga, desliga ou reconfigura a federação do tenant atual (ADR-0026 da plataforma). O
/// directory id é sempre exigido, mesmo para desligar — é o que o SecureGate grava junto do
/// flag, e a tela sempre reenvia o valor atual, então isso nunca sobra sem preencher no uso
/// normal.
/// </summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class DefinirFederacaoHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Directory id e se a federação deve ficar habilitada.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(DefinirFederacaoCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (!Guid.TryParse(command.DirectoryId, out var directoryId) || directoryId == Guid.Empty)
		{
			return Result.Failure(IntranetErrors.Acesso.DirectoryIdInvalido);
		}

		var definida = await gestao.DefinirFederacaoAsync(directoryId, command.Habilitada, cancellationToken).ConfigureAwait(false);

		if (definida.IsFailure)
		{
			return definida;
		}

		await AuditoriaDeAcesso.FederacaoAsync(trilha, directoryId, command.Habilitada, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
