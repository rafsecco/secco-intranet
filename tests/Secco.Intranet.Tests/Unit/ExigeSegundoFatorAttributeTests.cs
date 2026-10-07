using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Authentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ExigeSegundoFatorAttributeTests
{
	private static readonly Guid Admin = Guid.NewGuid();
	private static readonly Guid TenantDoTeste = Guid.NewGuid();

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
		public Guid? TenantId { get; } = TenantDoTeste;

		public bool IsResolved => true;
	}

	private static (AuthorizationFilterContext Contexto, IServiceProvider Servicos) Montar(
		IGestaoDeAcesso gestao, Guid? usuario, string ambiente = "Testing", IMemoryCache? cache = null)
	{
		var servicos = new ServiceCollection()
			.AddSingleton<IWebHostEnvironment>(new AmbienteFalso(ambiente))
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
			.AddSingleton<ITenantContext>(new TenantContextFalso())
			.AddSingleton(gestao)
			.AddSingleton(cache ?? new MemoryCache(new MemoryCacheOptions()))
			.AddSingleton<IModelMetadataProvider>(new EmptyModelMetadataProvider())
			.BuildServiceProvider();

		var http = new DefaultHttpContext { RequestServices = servicos };

		if (usuario is not null)
		{
			http.User = new ClaimsPrincipal(new ClaimsIdentity(
				[new Claim(SeccoClaims.Role, "intranet-admin"), new Claim(SeccoClaims.Subject, usuario.Value.ToString())], "Teste"));
		}

		return (new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []), servicos);
	}

	private static GestaoDeAcessoFalsa ComAdmin(bool doisFatores)
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Admin, "admin@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin");

		return doisFatores ? gestao.ComDoisFatores(Admin) : gestao;
	}

	[Fact]
	public async Task ComDoisFatores_Passa()
	{
		var (contexto, _) = Montar(ComAdmin(true), Admin);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public async Task SemDoisFatores_ViewQueExplica_403()
	{
		var (contexto, _) = Montar(ComAdmin(false), Admin);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		var view = contexto.Result.Should().BeOfType<ViewResult>().Subject;
		view.ViewName.Should().Be("SegundoFatorObrigatorio");
		view.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
	}

	[Fact]
	public async Task SecureGateFora_Indisponivel_503_NuncaAbre()
	{
		var gestao = ComAdmin(true);
		gestao.FalharCom = IntranetErrors.Acesso.Indisponivel;
		var (contexto, _) = Montar(gestao, Admin);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		var view = contexto.Result.Should().BeOfType<ViewResult>().Subject;
		view.ViewName.Should().Be("SegundoFatorIndisponivel");
		view.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
	}

	[Fact]
	public async Task SecureGateNaoConfigurado_Passa_AAreaExplica()
	{
		var gestao = ComAdmin(false);
		gestao.FalharCom = IntranetErrors.Acesso.NaoConfigurado;
		var (contexto, _) = Montar(gestao, Admin);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public async Task SemSub_403()
	{
		var (contexto, _) = Montar(ComAdmin(true), usuario: null);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
	}

	[Fact]
	public async Task UsuarioNaoEncontrado_403()
	{
		var (contexto, _) = Montar(new GestaoDeAcessoFalsa(), Admin);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
	}

	[Fact]
	public async Task Positivo_VemDoCache_NaSegundaVez()
	{
		var cache = new MemoryCache(new MemoryCacheOptions());
		var gestao = ComAdmin(true);
		var (primeiro, _) = Montar(gestao, Admin, cache: cache);
		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(primeiro);

		gestao.FalharCom = IntranetErrors.Acesso.Indisponivel;
		var (segundo, _) = Montar(gestao, Admin, cache: cache);
		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(segundo);

		segundo.Result.Should().BeNull("o positivo fica 60 s no cache");
	}

	[Fact]
	public async Task Negativo_NaoVaiParaOCache()
	{
		var cache = new MemoryCache(new MemoryCacheOptions());
		var semFator = ComAdmin(false);
		var (primeiro, _) = Montar(semFator, Admin, cache: cache);
		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(primeiro);

		var (segundo, _) = Montar(ComAdmin(true), Admin, cache: cache);
		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(segundo);

		segundo.Result.Should().BeNull("quem acabou de cadastrar o 2FA entra na hora");
	}

	[Fact]
	public void Ordem_DepoisDoGateDeRole_AntesDoAntifalsificacao() =>
		new ExigeSegundoFatorAttribute().Order.Should().BeGreaterThan(new SomenteIntranetAdminAttribute().Order).And.BeLessThan(1000);
}
