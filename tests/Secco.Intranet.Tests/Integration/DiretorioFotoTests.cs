using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Secco.SharedKernel.Results;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Rotas da foto do colaborador contra o host real: quem pode trocar a foto de quem, e que o
/// arquivo só sai para quem está autenticado e tem acesso ao Diretório.
/// </summary>
public class DiretorioFotoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;
		factory.GestaoDeAcesso = null;
		factory.ProcessadorDeImagem = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(UsuariosParaDiretorioFalso usuarios, Guid? usuarioId, params string[] roles)
	{
		factory.UsuariosDoDiretorio = usuarios;
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa();
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

	private static MultipartFormDataContent Envio(string? token, params (string Chave, string Valor)[] campos)
	{
		var corpo = new MultipartFormDataContent();
		var arquivo = new ByteArrayContent([9, 9, 9]);
		arquivo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
		corpo.Add(arquivo, "foto", "foto.png");

		foreach (var (chave, valor) in campos)
		{
			corpo.Add(new StringContent(valor), chave);
		}

		if (token is not null)
		{
			corpo.Add(new StringContent(token), "__RequestVerificationToken");
		}

		return corpo;
	}

	private static async Task<HttpResponseMessage> EnviarPropriaAsync(HttpClient client, params (string Chave, string Valor)[] campos) =>
		await client.PostAsync("/diretorio/perfil/foto", Envio(await TokenAsync(client, "/diretorio/perfil"), campos));

	[Fact]
	public async Task EnviarPropria_GravaParaQuemEstaLogado_IgnorandoIdNoFormulario()
	{
		var eu = Guid.NewGuid();
		var outro = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com").Com(outro, "outro@x.com");
		var client = CriarCliente(usuarios, eu, "diretorio-user");

		var resposta = await EnviarPropriaAsync(client, ("id", outro.ToString()), ("usuarioId", outro.ToString()), ("UsuarioId", outro.ToString()));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "volta para Meu perfil");
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Foto atualizada");
		(await client.GetAsync($"/diretorio/{eu}/foto")).StatusCode.Should().Be(HttpStatusCode.OK);
		(await client.GetAsync($"/diretorio/{outro}/foto")).StatusCode.Should().Be(HttpStatusCode.NotFound, "o id do formulário é ignorado");
	}

	[Fact]
	public async Task EnviarDeOutro_SemNivelAdministrador_403()
	{
		var eu = Guid.NewGuid();
		var outro = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com").Com(outro, "outro@x.com");
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");

		var resposta = await client.PostAsync($"/diretorio/{outro}/foto", Envio(token));
		var remocao = await client.PostAsync($"/diretorio/{outro}/foto/remover", Form(("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		remocao.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await client.GetAsync($"/diretorio/{outro}/foto")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task EnviarDeOutro_Administrador_Grava()
	{
		var admin = Guid.NewGuid();
		var alvo = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(admin, "admin@x.com").Com(alvo, "alvo@x.com");
		var client = CriarCliente(usuarios, admin, "diretorio-admin");
		var token = await TokenAsync(client, $"/diretorio/{alvo}/editar");

		var resposta = await client.PostAsync($"/diretorio/{alvo}/foto", Envio(token));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "volta para a edição");
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/diretorio/{alvo}/editar");
		(await client.GetAsync($"/diretorio/{alvo}/foto")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Entregar_ComFoto_200ComCabecalhos()
	{
		var eu = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com"), eu, "diretorio-user");
		await EnviarPropriaAsync(client);

		var resposta = await client.GetAsync($"/diretorio/{eu}/foto");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
		resposta.Headers.ETag.Should().NotBeNull();
		resposta.Headers.CacheControl!.Private.Should().BeTrue();
		resposta.Headers.CacheControl.NoCache.Should().BeTrue();
		resposta.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
		(await resposta.Content.ReadAsByteArrayAsync()).Should().Equal(1, 2, 3);
	}

	[Fact]
	public async Task Entregar_IfNoneMatchIgual_304()
	{
		var eu = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com"), eu, "diretorio-user");
		await EnviarPropriaAsync(client);
		var etag = (await client.GetAsync($"/diretorio/{eu}/foto")).Headers.ETag!.ToString();

		using var pedido = new HttpRequestMessage(HttpMethod.Get, $"/diretorio/{eu}/foto");
		pedido.Headers.TryAddWithoutValidation("If-None-Match", etag);
		var resposta = await client.SendAsync(pedido);

		resposta.StatusCode.Should().Be(HttpStatusCode.NotModified);
		(await resposta.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
	}

	[Fact]
	public async Task Entregar_SemFoto_404()
	{
		var eu = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com"), eu, "diretorio-user");

		(await client.GetAsync($"/diretorio/{eu}/foto")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Entregar_SemAcessoAoDiretorio_403()
	{
		var eu = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com");
		var dono = CriarCliente(usuarios, eu, "diretorio-user");
		await EnviarPropriaAsync(dono);
		var intruso = CriarCliente(usuarios, Guid.NewGuid());

		(await intruso.GetAsync($"/diretorio/{eu}/foto")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Enviar_SemToken_400()
	{
		var eu = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com"), eu, "diretorio-user");

		var resposta = await client.PostAsync("/diretorio/perfil/foto", Envio(token: null));

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await client.GetAsync($"/diretorio/{eu}/foto")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Enviar_ImagemRecusada_VoltaComAMensagem()
	{
		var eu = Guid.NewGuid();
		factory.ProcessadorDeImagem = new ProcessadorDeImagemFalso { FalharCom = Error.Validation("Foto.Invalida", "A imagem não é válida.") };
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com"), eu, "diretorio-user");

		var resposta = await EnviarPropriaAsync(client);

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("A imagem não é válida.");
		(await client.GetAsync($"/diretorio/{eu}/foto")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Remover_VoltaAsIniciais()
	{
		var eu = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com"), eu, "diretorio-user");
		await EnviarPropriaAsync(client);
		var token = await TokenAsync(client, "/diretorio/perfil");

		var resposta = await client.PostAsync("/diretorio/perfil/foto/remover", Form(("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Foto removida");
		(await client.GetAsync($"/diretorio/{eu}/foto")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
}
