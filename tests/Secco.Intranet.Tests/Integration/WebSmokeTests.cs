using System.Net;
using AwesomeAssertions;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Smoke tests do host <c>Secco.Intranet.Web</c> (ADR-0012): sobem o pipeline real ponta a
/// ponta (roteamento, tenancy, health checks) via <see cref="IntranetWebFactory"/>.
/// </summary>
public class WebSmokeTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureTenantDatabasesMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task GetHealthLive_Always_Returns200()
	{
		var response = await factory.CreateClient().GetAsync("/health/live");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task GetHome_Always_Returns200()
	{
		var response = await factory.CreateClient().GetAsync("/");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task GetSetores_WithTenantHeader_Returns200()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		var response = await client.GetAsync("/Setores");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}
}
