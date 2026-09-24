using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class RolesDeTesteMiddlewareTests
{
	[Fact]
	public async Task SemHeader_NaoAlteraUsuario()
	{
		var context = new DefaultHttpContext();
		var usuarioOriginal = context.User;
		var middleware = new RolesDeTesteMiddleware(_ => Task.CompletedTask);

		await middleware.InvokeAsync(context);

		context.User.Should().BeSameAs(usuarioOriginal);
	}

	[Fact]
	public async Task ComHeader_DefineClaimsDeRole()
	{
		var context = new DefaultHttpContext();
		context.Request.Headers[RolesDeTesteMiddleware.Header] = "intranet-admin, inventario-admin";
		var middleware = new RolesDeTesteMiddleware(_ => Task.CompletedTask);

		await middleware.InvokeAsync(context);

		context.User.FindAll(SeccoClaims.Role).Select(c => c.Value)
			.Should().BeEquivalentTo(["intranet-admin", "inventario-admin"]);
	}

	[Fact]
	public async Task ComHeaderDeUsuario_DefineOClaimSub()
	{
		var id = Guid.NewGuid();
		var context = new DefaultHttpContext();
		context.Request.Headers[RolesDeTesteMiddleware.HeaderUsuario] = id.ToString();
		var middleware = new RolesDeTesteMiddleware(_ => Task.CompletedTask);

		await middleware.InvokeAsync(context);

		context.User.FindFirst(SeccoClaims.Subject)!.Value.Should().Be(id.ToString());
		context.User.FindAll(SeccoClaims.Role).Should().BeEmpty();
	}
}
