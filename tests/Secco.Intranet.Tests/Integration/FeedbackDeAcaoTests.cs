using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O aviso de "salvo" saiu das views de página e passou a vir do layout, pelo tema. O risco
/// dessa mudança é silencioso: a página continua abrindo, só que sem confirmar nada. Estes
/// testes exercitam o caminho inteiro — POST, redirect e HTML da página de destino.
/// </summary>
public class FeedbackDeAcaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
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

	private static async Task<string> CriarSetorAsync(HttpClient client, string slug)
	{
		var token = await TokenAsync(client, "/Setores/Create");

		var resposta = await client.PostAsync("/Setores/Create", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", $"Setor {slug}"),
			new KeyValuePair<string, string>("Slug", slug),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		// O redirect leva aos detalhes, e o id do setor novo está no caminho.
		var id = resposta.RequestMessage?.RequestUri?.Segments[^1].Trim('/');
		id.Should().NotBeNullOrEmpty();

		return id!;
	}

	[Fact]
	public async Task SalvarSetor_ConfirmaNaPaginaDeDestino()
	{
		var client = CriarCliente();
		var slug = $"toast-{Guid.NewGuid():N}"[..20];
		var id = await CriarSetorAsync(client, slug);

		var token = await TokenAsync(client, $"/Setores/Edit/{id}");

		var resposta = await client.PostAsync($"/Setores/Edit/{id}", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Id", id),
			new KeyValuePair<string, string>("Nome", "Setor Renomeado"),
			new KeyValuePair<string, string>("Icone", "bi-diagram-3"),
			new KeyValuePair<string, string>("Ativo", "true"),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "salvar redireciona para os detalhes");

		var html = await resposta.Content.ReadAsStringAsync();

		html.Should().Contain("data-sc-toast", "o feedback agora é markup do tema, não da view de página");
		html.Should().Contain("Setor &quot;Setor Renomeado&quot; salvo.");
	}

	[Fact]
	public async Task SemAcao_NenhumAvisoNaPagina()
	{
		var html = await CriarCliente().GetStringAsync("/Setores");

		html.Should().NotContain(
			"data-sc-toast", "o componente está no layout de todas as páginas e precisa ficar calado sem mensagem");
	}
}
