using Secco.Intranet.Domain.Tenants;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Cadastro local de tenants administrados, no banco do tenant da Intranet.</summary>
public interface ITenantsAdministrados
{
	/// <summary>Todos os tenants administrados, desrastreados, por sistema.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantAdministrado>> ListarAsync(CancellationToken cancellationToken = default);

	/// <summary>Busca pelo id do tenant no SecureGate, <b>desrastreado</b> — caminho de leitura.</summary>
	/// <param name="tenantId">Id do tenant no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantAdministrado?> ObterAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Busca <b>rastreado</b>, para alteração seguida de <see cref="SalvarAsync"/>.</summary>
	/// <param name="tenantId">Id do tenant no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantAdministrado?> ObterParaEdicaoAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Persiste um registro novo. Devolve <c>false</c> — sem lançar — se o tenant já está no
	/// cadastro (dois registros simultâneos do mesmo tenant).
	/// </summary>
	/// <param name="tenant">Registro novo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> TentarAdicionarAsync(TenantAdministrado tenant, CancellationToken cancellationToken = default);

	/// <summary>Grava as alterações dos registros rastreados.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SalvarAsync(CancellationToken cancellationToken = default);
}
