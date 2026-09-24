using System.Net;
using AwesomeAssertions;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O cadastro de setores cria Roles no SecureGate; só o <c>intranet-admin</c> pode (ADR-0008).
/// Antes desta regra o controller não tinha checagem nenhuma.
/// </summary>
public class SetoresAutorizacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

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

	public static TheoryData<string[]> UsuariosSemAcesso => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin" },
		new[] { "inventario-admin" },
		new[] { "financeiro-user" },
	};

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Get_SemIntranetAdmin_Bloqueado(string[] roles)
	{
		var client = CriarCliente(roles);
		var id = Guid.NewGuid();

		foreach (var url in new[] { "/Setores", "/Setores/Create", $"/Setores/Edit/{id}", $"/Setores/Details/{id}" })
		{
			var resposta = await client.GetAsync(url);

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {url} exige intranet-admin");
		}
	}

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Post_SemIntranetAdmin_BloqueadoMesmoSemToken(string[] roles)
	{
		var client = CriarCliente(roles);
		var corpo = new FormUrlEncodedContent([new KeyValuePair<string, string>("Nome", "Setor Pirata")]);

		var criar = await client.PostAsync("/Setores/Create", corpo);
		var editar = await client.PostAsync($"/Setores/Edit/{Guid.NewGuid()}", corpo);

		criar.StatusCode.Should().Be(HttpStatusCode.Forbidden,
			"403 e não 400: o filtro de autorização roda antes do antifalsificação");
		editar.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task ComIntranetAdmin_Liberado()
	{
		var resposta = await CriarCliente("intranet-admin").GetAsync("/Setores");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task PostComIntranetAdminSemToken_PassaDoGateEBateNoAntifalsificacao()
	{
		var resposta = await CriarCliente("intranet-admin").PostAsync(
			"/Setores/Create", new FormUrlEncodedContent([new KeyValuePair<string, string>("Nome", "X")]));

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest,
			"o gate liberou o admin, e quem barrou foi o token antifalsificação — a ordem dos filtros está certa");
	}

	[Fact]
	public async Task Menu_AdminDeSetor_NaoVeOGrupoDeAdministracao()
	{
		var html = await CriarCliente("financeiro-admin").GetStringAsync("/");

		html.Should().NotContain("href=\"/setores\"");
	}

	[Fact]
	public async Task Menu_IntranetAdmin_VeOGrupoDeAdministracao()
	{
		var html = await CriarCliente("intranet-admin").GetStringAsync("/");

		html.Should().Contain("href=\"/setores\"");
	}
}
