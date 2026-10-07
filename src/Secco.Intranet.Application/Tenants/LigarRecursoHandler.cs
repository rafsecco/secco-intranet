using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Dados para ligar um recurso.</summary>
/// <param name="TenantId">Tenant.</param>
/// <param name="Recurso">Segmento de rota do recurso (não confiável).</param>
public sealed record LigarRecursoCommand(Guid TenantId, string? Recurso);

/// <summary>
/// Liga um recurso para o tenant. O SecureGate só marca o cadastro; LogStream e NotificationHub
/// provisionam o banco com os padrões da plataforma. Recurso já ligado não é reprovisionado. Em
/// modo script, o script volta para quem chamou e <b>nunca</b> entra na trilha.
/// </summary>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="gestao">API de tenants.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class LigarRecursoHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Tenant e recurso.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>O resultado do provisionamento (com o script, em modo script).</returns>
	public async Task<Result<ProvisionamentoDto>> HandleAsync(LigarRecursoCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var registro = await cadastro.ObterParaEdicaoAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

		if (registro is null)
		{
			return Result.Failure<ProvisionamentoDto>(IntranetErrors.Tenants.NaoEncontrado);
		}

		if (!RecursosDaPlataforma.TentarLer(command.Recurso, out var recurso))
		{
			return Result.Failure<ProvisionamentoDto>(IntranetErrors.Tenants.RecursoInvalido);
		}

		if (RecursosDaPlataforma.Produto(recurso) is not { } produto)
		{
			if (!registro.HabilitarSecureGate())
			{
				return Result.Failure<ProvisionamentoDto>(IntranetErrors.Tenants.RecursoJaLigado);
			}

			await cadastro.SalvarAsync(cancellationToken).ConfigureAwait(false);
			await AuditoriaDeTenants.RecursoAsync(
				trilha, VerbosDeAuditoria.TenantRecursoLigar, registro.TenantId, registro.Sistema, recurso, true, cancellationToken)
				.ConfigureAwait(false);

			return Result.Success(new ProvisionamentoDto(true, null));
		}

		var tenant = await gestao.ObterTenantAsync(registro.TenantId, cancellationToken).ConfigureAwait(false);

		if (tenant.IsFailure)
		{
			return Result.Failure<ProvisionamentoDto>(tenant.Error);
		}

		if (tenant.Value.Produtos.Contains(produto, StringComparer.OrdinalIgnoreCase))
		{
			return Result.Failure<ProvisionamentoDto>(IntranetErrors.Tenants.RecursoJaLigado);
		}

		var provisionado = await gestao.ProvisionarAsync(registro.TenantId, produto, cancellationToken).ConfigureAwait(false);

		if (provisionado.IsFailure)
		{
			return provisionado;
		}

		await AuditoriaDeTenants.RecursoAsync(
			trilha, VerbosDeAuditoria.TenantRecursoLigar, registro.TenantId, registro.Sistema, recurso,
			provisionado.Value.Aplicado, cancellationToken).ConfigureAwait(false);

		if (!provisionado.Value.Aplicado)
		{
			await AuditoriaDeTenants.RecursoAsync(
				trilha, VerbosDeAuditoria.TenantRecursoScriptGerado, registro.TenantId, registro.Sistema, recurso, false,
				cancellationToken).ConfigureAwait(false);
		}

		return provisionado;
	}
}
