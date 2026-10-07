using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>
/// Porta da API de tenants do SecureGate (ADR-0008), pelo client administrativo da Intranet.
/// Falha de rede, de status e timeout viram <see cref="Result"/> — quem administra precisa saber
/// que a ação não aconteceu. Nenhum método recebe produto livre: quem chama já passou pela lista
/// fechada de <see cref="RecursosDaPlataforma"/>.
/// </summary>
public interface IGestaoDeTenants
{
	/// <summary>Todos os tenants da instalação.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<TenantDaPlataformaDto>>> ListarTenantsAsync(CancellationToken cancellationToken = default);

	/// <summary>Um tenant, com os produtos que têm banco no catálogo.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<TenantDaPlataformaDetalheDto>> ObterTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Estado dos bancos do tenant.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<StatusDoBancoDto>>> ObterStatusDosBancosAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Cria um tenant.</summary>
	/// <param name="nome">Nome, já validado.</param>
	/// <param name="slug">Slug, já validado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<TenantDaPlataformaDto>> CriarTenantAsync(string nome, string slug, CancellationToken cancellationToken = default);

	/// <summary>Provisiona o banco do tenant no produto, com o alvo e os nomes padrão da plataforma.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="produto">Produto, de <see cref="RecursosDaPlataforma.Produto"/>.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<ProvisionamentoDto>> ProvisionarAsync(Guid tenantId, string produto, CancellationToken cancellationToken = default);

	/// <summary>Ativa o tenant.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> AtivarAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Desativa o tenant: login e catálogo param em até um TTL de cache.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> DesativarAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
