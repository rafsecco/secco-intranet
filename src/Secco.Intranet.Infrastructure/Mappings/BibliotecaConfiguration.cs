using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Bibliotecas;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="Biblioteca"/>. Nomes de tabela e colunas vêm da convention
/// (ADR-0017); aqui ficam os limites e a unicidade do slug por tenant.
/// </summary>
internal sealed class BibliotecaConfiguration : IEntityTypeConfiguration<Biblioteca>
{
	public void Configure(EntityTypeBuilder<Biblioteca> builder)
	{
		// Mesmo limite do nome do setor: a coluna indexada do SQL Server cabe em 450.
		builder.Property(biblioteca => biblioteca.Nome).HasMaxLength(450);
		builder.Property(biblioteca => biblioteca.Slug).HasMaxLength(Biblioteca.TamanhoMaximoDoSlug);
		builder.Property(biblioteca => biblioteca.Icone).HasMaxLength(64);

		builder.HasIndex(biblioteca => biblioteca.Slug).IsUnique().HasDatabaseName("uk_bibliotecas_ds_slug");
	}
}
