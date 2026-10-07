using System.Net;
using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Critério de aceite da ADR-0008, repetido para a área de tenants, mais o segundo fator: só o
/// <c>intranet-admin</c> com 2FA entra — em toda rota, <c>GET</c> e <c>POST</c>, e o 403 vem antes
/// do 400 do antifalsificação.
/// </summary>
public class TenantsAutorizacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid ComFator = Guid.NewGuid();
	private static readonly Guid SemFator = Guid.NewGuid();
	private static readonly Guid Qualquer = Guid.NewGuid();

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa()
			.ComUsuario(ComFator, "com@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin")
			.ComDoisFatores(ComFator)
			.ComUsuario(SemFator, "sem@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		factory.GestaoDeTenants = new GestaoDeTenantsFalsa();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Cliente(Guid? usuario, params string[] roles)
	{
		var client = factory.CreateClient(new() { AllowAutoRedirect = false });
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

	public static TheoryData<string[]> UsuariosSemAcesso => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-user" },
		new[] { "financeiro-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin", "marketing-admin" },
		new[] { "inventario-admin" },
		new[] { "diretorio-admin" },
	};

	public static IEnumerable<string> RotasGet => ["/tenants", "/tenants/novo", "/tenants/adotar", $"/tenants/{Qualquer}"];

	public static IEnumerable<string> RotasPost =>
	[
		"/tenants/novo",
		"/tenants/adotar",
		$"/tenants/{Qualquer}/recursos/logstream",
		$"/tenants/{Qualquer}/ativar",
		$"/tenants/{Qualquer}/desativar",
	];

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task SemIntranetAdmin_Bloqueado403_EmTodaRota(string[] roles)
	{
		var client = Cliente(ComFator, roles);

		foreach (var rota in RotasGet)
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota}");
		}

		foreach (var rota in RotasPost)
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode
				.Should().Be(HttpStatusCode.Forbidden, $"POST {rota}: 403 do gate, não 400 do antifalsificação");
		}
	}

	[Fact]
	public async Task IntranetAdminSemSegundoFator_Bloqueado_ComATelaQueExplica()
	{
		var client = Cliente(SemFator, "intranet-admin");

		foreach (var rota in RotasGet)
		{
			var resposta = await client.GetAsync(rota);

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota}");
			AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync())
				.Should().Contain("Ative o segundo fator");
		}

		foreach (var rota in RotasPost)
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode
				.Should().Be(HttpStatusCode.Forbidden, $"POST {rota}");
		}
	}

	[Fact]
	public async Task IntranetAdminSemSub_Bloqueado()
	{
		var resposta = await Cliente(null, "intranet-admin").GetAsync("/tenants");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task IntranetAdminComSegundoFator_Liberado()
	{
		var client = Cliente(ComFator, "intranet-admin");

		foreach (var rota in new[] { "/tenants", "/tenants/novo", "/tenants/adotar" })
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.OK, $"GET {rota}");
		}
	}

	[Fact]
	public async Task IntranetAdminComSegundoFator_PostSemToken_PassaDosFiltrosEBateNoAntifalsificacao()
	{
		var client = Cliente(ComFator, "intranet-admin");

		foreach (var rota in new[] { "/tenants/novo", "/tenants/adotar" })
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode
				.Should().Be(HttpStatusCode.BadRequest, $"POST {rota}: os gates liberaram, o token barrou");
		}
	}

	[Fact]
	public async Task Menu_QuemNaoEIntranetAdmin_NaoVeTenants()
	{
		foreach (var roles in new string[][] { [], ["financeiro-admin"], ["inventario-admin"] })
		{
			var html = await Cliente(ComFator, roles).GetStringAsync("/mural");

			html.Should().NotContain("href=\"/tenants\"", $"roles: {string.Join(",", roles)}");
		}
	}
}
