using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Authentication;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class TenantAdministradoAttributeTests
{
	private static readonly Guid Cadastrado = Guid.NewGuid();

	private static AuthorizationFilterContext Contexto(object? tenantId)
	{
		var servicos = new ServiceCollection()
			.AddSingleton<ITenantsAdministrados>(new TenantsAdministradosFalso().Com(Cadastrado))
			.AddScoped<VerificarTenantAdministradoHandler>()
			.BuildServiceProvider();

		var rota = new RouteData();

		if (tenantId is not null)
		{
			rota.Values["tenantId"] = tenantId;
		}

		return new AuthorizationFilterContext(
			new ActionContext(new DefaultHttpContext { RequestServices = servicos }, rota, new ActionDescriptor()), []);
	}

	[Fact]
	public async Task Cadastrado_Passa()
	{
		var contexto = Contexto(Cadastrado.ToString());

		await new TenantAdministradoAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeNull();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("nao-e-guid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public async Task SemTenantValido_404(string? valor)
	{
		var contexto = Contexto(valor);

		await new TenantAdministradoAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeOfType<NotFoundResult>();
	}

	[Fact]
	public async Task ForaDoCadastro_404_Nunca403()
	{
		var contexto = Contexto(Guid.NewGuid().ToString());

		await new TenantAdministradoAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeOfType<NotFoundResult>("403 confirmaria que o tenant existe");
	}

	[Fact]
	public void Ordem_DepoisDoSegundoFator() =>
		new TenantAdministradoAttribute().Order.Should().BeGreaterThan(new ExigeSegundoFatorAttribute().Order).And.BeLessThan(1000);
}
