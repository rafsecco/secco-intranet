using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Migrations.Postgres;

/// <summary>
/// Fábrica de design-time do <c>dotnet ef</c> para migrations PostgreSQL
/// (connection string fictícia: geração de migration não conecta ao banco).
/// </summary>
public sealed class IntranetPostgresDbContextFactory : IDesignTimeDbContextFactory<IntranetDbContext>
{
	public IntranetDbContext CreateDbContext(string[] args) =>
		new(new DbContextOptionsBuilder<IntranetDbContext>()
			.UseNpgsql(
				"Host=design-time;Database=design-time",
				npgsql => npgsql.MigrationsAssembly(typeof(IntranetPostgresDbContextFactory).Assembly.GetName().Name))
			.Options);
}
