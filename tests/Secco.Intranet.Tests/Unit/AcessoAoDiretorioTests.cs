using System.Security.Claims;
using AwesomeAssertions;
using Secco.Intranet.Web.Navigation;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class AcessoAoDiretorioTests
{
	private static ClaimsPrincipal Usuario(params string[] roles) =>
		new(new ClaimsIdentity(roles.Select(role => new Claim(SeccoClaims.Role, role)), authenticationType: "Teste"));

	[Theory]
	[InlineData("intranet-admin", NivelDeAcessoAoDiretorio.Administrador)]
	[InlineData("Intranet-Admin", NivelDeAcessoAoDiretorio.Administrador)]
	[InlineData("diretorio-admin", NivelDeAcessoAoDiretorio.Administrador)]
	[InlineData("diretorio-user", NivelDeAcessoAoDiretorio.Usuario)]
	[InlineData("inventario-admin", NivelDeAcessoAoDiretorio.Nenhum)]
	[InlineData("financeiro-admin", NivelDeAcessoAoDiretorio.Nenhum)]
	[InlineData("financeiro-user", NivelDeAcessoAoDiretorio.Nenhum)]
	[InlineData("diretorio-admin-falso", NivelDeAcessoAoDiretorio.Nenhum)]
	public void Nivel_ClassificaPelaRole(string role, NivelDeAcessoAoDiretorio esperado)
	{
		AcessoAoDiretorio.Nivel(Usuario(role)).Should().Be(esperado);
	}

	[Fact]
	public void Nivel_UsuarioNuloOuSemRole_Nenhum()
	{
		AcessoAoDiretorio.Nivel(null).Should().Be(NivelDeAcessoAoDiretorio.Nenhum);
		AcessoAoDiretorio.Nivel(Usuario()).Should().Be(NivelDeAcessoAoDiretorio.Nenhum);
	}

	[Fact]
	public void Nivel_ComVariasRoles_VenceAMaisAlta()
	{
		AcessoAoDiretorio.Nivel(Usuario("financeiro-admin", "diretorio-user", "diretorio-admin"))
			.Should().Be(NivelDeAcessoAoDiretorio.Administrador);
	}

	[Fact]
	public void TemNivel_ComparaComOMinimo()
	{
		AcessoAoDiretorio.TemNivel(Usuario("diretorio-user"), NivelDeAcessoAoDiretorio.Usuario).Should().BeTrue();
		AcessoAoDiretorio.TemNivel(Usuario("diretorio-user"), NivelDeAcessoAoDiretorio.Administrador).Should().BeFalse();
		AcessoAoDiretorio.TemNivel(Usuario("diretorio-admin"), NivelDeAcessoAoDiretorio.Usuario).Should().BeTrue();
		AcessoAoDiretorio.TemNivel(Usuario("inventario-admin"), NivelDeAcessoAoDiretorio.Usuario).Should().BeFalse();
	}

	[Fact]
	public void UsuarioId_LeDoClaimSub()
	{
		var id = Guid.NewGuid();
		var usuario = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SeccoClaims.Subject, id.ToString())], "Teste"));

		AcessoAoDiretorio.UsuarioId(usuario).Should().Be(id);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("nao-e-guid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public void UsuarioId_SemSubValido_Nulo(string? sub)
	{
		var claims = sub is null ? [] : new[] { new Claim(SeccoClaims.Subject, sub) };
		var usuario = new ClaimsPrincipal(new ClaimsIdentity(claims, "Teste"));

		AcessoAoDiretorio.UsuarioId(usuario).Should().BeNull();
		AcessoAoDiretorio.UsuarioId(null).Should().BeNull();
	}
}
