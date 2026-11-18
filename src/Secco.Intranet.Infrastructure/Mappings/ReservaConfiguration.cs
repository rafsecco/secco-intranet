using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Salas;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="Reserva"/> e de <see cref="ConvidadoDaReserva"/>. Nomes de tabela/colunas
/// vêm da convention (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class ReservaConfiguration : IEntityTypeConfiguration<Reserva>
{
	/// <summary>Tamanho máximo do identificador do evento na agenda externa.</summary>
	internal const int EventoExternoIdMaximo = 512;

	/// <summary>Tamanho máximo do motivo da falha (o mesmo corte que o domínio aplica ao gravá-lo).</summary>
	internal const int MotivoDaFalhaMaximo = 500;

	public void Configure(EntityTypeBuilder<Reserva> builder)
	{
		// Sem navegação para a sala: só a FK, para a convention reconhecer a coluna.
		// A sala com reservas não pode ser apagada.
		builder
			.HasOne<Sala>()
			.WithMany()
			.HasForeignKey(reserva => reserva.SalaId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.Property(reserva => reserva.Titulo).HasMaxLength(Reserva.TituloMaximo);
		builder.Property(reserva => reserva.Descricao).HasMaxLength(Reserva.DescricaoMaxima);
		builder.Property(reserva => reserva.ReservadoPorNome).HasMaxLength(256);
		builder.Property(reserva => reserva.ReservadoPorEmail).HasMaxLength(256);
		builder.Property(reserva => reserva.EventoExternoId).HasMaxLength(EventoExternoIdMaximo);
		builder.Property(reserva => reserva.MotivoDaFalha).HasMaxLength(ReservaConfiguration.MotivoDaFalhaMaximo);

		// Os convidados pertencem à reserva: vão e vêm com ela. Acesso pelo campo, porque a
		// propriedade pública é somente leitura e Alterar troca as instâncias.
		builder
			.HasMany(reserva => reserva.Convidados)
			.WithOne()
			.HasForeignKey(convidado => convidado.ReservaId)
			.IsRequired()
			.OnDelete(DeleteBehavior.Cascade);

		builder.Navigation(reserva => reserva.Convidados)
			.UsePropertyAccessMode(PropertyAccessMode.Field);

		// Conflito de horário e agenda por sala e dia.
		builder.HasIndex(reserva => new { reserva.SalaId, reserva.Inicio });

		// Varredura do espelhamento.
		builder.HasIndex(reserva => new { reserva.Espelhamento, reserva.ProximaTentativaEm });
	}
}

/// <summary>Mapeamento de <see cref="ConvidadoDaReserva"/>.</summary>
internal sealed class ConvidadoDaReservaConfiguration : IEntityTypeConfiguration<ConvidadoDaReserva>
{
	public void Configure(EntityTypeBuilder<ConvidadoDaReserva> builder)
	{
		// O Id nasce no construtor: sem isto o EF trata um convidado novo, alcançado pela coleção da
		// reserva já carregada (Alterar troca as instâncias), como existente e tenta um UPDATE.
		builder.Property(convidado => convidado.Id).ValueGeneratedNever();

		builder.Property(convidado => convidado.Nome).HasMaxLength(256);
		builder.Property(convidado => convidado.Email).HasMaxLength(256);

		// "Minhas reservas" busca por convidado.
		builder.HasIndex(convidado => convidado.UsuarioId);
	}
}

/// <summary>Mapeamento de <see cref="ExclusaoDeEventoPendente"/>.</summary>
internal sealed class ExclusaoDeEventoPendenteConfiguration : IEntityTypeConfiguration<ExclusaoDeEventoPendente>
{
	public void Configure(EntityTypeBuilder<ExclusaoDeEventoPendente> builder)
	{
		builder.Property(pendencia => pendencia.EventoExternoId).HasMaxLength(ReservaConfiguration.EventoExternoIdMaximo);
		builder.Property(pendencia => pendencia.MotivoDaFalha).HasMaxLength(ReservaConfiguration.MotivoDaFalhaMaximo);

		// Varredura das exclusões a tentar.
		builder.HasIndex(pendencia => new { pendencia.Falhou, pendencia.ProximaTentativaEm });
	}
}
