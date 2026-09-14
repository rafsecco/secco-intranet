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
}
