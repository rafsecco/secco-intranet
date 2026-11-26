using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Infrastructure.Armazenamento;
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
/// <param name="reembrulho">Reembrulho das chaves de arquivo para a chave mestra ativa, após a migration.</param>
/// <param name="logger">Logger.</param>
internal sealed partial class IntranetTenantMigrator(
	ITenantCatalog catalog,
	IntranetDatabaseOptions databaseOptions,
	ReembrulhoDeChavesDeArquivo reembrulho,
	ILogger<IntranetTenantMigrator> logger) : ISeccoDatabaseMigrator
{
	public string Name => "Intranet (tenants)";

	public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
	{
		var failures = new List<string>();

		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			var semChaveConhecida = false;

			try
			{
				// Sem interceptor de primeiro uso: este contexto só serve para migrar
				await using var context = new IntranetDbContext(IntranetDatabaseProviderConfigurator.CreateOptions(
					databaseOptions.Provider, tenant.ConnectionString));
				await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

				var resultado = await reembrulho.ReembrulharAsync(context, cancellationToken).ConfigureAwait(false);
				foreach (var coluna in resultado.Colunas.Where(c => c.Reembrulhadas != 0 || c.SemChaveConhecida != 0))
				{
					LogColunaReembrulhada(logger, tenant.TenantId, coluna.Tabela, coluna.Reembrulhadas, coluna.SemChaveConhecida);
				}

				if (resultado.SemChaveConhecida > 0)
				{
					semChaveConhecida = true;
					LogSemChaveConhecida(logger, tenant.TenantId);
				}
			}
#pragma warning disable CA1031 // Falha de um tenant não interrompe os demais; logada e contada
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				LogTenantFailed(logger, tenant.TenantId, exception.Message);
				failures.Add(tenant.TenantId.ToString());
			}
#pragma warning restore CA1031

			// Fora do try: o catch já cobre a exceção, e um tenant nunca entra duas vezes na lista
			if (semChaveConhecida)
			{
				failures.Add(tenant.TenantId.ToString());
			}
		}

		return failures;
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Migrations da Intranet falharam no tenant {TenantId}: {Reason}")]
	private static partial void LogTenantFailed(ILogger logger, Guid tenantId, string reason);

	[LoggerMessage(Level = LogLevel.Information, Message = "Chaves de arquivo reembrulhadas no tenant {TenantId}, tabela {Tabela}: {Reembrulhadas} trocadas, {SemChaveConhecida} sem chave conhecida")]
	private static partial void LogColunaReembrulhada(ILogger logger, Guid tenantId, string tabela, int reembrulhadas, int semChaveConhecida);

	[LoggerMessage(Level = LogLevel.Error, Message = "O tenant {TenantId} tem chave de arquivo que nenhuma chave mestra configurada abre; provavelmente uma chave aposentada foi retirada cedo demais")]
	private static partial void LogSemChaveConhecida(ILogger logger, Guid tenantId);
}
