using System.Net;
using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>Telas da área de tenants pelo host HTTP real, com dublês da plataforma.</summary>
public class TenantsTelasTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Admin = Guid.NewGuid();

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa()
			.ComUsuario(Admin, "admin@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin")
			.ComDoisFatores(Admin);
		factory.GestaoDeTenants = new GestaoDeTenantsFalsa();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Cliente()
	{
		var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, Admin.ToString());

		return client;
	}

	[Fact]
	public async Task Index_SemTenants_ExplicaECriaOuAdota()
	{
		var html = await Cliente().GetStringAsync("/tenants");

		html.Should().Contain("href=\"/tenants/novo\"").And.Contain("href=\"/tenants/adotar\"");
	}

	[Fact]
	public async Task Criar_PeloFormulario_ApareceNaLista()
	{
		var client = Cliente();
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/tenants/novo");

		var resposta = await client.PostAsync("/tenants/novo", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Sistema"] = "Sistema de compras",
			["Responsavel"] = "Ana",
			["Nome"] = "Compras",
			["Slug"] = "compras-tela",
		}));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		var lista = await client.GetStringAsync("/tenants");
		lista.Should().Contain("Sistema de compras").And.Contain("compras-tela");
	}

	[Fact]
	public async Task Criar_SlugInvalido_VoltaAoFormularioComOErro()
	{
		var client = Cliente();
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/tenants/novo");

		var resposta = await client.PostAsync("/tenants/novo", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Sistema"] = "S",
			["Responsavel"] = "Ana",
			["Nome"] = "Compras",
			["Slug"] = "Compras X",
		}));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("letras minúsculas sem acento");
	}

	[Fact]
	public async Task SemSecureGate_AreaExplica_NaoQuebra()
	{
		factory.GestaoDeTenants = null;

		try
		{
			var resposta = await Cliente().GetAsync("/tenants/adotar");

			resposta.StatusCode.Should().Be(HttpStatusCode.OK);
			AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("não está configurado");
		}
		finally
		{
			factory.GestaoDeTenants = new GestaoDeTenantsFalsa();
		}
	}

	[Fact]
	public async Task Menu_IntranetAdmin_VeTenants()
	{
		var html = await Cliente().GetStringAsync("/mural");

		html.Should().Contain("href=\"/tenants\"");
	}
}
