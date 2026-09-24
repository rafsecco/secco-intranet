using System.Net;
using AwesomeAssertions;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Leitura do Diretório: quem entra (só os três perfis) e o que a tela mostra. O SecureGate real
/// não existe no ambiente Testing — a fonte de usuários é um dublê.
/// </summary>
public class DiretorioLeituraTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(UsuariosParaDiretorioFalso? usuarios, Guid? usuarioId, params string[] roles)
	{
		factory.UsuariosDoDiretorio = usuarios;
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		if (usuarioId is not null)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, usuarioId.ToString());
		}

		return client;
	}

	public static TheoryData<string[]> UsuariosSemAcesso => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin" },
		new[] { "financeiro-user" },
		new[] { "inventario-admin" },
	};

	public static TheoryData<string[]> UsuariosComAcesso => new()
	{
		new[] { "diretorio-user" },
		new[] { "diretorio-admin" },
		new[] { "intranet-admin" },
	};

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task SemNenhumDosTresPerfis_ToDaRotaDaLeitura_403(string[] roles)
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), Ana, roles);

		foreach (var rota in new[] { "/diretorio", $"/diretorio/{Ana}", "/diretorio/perfil" })
		{
			var resposta = await client.GetAsync(rota);

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota} exige um dos três perfis");
		}
	}

	[Theory]
	[MemberData(nameof(UsuariosComAcesso))]
	public async Task ComAlgumDosTresPerfis_Abre(string[] roles)
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com");
		var client = CriarCliente(usuarios, Ana, roles);

		(await client.GetAsync("/diretorio")).StatusCode.Should().Be(HttpStatusCode.OK);
		(await client.GetAsync($"/diretorio/{Ana}")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Index_ListaAsPessoasComBuscaEBadgeDeEmail()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@y.com");
		var client = CriarCliente(usuarios, Ana, "diretorio-user");

		var html = Decodificar(await client.GetStringAsync("/diretorio?busca=bruno"));

		html.Should().Contain("bruno@y.com").And.NotContain("ana@x.com");
	}

	[Fact]
	public async Task Index_PaginaAlemDoFim_NaoQuebra()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), Ana, "diretorio-user");

		var resposta = await client.GetAsync("/diretorio?page=99");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task PessoaInexistente_404()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), Ana, "diretorio-user");

		var resposta = await client.GetAsync($"/diretorio/{Guid.NewGuid()}");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task SemSecureGateConfigurado_AbreEExplica_SemQuebrar()
	{
		var client = CriarCliente(usuarios: null, Ana, "diretorio-user");

		foreach (var rota in new[] { "/diretorio", $"/diretorio/{Ana}" })
		{
			var resposta = await client.GetAsync(rota);

			resposta.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {rota} explica em vez de dar 500");
			Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
		}
	}

	[Fact]
	public async Task SecureGateForaDoAr_ExplicaSemDizerQueEstaVazio()
	{
		var usuarios = new UsuariosParaDiretorioFalso { FalharCom = Secco.Intranet.Application.IntranetErrors.Acesso.Indisponivel };
		var client = CriarCliente(usuarios, Ana, "diretorio-user");

		var html = Decodificar(await client.GetStringAsync("/diretorio"));

		html.Should().Contain("Não foi possível falar com o SecureGate");
	}

	[Fact]
	public async Task MeuPerfil_AbreOFormularioDeContatoDaPropriaPessoa()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), Ana, "diretorio-user");

		var resposta = await client.GetAsync("/diretorio/perfil");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/diretorio/perfil");
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("ana@x.com");
	}

	[Fact]
	public async Task MeuPerfil_SemUsuarioIdentificado_ExplicaSemQuebrar()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), usuarioId: null, "diretorio-user");

		var resposta = await client.GetAsync("/diretorio/perfil");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Não há usuário identificado");
	}

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Menu_SemAcesso_NaoVeODiretorioNemOMeuPerfil(string[] roles)
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), Ana, roles);

		var html = await client.GetStringAsync("/");

		html.Should().NotContain("href=\"/diretorio\"");
		html.Should().NotContain("href=\"/diretorio/perfil\"");
	}

	[Theory]
	[MemberData(nameof(UsuariosComAcesso))]
	public async Task Menu_ComAcesso_VeODiretorio(string[] roles)
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), Ana, roles);

		var html = await client.GetStringAsync("/");

		html.Should().Contain("href=\"/diretorio\"");
	}
}
