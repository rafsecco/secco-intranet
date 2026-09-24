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

	private static GestaoDeAcessoFalsa CenarioDePerfil() => new GestaoDeAcessoFalsa()
		.ComPerfil("gerente-de-compras", reservado: false, "compras:read", "compras:write")
		.ComPerfil("intranet-admin")
		.ComUsuario(Ana, "ana@x.com", SituacaoDoUsuario.Ativo, "gerente-de-compras")
		.ComUsuario(Bruno, "bruno@x.com");

	[Fact]
	public async Task Perfil_MostraPermissoesSoParaLeituraMembrosECandidatos()
	{
		var html = await CriarClienteAdmin(CenarioDePerfil()).GetStringAsync("/Acesso/Perfil?nome=gerente-de-compras");

		html.Should().Contain("compras:read").And.Contain("compras:write");
		html.Should().Contain("ana@x.com", "é membro");
		html.Should().Contain("bruno@x.com", "é candidato a membro, aparece no seletor de adicionar");
	}

	[Fact]
	public async Task Perfil_Inexistente_404()
	{
		var resposta = await CriarClienteAdmin(CenarioDePerfil()).GetAsync("/Acesso/Perfil?nome=nao-existe");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Perfil_SemSecureGateConfigurado_AbreEExplica()
	{
		var resposta = await CriarClienteAdmin().GetAsync("/Acesso/Perfil?nome=qualquer");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
	}

	[Fact]
	public async Task AdicionarMembro_AtribuiEVoltaParaOPerfil()
	{
		var gestao = CenarioDePerfil();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso/Perfil?nome=gerente-de-compras");

		var resposta = await client.PostAsync("/Acesso/AtribuirPerfil", Form(
			("usuarioId", Bruno.ToString()), ("perfil", "gerente-de-compras"), ("origem", "perfil"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/Acesso/Perfil");
		gestao.Chamadas.Should().Equal($"perfil-atribuir:{Bruno}:gerente-de-compras");
	}

	[Fact]
	public async Task RetirarMembro_Retira()
	{
		var gestao = CenarioDePerfil();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso/Perfil?nome=gerente-de-compras");

		await client.PostAsync("/Acesso/RetirarPerfil", Form(
			("usuarioId", Ana.ToString()), ("perfil", "gerente-de-compras"), ("origem", "perfil"), ("__RequestVerificationToken", token)));

		gestao.Chamadas.Should().Equal($"perfil-retirar:{Ana}:gerente-de-compras");
	}

	[Fact]
	public async Task RetirarUltimoIntranetAdmin_RecusadoComToastDeErro()
	{
		var gestao = CenarioDePerfil().ComUsuario(Guid.NewGuid(), "admin@x.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		var adminId = gestao.Usuarios[^1].Id;
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso/Perfil?nome=intranet-admin");

		var resposta = await client.PostAsync("/Acesso/RetirarPerfil", Form(
			("usuarioId", adminId.ToString()), ("perfil", "intranet-admin"), ("origem", "perfil"), ("__RequestVerificationToken", token)));

		var html = Decodificar(await resposta.Content.ReadAsStringAsync());
		html.Should().Contain("sc-toast--erro").And.Contain("último intranet-admin");
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task OrigemDesconhecida_NaoVirRedirecionamentoAberto()
	{
		var gestao = CenarioDePerfil();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso/Perfil?nome=gerente-de-compras");

		var resposta = await client.PostAsync("/Acesso/AtribuirPerfil", Form(
			("usuarioId", Bruno.ToString()), ("perfil", "gerente-de-compras"), ("origem", "https://mal.example/x"), ("__RequestVerificationToken", token)));

		resposta.RequestMessage!.RequestUri!.Host.Should().NotBe("mal.example");
	}

	[Fact]
	public async Task ExcluirPerfil_Comum_ExcluiEVoltaParaAsListagem()
	{
		var gestao = CenarioDePerfil();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, "/Acesso/Perfil?nome=gerente-de-compras");

		var resposta = await client.PostAsync("/Acesso/ExcluirPerfil", Form(("nome", "gerente-de-compras"), ("__RequestVerificationToken", token)));

		gestao.Chamadas.Should().Equal("perfil-excluir:gerente-de-compras");
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/Acesso");
	}

	[Fact]
	public async Task Perfil_DoProdutoOuDeSetor_NaoOfereceExcluir()
	{
		var html = await CriarClienteAdmin(CenarioDePerfil()).GetStringAsync("/Acesso/Perfil?nome=intranet-admin");

		html.Should().NotContain("Excluir perfil");
	}

	private static GestaoDeAcessoFalsa CenarioDeUsuario() => new GestaoDeAcessoFalsa()
		.ComPerfil("marketing-admin").ComPerfil("marketing-user").ComPerfil("rh-user").ComPerfil("ti-admin")
		.ComPerfil("gerente-de-compras").ComPerfil("all-users")
		.ComUsuario(Ana, "ana@x.com", SituacaoDoUsuario.Ativo, "marketing-admin", "rh-user", "gerente-de-compras")
		.ComUsuario(Bruno, "bruno@x.com", SituacaoDoUsuario.Desativado);

	[Fact]
	public async Task Usuario_AgrupaOsPerfisPorSetorEMostraOsAvulsos()
	{
		var html = Decodificar(await CriarClienteAdmin(CenarioDeUsuario()).GetStringAsync($"/Acesso/Usuario/{Ana}"));

		html.Should().Contain("marketing").And.Contain("rh").And.Contain("gerente-de-compras");
		html.Should().Contain("Administrador").And.Contain("Usuário");
	}

	[Fact]
	public async Task Usuario_Inexistente_404()
	{
		var resposta = await CriarClienteAdmin(CenarioDeUsuario()).GetAsync($"/Acesso/Usuario/{Guid.NewGuid()}");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Usuario_UsuarioDesativado_OfereceReativar_ENaoDesativar()
	{
		var html = await CriarClienteAdmin(CenarioDeUsuario()).GetStringAsync($"/Acesso/Usuario/{Bruno}");

		html.Should().Contain("Reativar").And.NotContain("Desativar conta");
	}

	[Fact]
	public async Task AtribuirPerfilDeSetor_ComponheSlugEPapel()
	{
		var gestao = CenarioDeUsuario();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, $"/Acesso/Usuario/{Ana}");

		var resposta = await client.PostAsync("/Acesso/AtribuirPerfilDeSetor", Form(
			("usuarioId", Ana.ToString()), ("setor", "ti"), ("papel", "admin"), ("__RequestVerificationToken", token)));

		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/Acesso/Usuario/{Ana}");
		gestao.Chamadas.Should().Equal($"perfil-atribuir:{Ana}:ti-admin");
	}

	[Theory]
	[InlineData("superadmin")]
	[InlineData("")]
	[InlineData("admin-x")]
	public async Task AtribuirPerfilDeSetor_PapelInvalido_NaoAtribuiNada(string papel)
	{
		var gestao = CenarioDeUsuario();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, $"/Acesso/Usuario/{Ana}");

		var resposta = await client.PostAsync("/Acesso/AtribuirPerfilDeSetor", Form(
			("usuarioId", Ana.ToString()), ("setor", "ti"), ("papel", papel), ("__RequestVerificationToken", token)));

		gestao.Chamadas.Should().BeEmpty();
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("sc-toast--erro");
	}

	[Fact]
	public async Task AtribuirPerfilAvulso_VoltaParaOUsuario()
	{
		var gestao = CenarioDeUsuario();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, $"/Acesso/Usuario/{Ana}");

		var resposta = await client.PostAsync("/Acesso/AtribuirPerfil", Form(
			("usuarioId", Ana.ToString()), ("perfil", "all-users"), ("origem", "usuario"), ("__RequestVerificationToken", token)));

		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/Acesso/Usuario/{Ana}");
		gestao.Chamadas.Should().Equal($"perfil-atribuir:{Ana}:all-users");
	}

	[Fact]
	public async Task DesativarReativarEEncerrarSessoes_ChamamAPlataforma()
	{
		var gestao = CenarioDeUsuario();
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, $"/Acesso/Usuario/{Ana}");

		await client.PostAsync($"/Acesso/DesativarUsuario/{Ana}", Form(("__RequestVerificationToken", token)));
		await client.PostAsync($"/Acesso/ReativarUsuario/{Ana}", Form(("__RequestVerificationToken", token)));
		await client.PostAsync($"/Acesso/EncerrarSessoes/{Ana}", Form(("__RequestVerificationToken", token)));

		gestao.Chamadas.Should().Equal($"usuario-desativar:{Ana}", $"usuario-reativar:{Ana}", $"sessoes-encerrar:{Ana}");
	}

	[Fact]
	public async Task DesativarUltimoIntranetAdmin_RecusadoComToastDeErro()
	{
		var gestao = CenarioDeUsuario().ComUsuario(Guid.NewGuid(), "admin@x.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		var adminId = gestao.Usuarios[^1].Id;
		var client = CriarClienteAdmin(gestao);
		var token = await TokenAsync(client, $"/Acesso/Usuario/{adminId}");

		var resposta = await client.PostAsync($"/Acesso/DesativarUsuario/{adminId}", Form(("__RequestVerificationToken", token)));

		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("sc-toast--erro");
		gestao.Chamadas.Should().BeEmpty();
	}
}
