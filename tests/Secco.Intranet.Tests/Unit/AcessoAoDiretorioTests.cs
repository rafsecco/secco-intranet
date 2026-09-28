using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Navigation;
using Secco.SDK.AspNetCore.Authorization;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class AcessoAoDiretorioTests
{
	private sealed class TenantContextFalso : ITenantContext
	{
		public Guid? TenantId { get; set; } = Guid.NewGuid();

		public bool IsResolved => TenantId is not null;
	}

	private static ClaimsPrincipal Usuario(params string[] roles) =>
		new(new ClaimsIdentity(roles.Select(role => new Claim(SeccoClaims.Role, role)), authenticationType: "Teste"));

	private static IAuthorizationService AuthorizationService(IPermissionResolver resolvedor)
	{
		var servicos = new ServiceCollection()
			.AddLogging()
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
			.AddSingleton<ITenantContext>(new TenantContextFalso())
			.AddSingleton(resolvedor)
			.AddAuthorizationCore()
			.AddSeccoAuthorization()
			.BuildServiceProvider();

		return servicos.GetRequiredService<IAuthorizationService>();
	}

	[Fact]
	public async Task Nivel_IntranetAdmin_Administrador_SemConsultarPermissao()
	{
		var nivel = await AcessoAoDiretorio.NivelAsync(AuthorizationService(new PermissionResolverQueLanca()), Usuario("intranet-admin"));

		nivel.Should().Be(NivelDeAcessoAoDiretorio.Administrador);
	}

	[Fact]
	public async Task Nivel_ComPermissaoDeGerenciar_Administrador()
	{
		var resolvedor = new PermissionResolverDeTeste().ComPermissao("diretorio-admin", IntranetPermissoes.Diretorio.Manage);

		var nivel = await AcessoAoDiretorio.NivelAsync(AuthorizationService(resolvedor), Usuario("diretorio-admin"));

		nivel.Should().Be(NivelDeAcessoAoDiretorio.Administrador);
	}

	[Fact]
	public async Task Nivel_SoComPermissaoDeLeitura_Usuario()
	{
		var resolvedor = new PermissionResolverDeTeste().ComPermissao("diretorio-user", IntranetPermissoes.Diretorio.Read);

		var nivel = await AcessoAoDiretorio.NivelAsync(AuthorizationService(resolvedor), Usuario("diretorio-user"));

		nivel.Should().Be(NivelDeAcessoAoDiretorio.Usuario);
	}

	[Fact]
	public async Task Nivel_SemNenhumaPermissao_Nenhum()
	{
		var nivel = await AcessoAoDiretorio.NivelAsync(AuthorizationService(new PermissionResolverDeTeste()), Usuario("financeiro-admin"));

		nivel.Should().Be(NivelDeAcessoAoDiretorio.Nenhum);
	}

	[Fact]
	public async Task Nivel_UsuarioNulo_Nenhum()
	{
		var nivel = await AcessoAoDiretorio.NivelAsync(AuthorizationService(new PermissionResolverDeTeste()), null);

		nivel.Should().Be(NivelDeAcessoAoDiretorio.Nenhum);
	}

	[Fact]
	public async Task TemNivel_ComparaComOMinimo()
	{
		var resolvedor = new PermissionResolverDeTeste().ComPermissao("diretorio-user", IntranetPermissoes.Diretorio.Read);
		var authorizationService = AuthorizationService(resolvedor);
		var usuario = Usuario("diretorio-user");

		(await AcessoAoDiretorio.TemNivelAsync(authorizationService, usuario, NivelDeAcessoAoDiretorio.Usuario)).Should().BeTrue();
		(await AcessoAoDiretorio.TemNivelAsync(authorizationService, usuario, NivelDeAcessoAoDiretorio.Administrador)).Should().BeFalse();
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
