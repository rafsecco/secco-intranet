using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Application.Tenants.Credenciais;
using Secco.Intranet.Domain.Tenants;
using Secco.Intranet.Infrastructure.Access;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Credenciais OAuth de um tenant administrado pelo host real: o trio da ADR-0008 em toda rota, o
/// tenant e o client id só da rota, e o secret só na resposta do próprio POST — sem cookie, sem
/// log, sem trilha e com <c>no-store</c>.
/// </summary>
public class TenantsCredenciaisTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private const string ClientIdDeMaquina = "cli_aaaaaaaaaaaaaaaa";
	private const string ClientIdDeLogin = "cli_bbbbbbbbbbbbbbbb";
	private const string ClientIdComRecursoDesligado = "cli_cccccccccccccccc";
	private const string SecretDaEmissao = "secret-de-teste-1";
	private const string SecretDaRotacao = "secret-de-teste-rotacionado";

	private static readonly Guid Admin = Guid.NewGuid();
	private static readonly Guid SemFator = Guid.NewGuid();
	private readonly Guid _ligado = Guid.NewGuid();
	private readonly Guid _desligado = Guid.NewGuid();
	private readonly Guid _foraDoCadastro = Guid.NewGuid();
	private GestaoDeCredenciaisDoTenantFalsa _credenciais = null!;
	private GestaoDeAcessoDoTenantFalsa _acesso = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa()
			.ComUsuario(Admin, "admin@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin")
			.ComDoisFatores(Admin)
			.ComUsuario(SemFator, "sem@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		factory.GestaoDeTenants = new GestaoDeTenantsFalsa()
			.ComTenant(_ligado, $"lig-{_ligado:N}"[..20], true, "logstream")
			.ComTenant(_desligado, $"des-{_desligado:N}"[..20], true, "logstream")
			.ComTenant(_foraDoCadastro, $"for-{_foraDoCadastro:N}"[..20], true, "logstream");
		_acesso = new GestaoDeAcessoDoTenantFalsa()
			.ComPerfil("compras.aprovador", false, "pedido:aprovar")
			.ComPerfil("compras.leitor", false, "pedido:ler");
		factory.GestaoDeAcessoDoTenant = _acesso;
		_credenciais = new GestaoDeCredenciaisDoTenantFalsa()
			.ComCredencial(new CredencialDto(
				ClientIdDeMaquina, "Robô de compras", TipoDeCredencial.Maquina, ["logstream"], ["compras.aprovador"], [], [], DateTimeOffset.UnixEpoch))
			.ComCredencial(new CredencialDto(
				ClientIdDeLogin, "Portal de compras", TipoDeCredencial.Login, ["logstream"], [], ["https://portal.exemplo.com/signin"],
				["https://portal.exemplo.com/signout"], DateTimeOffset.UnixEpoch))
			.ComCredencial(new CredencialDto(
				ClientIdComRecursoDesligado, "Robô de avisos", TipoDeCredencial.Maquina, ["logstream", "notificationhub"], [], [], [], DateTimeOffset.UnixEpoch));
		factory.GestaoDeCredenciaisDoTenant = _credenciais;

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var cadastro = escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>();
		var comSecureGate = new TenantAdministrado(_ligado, "Compras", "Ana", OrigemDoTenant.Criado, "admin");
		comSecureGate.HabilitarSecureGate();
		await cadastro.TentarAdicionarAsync(comSecureGate);
		await cadastro.TentarAdicionarAsync(new TenantAdministrado(_desligado, "RH", "Bia", OrigemDoTenant.Criado, "admin"));
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Cliente(Guid? usuario, params string[] roles) => Cliente(factory, usuario, roles);

	private HttpClient Cliente(WebApplicationFactory<Program> fabrica, Guid? usuario, params string[] roles)
	{
		var client = fabrica.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		if (usuario is not null)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, usuario.Value.ToString());
		}

		return client;
	}

	private static string Texto(string html) => AuxiliaresDeHttp.Decodificar(html);

	private static IEnumerable<string> RotasGet(Guid tenant) =>
	[
		$"/tenants/{tenant}/credenciais",
		$"/tenants/{tenant}/credenciais/nova",
		$"/tenants/{tenant}/credenciais/nova?tipo=login",
		$"/tenants/{tenant}/credenciais/{ClientIdDeMaquina}",
	];

	private static IEnumerable<string> RotasPost(Guid tenant) =>
	[
		$"/tenants/{tenant}/credenciais/nova",
		$"/tenants/{tenant}/credenciais/{ClientIdDeMaquina}",
		$"/tenants/{tenant}/credenciais/{ClientIdDeMaquina}/rotacionar",
		$"/tenants/{tenant}/credenciais/{ClientIdDeMaquina}/revogar",
	];

	public static TheoryData<string[]> UsuariosSemAcesso => new()
	{
		Array.Empty<string>(),
		new[] { "compras-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin" },
		new[] { "inventario-admin" },
	};

	// ---- Autorização ----

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task SemIntranetAdmin_403_EmTodaRota_SemTocarAPorta(string[] roles)
	{
		var client = Cliente(Admin, roles);

		foreach (var rota in RotasGet(_ligado))
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota}");
		}

		foreach (var rota in RotasPost(_ligado))
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"POST {rota}");
		}

		_credenciais.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task IntranetAdminSemSegundoFator_403_EmTodaRota_SemTocarAPorta()
	{
		var client = Cliente(SemFator, "intranet-admin");

		foreach (var rota in RotasGet(_ligado))
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota}");
		}

		foreach (var rota in RotasPost(_ligado))
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"POST {rota}");
		}

		_credenciais.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task PostSemTokenAntifalsificacao_400_EmTodaRota_SemTocarAPorta()
	{
		var client = Cliente(Admin, "intranet-admin");

		foreach (var rota in RotasPost(_ligado))
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode.Should().Be(HttpStatusCode.BadRequest, $"POST {rota}");
		}

		_credenciais.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task SecureGateIndisponivel_MostraAPaginaQueExplica()
	{
		factory.GestaoDeCredenciaisDoTenant = new GestaoDeCredenciaisDoTenantIndisponivel();

		var resposta = await Cliente(Admin, "intranet-admin").GetAsync($"/tenants/{_ligado}/credenciais");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Texto(await resposta.Content.ReadAsStringAsync()).Should().Contain("não está configurado");
	}

	// ---- IDOR ----

	[Fact]
	public async Task TenantForaDoCadastro_404_EmTodaRota_SemTocarAPorta()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/nova");

		foreach (var rota in RotasGet(_foraDoCadastro))
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.NotFound, $"GET {rota}");
		}

		foreach (var rota in RotasPost(_foraDoCadastro))
		{
			(await client.PostAsync(rota, AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("Nome", "x"), ("confirmacao", "x"))))
				.StatusCode.Should().Be(HttpStatusCode.NotFound, $"POST {rota}");
		}

		_credenciais.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task ClientIdForaDoFormato_404_SemChamarAPorta()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/nova");
		var raiz = $"/tenants/{_ligado}/credenciais/nao-e-client";

		(await client.GetAsync(raiz)).StatusCode.Should().Be(HttpStatusCode.NotFound);

		foreach (var rota in new[] { raiz, $"{raiz}/rotacionar", $"{raiz}/revogar" })
		{
			(await client.PostAsync(rota, AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("Nome", "x"), ("confirmacao", "x"))))
				.StatusCode.Should().Be(HttpStatusCode.NotFound, $"POST {rota}");
		}

		_credenciais.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task TenantIdNoCorpo_Ignorado_ValeODaRota()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/nova");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/credenciais/nova",
			AuxiliaresDeHttp.Form(
				("__RequestVerificationToken", token), ("tipo", "maquina"), ("Nome", "Novo robô"), ("Escopos", "logstream"),
				("tenantId", _foraDoCadastro.ToString()), ("Tenant.TenantId", _foraDoCadastro.ToString())));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		_credenciais.Chamadas.Should().Contain($"Emitir:{_ligado}")
			.And.NotContain(c => c.Contains(_foraDoCadastro.ToString(), StringComparison.Ordinal));
	}

	// ---- Segredo ----

	[Fact]
	public async Task Emitir_SecretSoNaRespostaComNoStore_SemCookieLogOuTrilha()
	{
		var trilha = new TrilhaDeAcessoFalsa();
		var logs = new CaptorDeLog();
		var app = ComCaptores(trilha, logs);
		var client = Cliente(app, Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/nova");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/credenciais/nova",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("tipo", "maquina"), ("Nome", "Novo robô"), ("Escopos", "logstream")));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "o secret é a própria resposta, não um redirect");
		resposta.Headers.CacheControl!.NoStore.Should().BeTrue();
		var html = await resposta.Content.ReadAsStringAsync();
		html.Should().Contain(SecretDaEmissao);
		Texto(html).Should().Contain("Secco__SecureGate__ClientSecret=" + SecretDaEmissao).And.Contain("Secco__SecureGate__ClientId=cli_");
		SemSecretNosCookies(resposta);
		trilha.Registros.Should().NotBeEmpty("a emissão é registrada");
		trilha.Registros.Should().NotContain(r => (r.Metadata ?? string.Empty).Contains(SecretDaEmissao, StringComparison.Ordinal));
		logs.Tudo.Should().NotBeEmpty("sem isso a ausência do secret no log não prova nada").And.NotContain(SecretDaEmissao);

		(await client.GetStringAsync($"/tenants/{_ligado}/credenciais")).Should().NotContain(SecretDaEmissao);
	}

	[Fact]
	public async Task Rotacionar_SecretSoNaResposta_SemCookieLogOuTrilha()
	{
		var trilha = new TrilhaDeAcessoFalsa();
		var logs = new CaptorDeLog();
		var app = ComCaptores(trilha, logs);
		var client = Cliente(app, Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}/rotacionar",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.Headers.CacheControl!.NoStore.Should().BeTrue();
		(await resposta.Content.ReadAsStringAsync()).Should().Contain(SecretDaRotacao);
		SemSecretNosCookies(resposta);
		trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantCredencialRotacionar);
		trilha.Registros.Should().NotContain(r => (r.Metadata ?? string.Empty).Contains(SecretDaRotacao, StringComparison.Ordinal));
		logs.Tudo.Should().NotContain(SecretDaRotacao);

		(await client.GetStringAsync($"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}")).Should().NotContain(SecretDaRotacao);
	}

	[Fact]
	public async Task Emitir_ComErro_NaoChegaAoSecret_EVoltaAoFormulario()
	{
		_credenciais.FalharCom = IntranetErrors.Credenciais.Recusada("A plataforma disse não.");
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/nova");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/credenciais/nova",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("tipo", "maquina"), ("Nome", "Novo robô"), ("Escopos", "logstream")));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var html = Texto(await resposta.Content.ReadAsStringAsync());
		html.Should().Contain("A plataforma disse não.").And.Contain("Novo robô").And.NotContain("ClientSecret");
	}

	// ---- Fluxo ----

	[Fact]
	public async Task Nova_Login_SecureGateDesligado_MostraAvisoSemFormulario()
	{
		var html = Texto(await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_desligado}/credenciais/nova?tipo=login"));

		html.Should().Contain("Ligue o SecureGate do tenant").And.NotContain("<form method=\"post\" action=\"/tenants/");
	}

	[Fact]
	public async Task Nova_Maquina_SecureGateDesligado_AvisaDosPerfisEOfereceOFormulario()
	{
		var html = Texto(await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_desligado}/credenciais/nova"));

		html.Should().Contain("Ligue o SecureGate do tenant para dar perfis").And.Contain("name=\"Nome\"")
			.And.Contain("name=\"Escopos\" value=\"logstream\"").And.Contain("LogStream")
			.And.NotContain("name=\"Escopos\" value=\"notificationhub\"");
	}

	[Fact]
	public async Task Nova_Maquina_OferecePerfisComAQuantidadeDePermissoes()
	{
		var html = Texto(await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/credenciais/nova"));

		html.Should().Contain("name=\"Perfis\" value=\"compras.aprovador\"").And.Contain("1 permissão").And.NotContain("1 permissões")
			.And.NotContain("name=\"RedirectUris\"");
	}

	[Fact]
	public async Task Nova_Login_OfereceCaixasDeEnderecoESemPerfis()
	{
		var html = Texto(await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/credenciais/nova?tipo=login"));

		html.Should().Contain("<textarea").And.Contain("name=\"RedirectUris\"").And.Contain("name=\"RedirectUrisDeLogout\"")
			.And.NotContain("name=\"Perfis\"");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("qualquer")]
	public async Task Nova_TipoInvalidoOuAusente_ValeMaquina(string? tipo)
	{
		var url = $"/tenants/{_ligado}/credenciais/nova" + (tipo is null ? string.Empty : $"?tipo={tipo}");

		var html = Texto(await Cliente(Admin, "intranet-admin").GetStringAsync(url));

		html.Should().Contain("name=\"Perfis\"").And.NotContain("name=\"RedirectUris\"");
	}

	[Fact]
	public async Task Nova_ErroDePerfil_PreservaCampos()
	{
		_credenciais.FalharCom = IntranetErrors.Credenciais.PerfilRecusado;
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/nova");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/credenciais/nova",
			AuxiliaresDeHttp.Form(
				("__RequestVerificationToken", token), ("tipo", "maquina"), ("Nome", "Robô do financeiro"),
				("Escopos", "logstream"), ("Perfis", "compras.leitor")));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var html = Texto(await resposta.Content.ReadAsStringAsync());
		html.Should().Contain(IntranetErrors.Credenciais.PerfilRecusado.Description).And.Contain("value=\"Robô do financeiro\"")
			.And.Contain("name=\"Escopos\" value=\"logstream\" id=\"escopo-logstream\" checked")
			.And.Contain("name=\"Perfis\" value=\"compras.leitor\" id=\"perfil-compras.leitor\" checked");
	}

	[Fact]
	public async Task Nova_ErroDeLogin_PreservaOsEnderecos()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/nova?tipo=login");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/credenciais/nova",
			AuxiliaresDeHttp.Form(
				("__RequestVerificationToken", token), ("tipo", "login"), ("Nome", "Portal"),
				("RedirectUris", "http://inseguro.exemplo.com/signin")));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var html = Texto(await resposta.Content.ReadAsStringAsync());
		html.Should().Contain(IntranetErrors.Credenciais.EnderecoRecusado.Description).And.Contain("http://inseguro.exemplo.com/signin");
		_credenciais.Chamadas.Should().NotContain(c => c.StartsWith("Emitir", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Detalhe_MostraDadosAtuaisERedirectsEmCode()
	{
		var html = Texto(await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/credenciais/{ClientIdDeLogin}"));

		html.Should().Contain("Portal de compras").And.Contain($"<code>{ClientIdDeLogin}</code>")
			.And.Contain("<code>https://portal.exemplo.com/signin</code>").And.Contain("<code>https://portal.exemplo.com/signout</code>")
			.And.Contain("o sistema para de obter token até receber o secret novo")
			.And.Contain("tokens já emitidos valem até 5 minutos").And.Contain("confirm(");
		html.Should().NotContain("ClientSecret");
	}

	[Fact]
	public async Task Alterar_RedirecionaParaODetalhe_ComOsDadosDoFormulario()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}",
			AuxiliaresDeHttp.Form(
				("__RequestVerificationToken", token), ("Nome", "Robô renomeado"), ("Escopos", "logstream"), ("Perfis", "compras.leitor")));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.ToString().Should().Be($"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}");
		_credenciais.DadosAlterados.Should().NotBeNull();
		_credenciais.DadosAlterados!.Nome.Should().Be("Robô renomeado");
		_credenciais.DadosAlterados.Perfis.Should().Equal("compras.leitor");
	}

	[Fact]
	public async Task Detalhe_EscopoDeRecursoDesligado_AparecePreMarcadoComOAviso()
	{
		var html = Texto(await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/credenciais/{ClientIdComRecursoDesligado}"));

		html.Should().Contain("name=\"Escopos\" value=\"logstream\" id=\"escopo-logstream\" checked")
			.And.Contain("name=\"Escopos\" value=\"notificationhub\" id=\"escopo-notificationhub\" checked")
			.And.Contain("recurso desligado: ligue-o na página do tenant ou desmarque para remover");
	}

	[Fact]
	public async Task Alterar_ComEscopoDeRecursoDesligadoMarcado_RecusaCitandoORecurso_SemChamarAPorta()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/{ClientIdComRecursoDesligado}");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/credenciais/{ClientIdComRecursoDesligado}",
			AuxiliaresDeHttp.Form(
				("__RequestVerificationToken", token), ("Nome", "Robô de avisos"), ("Escopos", "logstream"), ("Escopos", "notificationhub")));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		_credenciais.Chamadas.Should().NotContain(c => c.StartsWith("Alterar", StringComparison.Ordinal));
		Texto(await client.GetStringAsync(resposta.Headers.Location!.ToString())).Should().Contain("O recurso NotificationHub não está ligado neste tenant");
	}

	[Fact]
	public async Task Revogar_NomeErrado_Recusa()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}/revogar",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("confirmacao", "outro nome")));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.ToString().Should().Be($"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}");
		_credenciais.Chamadas.Should().NotContain(c => c.StartsWith("Revogar", StringComparison.Ordinal));
		Texto(await client.GetStringAsync(resposta.Headers.Location.ToString())).Should().Contain("digite exatamente o nome da credencial");
	}

	[Fact]
	public async Task Revogar_NomeCerto_VoltaParaALista()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}/revogar",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("confirmacao", "Robô de compras")));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.ToString().Should().Be($"/tenants/{_ligado}/credenciais");
		_credenciais.Chamadas.Should().Contain($"Revogar:{_ligado}:{ClientIdDeMaquina}");
	}

	// ---- Telas ----

	[Theory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Telas_RenderizamNosDoisTemas(string tema)
	{
		var app = factory.WithWebHostBuilder(builder => builder.UseSetting("Intranet:Theme:Nome", tema));
		var client = Cliente(app, Admin, "intranet-admin");

		var lista = Texto(await client.GetStringAsync($"/tenants/{_ligado}/credenciais"));
		lista.Should().Contain(tema == "Horizontal" ? "sc-topnav" : "sc-sidebar", "garante que o tema trocou de fato")
			.And.Contain("Robô de compras").And.Contain("Máquina").And.Contain("Login de pessoas").And.Contain(ClientIdDeMaquina)
			.And.Contain("Emitir credencial de máquina").And.Contain("Emitir credencial de login");

		foreach (var rota in new[]
		{
			$"/tenants/{_ligado}/credenciais/nova",
			$"/tenants/{_ligado}/credenciais/nova?tipo=login",
			$"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}",
			$"/tenants/{_ligado}/credenciais/{ClientIdDeLogin}",
		})
		{
			var resposta = await client.GetAsync(rota);
			resposta.StatusCode.Should().Be(HttpStatusCode.OK, rota);
		}
	}

	[Fact]
	public async Task Secret_MostraOBlocoDeVariaveisEOAviso()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}");

		var html = Texto(await (await client.PostAsync($"/tenants/{_ligado}/credenciais/{ClientIdDeMaquina}/rotacionar",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token)))).Content.ReadAsStringAsync());

		html.Should().Contain("Secco__SecureGate__Authority=").And.Contain($"Secco__SecureGate__ClientId={ClientIdDeMaquina}")
			.And.Contain("Copie agora: o secret não aparece de novo.").And.Contain($"href=\"/tenants/{_ligado}/credenciais\"");
	}

	// ---- Página do tenant ----

	[Fact]
	public async Task DetalheDoTenant_TemOLinkParaAsCredenciais()
	{
		var html = await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}");

		html.Should().Contain($"href=\"/tenants/{_ligado}/credenciais\"");
	}

	private WebApplicationFactory<Program> ComCaptores(ITrilhaDeAuditoria trilha, CaptorDeLog logs) =>
		factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
		{
			services.AddSingleton(trilha);
			services.AddSingleton<ILoggerProvider>(logs);
		}));

	private static void SemSecretNosCookies(HttpResponseMessage resposta)
	{
		resposta.Headers.TryGetValues("Set-Cookie", out var cookies);
		// O framework pode devolver o cookie de TempData só para limpá-lo (valor vazio, já expirado);
		// o que não pode existir é um cookie de TempData com conteúdo, nem o secret em qualquer cookie.
		(cookies ?? []).Should().NotContain(c => c.Contains("TempData", StringComparison.OrdinalIgnoreCase)
			&& !c.Contains("TempDataProvider=;", StringComparison.OrdinalIgnoreCase));
		(cookies ?? []).Should().NotContain(c => c.Contains("secret-de-teste", StringComparison.Ordinal));
	}

	/// <summary>Guarda tudo que o host loga (mensagem, estado e exceção) para provar que o secret não passou por lá.</summary>
	private sealed class CaptorDeLog : ILoggerProvider
	{
		private readonly System.Collections.Concurrent.ConcurrentQueue<string> _linhas = new();

		public string Tudo => string.Join('\n', _linhas);

		public ILogger CreateLogger(string categoryName) => new Registro(_linhas);

		public void Dispose()
		{
		}

		private sealed class Registro(System.Collections.Concurrent.ConcurrentQueue<string> linhas) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state)
				where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => true;

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
			{
				var estado = state is IEnumerable<KeyValuePair<string, object?>> pares
					? string.Join(';', pares.Select(p => $"{p.Key}={p.Value}"))
					: string.Empty;
				linhas.Enqueue($"{formatter(state, exception)}|{estado}|{exception}");
			}
		}
	}
}
