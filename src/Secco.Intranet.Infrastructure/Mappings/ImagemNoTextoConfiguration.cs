using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Publicacoes;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="ImagemNoTexto"/>. Nomes de tabela/colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class ImagemNoTextoConfiguration : IEntityTypeConfiguration<ImagemNoTexto>
{
	public void Configure(EntityTypeBuilder<ImagemNoTexto> builder)
	{
		// A publicação nunca é excluída: sem cascata.
		builder
			.HasOne<Publicacao>()
			.WithMany()
			.HasForeignKey(imagem => imagem.PublicacaoId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.Property(imagem => imagem.CaminhoRelativo).HasMaxLength(256);
		builder.Property(imagem => imagem.ChaveEmbrulhada).HasMaxLength(256);

		builder.HasIndex(imagem => imagem.PublicacaoId);
	}
}
