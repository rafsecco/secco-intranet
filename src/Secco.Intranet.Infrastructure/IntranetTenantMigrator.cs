using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Migrations;

namespace Secco.Intranet.Infrastructure;

/// <summary>
/// Migrations de TODOS os tenants do catálogo — o processo controlado da ADR-0005, executado pelo
/// verbo <c>migrate</c> e pelo startup em Development (ADR-0038 da plataforma). Um tenant que
/// falha não interrompe os demais; o alvo devolvido é o id do tenant, nunca a connection string.
/// </summary>
/// <param name="catalog">Catálogo de tenants.</param>
/// <param name="databaseOptions">Engine do banco (ADR-0018).</param>
/// <param name="logger">Logger.</param>
internal sealed partial class IntranetTenantMigrator(
	ITenantCatalog catalog,
	IntranetDatabaseOptions databaseOptions,
	ILogger<IntranetTenantMigrator> logger) : ISeccoDatabaseMigrator
{
	public string Name => "Intranet (tenants)";

	public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
	{
		var failures = new List<string>();

		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			try
			{
				// Sem interceptor de primeiro uso: este contexto só serve para migrar
				await using var context = new IntranetDbContext(IntranetDatabaseProviderConfigurator.CreateOptions(
					databaseOptions.Provider, tenant.ConnectionString));
				await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
			}
#pragma warning disable CA1031 // Falha de um tenant não interrompe os demais; logada e contada
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				LogTenantFailed(logger, tenant.TenantId, exception.Message);
				failures.Add(tenant.TenantId.ToString());
			}
#pragma warning restore CA1031
		}

		return failures;
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Migrations da Intranet falharam no tenant {TenantId}: {Reason}")]
	private static partial void LogTenantFailed(ILogger logger, Guid tenantId, string reason);
}
