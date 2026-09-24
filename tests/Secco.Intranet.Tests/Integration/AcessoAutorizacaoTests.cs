using System.Net;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Controllers;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Critério de aceite da ADR-0008: só o <c>intranet-admin</c> vê e acessa a Área de acesso —
/// usuário comum, <c>{slug}-admin</c> (de um ou de todos os setores) e <c>inventario-admin</c>
/// são bloqueados, em toda rota, inclusive nos <c>POST</c> e sem token antifalsificação.
/// </summary>
public class AcessoAutorizacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Id = Guid.NewGuid();

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
		new[] { "financeiro-user" },
		new[] { "financeiro-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin", "marketing-admin" },
		new[] { "inventario-admin" },
		new[] { "gerente-de-compras" },
	};

	public static IEnumerable<string> RotasGet =>
	[
		"/Acesso",
		"/Acesso?aba=usuarios",
		"/Acesso/Perfil?nome=qualquer",
		$"/Acesso/Usuario/{Id}",
	];

	public static IEnumerable<string> RotasPost =>
	[
		"/Acesso/CriarPerfil",
		"/Acesso/ExcluirPerfil",
		"/Acesso/AtribuirPerfil",
		"/Acesso/AtribuirPerfilDeSetor",
		"/Acesso/RetirarPerfil",
		$"/Acesso/DesativarUsuario/{Id}",
		$"/Acesso/ReativarUsuario/{Id}",
		$"/Acesso/EncerrarSessoes/{Id}",
	];

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Get_SemIntranetAdmin_Bloqueado(string[] roles)
	{
		var client = CriarCliente(roles);

		foreach (var rota in RotasGet)
		{
			var resposta = await client.GetAsync(rota);

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota} é exclusivo do intranet-admin");
		}
	}

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Post_SemIntranetAdmin_Bloqueado403_NaoBarradoPeloAntifalsificacao(string[] roles)
	{
		var client = CriarCliente(roles);

		foreach (var rota in RotasPost)
		{
			var resposta = await client.PostAsync(rota, new FormUrlEncodedContent([]));

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden,
				$"POST {rota}: 403 do gate, e não 400 do filtro antifalsificação — o gate roda primeiro");
		}
	}

	[Fact]
	public async Task Get_ComIntranetAdmin_Liberado()
	{
		var client = CriarCliente("intranet-admin");

		foreach (var rota in RotasGet)
		{
			var resposta = await client.GetAsync(rota);

			resposta.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {rota} abre para o intranet-admin (com SecureGate ausente, explica)");
		}
	}

	[Fact]
	public async Task Post_ComIntranetAdminSemToken_PassaDoGateEBateNoAntifalsificacao()
	{
		var client = CriarCliente("intranet-admin");

		foreach (var rota in RotasPost)
		{
			var resposta = await client.PostAsync(rota, new FormUrlEncodedContent([]));

			resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"POST {rota}: o gate liberou, o token barrou");
		}
	}

	[Fact]
	public async Task Menu_QuemNaoEIntranetAdmin_NaoVeOItemAcesso()
	{
		foreach (var roles in new string[][] { [], ["financeiro-admin"], ["inventario-admin"] })
		{
			var html = await CriarCliente(roles).GetStringAsync("/");

			html.Should().NotContain("href=\"/acesso\"", $"roles: {string.Join(",", roles)}");
		}
	}

	[Fact]
	public async Task Menu_IntranetAdmin_VeOItemAcesso()
	{
		var html = await CriarCliente("intranet-admin").GetStringAsync("/");

		html.Should().Contain("href=\"/acesso\"");
	}

	[Fact]
	public void TodaActionDoControllerEstaSobOGate_EACobertaPelasListasDeRotas()
	{
		typeof(AcessoController).GetCustomAttribute<SomenteIntranetAdminAttribute>().Should().NotBeNull(
			"o atributo na classe é o que impede uma action nova de nascer desprotegida");

		var actions = typeof(AcessoController)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.Where(metodo => metodo.GetCustomAttributes().Any(a => a is HttpGetAttribute or HttpPostAttribute))
			.Select(metodo => metodo.Name)
			.ToList();

		var cobertas = RotasGet.Concat(RotasPost)
			.Select(rota => rota.Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries))
			.Select(partes => partes.Length > 1 ? partes[1] : "Index")
			.Distinct()
			.ToList();

		actions.Should().BeSubsetOf(cobertas,
			"toda action do AcessoController precisa aparecer nas listas de rotas deste teste — uma action nova sem teste de bloqueio é o que este teste existe para impedir");
	}
}
