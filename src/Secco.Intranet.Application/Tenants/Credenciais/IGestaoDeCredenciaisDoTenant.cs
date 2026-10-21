using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Credenciais;

/// <summary>Porta das credenciais OAuth de um tenant administrado, no SecureGate.</summary>
public interface IGestaoDeCredenciaisDoTenant
{
	/// <summary>Lista as credenciais do tenant.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	Task<Result<IReadOnlyList<CredencialDto>>> ListarAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Obtém uma credencial do tenant.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="clientId">Credencial.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	Task<Result<CredencialDto>> ObterAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default);

	/// <summary>Emite uma credencial e devolve o secret, que não se recupera depois.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="dados">Dados da credencial.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	Task<Result<CredencialEmitidaDto>> EmitirAsync(Guid tenantId, DadosDaCredencial dados, CancellationToken cancellationToken = default);

	/// <summary>Altera nome, escopos, perfis e endereços de uma credencial.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="clientId">Credencial.</param>
	/// <param name="dados">Novos dados.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	Task<Result> AlterarAsync(Guid tenantId, string clientId, DadosDaCredencial dados, CancellationToken cancellationToken = default);

	/// <summary>Troca o secret de uma credencial; o antigo deixa de valer.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="clientId">Credencial.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	Task<Result<SecretDaCredencial>> RotacionarSecretAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default);

	/// <summary>Revoga uma credencial.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="clientId">Credencial.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	Task<Result> RevogarAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default);
}
