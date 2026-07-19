using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Migrations.SqlServer;

/// <summary>
/// Fábrica de design-time do <c>dotnet ef</c> para migrations SQL Server
/// (connection string fictícia: geração de migration não conecta ao banco).
/// </summary>
public sealed class IntranetSqlServerDbContextFactory : IDesignTimeDbContextFactory<IntranetDbContext>
{
	public IntranetDbContext CreateDbContext(string[] args) =>
		new(new DbContextOptionsBuilder<IntranetDbContext>()
			.UseSqlServer(
				"Server=design-time;Database=design-time;Encrypt=false",
				sql => sql.MigrationsAssembly(typeof(IntranetSqlServerDbContextFactory).Assembly.GetName().Name))
			.Options);
}
