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
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class SomenteIntranetAdminAttributeTests
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

		return new AuthorizationFilterContext(
			new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
	}

	private static bool Bloqueou(AuthorizationFilterContext contexto) =>
		contexto.Result is StatusCodeResult { StatusCode: StatusCodes.Status403Forbidden };

	[Fact]
	public void SemRole_Bloqueia()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false);

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Theory]
	[InlineData("financeiro-admin")]
	[InlineData("inventario-admin")]
	public void OutraRole_Bloqueia(string role)
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false, role);

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Fact]
	public void IntranetAdmin_Libera()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false, "intranet-admin");

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public void ModoAbertoDeDev_SemAutenticacaoConfigurada_Libera()
	{
		var contexto = Contexto("Development", autenticacaoConfigurada: false);

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		contexto.Result.Should().BeNull("é o modo aberto de DEV local, sem SecureGate para emitir roles");
	}

	[Fact]
	public void Development_ComAutenticacaoConfigurada_NaoLiberaSemRole()
	{
		var contexto = Contexto("Development", autenticacaoConfigurada: true);

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue("com o SecureGate configurado o bypass de DEV não vale");
	}

	[Fact]
	public void Testing_NuncaTemBypass()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false);

		new SomenteIntranetAdminAttribute().OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue(
			"o ambiente Testing também não configura autenticação; se o bypass valesse lá, não haveria como testar o bloqueio");
	}

	[Fact]
	public void Order_RodaAntesDoFiltroAntifalsificacao()
	{
		// ValidateAntiForgeryToken tem Order = 1000. Um Order menor faz o 403 sair antes do 400,
		// então o POST de quem não é admin não depende de ter (ou não) um token.
		new SomenteIntranetAdminAttribute().Order.Should().BeLessThan(1000);
	}
}
