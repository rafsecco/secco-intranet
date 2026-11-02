using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A tela de editar documento: botão na tela de organizar, formulário preenchido, troca de título
/// e de arquivo, recusas devolvidas como mensagem e o 404 de quem não alcança o documento.
/// </summary>
public class DocumentoEdicaoTelaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly byte[] PdfOriginal = Encoding.UTF8.GetBytes("%PDF-1.7\noriginal\n%%EOF");
	private static readonly byte[] PdfNovo = Encoding.UTF8.GetBytes("%PDF-1.7\nsubstituto bem diferente\n%%EOF");

	private readonly string _raiz = Path.Combine(
		Path.GetTempPath(), "secco-intranet-testes", Guid.NewGuid().ToString("N"));

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		if (Directory.Exists(_raiz))
		{
			Directory.Delete(_raiz, recursive: true);
		}

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(string role = "intranet-admin", Guid? tenant = null)
	{
		var client = factory
			.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Intranet:Documentos:Armazenamento:Raiz"] = _raiz,
				})))
			.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, (tenant ?? factory.TenantAlfa).ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, role);

		return client;
	}

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private static async Task<string> CriarSetorAsync(HttpClient admin)
	{
		var slug = $"setor-{Guid.NewGuid():N}"[..20];
		var token = await TokenAsync(admin, "/Setores/Create");

		var resposta = await admin.PostAsync("/Setores/Create", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", "Setor de Teste"),
			new KeyValuePair<string, string>("Slug", slug),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);

		return slug;
	}

	private async Task<Guid> CriarGrupoAsync(string slug, string titulo)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var resultado = await escopo.ServiceProvider.GetRequiredService<CriarGrupoDeDocumentosHandler>()
			.HandleAsync(new CriarGrupoDeDocumentosCommand(ReferenciaDeDono.DoSetor(slug), titulo, null, "teste"), CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();

		return resultado.Value.Id;
	}

	private async Task<Guid> PublicarAsync(HttpClient admin, string slug, string titulo, string descricao = "")
	{
		var token = await TokenAsync(admin, $"/{slug}/documentos");

		using var arquivo = new ByteArrayContent(PdfOriginal);
		using var form = new MultipartFormDataContent
		{
			{ new StringContent(titulo), "Form.Titulo" },
			{ new StringContent(descricao), "Form.Descricao" },
			{ new StringContent("0"), "Form.Visibilidade" },
			{ new StringContent(string.Empty), "Form.ParentId" },
			{ new StringContent(token), "__RequestVerificationToken" },
			{ arquivo, "arquivo", "original.pdf" },
		};

		(await admin.PostAsync($"/{slug}/documentos", form)).StatusCode
			.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var nos = await escopo.ServiceProvider.GetRequiredService<ListarDocumentosHandler>()
			.HandleAsync(new ListarDocumentosQuery(ReferenciaDeDono.DoSetor(slug)), CancellationToken.None);

		return nos.Value.Select(no => no.Documento).Single(documento => documento.Titulo == titulo).Id;
	}

	private static async Task<HttpResponseMessage> EditarAsync(
		HttpClient client, string slug, Guid id, string tokenDe, string titulo, byte[]? novo, string nomeNovo = "novo.pdf")
	{
		var token = await TokenAsync(client, tokenDe);

		using var form = new MultipartFormDataContent
		{
			{ new StringContent(titulo), "Form.Titulo" },
			{ new StringContent(string.Empty), "Form.Descricao" },
			{ new StringContent("0"), "Form.Visibilidade" },
			{ new StringContent(token), "__RequestVerificationToken" },
		};

		// O navegador manda a parte do arquivo mesmo vazia, quando nada foi escolhido.
		using var arquivo = new ByteArrayContent(novo ?? []);
		form.Add(arquivo, "arquivo", novo is null ? "vazio.bin" : nomeNovo);

		return await client.PostAsync($"/{slug}/documentos/{id}/editar", form);
	}

	private static async Task<(byte[] Bytes, string? Disposicao)> BaixarAsync(HttpClient client, Guid id)
	{
		var resposta = await client.GetAsync($"/Documentos/{id}/Download");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		return (
			await resposta.Content.ReadAsByteArrayAsync(),
			resposta.Content.Headers.ContentDisposition?.ToString());
	}

	private int ArquivosDoAlfa()
	{
		var pasta = Path.Combine(_raiz, factory.TenantAlfa.ToString());

		return Directory.Exists(pasta) ? Directory.GetFiles(pasta, "*", SearchOption.AllDirectories).Length : 0;
	}

	[Fact]
	public async Task Organizar_MostraEditarEmCadaLinha()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var documento = await PublicarAsync(admin, slug, "Manual");
		var grupo = await CriarGrupoAsync(slug, "Pasta");

		var html = await admin.GetStringAsync($"/{slug}/documentos/organizar");

		html.Should().Contain($"href=\"/{slug}/documentos/{documento}/editar\"");
		html.Should().Contain($"href=\"/{slug}/documentos/{grupo}/editar\"");
		html.Should().Contain("aria-label=\"Editar Manual\"").And.Contain("aria-label=\"Editar Pasta\"");
	}

	[Fact]
	public async Task Get_DocumentoDoSetor_MostraFormularioPreenchido()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var id = await PublicarAsync(admin, slug, "Manual", "Descricao do manual");

		var html = WebUtility.HtmlDecode(await admin.GetStringAsync($"/{slug}/documentos/{id}/editar"));

		html.Should().Contain("value=\"Manual\"").And.Contain("value=\"Descricao do manual\"");
		html.Should().Contain("Arquivo atual: original.pdf. Deixe em branco para manter.");
		html.Should().Contain("name=\"arquivo\"");
		html.Should().Contain("data-confirmar=\"O arquivo atual \"original.pdf\" será substituído");
	}

	[Fact]
	public async Task Get_Grupo_NaoMostraArquivoNemVisibilidade()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var grupo = await CriarGrupoAsync(slug, "Pasta");

		var html = await admin.GetStringAsync($"/{slug}/documentos/{grupo}/editar");

		html.Should().Contain("value=\"Pasta\"");
		html.Should().NotContain("name=\"arquivo\"").And.NotContain("Visibilidade").And.NotContain("Arquivo atual");
	}

	[Fact]
	public async Task Post_SemArquivo_MantemArquivoETrocaTitulo()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var id = await PublicarAsync(admin, slug, "Antigo");

		var resposta = await EditarAsync(admin, slug, id, $"/{slug}/documentos/{id}/editar", "Novo Titulo", null);

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		WebUtility.HtmlDecode(await resposta.Content.ReadAsStringAsync()).Should().Contain("Documento atualizado.");

		var organizar = await admin.GetStringAsync($"/{slug}/documentos/organizar");

		organizar.Should().Contain("Novo Titulo").And.NotContain(">Antigo<");
		(await BaixarAsync(admin, id)).Bytes.Should().Equal(PdfOriginal);
		ArquivosDoAlfa().Should().Be(1);
	}

	[Fact]
	public async Task Post_ComArquivoNovo_DownloadEntregaONovoEOAntigoSomeDoDisco()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var id = await PublicarAsync(admin, slug, "Manual");

		ArquivosDoAlfa().Should().Be(1);

		var resposta = await EditarAsync(admin, slug, id, $"/{slug}/documentos/{id}/editar", "Manual", PdfNovo, "substituto.pdf");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		ArquivosDoAlfa().Should().Be(1);

		var (bytes, disposicao) = await BaixarAsync(admin, id);

		bytes.Should().Equal(PdfNovo);
		disposicao.Should().Contain("substituto.pdf");
	}

	[Fact]
	public async Task Post_ArquivoNaoAceito_VoltaComMensagemENadaMuda()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var id = await PublicarAsync(admin, slug, "Manual");

		var resposta = await EditarAsync(
			admin, slug, id, $"/{slug}/documentos/{id}/editar", "Titulo Rejeitado", Encoding.UTF8.GetBytes("MZ nao eh pdf"), "programa.exe");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		var html = WebUtility.HtmlDecode(await resposta.Content.ReadAsStringAsync());

		html.Should().Contain("Tipo de arquivo não aceito, ou o conteúdo não corresponde à extensão.");
		html.Should().Contain("value=\"Titulo Rejeitado\"");

		(await admin.GetStringAsync($"/{slug}/documentos/organizar")).Should().Contain("Manual").And.NotContain("Titulo Rejeitado");
		(await BaixarAsync(admin, id)).Bytes.Should().Equal(PdfOriginal);
		ArquivosDoAlfa().Should().Be(1);
	}

	[Fact]
	public async Task GetEPost_SemEscritaNoSetor_Devolvem404()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var outro = await CriarSetorAsync(admin);
		var id = await PublicarAsync(admin, slug, "Manual");

		var role = $"leitor-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao(role, IntranetPermissoes.Setor.Read(slug))
			.ComPermissao(role, IntranetPermissoes.Setor.Read(outro))
			.ComPermissao(role, IntranetPermissoes.Setor.Write(outro));

		try
		{
			var leitor = CriarCliente(role);

			(await leitor.GetAsync($"/{slug}/documentos/{id}/editar")).StatusCode.Should().Be(HttpStatusCode.NotFound);

			// Token da página do outro setor, onde o leitor escreve.
			var resposta = await EditarAsync(leitor, slug, id, $"/{outro}/documentos", "Invasor", null);

			resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
		}
		finally
		{
			factory.ResolvedorDePermissoes = null;
		}

		(await admin.GetStringAsync($"/{slug}/documentos/organizar")).Should().Contain("Manual").And.NotContain("Invasor");
	}

	[Fact]
	public async Task GetEPost_DocumentoArquivado_Devolvem404()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var id = await PublicarAsync(admin, slug, "Manual");

		var token = await TokenAsync(admin, $"/{slug}/documentos/organizar");
		(await admin.PostAsync($"/{slug}/documentos/{id}/arquivar", new FormUrlEncodedContent(
			[new KeyValuePair<string, string>("__RequestVerificationToken", token)]))).StatusCode.Should().Be(HttpStatusCode.OK);

		(await admin.GetAsync($"/{slug}/documentos/{id}/editar")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await EditarAsync(admin, slug, id, $"/{slug}/documentos/organizar", "Zumbi", null)).StatusCode
			.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Post_TenantBeta_NaoAlcancaDocumentoDoAlfa()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var id = await PublicarAsync(admin, slug, "Manual");

		var beta = CriarCliente(tenant: factory.TenantBeta);

		var resposta = await EditarAsync(beta, slug, id, "/Setores/Create", "Invasor Beta", null);

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await admin.GetStringAsync($"/{slug}/documentos/organizar")).Should().Contain("Manual").And.NotContain("Invasor Beta");
		(await BaixarAsync(admin, id)).Bytes.Should().Equal(PdfOriginal);
	}
}
