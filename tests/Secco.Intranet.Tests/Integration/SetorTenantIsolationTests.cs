using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Setores;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Prova o isolamento de tenant (ADR-0005) resolvendo os handlers reais em escopos de DI
/// criados manualmente, com o tenant fixado via <see cref="TenantScopeExtensions.SetTenant"/>
/// — o mesmo mecanismo que o SDK oferece para workers fora do pipeline HTTP, usado aqui
/// para exercitar a Application/Infrastructure sem precisar de uma rodada HTTP completa.
/// </summary>
public class SetorTenantIsolationTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureTenantDatabasesMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task GetSetorBySlug_FromAnotherTenant_ReturnsNotFoundBecauseDatabasesAreIsolated()
	{
		using var scopeAlfa = factory.Services.CreateScope();
		scopeAlfa.ServiceProvider.SetTenant(factory.TenantAlfa);
		var createHandler = scopeAlfa.ServiceProvider.GetRequiredService<CreateSetorHandler>();

		var created = await createHandler.HandleAsync(new CreateSetorCommand("Segredo do Alfa", "segredo-alfa"));
		created.IsSuccess.Should().BeTrue();

		using var scopeBeta = factory.Services.CreateScope();
		scopeBeta.ServiceProvider.SetTenant(factory.TenantBeta);
		var getByIdHandlerBeta = scopeBeta.ServiceProvider.GetRequiredService<GetSetorByIdHandler>();

		var fetched = await getByIdHandlerBeta.HandleAsync(created.Value.Id);

		fetched.IsFailure.Should().BeTrue("cada tenant possui banco próprio (ADR-0005)");
		fetched.Error.Should().Be(IntranetErrors.Setores.NotFound);
	}

	[Fact]
	public async Task CreateSetor_WithDuplicateSlugInSameTenant_ReturnsConflict()
	{
		using var scope = factory.Services.CreateScope();
		scope.ServiceProvider.SetTenant(factory.TenantAlfa);
		var createHandler = scope.ServiceProvider.GetRequiredService<CreateSetorHandler>();

		await createHandler.HandleAsync(new CreateSetorCommand("RH", "rh-duplicado-web"));
		var result = await createHandler.HandleAsync(new CreateSetorCommand("RH 2", "rh-duplicado-web"));

		result.IsFailure.Should().BeTrue();
		result.Error.Type.Should().Be(ErrorType.Conflict);
	}

	[Fact]
	public async Task CreateSetor_ThenSearchInSameTenant_FindsIt()
	{
		using var scope = factory.Services.CreateScope();
		scope.ServiceProvider.SetTenant(factory.TenantBeta);
		var createHandler = scope.ServiceProvider.GetRequiredService<CreateSetorHandler>();
		var searchHandler = scope.ServiceProvider.GetRequiredService<SearchSetoresHandler>();
		var marker = Guid.NewGuid().ToString("N")[..12];

		await createHandler.HandleAsync(new CreateSetorCommand($"Alvo {marker}", $"alvo-{marker}"));

		var result = await searchHandler.HandleAsync(new SetorSearchCriteria(NomeContains: marker));

		result.IsSuccess.Should().BeTrue();
		result.Value.TotalCount.Should().Be(1);
		result.Value.Items[0].Nome.Should().Contain(marker);
	}
}
