using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A imagem da publicação é servida só a quem pode ler o aviso (no ar e visível) ou a quem
/// escreve no setor dono. Tudo o mais responde 404, igual ao permalink.
/// </summary>
public class PublicacaoImagemTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.ProcessadorDeImagem = null;
		factory.ArquivoStore = null;
		factory.ResolvedorDePermissoes = null;

		return Task.CompletedTask;
	}

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

	private async Task<(Guid Id, string Slug)> PublicarAsync(
		Visibilidade visibilidade, DateTimeOffset publicadoEm, bool comImagem = true, string? slug = null)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		if (slug is null)
		{
			slug = $"img-{Guid.NewGuid():N}"[..20];
			var setor = await escopo.ServiceProvider
				.GetRequiredService<CreateSetorHandler>()
				.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));
			setor.IsSuccess.Should().BeTrue();
		}

		await using var arquivo = new MemoryStream([1, 2, 3]);

		var publicada = await escopo.ServiceProvider
			.GetRequiredService<PublicarPublicacaoHandler>()
			.HandleAsync(new PublicarPublicacaoCommand(
				slug, "Aviso com imagem", null, TipoPublicacao.Aviso, visibilidade,
				PrioridadePublicacao.Normal, publicadoEm, null, "teste",
				Imagem: comImagem ? new ImagemEnviada(arquivo) : null));

		publicada.IsSuccess.Should().BeTrue();

		return (publicada.Value.Publicacao.Id, slug);
	}

	[Fact]
	public async Task Entregar_ComImagem_200ComCabecalhos()
	{
		var (id, _) = await PublicarAsync(Visibilidade.Empresa, DateTimeOffset.UtcNow.AddMinutes(-5));

		var resposta = await CriarCliente().GetAsync($"/publicacoes/{id}/imagem");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
		resposta.Headers.CacheControl!.ToString().Should().Be("no-cache, private");
		resposta.Headers.ETag.Should().NotBeNull();
		resposta.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
		(await resposta.Content.ReadAsByteArrayAsync()).Should().Equal(1, 2, 3);
	}

	[Fact]
	public async Task Entregar_IfNoneMatchIgual_304()
	{
		var (id, _) = await PublicarAsync(Visibilidade.Empresa, DateTimeOffset.UtcNow.AddMinutes(-5));
		var client = CriarCliente();
		var primeira = await client.GetAsync($"/publicacoes/{id}/imagem");

		using var pedido = new HttpRequestMessage(HttpMethod.Get, $"/publicacoes/{id}/imagem");
		pedido.Headers.TryAddWithoutValidation("If-None-Match", primeira.Headers.ETag!.ToString());
		var resposta = await client.SendAsync(pedido);

		resposta.StatusCode.Should().Be(HttpStatusCode.NotModified);
	}

	[Fact]
	public async Task Entregar_SemImagem_404()
	{
		var (id, _) = await PublicarAsync(Visibilidade.Empresa, DateTimeOffset.UtcNow.AddMinutes(-5), comImagem: false);

		(await CriarCliente().GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Entregar_Inexistente_404() =>
		(await CriarCliente().GetAsync($"/publicacoes/{Guid.NewGuid()}/imagem")).StatusCode.Should().Be(HttpStatusCode.NotFound);

	[Fact]
	public async Task Entregar_SomenteSetor_LeitorDeFora_404()
	{
		var (id, slug) = await PublicarAsync(Visibilidade.Setor, DateTimeOffset.UtcNow.AddMinutes(-5));

		var role = $"dentro-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(role, IntranetPermissoes.Setor.Read(slug));

		(await CriarCliente().GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await CriarCliente(role).GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Entregar_Agendada_SoLeitor_404_AutorDoSetor_200()
	{
		var (id, slug) = await PublicarAsync(Visibilidade.Empresa, DateTimeOffset.UtcNow.AddDays(1));

		var leitor = $"leitor-{Guid.NewGuid():N}";
		var autor = $"autor-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao(leitor, IntranetPermissoes.Setor.Read(slug))
			.ComPermissao(autor, IntranetPermissoes.Setor.Read(slug))
			.ComPermissao(autor, IntranetPermissoes.Setor.Write(slug));

		(await CriarCliente(leitor).GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await CriarCliente(autor).GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Entregar_ArquivoSumiuDoDisco_404()
	{
		factory.ArquivoStore = new ArquivoStoreFalso { FalhaAoEscrever = new FileNotFoundException("sumiu") };
		var (id, _) = await PublicarAsync(Visibilidade.Empresa, DateTimeOffset.UtcNow.AddMinutes(-5));

		var resposta = await CriarCliente().GetAsync($"/publicacoes/{id}/imagem");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
		resposta.Headers.ETag.Should().BeNull();
	}

	// --- Telas: formulário, edição e exibição -------------------------------------------------

	private HttpClient CriarClienteAdmin(string? tema = null)
	{
		var client = tema is null
			? factory.CreateClient()
			: factory.WithWebHostBuilder(builder => builder.UseSetting("Intranet:Theme:Nome", tema)).CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		return client;
	}

	private static async Task<string> NovoSetorAsync(HttpClient client)
	{
		var slug = $"img-{Guid.NewGuid():N}"[..20];
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/Setores/Create");
		var resposta = await client.PostAsync("/Setores/Create", AuxiliaresDeHttp.Form(
			("Nome", $"Setor {slug}"), ("Slug", slug), ("__RequestVerificationToken", token)));
		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		return slug;
	}

	private static async Task<HttpResponseMessage> SalvarAvisoAsync(
		HttpClient client, string slug, string titulo, string? corpo, bool comArquivo,
		Guid? id = null, string? descricao = null, bool remover = false)
	{
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/{slug}/avisos");

		using var conteudo = new MultipartFormDataContent
		{
			{ new StringContent(titulo), "Form.Titulo" },
			{ new StringContent("0"), "Form.Tipo" },
			{ new StringContent("1"), "Form.Visibilidade" },
			{ new StringContent("0"), "Form.Prioridade" },
			{ new StringContent(DateTime.Now.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm")), "Form.PublicadoEm" },
			{ new StringContent(token), "__RequestVerificationToken" },
		};

		if (corpo is not null)
		{
			conteudo.Add(new StringContent(corpo), "Form.Corpo");
		}

		if (id is not null)
		{
			conteudo.Add(new StringContent(id.Value.ToString()), "Form.Id");
		}

		if (descricao is not null)
		{
			conteudo.Add(new StringContent(descricao), "Form.ImagemDescricao");
		}

		if (remover)
		{
			conteudo.Add(new StringContent("true"), "Form.RemoverImagem");
		}

		if (comArquivo)
		{
			var arquivo = new ByteArrayContent([1, 2, 3]);
			arquivo.Headers.ContentType = new("image/png");
			conteudo.Add(arquivo, "Form.Imagem", "arte.png");
		}

		return await client.PostAsync($"/{slug}/avisos", conteudo);
	}

	private static async Task<Guid> IdDoAvisoAsync(HttpClient client, string slug)
	{
		var aba = await client.GetStringAsync($"/{slug}/avisos");
		var id = Regex.Match(aba, @"avisos\?editar=([0-9a-fA-F-]{36})");
		id.Success.Should().BeTrue("cada aviso da lista tem o botão Editar");

		return Guid.Parse(id.Groups[1].Value);
	}

	[Fact]
	public async Task Publicar_PelaTela_ComImagem_CardDoMuralMostraImgDepoisDoTexto()
	{
		var client = CriarClienteAdmin();
		var slug = await NovoSetorAsync(client);
		var corpo = $"Texto-{Guid.NewGuid():N}";

		(await SalvarAvisoAsync(client, slug, "Com arte", corpo, comArquivo: true, descricao: "Cartaz da festa"))
			.StatusCode.Should().Be(HttpStatusCode.OK);
		var id = await IdDoAvisoAsync(client, slug);

		var mural = await client.GetStringAsync("/");
		var posicaoTexto = mural.IndexOf(corpo, StringComparison.Ordinal);
		var posicaoImagem = mural.IndexOf($"src=\"/publicacoes/{id}/imagem\"", StringComparison.Ordinal);

		posicaoTexto.Should().BeGreaterThan(-1);
		posicaoImagem.Should().BeGreaterThan(posicaoTexto, "a imagem vem depois do texto");
		mural.Should().Contain("alt=\"Cartaz da festa\"");
	}

	[Fact]
	public async Task Detalhe_ComImagem_MostraImg()
	{
		var client = CriarClienteAdmin();
		var slug = await NovoSetorAsync(client);

		await SalvarAvisoAsync(client, slug, "Detalhe", null, comArquivo: true);
		var id = await IdDoAvisoAsync(client, slug);

		var html = await client.GetStringAsync($"/publicacoes/{id}");

		html.Should().Contain($"src=\"/publicacoes/{id}/imagem\"").And.Contain("alt=\"\"");
		html.Should().NotContain("<div class=\"sc-card__body mt-2\"></div>");
	}

	[Theory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Mural_NosDoisTemas_MostraImgDaPublicacao(string tema)
	{
		var setup = CriarClienteAdmin();
		var slug = await NovoSetorAsync(setup);
		await SalvarAvisoAsync(setup, slug, "Dois temas", "Algo", comArquivo: true);
		var id = await IdDoAvisoAsync(setup, slug);
		var client = CriarClienteAdmin(tema);

		var html = await client.GetStringAsync("/");

		html.Should().Contain(tema == "Horizontal" ? "sc-topnav" : "sc-sidebar", "garante que o tema trocou de fato");
		html.Should().Contain($"<img class=\"sc-card__imagem\" src=\"/publicacoes/{id}/imagem\"");
	}

	[Fact]
	public async Task Avisos_Editar_CarregaOFormComMiniaturaERemover()
	{
		var client = CriarClienteAdmin();
		var slug = await NovoSetorAsync(client);
		await SalvarAvisoAsync(client, slug, "Para editar", "Texto", comArquivo: true, descricao: "Descricao atual");
		var id = await IdDoAvisoAsync(client, slug);

		var html = await client.GetStringAsync($"/{slug}/avisos?editar={id}");

		html.Should().Contain($"value=\"{id}\"").And.Contain("Para editar");
		html.Should().Contain("name=\"Form.RemoverImagem\"");
		html.Should().Contain("Descricao atual");
		html.Should().Contain($"src=\"/publicacoes/{id}/imagem\"");
		AuxiliaresDeHttp.Decodificar(html).Should().Contain("Remover a imagem");
	}

	[Fact]
	public async Task Avisos_Editar_IdDeOutroSetorOuInexistente_FormVazio()
	{
		var client = CriarClienteAdmin();
		var slugA = await NovoSetorAsync(client);
		var slugB = await NovoSetorAsync(client);
		await SalvarAvisoAsync(client, slugA, "Segredo do A", "Texto", comArquivo: true);
		var id = await IdDoAvisoAsync(client, slugA);

		foreach (var alvo in new[] { id, Guid.NewGuid() })
		{
			var html = await client.GetStringAsync($"/{slugB}/avisos?editar={alvo}");

			html.Should().NotContain("Segredo do A").And.NotContain("name=\"Form.RemoverImagem\"");
		}
	}

	[Fact]
	public async Task Editar_RemoverImagem_TiraDoCard()
	{
		var client = CriarClienteAdmin();
		var slug = await NovoSetorAsync(client);
		await SalvarAvisoAsync(client, slug, "Tirar arte", "Texto", comArquivo: true);
		var id = await IdDoAvisoAsync(client, slug);
		(await client.GetStringAsync("/")).Should().Contain($"/publicacoes/{id}/imagem");

		(await SalvarAvisoAsync(client, slug, "Tirar arte", "Texto", comArquivo: false, id: id, remover: true))
			.StatusCode.Should().Be(HttpStatusCode.OK);

		(await client.GetStringAsync("/")).Should().NotContain($"/publicacoes/{id}/imagem");
		(await client.GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Publicar_ImagemRecusada_VoltaComAMensagemEPedeOArquivoDeNovo()
	{
		factory.ProcessadorDeImagem = new ProcessadorDeImagemFalso { FalharCom = IntranetErrors.Publicacoes.ImagemFormatoNaoSuportado };
		var client = CriarClienteAdmin();
		var slug = await NovoSetorAsync(client);

		var resposta = await SalvarAvisoAsync(client, slug, "Imagem ruim", "Texto", comArquivo: true);
		var html = AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync());

		html.Should().Contain("Envie a imagem em PNG, JPG ou WebP.");
		html.Should().Contain("Escolha a imagem de novo.");
		html.Should().Contain("Imagem ruim", "o resto do formulário volta preenchido");
	}
}
