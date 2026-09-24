using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Navigation;
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
	public void Decide_PeloNivelMinimo(string role, NivelDeAcessoAoDiretorio minimo, bool deveBloquear)
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false, role);

		new ExigeNivelNoDiretorioAttribute(minimo).OnAuthorization(contexto);

		Bloqueou(contexto).Should().Be(deveBloquear);
	}

	[Fact]
	public void SemRole_Bloqueia()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false);

		new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Usuario).OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Fact]
	public void ModoAbertoDeDev_Libera()
	{
		var contexto = Contexto("Development", autenticacaoConfigurada: false);

		new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Administrador).OnAuthorization(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public void Testing_NuncaTemBypass()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false);

		new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Usuario).OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Fact]
	public void Order_RodaAntesDoFiltroAntifalsificacao()
	{
		new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Usuario).Order.Should().BeLessThan(1000);
	}
}
