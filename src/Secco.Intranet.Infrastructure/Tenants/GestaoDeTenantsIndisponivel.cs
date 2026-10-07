using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Tenants;

/// <summary>
/// No-op de <see cref="IGestaoDeTenants"/> para DEV e Testing sem <c>Secco:SecureGate</c>. Responde
/// "não configurado" em tudo: fingir que um tenant foi criado seria mentir para quem administra.
/// </summary>
public sealed class GestaoDeTenantsIndisponivel : IGestaoDeTenants
{
	private static readonly Error Erro = IntranetErrors.Tenants.NaoConfigurado;

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<TenantDaPlataformaDto>>> ListarTenantsAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<TenantDaPlataformaDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<TenantDaPlataformaDetalheDto>> ObterTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<TenantDaPlataformaDetalheDto>(Erro));

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<StatusDoBancoDto>>> ObterStatusDosBancosAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<StatusDoBancoDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<TenantDaPlataformaDto>> CriarTenantAsync(string nome, string slug, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<TenantDaPlataformaDto>(Erro));

	/// <inheritdoc />
	public Task<Result<ProvisionamentoDto>> ProvisionarAsync(Guid tenantId, string produto, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<ProvisionamentoDto>(Erro));

	/// <inheritdoc />
	public Task<Result> AtivarAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> DesativarAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));
}
