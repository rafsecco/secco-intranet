using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class RegrasDeTenantTests
{
	[Theory]
	[InlineData("compras")]
	[InlineData("sistema-de-compras")]
	[InlineData("erp2")]
	[InlineData("a")]
	public void SlugValido_KebabCase_Aceita(string slug) =>
		RegrasDeTenant.SlugValido(slug).Should().BeTrue();

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("Compras")]
	[InlineData("sistema de compras")]
	[InlineData("-compras")]
	[InlineData("compras-")]
	[InlineData("com--pras")]
	[InlineData("compras_2")]
	[InlineData("compras;drop")]
	[InlineData("comprás")]
	[InlineData("compras\n")]
	[InlineData("compras\r\n")]
	public void SlugValido_ForaDoFormato_Recusa(string? slug) =>
		RegrasDeTenant.SlugValido(slug).Should().BeFalse();

	[Fact]
	public void SlugValido_51Caracteres_Recusa() =>
		RegrasDeTenant.SlugValido(new string('a', 51)).Should().BeFalse();

	[Fact]
	public void ValidarCriacao_TudoCerto_SemErro() =>
		RegrasDeTenant.ValidarCriacao("Sistema de compras", "Ana", "Compras", "compras").Should().BeNull();

	[Fact]
	public void ValidarCriacao_CadaCampo_TemErroProprio()
	{
		RegrasDeTenant.ValidarCriacao(" ", "Ana", "Compras", "compras").Should().Be(IntranetErrors.Tenants.SistemaRequired);
		RegrasDeTenant.ValidarCriacao(new string('a', 121), "Ana", "Compras", "compras").Should().Be(IntranetErrors.Tenants.SistemaTooLong);
		RegrasDeTenant.ValidarCriacao("S", "", "Compras", "compras").Should().Be(IntranetErrors.Tenants.ResponsavelRequired);
		RegrasDeTenant.ValidarCriacao("S", new string('a', 121), "Compras", "compras").Should().Be(IntranetErrors.Tenants.ResponsavelTooLong);
		RegrasDeTenant.ValidarCriacao("S", "Ana", null, "compras").Should().Be(IntranetErrors.Tenants.NomeRequired);
		RegrasDeTenant.ValidarCriacao("S", "Ana", new string('a', 201), "compras").Should().Be(IntranetErrors.Tenants.NomeTooLong);
		RegrasDeTenant.ValidarCriacao("S", "Ana", "Compras", "Compras X").Should().Be(IntranetErrors.Tenants.SlugInvalido);
	}

	[Fact]
	public void RotuloDoAtor_SemAtor_Desconhecido() =>
		RegrasDeTenant.RotuloDoAtor(new AtorDeAcessoFalso(null)).Should().Be("desconhecido");

	[Fact]
	public void RotuloDoAtor_ComAtor_UsaONome() =>
		RegrasDeTenant.RotuloDoAtor(new AtorDeAcessoFalso(Guid.NewGuid())).Should().Be("admin@exemplo.com");
}
