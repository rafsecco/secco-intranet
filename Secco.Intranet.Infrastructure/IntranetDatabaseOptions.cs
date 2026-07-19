using Microsoft.EntityFrameworkCore;

namespace Secco.Intranet.Infrastructure;

/// <summary>Engines suportados (ADR-0018).</summary>
public enum IntranetDatabaseProvider
{
	/// <summary>Provider padrão da plataforma.</summary>
	SqlServer = 0,

	/// <summary>Segundo provider suportado.</summary>
	PostgreSql = 1,
}

/// <summary>
/// Seleção de engine (seção <c>Intranet:Database</c>). Todos os bancos de tenant de um
/// deployment usam o mesmo provider; as connection strings do catálogo devem corresponder.
/// </summary>
public sealed class IntranetDatabaseOptions
{
	/// <summary>Engine dos bancos de tenant (default: SQL Server, ADR-0018).</summary>
	public IntranetDatabaseProvider Provider { get; set; } = IntranetDatabaseProvider.SqlServer;
}

/// <summary>Aplicação do provider selecionado (assembly de migrations por engine, ADR-0018).</summary>
internal static class IntranetDatabaseProviderConfigurator
{
	private const string SqlServerMigrationsAssembly = "Secco.Intranet.Migrations.SqlServer";
	private const string PostgresMigrationsAssembly = "Secco.Intranet.Migrations.Postgres";

	public static void Configure(
		DbContextOptionsBuilder optionsBuilder,
		IntranetDatabaseProvider provider,
		string connectionString)
	{
		switch (provider)
		{
			case IntranetDatabaseProvider.PostgreSql:
				optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PostgresMigrationsAssembly));
				break;
			default:
				optionsBuilder.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(SqlServerMigrationsAssembly));
				break;
		}
	}

	/// <summary>Cria options do contexto para processos fora do request (migrations, manutenção).</summary>
	public static DbContextOptions<Contexts.IntranetDbContext> CreateOptions(
		IntranetDatabaseProvider provider,
		string connectionString)
	{
		var builder = new DbContextOptionsBuilder<Contexts.IntranetDbContext>();
		Configure(builder, provider, connectionString);
		return builder.Options;
	}
}
