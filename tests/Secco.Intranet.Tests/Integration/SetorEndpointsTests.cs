using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class SetorEndpointsTests(IntranetApiFactory factory) : IClassFixture<IntranetApiFactory>, IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task InitializeAsync() => await factory.EnsureTenantDatabasesMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CreateClientForTenant(Guid tenantId)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", IntranetApiFactory.CreateToken(tenantId));
		return client;
	}

	[Fact]
	public async Task HealthEndpoints_Always_RespondAnonymously()
	{
		var client = factory.CreateClient();

		(await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
		(await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task PostSetor_WithoutToken_Returns401()
	{
		var response = await factory.CreateClient()
			.PostAsJsonAsync("/api/v1/setores", new { nome = "sem token", slug = "sem-token" });

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
			"a FallbackPolicy protege endpoints sem metadata explícita (ADR-0020)");
	}

	[Fact]
	public async Task PostSetor_WithValidToken_Returns201AndRoundTrips()
	{
		var client = CreateClientForTenant(factory.TenantAlfa);

		var response = await client.PostAsJsonAsync("/api/v1/setores", new
		{
			nome = "Financeiro",
			slug = "financeiro",
		});

		response.StatusCode.Should().Be(HttpStatusCode.Created);
		var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
		var id = created.GetProperty("id").GetGuid();

		var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/v1/setores/{id}", Json);
		fetched.GetProperty("nome").GetString().Should().Be("Financeiro");
		fetched.GetProperty("slug").GetString().Should().Be("financeiro");
	}

	[Fact]
	public async Task PostSetor_WithoutNome_Returns400ProblemDetails()
	{
		var client = CreateClientForTenant(factory.TenantAlfa);

		var response = await client.PostAsJsonAsync("/api/v1/setores", new { nome = "", slug = "vazio" });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await response.Content.ReadAsStringAsync()).Should().Contain("Intranet.Setor.NomeRequired");
	}

	[Fact]
	public async Task PostSetor_WithDuplicateSlug_Returns409ProblemDetails()
	{
		var client = CreateClientForTenant(factory.TenantAlfa);

		await client.PostAsJsonAsync("/api/v1/setores", new { nome = "RH", slug = "rh-duplicado" });
		var response = await client.PostAsJsonAsync("/api/v1/setores", new { nome = "RH 2", slug = "rh-duplicado" });

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task GetSetor_FromAnotherTenant_Returns404BecauseDatabasesAreIsolated()
	{
		var clientAlfa = CreateClientForTenant(factory.TenantAlfa);
		var clientBeta = CreateClientForTenant(factory.TenantBeta);

		var response = await clientAlfa.PostAsJsonAsync("/api/v1/setores", new { nome = "Segredo do Alfa", slug = "segredo-alfa" });
		var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

		(await clientBeta.GetAsync($"/api/v1/setores/{id}"))
			.StatusCode.Should().Be(HttpStatusCode.NotFound,
				"cada tenant possui banco próprio (ADR-0005)");
	}

	[Fact]
	public async Task SearchSetores_ByNome_ReturnsPagedMatches()
	{
		var client = CreateClientForTenant(factory.TenantBeta);
		var marker = Guid.NewGuid().ToString("N")[..12];

		await client.PostAsJsonAsync("/api/v1/setores", new { nome = $"Alvo {marker}", slug = $"alvo-{marker}" });
		await client.PostAsJsonAsync("/api/v1/setores", new { nome = "Outro Qualquer", slug = $"outro-{marker}" });

		var result = await client.GetFromJsonAsync<JsonElement>($"/api/v1/setores?nome={marker}", Json);

		result.GetProperty("totalCount").GetInt64().Should().Be(1);
		result.GetProperty("items")[0].GetProperty("nome").GetString().Should().Contain(marker);
	}
}
