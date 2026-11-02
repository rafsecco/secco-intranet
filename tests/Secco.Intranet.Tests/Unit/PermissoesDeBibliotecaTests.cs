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

public class PermissoesDeBibliotecaTests
{
	private sealed class TenantContextFalso : ITenantContext
	{
		public Guid? TenantId { get; set; } = Guid.NewGuid();

		public bool IsResolved => TenantId is not null;
	}

	private static (IPermissoesDeBiblioteca Servico, ClaimsPrincipal Usuario) Montar(IPermissionResolver resolvedor, params string[] roles)
	{
		var servicos = new ServiceCollection()
			.AddLogging()
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
			.AddSingleton<ITenantContext>(new TenantContextFalso())
			.AddSingleton(resolvedor)
			.AddAuthorizationCore()
			.AddSeccoAuthorization()
			.BuildServiceProvider();

		var authorizationService = servicos.GetRequiredService<IAuthorizationService>();
		var usuario = new ClaimsPrincipal(new ClaimsIdentity(
			roles.Select(role => new Claim(SeccoClaims.Role, role)), authenticationType: "Teste"));

		return (new PermissoesDeBiblioteca(authorizationService), usuario);
	}

	[Fact]
	public async Task So_write_conta_como_leitura()
	{
		var resolvedor = new PermissionResolverDeTeste().ComPermissao("editores", IntranetPermissoes.Biblioteca.Write("ti"));
		var (servico, usuario) = Montar(resolvedor, "editores");

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "read", ["ti", "rh"]);

		slugs.Should().BeEquivalentTo(["ti"]);
	}

	[Fact]
	public async Task Admin_tem_todas()
	{
		var (servico, usuario) = Montar(new PermissionResolverQueLanca(), "intranet-admin");

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "write", ["ti", "rh"]);

		slugs.Should().BeEquivalentTo(["ti", "rh"]);
	}

	[Fact]
	public async Task Read_nao_da_write()
	{
		var resolvedor = new PermissionResolverDeTeste().ComPermissao("leitores", IntranetPermissoes.Biblioteca.Read("ti"));
		var (servico, usuario) = Montar(resolvedor, "leitores");

		var leitura = await servico.SlugsComPermissaoAsync(usuario, "read", ["ti"]);
		var escrita = await servico.SlugsComPermissaoAsync(usuario, "write", ["ti"]);

		leitura.Should().BeEquivalentTo(["ti"]);
		escrita.Should().BeEmpty();
	}

	[Fact]
	public async Task Setor_de_mesmo_slug_nao_conta()
	{
		var resolvedor = new PermissionResolverDeTeste().ComPermissao("ti-user", IntranetPermissoes.Setor.Read("ti"));
		var (servico, usuario) = Montar(resolvedor, "ti-user");

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "read", ["ti"]);

		slugs.Should().BeEmpty();
	}
}
