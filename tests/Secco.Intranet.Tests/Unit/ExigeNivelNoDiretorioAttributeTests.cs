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
using Secco.Intranet.Web.Navigation;
using Secco.SDK.AspNetCore.Authorization;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ExigeNivelNoDiretorioAttributeTests
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

	private sealed class TenantContextFalso : ITenantContext
	{
		public Guid? TenantId { get; set; } = Guid.NewGuid();

		public bool IsResolved => TenantId is not null;
	}

	/// <summary>Resolvedor com o mapeamento real de <c>diretorio-user</c>/<c>diretorio-admin</c> (Task 6).</summary>
	private static PermissionResolverDeTeste ResolvedorPadrao() => new PermissionResolverDeTeste()
		.ComPermissao("diretorio-user", IntranetPermissoes.Diretorio.Read)
		.ComPermissao("diretorio-admin", IntranetPermissoes.Diretorio.Read)
		.ComPermissao("diretorio-admin", IntranetPermissoes.Diretorio.Manage);

	private static AuthorizationFilterContext Contexto(string ambiente, bool autenticacaoConfigurada, params string[] roles)
	{
		var configuracao = new ConfigurationBuilder()
			.AddInMemoryCollection(autenticacaoConfigurada
				? new Dictionary<string, string?> { ["Secco:SecureGate:Authority"] = "https://securegate.exemplo" }
				: [])
			.Build();

		var servicos = new ServiceCollection()
			.AddSingleton<IWebHostEnvironment>(new AmbienteFalso(ambiente))
			.AddSingleton<IConfiguration>(configuracao)
			.AddLogging()
			.AddSingleton<ITenantContext>(new TenantContextFalso())
			.AddSingleton<IPermissionResolver>(ResolvedorPadrao())
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

	[Theory]
	[InlineData("diretorio-user", NivelDeAcessoAoDiretorio.Usuario, false)]
	[InlineData("diretorio-user", NivelDeAcessoAoDiretorio.Administrador, true)]
	[InlineData("diretorio-admin", NivelDeAcessoAoDiretorio.Administrador, false)]
	[InlineData("intranet-admin", NivelDeAcessoAoDiretorio.Administrador, false)]
	[InlineData("inventario-admin", NivelDeAcessoAoDiretorio.Usuario, true)]
	[InlineData("financeiro-admin", NivelDeAcessoAoDiretorio.Usuario, true)]
	[InlineData("financeiro-user", NivelDeAcessoAoDiretorio.Usuario, true)]
	public async Task Decide_PeloNivelMinimo(string role, NivelDeAcessoAoDiretorio minimo, bool deveBloquear)
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false, role);

		await new ExigeNivelNoDiretorioAttribute(minimo).OnAuthorizationAsync(contexto);

		Bloqueou(contexto).Should().Be(deveBloquear);
	}

	[Fact]
	public async Task SemRole_Bloqueia()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false);

		await new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Usuario).OnAuthorizationAsync(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Fact]
	public async Task ModoAbertoDeDev_Libera()
	{
		var contexto = Contexto("Development", autenticacaoConfigurada: false);

		await new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Administrador).OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public async Task Testing_NuncaTemBypass()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false);

		await new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Usuario).OnAuthorizationAsync(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Fact]
	public void Order_RodaAntesDoFiltroAntifalsificacao()
	{
		new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Usuario).Order.Should().BeLessThan(1000);
	}
}
