using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="PosicaoMenu"/>. Nomes de tabela/colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class PosicaoMenuConfiguration : IEntityTypeConfiguration<PosicaoMenu>
{
	public void Configure(EntityTypeBuilder<PosicaoMenu> builder)
	{
		builder.Property(posicao => posicao.Chave).HasMaxLength(64);

		builder.HasIndex(posicao => new { posicao.Grupo, posicao.Chave }).IsUnique();
	}
}
