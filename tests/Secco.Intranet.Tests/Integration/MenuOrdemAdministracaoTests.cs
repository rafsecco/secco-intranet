using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A tela de ordenação do menu (<c>/menu</c>) de ponta a ponta. Os handlers têm testes próprios;
/// aqui se prova a ligação: acesso exclusivo do intranet-admin, nomes de campo, enum por nome,
/// redirecionamento com mensagem e o efeito no menu de outro usuário. O assembly de testes é
/// application part do host, então há mais extras de exemplo: nada aqui conta itens.
/// </summary>
public class MenuOrdemAdministracaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private const string ChaveDoExtra = "extra-de-teste";

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente(Guid tenant, params string[] roles) => Configurar(
		factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }), tenant, roles);

	private static HttpClient Configurar(HttpClient client, Guid tenant, string[] roles)
	{
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, tenant.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private HttpClient Admin() => CriarCliente(factory.TenantAlfa, "intranet-admin");

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private static async Task<HttpResponseMessage> PostarAsync(
		HttpClient client, string acao, string grupo, string? chave = null, bool? paraCima = null)
	{
		var token = await TokenAsync(client, "/menu");
		var campos = new List<KeyValuePair<string, string>>
		{
			new("grupo", grupo),
			new("__RequestVerificationToken", token),
		};

		if (chave is not null)
		{
			campos.Add(new("chave", chave));
		}

		if (paraCima is { } valor)
		{
			campos.Add(new("paraCima", valor ? "true" : "false"));
		}

		return await client.PostAsync($"/menu/{acao}", new FormUrlEncodedContent(campos));
	}

	/// <summary>Chaves das linhas de um grupo, na ordem em que a página as mostra (atributos <c>data-grupo</c>/<c>data-chave</c> do item).</summary>
	private static List<string> ChavesDoGrupo(string html, string grupo) =>
		Regex.Matches(html, $"data-grupo=\"{grupo}\"[^>]*data-chave=\"([^\"]+)\"")
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

	[Fact]
	public async Task SemIntranetAdmin_Recebe403()
	{
		var client = CriarCliente(factory.TenantAlfa, "extra-de-teste-leitor");

		(await client.GetAsync("/menu")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

		var post = await client.PostAsync("/menu/mover", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("grupo", "Principal"),
			new KeyValuePair<string, string>("chave", "mural"),
			new KeyValuePair<string, string>("paraCima", "false"),
		]));

		post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Tela_ListaFixosExtrasESetores_NaOrdemEfetiva()
	{
		await LimparPosicoesAsync(factory.TenantAlfa);
		var slug = $"ord-{Guid.NewGuid():N}"[..20];
		Guid setorId;

		using (var escopo = factory.Services.CreateScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
			var criado = await escopo.ServiceProvider.GetRequiredService<CreateSetorHandler>()
				.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));
			criado.IsSuccess.Should().BeTrue();
			setorId = criado.Value.Id;
		}

		// Sem a role do gate do item de teste: o admin ordena o que existe, não só o que enxerga.
		var html = await Admin().GetStringAsync("/menu");
		var principal = ChavesDoGrupo(html, "Principal");

		principal.Should().StartWith(["mural", "diretorio"]);
		principal.Should().Contain(ChaveDoExtra);
		html.Should().Contain("Extra de teste");
		ChavesDoGrupo(html, "Setores").Should().Contain(setorId.ToString("D"));
		html.Should().Contain($"Subir Setor {slug}").And.Contain($"Descer Setor {slug}");
	}

	[Fact]
	public async Task Mover_SobeOExtraAteOTopo_EOMenuDeOutroUsuarioRefleteAOrdem()
	{
		await LimparPosicoesAsync(factory.TenantAlfa);

		try
		{
			var admin = Admin();
			var html = await admin.GetStringAsync("/menu");

			// O número de extras de exemplo não é fixo: sobe até o topo, com limite.
			for (var i = 0; i < 20 && ChavesDoGrupo(html, "Principal")[0] != ChaveDoExtra; i++)
			{
				var resposta = await PostarAsync(admin, "mover", "Principal", ChaveDoExtra, paraCima: true);

				resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
				resposta.Headers.Location!.OriginalString.Should().BeEquivalentTo("/menu");
				html = await admin.GetStringAsync("/menu");
			}

			ChavesDoGrupo(html, "Principal")[0].Should().Be(ChaveDoExtra);

			var outro = CriarCliente(factory.TenantAlfa, "extra-de-teste-leitor");
			var menu = await outro.GetStringAsync("/");

			var extra = menu.IndexOf("href=\"/extra-de-teste\"", StringComparison.Ordinal);
			var mural = menu.IndexOf("href=\"/\"", menu.IndexOf("sc-nav__item", StringComparison.Ordinal), StringComparison.Ordinal);

			extra.Should().BePositive();
			mural.Should().BePositive();
			extra.Should().BeLessThan(mural);
		}
		finally
		{
			await LimparPosicoesAsync(factory.TenantAlfa);
		}
	}

	[Fact]
	public async Task Mover_ChaveQueNaoExisteMais_VoltaComAviso()
	{
		await LimparPosicoesAsync(factory.TenantAlfa);
		var admin = Admin();

		var resposta = await PostarAsync(admin, "mover", "Setores", Guid.NewGuid().ToString("D"), paraCima: true);

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.OriginalString.Should().BeEquivalentTo("/menu");

		var html = WebUtility.HtmlDecode(await admin.GetStringAsync("/menu"));
		html.Should().Contain("não está mais no menu");
	}

	[Fact]
	public async Task Restaurar_VoltaAOrdemPadrao()
	{
		await LimparPosicoesAsync(factory.TenantAlfa);

		try
		{
			var admin = Admin();
			var padrao = ChavesDoGrupo(await admin.GetStringAsync("/menu"), "Principal");

			(await PostarAsync(admin, "mover", "Principal", "mural", paraCima: false)).StatusCode.Should().Be(HttpStatusCode.Redirect);
			ChavesDoGrupo(await admin.GetStringAsync("/menu"), "Principal").Should().NotEqual(padrao);

			var resposta = await PostarAsync(admin, "restaurar", "Principal");

			resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
			resposta.Headers.Location!.OriginalString.Should().BeEquivalentTo("/menu");
			ChavesDoGrupo(await admin.GetStringAsync("/menu"), "Principal").Should().Equal(padrao);
		}
		finally
		{
			await LimparPosicoesAsync(factory.TenantAlfa);
		}
	}

	[Fact]
	public async Task Mover_NoTenantAlfa_NaoAfetaOBeta()
	{
		await LimparPosicoesAsync(factory.TenantAlfa);
		await LimparPosicoesAsync(factory.TenantBeta);

		try
		{
			var beta = CriarCliente(factory.TenantBeta, "intranet-admin");
			var padraoBeta = ChavesDoGrupo(await beta.GetStringAsync("/menu"), "Principal");

			(await PostarAsync(Admin(), "mover", "Principal", "mural", paraCima: false)).StatusCode.Should().Be(HttpStatusCode.Redirect);

			ChavesDoGrupo(await beta.GetStringAsync("/menu"), "Principal").Should().Equal(padraoBeta);
		}
		finally
		{
			await LimparPosicoesAsync(factory.TenantAlfa);
			await LimparPosicoesAsync(factory.TenantBeta);
		}
	}

	[Fact]
	public async Task Mover_GrupoInexistente_Recebe400_ENaoGrava()
	{
		await LimparPosicoesAsync(factory.TenantAlfa);

		try
		{
			var resposta = await PostarAsync(Admin(), "mover", "99", "mural", paraCima: false);

			resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);

			using var escopo = factory.Services.CreateScope();
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
			var posicoes = await escopo.ServiceProvider.GetRequiredService<IPosicaoMenuRepository>().ListarAsync(CancellationToken.None);
			posicoes.Should().NotContain(p => (int)p.Grupo == 99);
		}
		finally
		{
			await LimparPosicoesAsync(factory.TenantAlfa);
		}
	}

	[Fact]
	public async Task Restaurar_GrupoInexistente_Recebe400()
	{
		var resposta = await PostarAsync(Admin(), "restaurar", "99");

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task TemaHorizontal_RenderizaATelaDeOrdenacao()
	{
		var client = Configurar(
			factory.WithWebHostBuilder(builder => builder.UseSetting("Intranet:Theme:Nome", "Horizontal"))
				.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }),
			factory.TenantAlfa, ["intranet-admin"]);

		var html = await client.GetStringAsync("/menu");

		html.Should().Contain("sc-topnav", "garante que o tema trocou de fato");
		ChavesDoGrupo(html, "Principal").Should().Contain("mural");
		html.Should().Contain("Restaurar padrão");
	}
}
