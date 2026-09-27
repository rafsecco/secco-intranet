using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Authentication;
using Secco.SDK.AspNetCore.Authorization;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ExigePermissaoAttributeTests
{
	private sealed class AmbienteFalso(string nome) : IWebHostEnvironment
	{
		public string ApplicationName { get; set; } = "Teste";

		public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

		public string WebRootPath { get; set; } = string.Empty;

		public string EnvironmentName { get; set; } = nome;

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	/// <summary>
	/// Fake de <see cref="ITenantContext"/>: o <c>TenantContext</c> real do SDK só permite
	/// setter interno (só a própria middleware de tenancy pode resolver), então um teste fora
	/// daquele assembly implementa a interface direto.
	/// </summary>
	private sealed class TenantContextFalso : ITenantContext
	{
		public Guid? TenantId { get; set; } = Guid.NewGuid();

		public bool IsResolved => TenantId is not null;
	}

	private static AuthorizationFilterContext Contexto(string ambiente, IPermissionResolver resolvedor, params string[] roles)
	{
		var configuracao = new ConfigurationBuilder().Build();

		var servicos = new ServiceCollection()
			.AddSingleton<IWebHostEnvironment>(new AmbienteFalso(ambiente))
			.AddSingleton<IConfiguration>(configuracao)
			.AddLogging()
			.AddSingleton<ITenantContext>(new TenantContextFalso())
			.AddSingleton(resolvedor)
			.AddAuthorizationCore()
			.AddSeccoAuthorization()
			.BuildServiceProvider();

		var http = new DefaultHttpContext { RequestServices = servicos };

		if (roles.Length > 0)
		{
			http.User = new ClaimsPrincipal(new ClaimsIdentity(
				roles.Select(role => new Claim(SeccoClaims.Role, role)), authenticationType: "Teste"));
		}

		return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
	}

	private static bool Bloqueou(AuthorizationFilterContext contexto) =>
		contexto.Result is StatusCodeResult { StatusCode: StatusCodes.Status403Forbidden };

	[Fact]
	public async Task SemAPermissao_Bloqueia()
	{
		var contexto = Contexto("Testing", new PermissionResolverDeTeste(), "qualquer-role");

		await new ExigePermissaoAttribute(IntranetPermissoes.Diretorio.Read).OnAuthorizationAsync(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Fact]
	public async Task ComAPermissao_Libera()
	{
		var resolvedor = new PermissionResolverDeTeste().ComPermissao("diretorio-user", IntranetPermissoes.Diretorio.Read);
		var contexto = Contexto("Testing", resolvedor, "diretorio-user");

		await new ExigePermissaoAttribute(IntranetPermissoes.Diretorio.Read).OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public async Task IntranetAdmin_SempreLibera_MesmoSemAPermissaoGravada()
	{
		var contexto = Contexto("Testing", new PermissionResolverDeTeste(), "intranet-admin");

		await new ExigePermissaoAttribute(IntranetPermissoes.Diretorio.Read).OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public async Task ModoAbertoDeDev_Libera()
	{
		var contexto = Contexto("Development", new PermissionResolverDeTeste());

		await new ExigePermissaoAttribute(IntranetPermissoes.Diretorio.Manage).OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public async Task ResolvedorIndisponivel_Nega_NaoLancaExcecao()
	{
		var contexto = Contexto("Testing", new PermissionResolverQueLanca(), "diretorio-user");

		await new ExigePermissaoAttribute(IntranetPermissoes.Diretorio.Read).OnAuthorizationAsync(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Fact]
	public void Order_RodaAntesDoFiltroAntifalsificacao()
	{
		new ExigePermissaoAttribute(IntranetPermissoes.Diretorio.Read).Order.Should().BeLessThan(1000);
	}
}
