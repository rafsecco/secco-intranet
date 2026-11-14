using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Imagens no meio do texto: o envio pelo formulário e a leitura pela rota própria, com a mesma
/// regra de visibilidade da imagem de destaque.
/// </summary>
public class ImagemNoTextoFluxoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
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

	private HttpClient CriarClienteAdmin() => CriarCliente("intranet-admin");

	private static async Task<string> NovoSetorAsync(HttpClient client)
	{
		var slug = $"img-{Guid.NewGuid():N}"[..20];
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/Setores/Create");
		var resposta = await client.PostAsync("/Setores/Create", AuxiliaresDeHttp.Form(
			("Nome", $"Setor {slug}"), ("Slug", slug), ("__RequestVerificationToken", token)));
		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		return slug;
	}

	private static MultipartFormDataContent Multipart(string titulo, string corpo, string visibilidade, string? token, int arquivos)
	{
		var conteudo = new MultipartFormDataContent
		{
			{ new StringContent(titulo), "Form.Titulo" },
			{ new StringContent(corpo), "Form.Corpo" },
			{ new StringContent("0"), "Form.Tipo" },
			{ new StringContent(visibilidade), "Form.Visibilidade" },
			{ new StringContent("0"), "Form.Prioridade" },
			{ new StringContent(DateTime.Now.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm")), "Form.PublicadoEm" },
		};

		if (token is not null)
		{
			conteudo.Add(new StringContent(token), "__RequestVerificationToken");
		}

		for (var i = 0; i < arquivos; i++)
		{
			var arquivo = new ByteArrayContent([1, 2, 3]);
			arquivo.Headers.ContentType = new("image/png");
			conteudo.Add(arquivo, "Form.ImagensDoTexto", $"imagem{i}.png");
		}

		return conteudo;
	}

	private static async Task<Guid> IdDoAvisoAsync(HttpClient client, string slug)
	{
		var aba = await client.GetStringAsync($"/{slug}/avisos");
		var id = Regex.Match(aba, @"avisos\?editar=([0-9a-fA-F-]{36})");
		id.Success.Should().BeTrue("cada aviso da lista tem o botão Editar");

		return Guid.Parse(id.Groups[1].Value);
	}

	private async Task<(Guid Id, string Slug, List<string> Urls)> PublicarComDuasImagensAsync(string visibilidade = "1")
	{
		var client = CriarClienteAdmin();
		var slug = await NovoSetorAsync(client);
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/{slug}/avisos");

		using var conteudo = Multipart(
			"Com imagens", "a ![um](nova-imagem:0) b ![dois](nova-imagem:1)", visibilidade, token, arquivos: 2);
		(await client.PostAsync($"/{slug}/avisos", conteudo)).StatusCode.Should().Be(HttpStatusCode.OK);

		var id = await IdDoAvisoAsync(client, slug);
		var html = await client.GetStringAsync($"/publicacoes/{id}");
		var urls = Regex.Matches(html, @"<img[^>]*src=""(/publicacoes/[0-9a-fA-F-]{36}/imagens/[0-9a-fA-F-]{36})""")
			.Select(m => m.Groups[1].Value)
			.ToList();

		return (id, slug, urls);
	}

	[Fact]
	public async Task Publicar_DuasImagensNoTexto_LeituraMostraAsDuas()
	{
		var (id, _, urls) = await PublicarComDuasImagensAsync();

		urls.Should().HaveCount(2);
		urls.Should().OnlyContain(url => url.StartsWith($"/publicacoes/{id}/imagens/", StringComparison.Ordinal));

		foreach (var url in urls)
		{
			var resposta = await CriarClienteAdmin().GetAsync(url);

			resposta.StatusCode.Should().Be(HttpStatusCode.OK);
			resposta.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
			resposta.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
		}
	}

	[Fact]
	public async Task Publicar_ImagensNoTexto_GravaNoMesmoSaveChanges()
	{
		var (id, _, _) = await PublicarComDuasImagensAsync();

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

		(await contexto.Publicacoes.CountAsync(p => p.Id == id)).Should().Be(1);
		(await contexto.ImagensNoTexto.CountAsync(i => i.PublicacaoId == id)).Should().Be(2);
	}

	[Fact]
	public async Task ImagemNoTexto_OutraPublicacao_404()
	{
		var (_, _, urlsA) = await PublicarComDuasImagensAsync();
		var (idB, _, _) = await PublicarComDuasImagensAsync();
		var imagemDeA = urlsA[0][(urlsA[0].LastIndexOf('/') + 1)..];

		var resposta = await CriarClienteAdmin().GetAsync($"/publicacoes/{idB}/imagens/{imagemDeA}");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task ImagemNoTexto_LeitorSemVisibilidade_404()
	{
		// 0 = só o setor: quem não tem Role do setor não enxerga.
		var (_, _, urls) = await PublicarComDuasImagensAsync(visibilidade: "0");
		urls.Should().HaveCount(2);

		(await CriarCliente().GetAsync(urls[0])).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await CriarClienteAdmin().GetAsync(urls[0])).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Publicar_MarcadorSemArquivo_RecusaComMensagem()
	{
		var client = CriarClienteAdmin();
		var slug = await NovoSetorAsync(client);
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/{slug}/avisos");

		using var conteudo = Multipart("Sem arquivo", "texto ![x](nova-imagem:0)", "1", token, arquivos: 0);
		var resposta = await client.PostAsync($"/{slug}/avisos", conteudo);
		var html = AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync());

		html.Should().Contain("As imagens novas do texto não foram guardadas: insira-as de novo antes de enviar.");
		html.Should().Contain("Sem arquivo", "o texto volta preenchido");
		html.Should().Contain("nova-imagem:0", "o Markdown mantém o marcador");
	}

	[Fact]
	public async Task Publicar_SemAntiforgery_Recusa()
	{
		var client = CriarClienteAdmin();
		var slug = await NovoSetorAsync(client);

		using var conteudo = Multipart("Sem token", "texto", "1", token: null, arquivos: 0);
		var resposta = await client.PostAsync($"/{slug}/avisos", conteudo);

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}
}
