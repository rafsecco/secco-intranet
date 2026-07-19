using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="Setor"/>. Nomes de tabela/colunas/constraints vêm da
/// <c>SeccoNamingConvention</c> (ADR-0017) — aqui só o que a convention não decide:
/// índices e a unicidade do slug por tenant.
/// </summary>
internal sealed class SetorConfiguration : IEntityTypeConfiguration<Setor>
{
	public void Configure(EntityTypeBuilder<Setor> builder)
	{
		builder.HasIndex(setor => setor.Slug).IsUnique();
		builder.HasIndex(setor => setor.Nome);
		builder.HasIndex(setor => setor.Ativo);
	}
}
