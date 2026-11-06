using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="Publicacao"/>. Nomes de tabela e coluna vêm da convention
/// (ADR-0017); aqui fica o que ela não decide.
/// </summary>
internal sealed class PublicacaoConfiguration : IEntityTypeConfiguration<Publicacao>
{
	public void Configure(EntityTypeBuilder<Publicacao> builder)
	{
		// Sem navegação: o agregado não carrega o Setor. A relação existe para a convention
		// reconhecer SetorId como chave estrangeira e gerar id_fk_setor (ADR-0017).
		builder
			.HasOne<Setor>()
			.WithMany()
			.HasForeignKey(publicacao => publicacao.SetorId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.Property(publicacao => publicacao.Titulo).HasMaxLength(256);

		// Acima de 4000 o SQL Server troca nvarchar(n) por nvarchar(max); o limite de 20000 e imposto
		// pela validacao da aplicacao. No Postgres a coluna continua com limite real (varchar(20000)).
		builder.Property(publicacao => publicacao.Corpo).HasMaxLength(20_000);
		builder.Property(publicacao => publicacao.CriadoPor).HasMaxLength(256);

		builder.Property(publicacao => publicacao.ImagemCaminhoRelativo).HasMaxLength(256);
		builder.Property(publicacao => publicacao.ImagemChaveEmbrulhada).HasMaxLength(256);
		builder.Property(publicacao => publicacao.ImagemDescricao).HasMaxLength(Publicacao.LimiteDaDescricaoDaImagem);

		builder.HasIndex(publicacao => publicacao.SetorId);

		// A consulta do mural filtra por ativo e ordena por data de publicação.
		builder.HasIndex(publicacao => new { publicacao.Ativo, publicacao.PublicadoEm });
	}
}
