using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Publicacoes;
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

	/// <summary>Documentos publicados por setor (tabela <c>tb_documentos</c>).</summary>
	public DbSet<Documento> Documentos => Set<Documento>();

	/// <summary>Publicações do mural (tabela <c>tb_publicacoes</c>).</summary>
	public DbSet<Publicacao> Publicacoes => Set<Publicacao>();

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.ApplyConfigurationsFromAssembly(typeof(IntranetDbContext).Assembly);
	}
}
