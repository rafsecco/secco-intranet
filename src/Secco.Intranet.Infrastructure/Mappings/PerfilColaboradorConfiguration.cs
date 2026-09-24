using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="PerfilColaborador"/>. Nomes de tabela e colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class PerfilColaboradorConfiguration : IEntityTypeConfiguration<PerfilColaborador>
{
	public void Configure(EntityTypeBuilder<PerfilColaborador> builder)
	{
		// SetorId é uma FK de verdade (o setor é local), sem navegação — mesmo padrão de
		// ItemInventarioConfiguration. GestorUsuarioId e UsuarioId NÃO são FK: identidade vive só
		// no SecureGate (ADR-0006), então ficam como Guid simples, sem prefixo id_fk_.
		builder
			.HasOne<Setor>()
			.WithMany()
			.HasForeignKey(perfil => perfil.SetorId)
			.OnDelete(DeleteBehavior.Restrict)
			.IsRequired(false);

		builder.Property(perfil => perfil.NomeExibicao).HasMaxLength(PerfilColaborador.NomeMaxLength);
		builder.Property(perfil => perfil.Cargo).HasMaxLength(PerfilColaborador.CargoMaxLength);
		builder.Property(perfil => perfil.Ramal).HasMaxLength(PerfilColaborador.RamalMaxLength);
		builder.Property(perfil => perfil.Sobre).HasMaxLength(PerfilColaborador.SobreMaxLength);

		// Um perfil por usuário — é o que sustenta o "upsert" sob demanda.
		builder.HasIndex(perfil => perfil.UsuarioId).IsUnique();
		builder.HasIndex(perfil => perfil.SetorId);
		builder.HasIndex(perfil => perfil.GestorUsuarioId);
	}
}
