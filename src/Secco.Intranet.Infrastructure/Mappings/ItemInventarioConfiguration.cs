using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Inventario;
using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="ItemInventario"/>. Nomes de tabela/colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class ItemInventarioConfiguration : IEntityTypeConfiguration<ItemInventario>
{
	public void Configure(EntityTypeBuilder<ItemInventario> builder)
	{
		// SetorId é informativo (ver comentário na entidade) — sem navegação, porque o
		// agregado não precisa carregar o Setor; a relação existe só para a convention
		// reconhecer a coluna como id_fk_setor (mesmo padrão de DocumentoConfiguration).
		// Nullable: diferente de Documento, aqui o setor não é obrigatório.
		builder
			.HasOne<Setor>()
			.WithMany()
			.HasForeignKey(item => item.SetorId)
			.OnDelete(DeleteBehavior.Restrict)
			.IsRequired(false);

		builder.Property(item => item.Nome).HasMaxLength(256);
		builder.Property(item => item.Descricao).HasMaxLength(4_096);
		builder.Property(item => item.Categoria).HasMaxLength(128);
		builder.Property(item => item.CodigoPatrimonio).HasMaxLength(128);
		builder.Property(item => item.AtribuidoANome).HasMaxLength(256);

		// O filtro mais comum da listagem (Decisões da spec: baixado some por padrão).
		builder.HasIndex(item => item.Status);
		builder.HasIndex(item => item.SetorId);
		builder.HasIndex(item => item.Nome);
	}
}
