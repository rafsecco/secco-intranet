using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>O que toda tela de acesso do tenant mostra no topo — em qual sistema se está mexendo.</summary>
/// <param name="TenantId">Id.</param>
/// <param name="Sistema">Sistema que o tenant representa.</param>
/// <param name="Nome">Nome na plataforma.</param>
/// <param name="Ativo">Se o tenant está ativo.</param>
public sealed record CabecalhoDoTenantDto(Guid TenantId, string Sistema, string Nome, bool Ativo);

/// <summary>Se o tenant está no cadastro com o SecureGate ligado — a checagem do filtro <c>[SecureGateLigado]</c>.</summary>
/// <param name="cadastro">Cadastro local.</param>
public sealed class VerificarSecureGateLigadoHandler(ITenantsAdministrados cadastro)
{
	/// <summary>Executa a checagem.</summary>
	/// <param name="tenantId">Id recebido na rota.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<bool> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false) is { SecureGateHabilitado: true };
}

/// <summary>Monta o cabeçalho: sistema do cadastro, nome e situação da plataforma.</summary>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="gestao">Porta de tenants.</param>
public sealed class ObterCabecalhoDoTenantHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao)
{
	/// <summary>Executa a consulta.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<CabecalhoDoTenantDto>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var registro = await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (registro is null)
		{
			return Result.Failure<CabecalhoDoTenantDto>(IntranetErrors.Tenants.NaoEncontrado);
		}

		var tenant = await gestao.ObterTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return tenant.IsFailure
			? Result.Failure<CabecalhoDoTenantDto>(tenant.Error)
			: Result.Success(new CabecalhoDoTenantDto(tenantId, registro.Sistema, tenant.Value.Nome, tenant.Value.Ativo));
	}
}
