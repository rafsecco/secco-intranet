using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="ItemMenu"/>. Nomes de tabela/colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class ItemMenuConfiguration : IEntityTypeConfiguration<ItemMenu>
{
	public void Configure(EntityTypeBuilder<ItemMenu> builder)
	{
		// Sem navegação: o agregado não precisa carregar o Setor nem o pai — só as FKs, para a
		// convention reconhecer as colunas como chave estrangeira (mesmo padrão de
		// DocumentoConfiguration).
		builder
			.HasOne<Setor>()
			.WithMany()
			.HasForeignKey(item => item.SetorId)
			.OnDelete(DeleteBehavior.Restrict);

		builder
			.HasOne<ItemMenu>()
			.WithMany()
			.HasForeignKey(item => item.ParentId)
			.OnDelete(DeleteBehavior.Restrict)
			.IsRequired(false);

		builder.Property(item => item.Nome).HasMaxLength(256);
		builder.Property(item => item.Slug).HasMaxLength(128);
		builder.Property(item => item.Rota).HasMaxLength(512);
		builder.Property(item => item.Icone).HasMaxLength(64);

		builder.HasIndex(item => item.SetorId);
		// Slug só precisa ser único entre irmãos; a aplicação confere antes de gravar. O índice
		// (não único) serve à busca de filhos por pai.
		builder.HasIndex(item => new { item.ParentId, item.Slug });
	}
}
