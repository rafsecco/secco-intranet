using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Bibliotecas;
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
			.IsRequired(false)
			.OnDelete(DeleteBehavior.Restrict);

		// O dono é exatamente um entre setor e biblioteca (ck_documentos_dono, abaixo); por isso as duas
		// chaves estrangeiras são opcionais.
		builder
			.HasOne<Biblioteca>()
			.WithMany()
			.HasForeignKey(documento => documento.BibliotecaId)
			.HasConstraintName("fk_documentos_biblioteca")
			.IsRequired(false)
			.OnDelete(DeleteBehavior.Restrict);

		// SQL que vale nos dois engines (SQL Server e PostgreSQL).
		builder.ToTable(tabela => tabela.HasCheckConstraint(
			"ck_documentos_dono",
			"(id_fk_setor IS NOT NULL AND id_fk_biblioteca IS NULL) OR (id_fk_setor IS NULL AND id_fk_biblioteca IS NOT NULL)"));

		builder
			.HasOne<Documento>()
			.WithMany()
			.HasForeignKey(documento => documento.ParentId)
			.OnDelete(DeleteBehavior.Restrict)
			.IsRequired(false);

		// Sem limite explicito o provider gera nvarchar(max)/text para tudo. Caminho e chave
		// embrulhada tem tamanho conhecido; titulo e descricao seguem os limites de entrada.
		builder.Property(documento => documento.Titulo).HasMaxLength(256);
		builder.Property(documento => documento.Descricao).HasMaxLength(4_096);
		builder.Property(documento => documento.NomeArquivo).HasMaxLength(512);
		builder.Property(documento => documento.ContentType).HasMaxLength(128);
		builder.Property(documento => documento.CaminhoRelativo).HasMaxLength(256);
		builder.Property(documento => documento.ChaveEmbrulhada).HasMaxLength(256);
		builder.Property(documento => documento.CriadoPor).HasMaxLength(256);

		builder.HasIndex(documento => new { documento.SetorId, documento.ParentId, documento.Ordem });
		builder
			.HasIndex(documento => new { documento.BibliotecaId, documento.ParentId, documento.Ordem })
			.HasDatabaseName("idx_documentos_id_fk_biblioteca_id_fk_parent_nr_ordem");
		builder.HasIndex(documento => documento.Ativo);
		builder.HasIndex(documento => documento.CreatedAt);
	}
}
