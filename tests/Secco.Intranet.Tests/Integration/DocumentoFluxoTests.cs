using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Fluxo real de documentos sobre o host completo (ADR-0012): publicar, baixar de volta, e
/// conferir que o arquivo em repouso está cifrado e não é alcançável por fora do endpoint
/// de download.
/// </summary>
public class DocumentoFluxoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly byte[] ConteudoPdf =
		Encoding.UTF8.GetBytes("%PDF-1.7\nmarcador-secreto-do-teste-de-documento\n%%EOF");

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

	private HttpClient CriarCliente()
	{
		var client = factory
			.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Intranet:Documentos:Armazenamento:Raiz"] = _raiz,
				})))
			.CreateClient();

		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		return client;
	}

	private static async Task<string> TokenAntiFalsificacaoAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private static async Task CriarSetorAsync(HttpClient client, string nome, string slug)
	{
		var token = await TokenAntiFalsificacaoAsync(client, "/Setores/Create");

		var resposta = await client.PostAsync("/Setores/Create", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", nome),
			new KeyValuePair<string, string>("Slug", slug),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "o cadastro de setor redireciona para os detalhes");
	}

	private static async Task PublicarAsync(HttpClient client, string slug, string titulo, byte[] conteudo)
	{
		var token = await TokenAntiFalsificacaoAsync(client, $"/setor/{slug}/documentos");

		using var arquivo = new ByteArrayContent(conteudo);
		using var form = new MultipartFormDataContent
		{
			{ new StringContent(titulo), "Form.Titulo" },
			{ new StringContent("0"), "Form.Visibilidade" },
			{ new StringContent(token), "__RequestVerificationToken" },
			{ arquivo, "arquivo", "politica-interna.pdf" },
		};

		var resposta = await client.PostAsync($"/setor/{slug}/documentos", form);

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "a publicação redireciona de volta para a aba");
	}

	[Fact]
	public async Task PublicarEBaixar_DevolveOsBytesOriginais()
	{
		var client = CriarCliente();
		var slug = $"setor-{Guid.NewGuid():N}"[..20];

		await CriarSetorAsync(client, "Setor de Teste", slug);
		await PublicarAsync(client, slug, "Política interna", ConteudoPdf);

		var pagina = await client.GetStringAsync($"/setor/{slug}/documentos");
		var link = Regex.Match(pagina, @"/[Dd]ocumentos/([0-9a-fA-F-]{36})/[Dd]ownload");

		link.Success.Should().BeTrue("o documento publicado precisa aparecer na listagem com link de download");

		var download = await client.GetAsync(link.Value);

		download.StatusCode.Should().Be(HttpStatusCode.OK);
		download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
		download.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).Should().BeTrue();
		nosniff!.Should().Contain("nosniff");
		(await download.Content.ReadAsByteArrayAsync()).Should().Equal(ConteudoPdf);
	}

	[Fact]
	public async Task Publicar_GravaOArquivoCifradoNoDisco()
	{
		var client = CriarCliente();
		var slug = $"setor-{Guid.NewGuid():N}"[..20];

		await CriarSetorAsync(client, "Setor de Teste", slug);
		await PublicarAsync(client, slug, "Política interna", ConteudoPdf);

		var gravados = Directory.GetFiles(_raiz, "*", SearchOption.AllDirectories);

		gravados.Should().ContainSingle("cada publicação grava exatamente um arquivo");

		var bytes = await File.ReadAllBytesAsync(gravados[0]);
		var texto = Encoding.UTF8.GetString(bytes);

		texto.Should().NotContain("marcador-secreto-do-teste-de-documento", "o conteúdo em repouso precisa estar cifrado");
		texto.Should().NotStartWith("%PDF", "nem a assinatura do formato pode vazar");
		texto.Should().StartWith("SCCOFILE", "o cabeçalho do formato cifrado identifica o arquivo");
	}

	[Fact]
	public async Task Arquivo_NaoEhServidoComoConteudoEstatico()
	{
		var client = CriarCliente();
		var slug = $"setor-{Guid.NewGuid():N}"[..20];

		await CriarSetorAsync(client, "Setor de Teste", slug);
		await PublicarAsync(client, slug, "Política interna", ConteudoPdf);

		var gravado = Directory.GetFiles(_raiz, "*", SearchOption.AllDirectories).Single();
		var relativo = Path.GetRelativePath(_raiz, gravado).Replace(Path.DirectorySeparatorChar, '/');

		// O mesmo endereço que o banco guarda, tentado direto: precisa não existir como rota.
		foreach (var tentativa in new[] { $"/{relativo}", $"/documentos/{relativo}", $"/_content/{relativo}" })
		{
			var resposta = await client.GetAsync(tentativa);

			resposta.StatusCode.Should().Be(
				HttpStatusCode.NotFound,
				$"o arquivo não pode ser alcançável por {tentativa} — a visibilidade só é avaliada no endpoint de download");
		}
	}

	[Fact]
	public async Task Arquivar_PelaRota_TiraODocumentoDaListagem()
	{
		var client = CriarCliente();
		var slug = $"setor-{Guid.NewGuid():N}"[..20];

		await CriarSetorAsync(client, "Setor de Teste", slug);
		await PublicarAsync(client, slug, "Política interna", ConteudoPdf);

		var pagina = await client.GetStringAsync($"/setor/{slug}/documentos");
		var id = Regex.Match(pagina, @"/[Dd]ocumentos/([0-9a-fA-F-]{36})/[Dd]ownload").Groups[1].Value;

		id.Should().NotBeEmpty();

		var token = await TokenAntiFalsificacaoAsync(client, $"/setor/{slug}/documentos");

		var resposta = await client.PostAsync(
			$"/setor/{slug}/documentos/{id}/arquivar",
			new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token)]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "arquivar redireciona de volta para a aba");

		var depois = await client.GetStringAsync($"/setor/{slug}/documentos");

		depois.Should().NotContain(id, "o documento arquivado sai da listagem");
		depois.Should().Contain("sc-empty", "sem documentos ativos, a aba mostra o estado vazio");

		var download = await client.GetAsync($"/documentos/{id}/download");

		download.StatusCode.Should().Be(HttpStatusCode.NotFound, "arquivar tira de circulação, não só da listagem");
	}

	[Fact]
	public async Task Download_DeIdentificadorInexistente_Retorna404()
	{
		var resposta = await CriarCliente().GetAsync($"/documentos/{Guid.NewGuid()}/download");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
}
