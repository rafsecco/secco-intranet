using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

public class DiretorioImportacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(UsuariosParaDiretorioFalso? usuarios, params string[] roles)
	{
		factory.UsuariosDoDiretorio = usuarios;
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private async Task<bool> TemPerfilAsync(UsuariosParaDiretorioFalso usuarios, Guid usuarioId)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		factory.UsuariosDoDiretorio = usuarios;

		var resultado = await escopo.ServiceProvider.GetRequiredService<ObterPessoaHandler>().HandleAsync(usuarioId);

		return resultado.IsSuccess && resultado.Value.Pessoa.TemPerfil;
	}

	private static MultipartFormDataContent Arquivo(string token, string conteudo, string nome = "pessoas.csv")
	{
		var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes(conteudo));
		arquivo.Headers.ContentType = new("text/csv");

		return new MultipartFormDataContent
		{
			{ new StringContent(token), "__RequestVerificationToken" },
			{ arquivo, "arquivo", nome },
		};
	}

	public static TheoryData<string[]> SemPermissaoDeAdmin => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-admin" },
		new[] { "inventario-admin" },
		new[] { "diretorio-user" },
	};

	[Theory]
	[MemberData(nameof(SemPermissaoDeAdmin))]
	public async Task SemSerAdminDoDiretorio_ToDaRotaDaImportacao_403(string[] roles)
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), roles);

		(await client.GetAsync("/diretorio/importar")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await client.PostAsync("/diretorio/importar", Arquivo("x", "email\na@x.com\n"))).StatusCode
			.Should().Be(HttpStatusCode.Forbidden, "403 do gate, e não 400 do antifalsificação");
		(await client.PostAsync("/diretorio/importar/aplicar", Form(("csv", "email\na@x.com\n")))).StatusCode
			.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Admin_TelaDeImportacaoAbre()
	{
		var resposta = await CriarCliente(new UsuariosParaDiretorioFalso(), "diretorio-admin").GetAsync("/diretorio/importar");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("email;nome;cargo;ramal;setor;gestor");
	}

	[Fact]
	public async Task Previsualizar_MostraOsTotais_ENaoGravaNada()
	{
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var ana = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(ana, $"ana{sufixo}@x.com");
		var client = CriarCliente(usuarios, "intranet-admin");
		var token = await TokenAsync(client, "/diretorio/importar");

		var resposta = await client.PostAsync("/diretorio/importar", Arquivo(token,
			$"email;nome;cargo\nana{sufixo}@x.com;Ana;Diretora\nfantasma{sufixo}@x.com;Fantasma;\n"));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var html = Decodificar(await resposta.Content.ReadAsStringAsync());
		html.Should().Contain("data-total=\"criados\">1<").And.Contain("data-total=\"erros\">1<");
		html.Should().Contain("usuário ativo", "o motivo do erro da linha aparece");
		html.Should().Contain("name=\"csv\"", "há o formulário de confirmação");
		(await TemPerfilAsync(usuarios, ana)).Should().BeFalse("a pré-visualização não grava");
	}

	[Fact]
	public async Task Aplicar_GravaDeVerdade_EMostraORelatorioFinal()
	{
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var ana = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(ana, $"ana{sufixo}@x.com");
		var client = CriarCliente(usuarios, "diretorio-admin");
		var token = await TokenAsync(client, "/diretorio/importar");

		var resposta = await client.PostAsync("/diretorio/importar/aplicar", Form(
			("csv", $"email;nome;cargo\nana{sufixo}@x.com;Ana Ribeiro;Diretora\n"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var html = Decodificar(await resposta.Content.ReadAsStringAsync());
		html.Should().Contain("Importação concluída").And.Contain("data-total=\"criados\">1<");
		(await TemPerfilAsync(usuarios, ana)).Should().BeTrue();
	}

	[Fact]
	public async Task ArquivoComColunaDesconhecida_ExplicaSemQuebrar()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), "diretorio-admin");
		var token = await TokenAsync(client, "/diretorio/importar");

		var resposta = await client.PostAsync("/diretorio/importar", Arquivo(token, "email;salario\na@x.com;10\n"));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("coluna desconhecida: salario");
	}

	[Fact]
	public async Task ArquivoGrandeDemais_Recusado_SemLerTudo()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), "diretorio-admin");
		var token = await TokenAsync(client, "/diretorio/importar");
		var enorme = "email\n" + new string('a', 3 * 1024 * 1024);

		var resposta = await client.PostAsync("/diretorio/importar", Arquivo(token, enorme));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("passa do limite");
	}

	[Fact]
	public async Task NenhumArquivoEnviado_ExplicaSemQuebrar()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), "diretorio-admin");
		var token = await TokenAsync(client, "/diretorio/importar");

		var resposta = await client.PostAsync("/diretorio/importar", new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } });

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Escolha um arquivo");
	}

	[Fact]
	public async Task SemSecureGateConfigurado_ExplicaSemQuebrar()
	{
		var client = CriarCliente(usuarios: null, "diretorio-admin");
		var token = await TokenAsync(client, "/diretorio/importar");

		var resposta = await client.PostAsync("/diretorio/importar", Arquivo(token, "email\na@x.com\n"));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
	}

	[Fact]
	public async Task Admin_PostSemToken_PassaDoGateEBateNoAntifalsificacao()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), "diretorio-admin");

		(await client.PostAsync("/diretorio/importar/aplicar", Form(("csv", "email\na@x.com\n")))).StatusCode
			.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task BotaoImportar_SoApareceParaOAdmin()
	{
		var usuarios = new UsuariosParaDiretorioFalso();

		var comoUsuario = await CriarCliente(usuarios, "diretorio-user").GetStringAsync("/diretorio");
		var comoAdmin = await CriarCliente(usuarios, "diretorio-admin").GetStringAsync("/diretorio");

		comoUsuario.Should().NotContain("/diretorio/importar");
		comoAdmin.Should().Contain("/diretorio/importar");
	}
}
