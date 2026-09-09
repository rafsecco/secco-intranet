using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Web.Auditoria;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// <see cref="AtorDoHttpContext"/> decide se uma ação é auditada: sem ator resolvido, o
/// <c>LogStreamTrilhaDeAuditoria</c> não registra nada — se o claim <c>sub</c> sumisse em
/// produção, a trilha inteira ficaria vazia sem que nada além de um log em <c>Debug</c>
/// avisasse. Os três ramos de <see cref="AtorDoHttpContext.Atual"/> ficam cobertos aqui.
/// </summary>
public class AtorDoHttpContextTests
{
	private sealed class HttpContextAccessorFalso : IHttpContextAccessor
	{
		public HttpContext? HttpContext { get; set; }
	}

	private static AtorDoHttpContext Montar(HttpContext? httpContext) =>
		new(new HttpContextAccessorFalso { HttpContext = httpContext });

	[Fact]
	public void NaoAutenticado_DevolveNull()
	{
		// DefaultHttpContext.User já vem com um ClaimsPrincipal sem AuthenticationType, que é
		// o que ASP.NET Core considera "não autenticado" — o mesmo estado de uma requisição
		// anônima de verdade.
		var httpContext = new DefaultHttpContext();

		Montar(httpContext).Atual().Should().BeNull();
	}

	[Fact]
	public void AutenticadoSemClaimSub_DevolveNull()
	{
		var identidade = new ClaimsIdentity(authenticationType: "Test");
		var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identidade) };

		Montar(httpContext).Atual().Should().BeNull(
			"sem o claim sub não há como identificar quem agiu, e um ator inventado sujaria a trilha");
	}

	[Fact]
	public void AutenticadoComSub_DevolveAtorComFallbackDeNome()
	{
		var identidade = new ClaimsIdentity(
			[new Claim(SeccoClaims.Subject, "018f0000-0000-7000-8000-000000000009")],
			authenticationType: "Test");
		var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identidade) };

		var ator = Montar(httpContext).Atual();

		// Sem claim de nome, Identity.Name é nulo e o ator cai no fallback: o próprio id.
		ator.Should().Be(new AtorDaAcao(
			"018f0000-0000-7000-8000-000000000009", "018f0000-0000-7000-8000-000000000009"));
	}
}
