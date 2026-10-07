namespace Secco.Intranet.Application.Tenants;

/// <summary>
/// Se o tenant está no cadastro — a checagem de IDOR que o filtro <c>[TenantAdministrado]</c> faz
/// antes de qualquer action com <c>{tenantId}</c>. Existe como handler para a Web não tocar no
/// repositório (ADR-0002).
/// </summary>
/// <param name="cadastro">Cadastro local.</param>
public sealed class VerificarTenantAdministradoHandler(ITenantsAdministrados cadastro)
{
	/// <summary>Executa a checagem.</summary>
	/// <param name="tenantId">Id recebido na rota.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<bool> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		tenantId != Guid.Empty
		&& await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false) is not null;
}
