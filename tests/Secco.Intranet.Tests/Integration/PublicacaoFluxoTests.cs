using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Fluxo real de publicação sobre o host completo: publicar aparece no mural, agendar não
/// aparece, arquivar sai.
/// </summary>
public class PublicacaoFluxoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		return client;
	}

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private static async Task CriarSetorAsync(HttpClient client, string slug)
	{
		var token = await TokenAsync(client, "/Setores/Create");

		var resposta = await client.PostAsync("/Setores/Create", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", $"Setor {slug}"),
			new KeyValuePair<string, string>("Slug", slug),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	private static async Task PublicarAsync(HttpClient client, string slug, string titulo, DateTime publicadoEm)
	{
		var token = await TokenAsync(client, $"/setor/{slug}/avisos");

		var resposta = await client.PostAsync($"/setor/{slug}/avisos", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Form.Titulo", titulo),
			new KeyValuePair<string, string>("Form.Corpo", "Corpo em **markdown**."),
			new KeyValuePair<string, string>("Form.Tipo", "0"),
			new KeyValuePair<string, string>("Form.Visibilidade", "1"),
			new KeyValuePair<string, string>("Form.Prioridade", "0"),
			new KeyValuePair<string, string>("Form.PublicadoEm", publicadoEm.ToString("yyyy-MM-ddTHH:mm")),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "salvar redireciona de volta para a aba");
	}

	[Fact]
	public async Task Publicada_ApareceNoMuralComOCorpoRenderizado()
	{
		var client = CriarCliente();
		var slug = $"avisos-{Guid.NewGuid():N}"[..20];
		var titulo = $"Comunicado {Guid.NewGuid():N}"[..24];

		await CriarSetorAsync(client, slug);
		await PublicarAsync(client, slug, titulo, DateTime.Now.AddMinutes(-5));

		var mural = await client.GetStringAsync("/");

		mural.Should().Contain(titulo);
		mural.Should().Contain("<strong>markdown</strong>", "o corpo é renderizado a partir do Markdown");
	}

	[Fact]
	public async Task Agendada_NaoApareceNoMuralMasApareceNaAbaDoSetor()
	{
		var client = CriarCliente();
		var slug = $"avisos-{Guid.NewGuid():N}"[..20];
		var titulo = $"Agendado {Guid.NewGuid():N}"[..24];

		await CriarSetorAsync(client, slug);
		await PublicarAsync(client, slug, titulo, DateTime.Now.AddDays(1));

		(await client.GetStringAsync("/")).Should().NotContain(titulo);

		var aba = await client.GetStringAsync($"/setor/{slug}/avisos");
		aba.Should().Contain(titulo, "quem administra precisa ver a agendada para poder editá-la");
		aba.Should().Contain("Agendada");
	}

	[Fact]
	public async Task Arquivada_SaiDoMural()
	{
		var client = CriarCliente();
		var slug = $"avisos-{Guid.NewGuid():N}"[..20];
		var titulo = $"Arquivar {Guid.NewGuid():N}"[..24];

		await CriarSetorAsync(client, slug);
		await PublicarAsync(client, slug, titulo, DateTime.Now.AddMinutes(-5));

		var aba = await client.GetStringAsync($"/setor/{slug}/avisos");
		var id = Regex.Match(aba, @"/avisos/([0-9a-fA-F-]{36})/arquivar").Groups[1].Value;
		id.Should().NotBeEmpty();

		var token = await TokenAsync(client, $"/setor/{slug}/avisos");
		var resposta = await client.PostAsync(
			$"/setor/{slug}/avisos/{id}/arquivar",
			new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token)]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await client.GetStringAsync("/")).Should().NotContain(titulo);
	}
}
