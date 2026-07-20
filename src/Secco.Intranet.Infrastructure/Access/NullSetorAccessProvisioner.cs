using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Adapter no-op da porta <see cref="ISetorAccessProvisioner"/> — modo DEV/Testing, usado
/// quando a seção <c>Secco:SecureGate</c> não está configurada (ver
/// <see cref="IntranetInfrastructureExtensions"/>). Sempre retorna sucesso: sem SecureGate
/// remoto configurado, não há Roles a provisionar.
/// </summary>
/// <param name="logger">Logger para o aviso de modo desativado (nível Debug).</param>
public sealed class NullSetorAccessProvisioner(ILogger<NullSetorAccessProvisioner> logger) : ISetorAccessProvisioner
{
	/// <inheritdoc />
	public Task<Result> EnsureSetorRolesAsync(string slug, CancellationToken cancellationToken = default)
	{
		logger.LogDebug("provisionamento de roles desativado — seção Secco:SecureGate ausente");

		return Task.FromResult(Result.Success());
	}
}
