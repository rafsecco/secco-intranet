using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="Documento"/>. Nomes de tabela e colunas vêm da convention
/// (ADR-0017); aqui fica o que ela não decide.
/// </summary>
internal sealed class DocumentoConfiguration : IEntityTypeConfiguration<Documento>
{
	public void Configure(EntityTypeBuilder<Documento> builder)
	{
		// A relação é declarada sem navegação: o agregado Documento não precisa carregar o
		// Setor, mas sem a relação a convention não reconheceria SetorId como chave
		// estrangeira e a coluna sairia fora do padrão id_fk_ (ADR-0017).
		builder
			.HasOne<Setor>()
			.WithMany()
			.HasForeignKey(documento => documento.SetorId)
			.OnDelete(DeleteBehavior.Restrict);

		// Sem limite explicito o provider gera nvarchar(max)/text para tudo. Caminho e chave
		// embrulhada tem tamanho conhecido; titulo e descricao seguem os limites de entrada.
		builder.Property(documento => documento.Titulo).HasMaxLength(256);
		builder.Property(documento => documento.Descricao).HasMaxLength(4_096);
		builder.Property(documento => documento.NomeArquivo).HasMaxLength(512);
		builder.Property(documento => documento.ContentType).HasMaxLength(128);
		builder.Property(documento => documento.CaminhoRelativo).HasMaxLength(256);
		builder.Property(documento => documento.ChaveEmbrulhada).HasMaxLength(256);
		builder.Property(documento => documento.CriadoPor).HasMaxLength(256);

		builder.HasIndex(documento => documento.SetorId);
		builder.HasIndex(documento => documento.Ativo);
		builder.HasIndex(documento => documento.CreatedAt);
	}
}
