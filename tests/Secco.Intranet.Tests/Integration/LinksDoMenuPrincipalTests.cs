using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Links do menu principal de ponta a ponta: as telas em <c>/menu/links</c>, o efeito no menu de um
/// usuário comum, a ordenação e o isolamento por tenant. Os handlers têm testes próprios. O banco do
/// tenant é compartilhado pela classe: cada teste usa rótulos únicos (sufixo Guid), olha hrefs e
/// rótulos, nunca contagens, e remove o que criou.
/// </summary>
public class LinksDoMenuPrincipalTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private const string UrlExterna = "https://suporte.exemplo.com.br";

	private readonly List<(Guid Tenant, string Rotulo)> _criados = [];

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public async Task DisposeAsync()
	{
		foreach (var (tenant, rotulo) in _criados)
		{
			using var escopo = factory.Services.CreateScope();
			escopo.ServiceProvider.SetTenant(tenant);
			var lista = await escopo.ServiceProvider.GetRequiredService<ListarLinksDoMenuPrincipalHandler>().HandleAsync();

			foreach (var link in lista.Value.Where(l => l.Rotulo == rotulo))
			{
				await escopo.ServiceProvider.GetRequiredService<ExcluirLinkDoMenuPrincipalHandler>().HandleAsync(link.Id);
			}
		}

		await LimparPosicoesAsync(factory.TenantAlfa);
		await LimparPosicoesAsync(factory.TenantBeta);
	}

	private static HttpClient Configurar(HttpClient client, Guid tenant, string[] roles)
	{
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, tenant.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private HttpClient CriarCliente(Guid tenant, params string[] roles) => Configurar(
		factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }), tenant, roles);

	private HttpClient Admin() => CriarCliente(factory.TenantAlfa, "intranet-admin");

	private HttpClient Comum() => CriarCliente(factory.TenantAlfa);

	private static string Rotulo(string prefixo = "Chamado") => $"{prefixo} {Guid.NewGuid():N}"[..28];

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private static async Task<HttpResponseMessage> PostarAsync(
		HttpClient client, string url, string paginaDoToken, params (string Nome, string Valor)[] campos)
	{
		var token = await TokenAsync(client, paginaDoToken);
		var lista = campos.Select(c => new KeyValuePair<string, string>(c.Nome, c.Valor)).ToList();
		lista.Add(new("__RequestVerificationToken", token));

		return await client.PostAsync(url, new FormUrlEncodedContent(lista));
	}

	private async Task<HttpResponseMessage> CriarLinkAsync(
		HttpClient client, string rotulo, string url = UrlExterna, bool novaAba = true, string? icone = null, Guid? tenant = null)
	{
		_criados.Add((tenant ?? factory.TenantAlfa, rotulo));
		var campos = new List<(string, string)> { ("Rotulo", rotulo), ("Url", url) };

		if (icone is not null)
		{
			campos.Add(("Icone", icone));
		}

		if (novaAba)
		{
			campos.Add(("NovaAba", "true"));
			campos.Add(("NovaAba", "false"));
		}

		return await PostarAsync(client, "/menu/links/novo", "/menu/links/novo", [.. campos]);
	}

	private async Task<Guid> IdDoLinkAsync(Guid tenant, string rotulo)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(tenant);
		var lista = await escopo.ServiceProvider.GetRequiredService<ListarLinksDoMenuPrincipalHandler>().HandleAsync();

		return lista.Value.Single(l => l.Rotulo == rotulo).Id;
	}

	private static List<string> ChavesDoPrincipal(string html) =>
		Regex.Matches(html, "data-grupo=\"Principal\"[^>]*data-chave=\"([^\"]+)\"")
			.Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
			.ToList();

	private async Task LimparPosicoesAsync(Guid tenant)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(tenant);
		var repositorio = escopo.ServiceProvider.GetRequiredService<IPosicaoMenuRepository>();

		await repositorio.RemoverGrupoAsync(GrupoDoMenu.Principal, CancellationToken.None);
		await repositorio.RemoverGrupoAsync(GrupoDoMenu.Setores, CancellationToken.None);
	}

	/// <summary>A âncora do menu (<c>&lt;a ...&gt;</c>) cujo href é o informado.</summary>
	private static string? AncoraDe(string html, string href) =>
		Regex.Matches(html, "<a\\b[^>]*>").Select(m => m.Value).FirstOrDefault(a => a.Contains($"href=\"{href}\"", StringComparison.Ordinal));

	[Fact]
	public async Task Criar_LinkExterno_ApareceNoMenuDeUsuarioComumComTargetERel()
	{
		var rotulo = Rotulo("Abrir um chamado");
		var url = $"{UrlExterna}/{Guid.NewGuid():N}";

		var resposta = await CriarLinkAsync(Admin(), rotulo, url);

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.OriginalString.Should().BeEquivalentTo("/menu");

		var html = await Comum().GetStringAsync("/");
		var ancora = AncoraDe(html, url);

		ancora.Should().NotBeNull();
		ancora.Should().Contain("target=\"_blank\"").And.Contain("rel=\"noopener noreferrer\"");
		html.Should().Contain(rotulo);
	}

	[Fact]
	public async Task Criar_TemaHorizontal_MostraOLinkComTarget()
	{
		var rotulo = Rotulo("Abrir um chamado");
		var url = $"{UrlExterna}/{Guid.NewGuid():N}";
		await CriarLinkAsync(Admin(), rotulo, url);

		var horizontal = Configurar(
			factory.WithWebHostBuilder(b => b.UseSetting("Intranet:Theme:Nome", "Horizontal"))
				.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }),
			factory.TenantAlfa, []);
		var html = await horizontal.GetStringAsync("/");

		html.Should().Contain("sc-topnav", "garante que o tema trocou de fato");
		var ancora = AncoraDe(html, url);

		ancora.Should().NotBeNull();
		ancora.Should().Contain("target=\"_blank\"").And.Contain("rel=\"noopener noreferrer\"");
	}

	[Fact]
	public async Task GetNovo_CaixaDeNovaAbaVemMarcada()
	{
		var html = await Admin().GetStringAsync("/menu/links/novo");

		var caixa = Regex.Match(html, "<input[^>]*type=\"checkbox\"[^>]*name=\"NovaAba\"[^>]*>");

		caixa.Success.Should().BeTrue();
		caixa.Value.Should().Contain("checked");
	}

	[Theory]
	[InlineData("javascript:alert(1)")]
	[InlineData("//outro.host")]
	public async Task Criar_UrlRecusada_FormularioComMensagem(string url)
	{
		var rotulo = Rotulo();

		var resposta = await CriarLinkAsync(Admin(), rotulo, url);

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var corpo = WebUtility.HtmlDecode(await resposta.Content.ReadAsStringAsync());
		corpo.Should().Contain(IntranetErrors.Menu.RotaInvalida.Description);

		(await Comum().GetStringAsync("/")).Should().NotContain(rotulo);
	}

	[Fact]
	public async Task Criar_RotuloDe257_MensagemNoCampoRotulo()
	{
		var rotulo = new string('r', 257);

		var resposta = await CriarLinkAsync(Admin(), rotulo);

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var corpo = await resposta.Content.ReadAsStringAsync();
		MensagemDoCampo(corpo, "Rotulo").Should().Be(IntranetErrors.LinksDoMenu.RotuloMuitoLongo.Description);
		MensagemDoCampo(corpo, "Url").Should().BeEmpty();
		(await Comum().GetStringAsync("/")).Should().NotContain(rotulo);
	}

	[Fact]
	public async Task Criar_UrlDe513_MensagemNoCampoUrl()
	{
		var rotulo = Rotulo();
		var url = "https://exemplo.com.br/" + new string('a', 513 - 23);

		var resposta = await CriarLinkAsync(Admin(), rotulo, url);

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var corpo = await resposta.Content.ReadAsStringAsync();
		MensagemDoCampo(corpo, "Url").Should().Be(IntranetErrors.LinksDoMenu.UrlMuitoLonga.Description);
		MensagemDoCampo(corpo, "Rotulo").Should().BeEmpty();
		(await Comum().GetStringAsync("/")).Should().NotContain(rotulo);
	}

	private static string MensagemDoCampo(string html, string campo)
	{
		var m = Regex.Match(html, $"data-valmsg-for=\"{campo}\"[^>]*>([^<]*)<");

		return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : string.Empty;
	}

	[Fact]
	public async Task Link_ApareceNaOrdenacaoEPodeSubirEntreOsFixos()
	{
		var rotulo = Rotulo();
		var url = $"{UrlExterna}/{Guid.NewGuid():N}";
		await CriarLinkAsync(Admin(), rotulo, url);
		var chave = $"link-{await IdDoLinkAsync(factory.TenantAlfa, rotulo):D}";
		var admin = Admin();
		var html = await admin.GetStringAsync("/menu");

		ChavesDoPrincipal(html).Should().Contain(chave);

		for (var i = 0; i < 60 && ChavesDoPrincipal(html).IndexOf(chave) != 1; i++)
		{
			var resposta = await PostarAsync(admin, "/menu/mover", "/menu",
				("grupo", "Principal"), ("chave", chave), ("paraCima", "true"));

			resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
			html = await admin.GetStringAsync("/menu");
		}

		ChavesDoPrincipal(html).Should().StartWith(["mural", chave]);

		var menu = await Comum().GetStringAsync("/");
		var mural = menu.IndexOf("href=\"/\"", menu.IndexOf("sc-nav__item", StringComparison.Ordinal), StringComparison.Ordinal);
		var link = menu.IndexOf($"href=\"{url}\"", StringComparison.Ordinal);
		var diretorio = menu.IndexOf("href=\"/diretorio\"", StringComparison.Ordinal);

		mural.Should().BePositive();
		link.Should().BeGreaterThan(mural);

		if (diretorio >= 0)
		{
			link.Should().BeLessThan(diretorio);
		}
	}

	[Fact]
	public async Task Ordem_ValePorTenant()
	{
		var rotulo = Rotulo();
		await CriarLinkAsync(Admin(), rotulo);
		var chave = $"link-{await IdDoLinkAsync(factory.TenantAlfa, rotulo):D}";
		var beta = CriarCliente(factory.TenantBeta, "intranet-admin");
		var antes = ChavesDoPrincipal(await beta.GetStringAsync("/menu"));

		(await PostarAsync(Admin(), "/menu/mover", "/menu", ("grupo", "Principal"), ("chave", chave), ("paraCima", "true")))
			.StatusCode.Should().Be(HttpStatusCode.Redirect);

		var htmlBeta = await beta.GetStringAsync("/menu");

		ChavesDoPrincipal(htmlBeta).Should().Equal(antes);
		htmlBeta.Should().NotContain(rotulo);
		(await CriarCliente(factory.TenantBeta).GetStringAsync("/")).Should().NotContain(rotulo);
	}

	[Fact]
	public async Task Editar_TrocaRotuloEUrl_MenuMostraONovo()
	{
		var rotulo = Rotulo();
		var novoRotulo = Rotulo("Editado");
		var novaUrl = $"{UrlExterna}/{Guid.NewGuid():N}";
		var admin = Admin();
		await CriarLinkAsync(admin, rotulo);
		var id = await IdDoLinkAsync(factory.TenantAlfa, rotulo);
		_criados.Add((factory.TenantAlfa, novoRotulo));

		var form = await admin.GetStringAsync($"/menu/links/{id}/editar");
		form.Should().Contain(rotulo);

		var resposta = await PostarAsync(admin, $"/menu/links/{id}/editar", $"/menu/links/{id}/editar",
			("Rotulo", novoRotulo), ("Url", novaUrl));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.OriginalString.Should().BeEquivalentTo("/menu");

		var html = await Comum().GetStringAsync("/");
		html.Should().Contain(novoRotulo).And.Contain($"href=\"{novaUrl}\"").And.NotContain(rotulo);
		WebUtility.HtmlDecode(await admin.GetStringAsync("/menu")).Should().Contain("Link salvo.");
	}

	[Fact]
	public async Task Excluir_SomeDoMenuEDaOrdenacao()
	{
		var rotulo = Rotulo();
		var admin = Admin();
		await CriarLinkAsync(admin, rotulo);
		var id = await IdDoLinkAsync(factory.TenantAlfa, rotulo);

		var resposta = await PostarAsync(admin, $"/menu/links/{id}/excluir", "/menu");

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.OriginalString.Should().BeEquivalentTo("/menu");
		(await Comum().GetStringAsync("/")).Should().NotContain(rotulo);
		var ordenacao = await admin.GetStringAsync("/menu");
		ChavesDoPrincipal(ordenacao).Should().NotContain($"link-{id:D}");
		WebUtility.HtmlDecode(ordenacao).Should().Contain("Link excluído.");
	}

	[Fact]
	public async Task Excluir_ComPosicaoSalva_MoverOutroItemContinuaFuncionando()
	{
		var rotulo = Rotulo();
		var admin = Admin();
		await CriarLinkAsync(admin, rotulo);
		var id = await IdDoLinkAsync(factory.TenantAlfa, rotulo);
		var chave = $"link-{id:D}";

		(await PostarAsync(admin, "/menu/mover", "/menu", ("grupo", "Principal"), ("chave", chave), ("paraCima", "true")))
			.StatusCode.Should().Be(HttpStatusCode.Redirect);

		using (var escopo = factory.Services.CreateScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
			var salvas = await escopo.ServiceProvider.GetRequiredService<ObterPosicoesDoMenuHandler>().HandleAsync();
			salvas.Principal.Keys.Should().Contain(chave);
		}

		(await PostarAsync(admin, $"/menu/links/{id}/excluir", "/menu")).StatusCode.Should().Be(HttpStatusCode.Redirect);

		var resposta = await PostarAsync(admin, "/menu/mover", "/menu", ("grupo", "Principal"), ("chave", "mural"), ("paraCima", "false"));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		var html = await admin.GetStringAsync("/menu");
		ChavesDoPrincipal(html).Should().NotContain(chave);
		WebUtility.HtmlDecode(html).Should().NotContain("não está mais no menu");
		(await admin.GetAsync("/menu")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task EditarOuExcluir_IdInexistenteOuDeOutroTenant_404()
	{
		var rotulo = Rotulo();
		await CriarLinkAsync(Admin(), rotulo);
		var id = await IdDoLinkAsync(factory.TenantAlfa, rotulo);
		var beta = CriarCliente(factory.TenantBeta, "intranet-admin");
		var inexistente = Guid.NewGuid();

		foreach (var alvo in new[] { id, inexistente })
		{
			(await beta.GetAsync($"/menu/links/{alvo}/editar")).StatusCode.Should().Be(HttpStatusCode.NotFound);
			(await PostarAsync(beta, $"/menu/links/{alvo}/editar", "/menu/links/novo", ("Rotulo", "X"), ("Url", "/x")))
				.StatusCode.Should().Be(HttpStatusCode.NotFound);
			(await PostarAsync(beta, $"/menu/links/{alvo}/excluir", "/menu")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		}

		(await Comum().GetStringAsync("/")).Should().Contain(rotulo, "o link do Alfa não foi tocado");
	}

	[Fact]
	public async Task RotuloComHtml_ApareceEscapado()
	{
		var sufixo = Guid.NewGuid().ToString("N")[..8];
		var rotulo = $"<b>Chamado</b> {sufixo}";
		var admin = Admin();
		await CriarLinkAsync(admin, rotulo);

		var menu = await Comum().GetStringAsync("/");
		var tela = await admin.GetStringAsync("/menu");

		foreach (var html in new[] { menu, tela })
		{
			html.Should().Contain($"&lt;b&gt;Chamado&lt;/b&gt; {sufixo}");
			html.Should().NotContain($"<b>Chamado</b> {sufixo}");
		}
	}

	[Fact]
	public async Task SemIntranetAdmin_GetEPosts_403()
	{
		var comum = Comum();
		var id = Guid.NewGuid();

		foreach (var rota in new[] { "/menu/links/novo", $"/menu/links/{id}/editar" })
		{
			(await comum.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		}

		foreach (var rota in new[] { "/menu/links/novo", $"/menu/links/{id}/editar", $"/menu/links/{id}/excluir" })
		{
			var post = await comum.PostAsync(rota, new FormUrlEncodedContent(
				[new KeyValuePair<string, string>("Rotulo", "X"), new KeyValuePair<string, string>("Url", "/x")]));

			post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		}
	}

	[Fact]
	public async Task PostSemAntiforgery_400()
	{
		var admin = Admin();
		var id = Guid.NewGuid();

		foreach (var rota in new[] { "/menu/links/novo", $"/menu/links/{id}/editar", $"/menu/links/{id}/excluir" })
		{
			var post = await admin.PostAsync(rota, new FormUrlEncodedContent(
				[new KeyValuePair<string, string>("Rotulo", "X"), new KeyValuePair<string, string>("Url", "/x")]));

			post.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		}
	}

	[Fact]
	public async Task CriarEditarExcluir_RegistramNaTrilha()
	{
		var trilha = new TrilhaDeAcessoFalsa();
		var admin = Configurar(
			factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<ITrilhaDeAuditoria>(trilha)))
				.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }),
			factory.TenantAlfa, ["intranet-admin"]);
		var rotulo = Rotulo();
		var novoRotulo = Rotulo("Editado");

		await CriarLinkAsync(admin, rotulo);
		var id = await IdDoLinkAsync(factory.TenantAlfa, rotulo);
		_criados.Add((factory.TenantAlfa, novoRotulo));
		await PostarAsync(admin, $"/menu/links/{id}/editar", $"/menu/links/{id}/editar", ("Rotulo", novoRotulo), ("Url", UrlExterna));
		await PostarAsync(admin, $"/menu/links/{id}/excluir", "/menu");

		var doLink = trilha.Registros.Where(r => r.RecursoId == id.ToString("D")).Select(r => r.Verbo).ToList();

		doLink.Should().Equal(VerbosDeAuditoria.MenuLinkCriar, VerbosDeAuditoria.MenuLinkEditar, VerbosDeAuditoria.MenuLinkExcluir);
	}
}
