using AwesomeAssertions;
using Secco.Intranet.Domain.Tenants;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class TenantAdministradoTests
{
	private static TenantAdministrado Novo(
		Guid? tenantId = null, string sistema = "Sistema de compras", string responsavel = "Ana", string registradoPor = "admin@exemplo.com") =>
		new(tenantId ?? Guid.NewGuid(), sistema, responsavel, OrigemDoTenant.Criado, registradoPor);

	[Fact]
	public void Criar_GuardaOsDados_ENasceSemSecureGate()
	{
		var tenantId = Guid.NewGuid();

		var tenant = new TenantAdministrado(tenantId, "  Sistema de compras ", " Ana ", OrigemDoTenant.Adotado, "admin@exemplo.com");

		tenant.TenantId.Should().Be(tenantId);
		tenant.Sistema.Should().Be("Sistema de compras");
		tenant.Responsavel.Should().Be("Ana");
		tenant.Origem.Should().Be(OrigemDoTenant.Adotado);
		tenant.SecureGateHabilitado.Should().BeFalse();
		tenant.RegistradoPor.Should().Be("admin@exemplo.com");
		tenant.UpdatedAt.Should().BeNull();
	}

	[Fact]
	public void Criar_TenantVazio_Lanca()
	{
		var criar = () => Novo(Guid.Empty);

		criar.Should().Throw<DomainInvariantException>();
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Criar_SistemaVazio_Lanca(string sistema)
	{
		var criar = () => Novo(sistema: sistema);

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_SistemaAcimaDoLimite_Lanca()
	{
		var criar = () => Novo(sistema: new string('a', TenantAdministrado.SistemaMaxLength + 1));

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_ResponsavelVazio_Lanca()
	{
		var criar = () => Novo(responsavel: " ");

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_RegistradoPorLongo_Trunca_NaoLanca()
	{
		var tenant = Novo(registradoPor: new string('x', 300));

		tenant.RegistradoPor.Should().HaveLength(TenantAdministrado.RegistradoPorMaxLength);
	}

	[Fact]
	public void HabilitarSecureGate_PrimeiraVez_LigaEMarcaAtualizacao()
	{
		var tenant = Novo();

		tenant.HabilitarSecureGate().Should().BeTrue();

		tenant.SecureGateHabilitado.Should().BeTrue();
		tenant.UpdatedAt.Should().NotBeNull();
	}

	[Fact]
	public void HabilitarSecureGate_JaHabilitado_DevolveFalse()
	{
		var tenant = Novo();
		tenant.HabilitarSecureGate();

		tenant.HabilitarSecureGate().Should().BeFalse();
	}
}
