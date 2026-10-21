using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants.Credenciais;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>Adapter no-op para DEV e Testing sem a seção <c>Secco:SecureGate</c>.</summary>
public sealed class GestaoDeCredenciaisDoTenantIndisponivel : IGestaoDeCredenciaisDoTenant
{
	private static readonly Error Erro = IntranetErrors.Credenciais.NaoConfigurado;

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<CredencialDto>>> ListarAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<CredencialDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<CredencialDto>> ObterAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<CredencialDto>(Erro));

	/// <inheritdoc />
	public Task<Result<CredencialEmitidaDto>> EmitirAsync(Guid tenantId, DadosDaCredencial dados, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<CredencialEmitidaDto>(Erro));

	/// <inheritdoc />
	public Task<Result> AlterarAsync(Guid tenantId, string clientId, DadosDaCredencial dados, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result<SecretDaCredencial>> RotacionarSecretAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<SecretDaCredencial>(Erro));

	/// <inheritdoc />
	public Task<Result> RevogarAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));
}
