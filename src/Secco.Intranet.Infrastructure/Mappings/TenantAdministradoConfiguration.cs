using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Tenants;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="TenantAdministrado"/>. Nomes de tabela e colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class TenantAdministradoConfiguration : IEntityTypeConfiguration<TenantAdministrado>
{
	public void Configure(EntityTypeBuilder<TenantAdministrado> builder)
	{
		builder.Property(tenant => tenant.Sistema).HasMaxLength(TenantAdministrado.SistemaMaxLength);
		builder.Property(tenant => tenant.Responsavel).HasMaxLength(TenantAdministrado.ResponsavelMaxLength);
		builder.Property(tenant => tenant.RegistradoPor).HasMaxLength(TenantAdministrado.RegistradoPorMaxLength);

		// Um registro por tenant — é o que sustenta o "já administrado" e o TentarAdicionar.
		builder.HasIndex(tenant => tenant.TenantId).IsUnique();
	}
}
