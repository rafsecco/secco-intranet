using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Edição do Diretório contra o banco real: o colaborador edita só o próprio contato, e o que ele
/// não pode editar — cargo, setor, gestor — não muda nem com o corpo forjado.
/// </summary>
public class DiretorioEdicaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(UsuariosParaDiretorioFalso usuarios, Guid? usuarioId, params string[] roles)
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

	private async Task<PessoaDto?> LerAsync(UsuariosParaDiretorioFalso usuarios, Guid usuarioId)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		factory.UsuariosDoDiretorio = usuarios;

		var resultado = await escopo.ServiceProvider.GetRequiredService<ObterPessoaHandler>().HandleAsync(usuarioId);

		return resultado.IsSuccess ? resultado.Value.Pessoa : null;
	}

	private async Task<SetorDto> CriarSetorAsync(bool ativo = true)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var sufixo = Guid.NewGuid().ToString("N")[..8];

		var criado = await escopo.ServiceProvider.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand($"Setor {sufixo}", $"dir-{sufixo}", false, null));
		criado.IsSuccess.Should().BeTrue();

		if (!ativo)
		{
			var editado = await escopo.ServiceProvider.GetRequiredService<EditarSetorHandler>()
				.HandleAsync(new EditarSetorCommand(criado.Value.Id, criado.Value.Nome, criado.Value.Icone, false));
			editado.IsSuccess.Should().BeTrue();
		}

		return criado.Value;
	}

	[Fact]
	public async Task Meu_perfil_ColaboradorEditaOProprioContato_EGravaDeVerdade()
	{
		var eu = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com");
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");

		var resposta = await client.PostAsync("/diretorio/perfil", Form(
			("Nome", "Eva Souza"), ("Ramal", "2222"), ("Sobre", "Trabalho com dados."), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "salvar redireciona de volta para Meu perfil");
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Perfil atualizado");

		var lida = await LerAsync(usuarios, eu);
		lida!.Nome.Should().Be("Eva Souza");
		lida.Ramal.Should().Be("2222");
		lida.Sobre.Should().Be("Trabalho com dados.");
	}

	[Fact]
	public async Task Meu_perfil_ComCamposFuncionaisForjados_NaoAlteraCargoSetorNemGestor()
	{
		var eu = Guid.NewGuid();
		var chefe = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com").Com(chefe, "chefe@x.com");
		var setor = await CriarSetorAsync();

		// Estado inicial definido pelo admin.
		var admin = CriarCliente(usuarios, Guid.NewGuid(), "diretorio-admin");
		var tokenAdmin = await TokenAsync(admin, $"/diretorio/{eu}/editar");
		(await admin.PostAsync($"/diretorio/{eu}/editar", Form(
			("Nome", "Eva"), ("Cargo", "Analista"), ("SetorId", setor.Id.ToString()), ("GestorUsuarioId", chefe.ToString()),
			("__RequestVerificationToken", tokenAdmin)))).StatusCode.Should().Be(HttpStatusCode.OK);

		// O colaborador tenta se promover.
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");
		await client.PostAsync("/diretorio/perfil", Form(
			("Nome", "Eva Souza"),
			("Cargo", "Diretora Geral"),
			("SetorId", Guid.NewGuid().ToString()),
			("GestorUsuarioId", string.Empty),
			("__RequestVerificationToken", token)));

		var lida = await LerAsync(usuarios, eu);
		lida!.Nome.Should().Be("Eva Souza", "o contato é dela e mudou");
		lida.Cargo.Should().Be("Analista", "cargo forjado é ignorado");
		lida.SetorId.Should().Be(setor.Id, "setor forjado é ignorado");
		lida.GestorUsuarioId.Should().Be(chefe, "gestor forjado é ignorado");
	}

	[Fact]
	public async Task Meu_perfil_ComUsuarioIdForjado_EditaSoOProprio()
	{
		var eu = Guid.NewGuid();
		var outro = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com").Com(outro, "outro@x.com");
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");

		await client.PostAsync("/diretorio/perfil", Form(
			("Nome", "Eva"), ("UsuarioId", outro.ToString()), ("usuarioId", outro.ToString()), ("id", outro.ToString()),
			("__RequestVerificationToken", token)));

		(await LerAsync(usuarios, eu))!.Nome.Should().Be("Eva");
		(await LerAsync(usuarios, outro))!.TemPerfil.Should().BeFalse("o id de quem sou vem do claim, nunca do formulário");
	}

	[Fact]
	public async Task Meu_perfil_NomeAcimaDoLimite_MostraOErroENaoGrava()
	{
		var eu = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com");
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");

		var resposta = await client.PostAsync("/diretorio/perfil", Form(("Nome", new string('n', 121)), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await LerAsync(usuarios, eu))!.TemPerfil.Should().BeFalse();
	}

	[Fact]
	public async Task Meu_perfil_UsuarioNaoEstaEntreOsAtivos_404_SemQuebrar()
	{
		var eu = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com");
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");
		usuarios.Usuarios.Clear();

		var resposta = await client.PostAsync("/diretorio/perfil", Form(("Nome", "Eva"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound, "quem deixou de ser um usuário ativo não tem perfil — igual ao GET");
	}

	[Fact]
	public async Task Admin_EditaCargoSetorEGestor_EGravaDeVerdade()
	{
		var alvo = Guid.NewGuid();
		var chefe = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(alvo, "alvo@x.com").Com(chefe, "chefe@x.com");
		var setor = await CriarSetorAsync();
		var client = CriarCliente(usuarios, Guid.NewGuid(), "diretorio-admin");
		var token = await TokenAsync(client, $"/diretorio/{alvo}/editar");

		var resposta = await client.PostAsync($"/diretorio/{alvo}/editar", Form(
			("Nome", "Alvo"), ("Cargo", "Coordenador"), ("SetorId", setor.Id.ToString()), ("GestorUsuarioId", chefe.ToString()),
			("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var lida = await LerAsync(usuarios, alvo);
		lida!.Cargo.Should().Be("Coordenador");
		lida.SetorId.Should().Be(setor.Id);
		lida.GestorUsuarioId.Should().Be(chefe);
	}

	[Fact]
	public async Task Admin_CicloDeGestor_RecusadoComToastDeErro()
	{
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(a, "a@x.com").Com(b, "b@x.com");
		var client = CriarCliente(usuarios, Guid.NewGuid(), "intranet-admin");
		var token = await TokenAsync(client, $"/diretorio/{b}/editar");
		await client.PostAsync($"/diretorio/{b}/editar", Form(("GestorUsuarioId", a.ToString()), ("__RequestVerificationToken", token)));

		var tokenA = await TokenAsync(client, $"/diretorio/{a}/editar");
		var resposta = await client.PostAsync($"/diretorio/{a}/editar", Form(("GestorUsuarioId", b.ToString()), ("__RequestVerificationToken", tokenA)));

		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("reportar, direta ou indiretamente, a si mesma");
		(await LerAsync(usuarios, a))!.GestorUsuarioId.Should().BeNull();
	}

	[Fact]
	public async Task Admin_SetorInativo_RecusadoParaLotacaoNova()
	{
		var alvo = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(alvo, "alvo@x.com");
		var inativo = await CriarSetorAsync(ativo: false);
		var client = CriarCliente(usuarios, Guid.NewGuid(), "diretorio-admin");
		var token = await TokenAsync(client, $"/diretorio/{alvo}/editar");

		var resposta = await client.PostAsync($"/diretorio/{alvo}/editar", Form(("SetorId", inativo.Id.ToString()), ("__RequestVerificationToken", token)));

		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Escolha um setor existente e ativo");
		(await LerAsync(usuarios, alvo))!.SetorId.Should().BeNull();
	}

	[Fact]
	public async Task Editar_TelaDoAdmin_MostraOsCampos()
	{
		var alvo = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(alvo, "alvo@x.com");
		var client = CriarCliente(usuarios, Guid.NewGuid(), "diretorio-admin");

		var html = await client.GetStringAsync($"/diretorio/{alvo}/editar");

		html.Should().Contain("name=\"Cargo\"").And.Contain("name=\"GestorUsuarioId\"").And.Contain("name=\"SetorId\"");
	}

	[Fact]
	public async Task Meu_perfil_TelaDoColaborador_NaoTemOsCamposFuncionais()
	{
		var eu = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com"), eu, "diretorio-user");

		var html = await client.GetStringAsync("/diretorio/perfil");

		html.Should().Contain("name=\"Nome\"").And.Contain("name=\"Ramal\"");
		html.Should().NotContain("name=\"Cargo\"").And.NotContain("name=\"GestorUsuarioId\"").And.NotContain("name=\"SetorId\"");
	}

	public static TheoryData<string[]> SemPermissaoParaEditarOsOutros => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-admin" },
		new[] { "inventario-admin" },
		new[] { "diretorio-user" },
	};

	[Theory]
	[MemberData(nameof(SemPermissaoParaEditarOsOutros))]
	public async Task EditarOutraPessoa_SemSerAdmin_403_MesmoSemToken(string[] roles)
	{
		var alvo = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(alvo, "alvo@x.com"), Guid.NewGuid(), roles);

		(await client.GetAsync($"/diretorio/{alvo}/editar")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await client.PostAsync($"/diretorio/{alvo}/editar", Form(("Cargo", "Chefe")))).StatusCode
			.Should().Be(HttpStatusCode.Forbidden, "403 do gate, e não 400 do antifalsificação");
	}

	[Theory]
	[InlineData("financeiro-admin")]
	[InlineData("inventario-admin")]
	public async Task MeuPerfil_SemNenhumDosTresPerfis_403_NoPost_MesmoSemToken(string role)
	{
		var eu = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com"), eu, role);

		var resposta = await client.PostAsync("/diretorio/perfil", Form(("Nome", "Eva")));

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Admin_PostSemToken_PassaDoGateEBateNoAntifalsificacao()
	{
		var alvo = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(alvo, "alvo@x.com"), Guid.NewGuid(), "diretorio-admin");

		var resposta = await client.PostAsync($"/diretorio/{alvo}/editar", Form(("Cargo", "X")));

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}
}
