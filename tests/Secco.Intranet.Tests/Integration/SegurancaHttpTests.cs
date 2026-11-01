using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Cabeçalhos de segurança do SDK (ADR-0057 da plataforma, secco-platform#53) no host real, e a
/// condição para a CSP não abrir exceção: nenhuma página, em nenhum dos dois temas, emite atributo
/// <c>style</c> nem <c>&lt;script&gt;</c> sem <c>src</c>.
/// </summary>
public partial class SegurancaHttpTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>", RegexOptions.IgnoreCase)]
	private static partial Regex ScriptInline();

	[GeneratedRegex("\\sstyle\\s*=", RegexOptions.IgnoreCase)]
	private static partial Regex AtributoStyle();

	private HttpClient CriarCliente(string tema = "Vertical", bool seguirRedirects = true)
	{
		var client = factory
			.WithWebHostBuilder(builder => builder.UseSetting("Intranet:Theme:Nome", tema))
			.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = seguirRedirects });

		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		return client;
	}

	private static async Task<string> CriarSetorAsync(HttpClient admin)
	{
		var slug = $"setor-{Guid.NewGuid():N}"[..20];
		var token = await AuxiliaresDeHttp.TokenAsync(admin, "/Setores/Create");

		var resposta = await admin.PostAsync("/Setores/Create", AuxiliaresDeHttp.Form(
			("Nome", "Setor de Teste"),
			("Slug", slug),
			("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);

		return slug;
	}

	[Fact]
	public async Task Pagina_SaiComOsCabecalhosEACspEmReportOnlyPorPadrao()
	{
		var resposta = await CriarCliente().GetAsync("/Setores");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
		resposta.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("strict-origin-when-cross-origin");
		resposta.Headers.Contains("Permissions-Policy").Should().BeTrue();

		var csp = resposta.Headers.GetValues("Content-Security-Policy-Report-Only").Should().ContainSingle().Subject;

		csp.Should().Contain("script-src 'self' 'nonce-");
		csp.Should().Contain("style-src-attr 'unsafe-hashes' 'sha256-", "só os valores exatos de terceiros são liberados, pelo hash");
		csp.Should().NotContain("'unsafe-inline'");
		csp.Should().Contain("object-src 'none'");
		resposta.Headers.Contains("Content-Security-Policy").Should().BeFalse("o modo padrão por enquanto é só relatar");
	}

	[Fact]
	public async Task Pagina_ComModoEnforce_BloqueiaEmVezDeRelatar()
	{
		var client = factory
			.WithWebHostBuilder(builder => builder.UseSetting("Intranet:Csp:Modo", "Enforce"))
			.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		var resposta = await client.GetAsync("/Setores");

		resposta.Headers.Contains("Content-Security-Policy").Should().BeTrue();
		resposta.Headers.Contains("Content-Security-Policy-Report-Only").Should().BeFalse();
	}

	[Theory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Paginas_NaoTemEstiloNemScriptInline(string tema)
	{
		var client = CriarCliente(tema);
		var slug = await CriarSetorAsync(client);

		// Um setor existe, então a lista, o menu e o cabeçalho da tela do setor pintam a cor dele.
		foreach (var caminho in new[] { "/", "/Setores", "/menu", "/diretorio", "/Inventario", $"/{slug}/documentos/organizar" })
		{
			var resposta = await client.GetAsync(caminho);
			resposta.StatusCode.Should().Be(HttpStatusCode.OK, $"{caminho} no tema {tema} precisa abrir para ser conferido");
			var html = await resposta.Content.ReadAsStringAsync();

			ScriptInline().Matches(html).Should().BeEmpty($"{caminho} no tema {tema} não pode ter <script> sem src");
			AtributoStyle().Matches(html).Should().BeEmpty($"{caminho} no tema {tema} não pode ter atributo style");
		}

		(await client.GetStringAsync("/Setores")).Should().Contain("sc-hue-", "a cor do setor continua saindo, agora por classe");
	}
}
