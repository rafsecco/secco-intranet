using System.Net;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Setores;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Adapter real da porta <see cref="ISetorAccessProvisioner"/> (ADR-0001): cria as Roles
/// tenant-scoped <c>{slug}-admin</c>/<c>{slug}-user</c> no Secco.SecureGate via
/// <see cref="ISecureGateClient"/>, autenticado por client credentials (seção
/// <c>Secco:SecureGate</c>). Composição em <see cref="IntranetInfrastructureExtensions"/>:
/// só é resolvido quando a seção está configurada — ver <see cref="NullSetorAccessProvisioner"/>
/// para o modo DEV/Testing.
/// </summary>
/// <param name="client">Client administrativo do SecureGate (autenticado, ver <c>AddSecureGateAdminClient()</c>).</param>
/// <param name="tenantContext">Tenant da requisição atual (ADR-0005).</param>
/// <param name="logger">Logger para falhas de provisionamento (ADR-0020: sem dados sensíveis).</param>
public sealed class SecureGateSetorAccessProvisioner(
	ISecureGateClient client,
	ITenantContext tenantContext,
	ILogger<SecureGateSetorAccessProvisioner> logger) : ISetorAccessProvisioner
{
	/// <inheritdoc />
	public async Task<Result> EnsureSetorRolesAsync(string slug, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(slug);

		if (!tenantContext.IsResolved)
		{
			return Result.Failure(IntranetErrors.Setores.AccessProvisioningUnavailable);
		}

		var tenantId = tenantContext.TenantId!.Value;

		// Mesma normalização de Setor.Slug (Domain): garante que os nomes das Roles criadas
		// aqui sempre coincidam com o slug efetivamente persistido.
		var normalizedSlug = slug.Trim().ToLowerInvariant();

		var adminRole = $"{normalizedSlug}-admin";
		var userRole = $"{normalizedSlug}-user";

		var adminResult = await EnsureRoleAsync(tenantId, adminRole, cancellationToken).ConfigureAwait(false);
		if (adminResult.IsFailure)
		{
			return adminResult;
		}

		var userResult = await EnsureRoleAsync(tenantId, userRole, cancellationToken).ConfigureAwait(false);
		if (userResult.IsFailure)
		{
			return userResult;
		}

		return await GarantirPermissoesDoSetorAsync(tenantId, normalizedSlug, adminRole, userRole, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>
	/// Garante a permissão de leitura/escrita de cada Role do setor (ADR-0021), por mesclagem —
	/// nunca apaga o que já estava lá (<see cref="PermissoesDoPerfil.GarantirAsync"/>).
	/// </summary>
	private async Task<Result> GarantirPermissoesDoSetorAsync(
		Guid tenantId, string slug, string adminRole, string userRole, CancellationToken cancellationToken)
	{
		try
		{
			await PermissoesDoPerfil
				.GarantirAsync(client, tenantId, userRole, [IntranetPermissoes.Setor.Read(slug)], cancellationToken)
				.ConfigureAwait(false);

			await PermissoesDoPerfil
				.GarantirAsync(
					client, tenantId, adminRole,
					[IntranetPermissoes.Setor.Read(slug), IntranetPermissoes.Setor.Write(slug)],
					cancellationToken)
				.ConfigureAwait(false);

			return Result.Success();
		}
		catch (ApiException apiException)
		{
			logger.LogWarning(
				"Falha ao garantir permissao do setor '{Slug}' no SecureGate (status {StatusCode}).",
				slug,
				apiException.StatusCode);

			return Result.Failure(IntranetErrors.Setores.AccessProvisioningUnavailable);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(
				httpRequestException,
				"Falha de rede ao garantir permissao do setor '{Slug}' no SecureGate.",
				slug);

			return Result.Failure(IntranetErrors.Setores.AccessProvisioningUnavailable);
		}
	}

	private async Task<Result> EnsureRoleAsync(Guid tenantId, string roleName, CancellationToken cancellationToken)
	{
		try
		{
			await client
				.CreateRoleAsync(tenantId, new CreateRoleRequest { Name = roleName }, cancellationToken)
				.ConfigureAwait(false);

			return Result.Success();
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.Conflict)
		{
			// Idempotente (ADR-0001): a Role já existe — nada a fazer, não é uma falha.
			return Result.Success();
		}
		catch (ApiException apiException)
		{
			// ADR-0020: log sem dados sensíveis — só status e nome da role, nunca corpo/headers.
			logger.LogWarning(
				"Falha ao provisionar a Role '{RoleName}' no SecureGate (status {StatusCode}).",
				roleName,
				apiException.StatusCode);

			return Result.Failure(IntranetErrors.Setores.AccessProvisioningUnavailable);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(
				httpRequestException,
				"Falha de rede ao provisionar a Role '{RoleName}' no SecureGate.",
				roleName);

			return Result.Failure(IntranetErrors.Setores.AccessProvisioningUnavailable);
		}
	}
}
