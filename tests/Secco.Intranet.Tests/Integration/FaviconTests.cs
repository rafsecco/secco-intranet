using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O navegador procura <c>/favicon.ico</c> na raiz sempre que a resposta não declara um ícone — a
/// foto servida direto, as páginas de 404 em texto puro. O ícone é do tema (cada um tem a sua
/// cor), então a raiz aponta para o SVG do tema ativo, e sem exigir login: senão o próprio
/// ícone mandaria o navegador para o SecureGate. O SVG em si não é buscado aqui: no ambiente
/// Testing os assets estáticos das RCLs de tema não são servidos (só em Development e no publish),
/// e quem o serve antes da autenticação é o <c>UseStaticFiles</c> do pipeline.
/// </summary>
public class FaviconTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>
{
	private static readonly WebApplicationFactoryClientOptions SemSeguirRedirect = new() { AllowAutoRedirect = false };

	[Fact]
	public async Task Favicon_ApontaParaOSvgDoTemaPadrao()
	{
		var resposta = await factory.CreateClient(SemSeguirRedirect).GetAsync("/favicon.ico");

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.OriginalString.Should().Be("/_content/Secco.Intranet.Themes.Vertical/favicon.svg");
	}

	[Fact]
	public async Task Favicon_SegueOTemaAtivo()
	{
		var client = factory
			.WithWebHostBuilder(builder => builder.UseSetting("Intranet:Theme:Nome", "Horizontal"))
			.CreateClient(SemSeguirRedirect);

		var resposta = await client.GetAsync("/favicon.ico");

		resposta.Headers.Location!.OriginalString.Should().Be("/_content/Secco.Intranet.Themes.Horizontal/favicon.svg");
	}

	[Fact]
	public async Task Favicon_ComLoginLigado_NaoExigeAutenticacao()
	{
		// UseSetting: a autenticação é composta durante o Program.cs (ver RevogacaoDeSessaoTests)
		var client = factory.WithWebHostBuilder(builder =>
		{
			builder.UseSetting("Secco:SecureGate:Authority", "https://securegate.exemplo.com");
			builder.UseSetting("Secco:SecureGate:BaseUrl", "https://securegate.exemplo.com");
			builder.UseSetting("Secco:SecureGate:ClientId", "secco-intranet");
			builder.UseSetting("Secco:SecureGate:ClientSecret", "segredo-de-teste");
			builder.UseSetting("Intranet:Login:ClientId", "secco-intranet-login");
			builder.UseSetting("Intranet:Login:ClientSecret", "segredo-de-teste-do-login");
		}).CreateClient(SemSeguirRedirect);

		var icone = await client.GetAsync("/favicon.ico");

		icone.StatusCode.Should().Be(HttpStatusCode.Redirect);
		icone.Headers.Location!.OriginalString.Should().Be(
			"/_content/Secco.Intranet.Themes.Vertical/favicon.svg", "o ícone não pode virar desafio de login");
	}
}
