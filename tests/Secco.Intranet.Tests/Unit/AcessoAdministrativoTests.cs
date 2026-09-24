using System.Security.Claims;
using AwesomeAssertions;
using Secco.Intranet.Web.Navigation;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Generaliza o padrão de <see cref="SetorAcesso"/> para Roles fixas, não derivadas de slug
/// — o primeiro recurso a sair do molde <c>{slug}-admin</c> do ADR-0001 (ADR-0008).
/// </summary>
public class AcessoAdministrativoTests
{
	private static ClaimsPrincipal Usuario(params string[] roles) =>
		new(new ClaimsIdentity(roles.Select(role => new Claim(SeccoClaims.Role, role)), authenticationType: "Teste"));

	[Fact]
	public void UsuarioNulo_SemAcesso()
	{
		AcessoAdministrativo.TemAcesso(null, "inventario-admin").Should().BeFalse();
	}

	[Fact]
	public void SemNenhumaRole_SemAcesso()
	{
		AcessoAdministrativo.TemAcesso(Usuario(), "inventario-admin").Should().BeFalse();
	}

	[Fact]
	public void ComRoleEspecifica_TemAcesso()
	{
		AcessoAdministrativo.TemAcesso(Usuario("inventario-admin"), "inventario-admin").Should().BeTrue();
	}

	[Fact]
	public void ComIntranetAdmin_TemAcessoMesmoSemARoleEspecifica()
	{
		AcessoAdministrativo.TemAcesso(Usuario("intranet-admin"), "inventario-admin").Should().BeTrue();
	}

	[Fact]
	public void ComOutraRoleQualquer_SemAcesso()
	{
		AcessoAdministrativo.TemAcesso(Usuario("financeiro-admin"), "inventario-admin").Should().BeFalse(
			"admin de setor não é atalho para uma Role fixa — são modelos de autorização independentes");
	}

	[Fact]
	public void SomenteIntranetAdmin_UsuarioNulo_SemAcesso()
	{
		AcessoAdministrativo.SomenteIntranetAdmin(null).Should().BeFalse();
	}

	[Fact]
	public void SomenteIntranetAdmin_ComIntranetAdmin_TemAcesso()
	{
		AcessoAdministrativo.SomenteIntranetAdmin(Usuario("intranet-admin")).Should().BeTrue();
	}

	[Fact]
	public void SomenteIntranetAdmin_IgnoraCaixa()
	{
		AcessoAdministrativo.SomenteIntranetAdmin(Usuario("Intranet-Admin")).Should().BeTrue();
	}

	[Theory]
	[InlineData("inventario-admin")]
	[InlineData("financeiro-admin")]
	[InlineData("financeiro-user")]
	[InlineData("intranet-admin-falso")]
	public void SomenteIntranetAdmin_QualquerOutraRole_SemAcesso(string role)
	{
		AcessoAdministrativo.SomenteIntranetAdmin(Usuario(role)).Should().BeFalse(
			"a Área administrativa não é delegável: sem o OR que o Inventário tem (ADR-0008)");
	}

	[Fact]
	public void SomenteIntranetAdmin_AdminDeTodosOsSetores_SemAcesso()
	{
		AcessoAdministrativo.SomenteIntranetAdmin(Usuario("a-admin", "b-admin", "c-admin")).Should().BeFalse();
	}
}
