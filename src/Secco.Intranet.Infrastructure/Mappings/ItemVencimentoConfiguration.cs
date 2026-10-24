using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Domain.Vencimentos;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="ItemVencimento"/>. Nomes de tabela/colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class ItemVencimentoConfiguration : IEntityTypeConfiguration<ItemVencimento>
{
	private static readonly ValueConverter<IReadOnlyList<int>, string> ConversorDeAntecedencias = new(
		antecedencias => string.Join(',', antecedencias),
		texto => texto.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray());

	private static readonly ValueComparer<IReadOnlyList<int>> ComparadorDeAntecedencias = new(
		(a, b) => a != null && b != null && a.SequenceEqual(b),
		lista => lista.Aggregate(0, (hash, valor) => HashCode.Combine(hash, valor)),
		lista => lista.ToArray());

	public void Configure(EntityTypeBuilder<ItemVencimento> builder)
	{
		// Sem navegação: o agregado não precisa carregar o Setor — só a FK, para a convention
		// reconhecer a coluna (mesmo padrão de ItemInventarioConfiguration).
		builder
			.HasOne<Setor>()
			.WithMany()
			.HasForeignKey(item => item.SetorId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.Property(item => item.Titulo).HasMaxLength(256);
		builder.Property(item => item.Descricao).HasMaxLength(4_096);
		builder.Property(item => item.EncerradoPor).HasMaxLength(256);

		// Lista curta de inteiros ("30,7,1"): até 6 valores de até 3 dígitos mais separadores.
		builder.Property(item => item.Antecedencias)
			.HasMaxLength(32)
			.HasColumnName("ds_antecedencias")
			.HasConversion(ConversorDeAntecedencias, ComparadorDeAntecedencias);

		// A listagem do setor ordena por data de vencimento.
		builder.HasIndex(item => new { item.SetorId, item.DataVencimento });
	}
}

/// <summary>Mapeamento de <see cref="RenovacaoDeVencimento"/>.</summary>
internal sealed class RenovacaoDeVencimentoConfiguration : IEntityTypeConfiguration<RenovacaoDeVencimento>
{
	public void Configure(EntityTypeBuilder<RenovacaoDeVencimento> builder)
	{
		// O item nunca é excluído: sem cascata.
		builder
			.HasOne<ItemVencimento>()
			.WithMany()
			.HasForeignKey(renovacao => renovacao.ItemVencimentoId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.Property(renovacao => renovacao.RenovadoPor).HasMaxLength(256);
		builder.Property(renovacao => renovacao.Observacao).HasMaxLength(4_096);

		builder.HasIndex(renovacao => renovacao.ItemVencimentoId);
	}
}

/// <summary>Mapeamento de <see cref="AvisoDeVencimentoEnviado"/>.</summary>
internal sealed class AvisoDeVencimentoEnviadoConfiguration : IEntityTypeConfiguration<AvisoDeVencimentoEnviado>
{
	public void Configure(EntityTypeBuilder<AvisoDeVencimentoEnviado> builder)
	{
		builder
			.HasOne<ItemVencimento>()
			.WithMany()
			.HasForeignKey(aviso => aviso.ItemVencimentoId)
			.OnDelete(DeleteBehavior.Restrict);

		// Um marco só é tratado uma vez por ciclo: é esta unicidade que impede o aviso duplicado
		// quando dois processos varrem ao mesmo tempo.
		builder.HasIndex(aviso => new { aviso.ItemVencimentoId, aviso.Ciclo, aviso.Marco }).IsUnique();
	}
}
