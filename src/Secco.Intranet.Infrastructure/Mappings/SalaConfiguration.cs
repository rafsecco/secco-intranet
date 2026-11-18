using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Salas;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="Sala"/>. Nomes de tabela/colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class SalaConfiguration : IEntityTypeConfiguration<Sala>
{
	public void Configure(EntityTypeBuilder<Sala> builder)
	{
		builder.Property(sala => sala.Nome).HasMaxLength(Sala.NomeMaximo);
		builder.Property(sala => sala.Localizacao).HasMaxLength(Sala.LocalizacaoMaxima);
		builder.Property(sala => sala.Recursos).HasMaxLength(Sala.RecursosMaximo);
		builder.Property(sala => sala.EmailNaAgenda).HasMaxLength(Sala.EmailMaximo);

		// Duas salas não podem ter o mesmo nome.
		builder.HasIndex(sala => sala.Nome).IsUnique();
	}
}
