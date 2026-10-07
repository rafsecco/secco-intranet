using Secco.Intranet.Application.Tenants;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.Intranet.Infrastructure.Tenants;

/// <summary>
/// Tenants protegidos: o de instalação da plataforma, todo tenant que tem banco no catálogo da
/// Intranet (ela própria e as filiais da mesma instalação) e o da requisição, por garantia —
/// em DEV o catálogo pode vir de configuração e não listar o tenant em uso.
/// </summary>
/// <param name="catalogo">Catálogo de tenants do produto Intranet.</param>
/// <param name="tenantContext">Tenant da requisição.</param>
public sealed class TenantsProtegidosDoCatalogo(ITenantCatalog catalogo, ITenantContext tenantContext) : ITenantsProtegidos
{
	/// <summary>
	/// Tenant de instalação da plataforma — espelho de <c>SecureGatePlatform.TenantId</c> no
	/// <c>secco-platform</c>. É um Guid fixo por decisão de lá (localizado sempre por Guid, nunca por slug).
	/// </summary>
	public static readonly Guid TenantDeInstalacao = Guid.Parse("018f0000-0000-7000-8000-0000000000ff");

	/// <inheritdoc />
	public async Task<IReadOnlySet<Guid>> ListarAsync(CancellationToken cancellationToken = default)
	{
		var protegidos = new HashSet<Guid> { TenantDeInstalacao };

		foreach (var tenant in await catalogo.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			protegidos.Add(tenant.TenantId);
		}

		if (tenantContext.IsResolved && tenantContext.TenantId is { } proprio)
		{
			protegidos.Add(proprio);
		}

		return protegidos;
	}
}
