using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Ativa ou desativa um tenant do cadastro. Desativar exige o slug digitado.</summary>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="gestao">API de tenants.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class AlterarSituacaoDoTenantHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Ativa o tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Sucesso ou o motivo da recusa.</returns>
	public async Task<Result> AtivarAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var registro = await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (registro is null)
		{
			return Result.Failure(IntranetErrors.Tenants.NaoEncontrado);
		}

		var ativado = await gestao.AtivarAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (ativado.IsSuccess)
		{
			await AuditoriaDeTenants.TenantAsync(trilha, VerbosDeAuditoria.TenantAtivar, tenantId, registro.Sistema, cancellationToken)
				.ConfigureAwait(false);
		}

		return ativado;
	}

	/// <summary>Desativa o tenant: login e catálogo do sistema param em até um TTL de cache.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="confirmacaoSlug">Slug digitado pelo admin; precisa ser igual ao do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Sucesso ou o motivo da recusa.</returns>
	public async Task<Result> DesativarAsync(Guid tenantId, string? confirmacaoSlug, CancellationToken cancellationToken = default)
	{
		var registro = await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (registro is null)
		{
			return Result.Failure(IntranetErrors.Tenants.NaoEncontrado);
		}

		var tenant = await gestao.ObterTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (tenant.IsFailure)
		{
			return Result.Failure(tenant.Error);
		}

		if (!string.Equals(confirmacaoSlug?.Trim(), tenant.Value.Slug, StringComparison.Ordinal))
		{
			return Result.Failure(IntranetErrors.Tenants.ConfirmacaoNaoConfere);
		}

		var desativado = await gestao.DesativarAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (desativado.IsSuccess)
		{
			await AuditoriaDeTenants.TenantAsync(trilha, VerbosDeAuditoria.TenantDesativar, tenantId, registro.Sistema, cancellationToken)
				.ConfigureAwait(false);
		}

		return desativado;
	}
}
