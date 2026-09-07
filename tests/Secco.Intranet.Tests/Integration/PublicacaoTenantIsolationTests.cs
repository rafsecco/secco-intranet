using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Isolamento de publicações entre tenants (ADR-0005), no padrão de
/// <see cref="DocumentoTenantIsolationTests"/>.
/// </summary>
public class PublicacaoTenantIsolationTests(IntranetWebFactory factory)
	: IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<Guid> PublicarNoAlfaAsync()
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var slug = $"pub-alfa-{Guid.NewGuid():N}"[..20];
		var criarSetor = escopo.ServiceProvider.GetRequiredService<CreateSetorHandler>();
		var setor = await criarSetor.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));
		setor.IsSuccess.Should().BeTrue();

		var repositorio = escopo.ServiceProvider.GetRequiredService<IPublicacaoRepository>();
		var publicacao = new Publicacao(
			setor.Value.Id, "Segredo do Alfa", "Corpo", TipoPublicacao.Aviso,
			// Visibilidade mais permissiva de propósito: assim o teste prova o isolamento do
			// BANCO, e não a regra de visibilidade, que tem testes próprios.
			Visibilidade.Empresa, PrioridadePublicacao.Normal,
			DateTimeOffset.UtcNow.AddHours(-1), null, "teste");

		await repositorio.AddAsync(publicacao);

		return publicacao.Id;
	}

	[Fact]
	public async Task PublicacaoDeUmTenant_NaoEhAlcancavelPeloOutro()
	{
		var id = await PublicarNoAlfaAsync();

		using var escopoBeta = factory.Services.CreateScope();
		escopoBeta.ServiceProvider.SetTenant(factory.TenantBeta);
		var repositorio = escopoBeta.ServiceProvider.GetRequiredService<IPublicacaoRepository>();

		(await repositorio.GetByIdAsync(id)).Should().BeNull(
			"cada tenant possui banco próprio (ADR-0005)");
	}

	[Fact]
	public async Task PublicacaoDeUmTenant_ContinuaAlcancavelNoProprioTenant()
	{
		var id = await PublicarNoAlfaAsync();

		using var escopoAlfa = factory.Services.CreateScope();
		escopoAlfa.ServiceProvider.SetTenant(factory.TenantAlfa);
		var repositorio = escopoAlfa.ServiceProvider.GetRequiredService<IPublicacaoRepository>();

		var encontrada = await repositorio.GetByIdAsync(id);

		encontrada.Should().NotBeNull(
			"sem esta metade, o teste acima passaria mesmo com o repositório quebrado");
		encontrada!.Publicacao.Titulo.Should().Be("Segredo do Alfa");
	}
}
