using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>A tela de cadastro de bibliotecas: exclusiva do intranet-admin (ADR-0008).</summary>
public class BibliotecasCadastroTelaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.GestaoDeAcesso = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(params string[] roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private HttpClient CriarAdmin()
	{
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa().ComPerfilImplicito();

		return CriarCliente("intranet-admin");
	}

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private static async Task<HttpResponseMessage> CriarAsync(HttpClient admin, string nome, string slug)
	{
		var token = await TokenAsync(admin, "/bibliotecas/nova");

		return await admin.PostAsync("/bibliotecas/nova", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", nome),
			new KeyValuePair<string, string>("Slug", slug),
			new KeyValuePair<string, string>("TodosLeem", "true"),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));
	}

	private static string NovoSlug() => $"bib-{Guid.NewGuid():N}"[..16];

	[Fact]
	public async Task Nao_admin_recebe_403_em_bibliotecas()
	{
		var id = Guid.NewGuid();

		foreach (var roles in new[] { Array.Empty<string>(), ["financeiro-admin"], ["financeiro-user"] })
		{
			var client = CriarCliente(roles);

			foreach (var url in new[] { "/bibliotecas", "/bibliotecas/nova", $"/bibliotecas/{id}/editar" })
			{
				var resposta = await client.GetAsync(url);

				resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {url} exige intranet-admin");
			}
		}
	}

	[Fact]
	public async Task Admin_cria_e_ve_na_lista()
	{
		var admin = CriarAdmin();
		var formulario = await admin.GetStringAsync("/bibliotecas/nova");
		var caixa = Regex.Match(formulario, "<input[^>]*name=\"TodosLeem\"[^>]*>");

		caixa.Success.Should().BeTrue();
		caixa.Value.Should().Contain("checked", "\"Todos os membros leem\" nasce marcada");

		var slug = NovoSlug();
		var resposta = await CriarAsync(admin, "Manuais Internos", slug);

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		var lista = await resposta.Content.ReadAsStringAsync();

		lista.Should().Contain("Manuais Internos").And.Contain(slug);
		WebUtility.HtmlDecode(lista).Should().Contain(
			"Biblioteca \"Manuais Internos\" criada. Para definir quem publica nela, conceda \"Alterar\" ao perfil responsável em Acesso.");
	}

	[Fact]
	public async Task Admin_edita_nome_e_desativa()
	{
		var admin = CriarAdmin();
		var slug = NovoSlug();
		var lista = await (await CriarAsync(admin, "Para Editar", slug)).Content.ReadAsStringAsync();
		var id = Regex.Match(lista, @"/bibliotecas/([0-9a-f-]{36})/editar").Groups[1].Value;

		id.Should().NotBeEmpty();

		var editar = $"/bibliotecas/{id}/editar";
		var token = await TokenAsync(admin, editar);
		var resposta = await admin.PostAsync(editar, new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Id", id),
			new KeyValuePair<string, string>("Nome", "Nome Novo"),
			new KeyValuePair<string, string>("Slug", slug),
			new KeyValuePair<string, string>("Icone", string.Empty),
			new KeyValuePair<string, string>("Ativo", "false"),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		var depois = await resposta.Content.ReadAsStringAsync();

		depois.Should().Contain("Nome Novo").And.Contain("Inativa");
	}

	[Fact]
	public async Task Slug_reservado_volta_com_erro_no_formulario()
	{
		var admin = CriarAdmin();

		var resposta = await CriarAsync(admin, "Reservada", "nova");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		var html = await resposta.Content.ReadAsStringAsync();

		WebUtility.HtmlDecode(html).Should().Contain("é usado pelo próprio sistema");
		html.Should().Contain("name=\"Slug\"");
	}

	[Fact]
	public async Task Menu_de_administracao_mostra_Bibliotecas_so_para_admin()
	{
		var paraAdmin = await CriarCliente("intranet-admin").GetStringAsync("/");
		var paraSetor = await CriarCliente("financeiro-admin").GetStringAsync("/");

		paraAdmin.Should().Contain("href=\"/bibliotecas\"");
		paraSetor.Should().NotContain("href=\"/bibliotecas\"");
	}
}
