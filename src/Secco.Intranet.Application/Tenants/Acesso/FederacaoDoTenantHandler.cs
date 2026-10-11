using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>Pedido de definição da federação do tenant administrado.</summary>
/// <param name="DirectoryId">Tenant GUID do Entra, como digitado.</param>
/// <param name="Habilitada">Se o login por Entra fica ativo.</param>
/// <param name="ConfirmarTroca">Confirmação explícita para trocar um diretório já configurado.</param>
public sealed record DefinirFederacaoDoTenantCommand(string? DirectoryId, bool Habilitada, bool ConfirmarTroca);

/// <summary>
/// Define a federação Entra do tenant administrado. Trocar um directory id já configurado muda de
/// qual Entra as pessoas entram, e exige confirmação; configurar a primeira vez ou manter o mesmo, não.
/// </summary>
/// <param name="gestao">Porta.</param>
/// <param name="trilha">Trilha da Intranet.</param>
public sealed class FederacaoDoTenantHandler(IGestaoDeAcessoDoTenant gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result> DefinirAsync(Guid tenantId, DefinirFederacaoDoTenantCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (!Guid.TryParse(command.DirectoryId, out var directoryId) || directoryId == Guid.Empty)
		{
			return Result.Failure(IntranetErrors.Acesso.DirectoryIdInvalido);
		}

		var atual = await gestao.ObterFederacaoAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (atual.IsFailure)
		{
			return Result.Failure(atual.Error);
		}

		if (atual.Value.DirectoryId is { } anterior && anterior != directoryId && !command.ConfirmarTroca)
		{
			return Result.Failure(IntranetErrors.Acesso.TrocaDeDiretorioNaoConfirmada);
		}

		var definida = await gestao.DefinirFederacaoAsync(tenantId, directoryId, command.Habilitada, cancellationToken).ConfigureAwait(false);

		if (definida.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant.FederacaoAsync(trilha, tenantId, directoryId, command.Habilitada, cancellationToken).ConfigureAwait(false);
		}

		return definida;
	}
}
