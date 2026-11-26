using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="LinkDoMenuPrincipal"/>. Nomes de tabela/colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class LinkDoMenuPrincipalConfiguration : IEntityTypeConfiguration<LinkDoMenuPrincipal>
{
	public void Configure(EntityTypeBuilder<LinkDoMenuPrincipal> builder)
	{
		builder.Property(link => link.Rotulo).HasMaxLength(256);
		builder.Property(link => link.Url).HasMaxLength(512);
		builder.Property(link => link.Icone).HasMaxLength(64);
	}
}
