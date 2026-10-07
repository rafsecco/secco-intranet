using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência do cadastro de tenants administrados no banco do tenant atual.</summary>
internal sealed class TenantsAdministradosRepository(IntranetDbContext context) : ITenantsAdministrados
{
	public async Task<IReadOnlyList<TenantAdministrado>> ListarAsync(CancellationToken cancellationToken = default) =>
		await context.TenantsAdministrados
			.AsNoTracking()
			.OrderBy(tenant => tenant.Sistema)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<TenantAdministrado?> ObterAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		await context.TenantsAdministrados
			.AsNoTracking()
			.FirstOrDefaultAsync(tenant => tenant.TenantId == tenantId, cancellationToken)
			.ConfigureAwait(false);

	public async Task<TenantAdministrado?> ObterParaEdicaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		await context.TenantsAdministrados
			.FirstOrDefaultAsync(tenant => tenant.TenantId == tenantId, cancellationToken)
			.ConfigureAwait(false);

	public async Task<bool> TentarAdicionarAsync(TenantAdministrado tenant, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(tenant);

		context.TenantsAdministrados.Add(tenant);

		try
		{
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

			return true;
		}
		catch (DbUpdateException)
		{
			context.Entry(tenant).State = EntityState.Detached;

			var jaExiste = await context.TenantsAdministrados
				.AsNoTracking()
				.AnyAsync(outro => outro.TenantId == tenant.TenantId, cancellationToken)
				.ConfigureAwait(false);

			// Outro pedido registrou o mesmo tenant um instante antes: não é falha de
			// infraestrutura. Qualquer outra causa continua subindo.
			if (jaExiste)
			{
				return false;
			}

			throw;
		}
	}

	public Task SalvarAsync(CancellationToken cancellationToken = default) =>
		context.SaveChangesAsync(cancellationToken);
}
