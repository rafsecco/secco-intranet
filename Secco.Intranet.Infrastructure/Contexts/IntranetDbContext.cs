using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Domain.Setores;
using Secco.SDK.EntityFrameworkCore;

namespace Secco.Intranet.Infrastructure.Contexts;

/// <summary>
/// Contexto de dados sobre o banco do tenant atual (ADR-0005) — a connection string vem
/// do <c>ITenantConnectionFactory</c> a cada requisição. Herda de <see cref="SeccoDbContext"/>:
/// nomenclatura da ADR-0017 aplicada por convention — ninguém digita nomes de coluna.
/// </summary>
public sealed class IntranetDbContext(DbContextOptions<IntranetDbContext> options)
	: SeccoDbContext(options)
{
	/// <summary>Setores/departamentos (tabela <c>tb_setores</c>).</summary>
	public DbSet<Setor> Setores => Set<Setor>();

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.ApplyConfigurationsFromAssembly(typeof(IntranetDbContext).Assembly);
	}
}
