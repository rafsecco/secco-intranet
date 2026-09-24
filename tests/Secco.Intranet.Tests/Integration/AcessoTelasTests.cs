using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// As telas da Área de acesso, com a gestão substituída por um dublê em memória — o SecureGate
/// real não existe no ambiente Testing. Cada teste monta o cenário que quer.
/// </summary>
public class AcessoTelasTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.GestaoDeAcesso = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarClienteAdmin(GestaoDeAcessoFalsa? gestao = null)
	{
		factory.GestaoDeAcesso = gestao;
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		return client;
	}

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	/// <summary>O Razor codifica acentos em HTML (<c>&#xE3;</c>); comparar texto exige decodificar.</summary>
	private static string Decodificar(string html) => WebUtility.HtmlDecode(html);

	private static FormUrlEncodedContent Form(params (string Chave, string Valor)[] campos) =>
		new(campos.Select(c => new KeyValuePair<string, string>(c.Chave, c.Valor)));

	[Fact]
	public async Task Index_SemSecureGateConfigurado_AbreEExplica()
	{
		var resposta = await CriarClienteAdmin().GetAsync("/Acesso");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
	}

	[Fact]
	public async Task Index_AbaUsuarios_SemSecureGateConfigurado_AbreEExplica()
	{
		var resposta = await CriarClienteAdmin().GetAsync("/Acesso?aba=usuarios");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
	}

	[Fact]
	public async Task Index_ListaPerfisComClassificacao_EOferecerCriarOsDoProdutoQueFaltam()
	{
		var gestao = new GestaoDeAcessoFalsa().ComPerfil("marketing-admin").ComPerfil("gerente-de-compras").ComPerfil("intranet-admin");

		var html = await CriarClienteAdmin(gestao).GetStringAsync("/Acesso");

		html.Should().Contain("marketing-admin").And.Contain("gerente-de-compras").And.Contain("intranet-admin");
		html.Should().Contain("inventario-admin", "é o perfil do produto que ainda não existe e pode ser criado");
	}

	[Fact]
	public async Task Index_AbaUsuarios_BuscaPorEmail()
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Ana, "ana@x.com").ComUsuario(Bruno, "bruno@y.com");

		var html = await CriarClienteAdmin(gestao).GetStringAsync("/Acesso?aba=usuarios&busca=bruno");

		html.Should().Contain("bruno@y.com").And.NotContain("ana@x.com");
	}

	[Fact]
	public async Task Index_AbaUsuarios_PaginaAlemDoFim_AbreSemQuebrar()
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Ana, "ana@x.com");

		var resposta = await CriarClienteAdmin(gestao).GetAsync("/Acesso?aba=usuarios&page=99");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Index_AbaUsuarios_UsuarioSemEmail_AbreSemQuebrar()
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Ana, string.Empty).ComUsuario(Bruno, "bruno@x.com");

		var resposta = await CriarClienteAdmin(gestao).GetAsync("/Acesso?aba=usuarios");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task CriarPerfil_Valido_CriaEConfirmaNaPaginaDeDestino()
	{
		var gestao = new GestaoDeAcessoFalsa();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso");

		var resposta = await client.PostAsync("/Acesso/CriarPerfil", Form(("nome", "equipe-financeiro"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "a criação redireciona de volta para /Acesso");
		gestao.Chamadas.Should().Equal("perfil-criar:equipe-financeiro");
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("equipe-financeiro");
	}

	[Fact]
	public async Task CriarPerfil_NomeInvalido_MostraErroNoToast_ENaoCria()
	{
		var gestao = new GestaoDeAcessoFalsa();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso");

		var resposta = await client.PostAsync("/Acesso/CriarPerfil", Form(("nome", "gerente de compras"), ("__RequestVerificationToken", token)));

		var html = Decodificar(await resposta.Content.ReadAsStringAsync());
		html.Should().Contain("sc-toast--erro");
		html.Should().Contain("O nome do perfil aceita letras", "o texto do erro vai no toast, não só a dica estática do formulário");
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task CriarPerfil_SemSecureGateConfigurado_MostraErroNoToast_NaoDa500()
	{
		var client = CriarClienteAdmin();

		// Sem SecureGate a página /Acesso é a que explica e não tem formulário; o token vem de
		// outra página com formulário que o admin abre (o token antifalsificação vale para o host).
		var token = await TokenAsync(client, "/Setores/Create");

		var resposta = await client.PostAsync("/Acesso/CriarPerfil", Form(("nome", "equipe-financeiro"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("sc-toast--erro");
	}
}
