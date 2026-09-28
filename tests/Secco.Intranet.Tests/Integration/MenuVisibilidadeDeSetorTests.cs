using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Setores;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O menu (<see cref="Secco.Intranet.Web.ViewComponents.NavigationViewComponent"/>) passou a
/// filtrar setores por permissão (ADR-0021), não mais por nome de Role — a lógica que
/// <c>SetorAcesso.Visiveis</c> continha antes de sair do código. O ramo restritivo (autenticação
/// configurada) depende de <c>IsConfigured</c>, que esta fábrica de testes nunca liga — coberto
/// pelos testes de <see cref="PermissoesDeSetorTests"/> (a permissão em si) e não repetido aqui;
/// o que esta classe prova é o outro ramo, que só é alcançável por HTTP de verdade.
/// </summary>
public class MenuVisibilidadeDeSetorTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<string> CriarSetorAsync(string slug)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criado = await escopo.ServiceProvider
			.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));

		criado.IsSuccess.Should().BeTrue();

		return slug;
	}

	[Fact]
	public async Task ModoAberto_MostraTodoSetorAtivo_SemFiltrarPorRole()
	{
		var slug = $"menu-{Guid.NewGuid():N}"[..20];
		await CriarSetorAsync(slug);

		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		// Sem X-Test-Roles: nenhuma role — se o menu ainda filtrasse por role, não veria nada.

		var html = await client.GetStringAsync("/");

		html.Should().Contain(
			$"href=\"/setor/{slug}\"",
			"sem SecureGate configurado o ambiente Testing mostra todo setor ativo, do mesmo jeito que o modo aberto de DEV");
	}

	// Tentei, na auto-revisão final, escrever aqui um teste indo e voltando (com e sem a
	// permissão de escrita) para PodePublicarAsync do SetorController — e descobri que não dá:
	// PodePublicarAsync começa com "!IsConfigured(configuration) → libera", exatamente o mesmo
	// bypass do menu acima, e IsConfigured nunca é true neste ambiente. O caminho de permissão
	// de verdade (a parte nova desta spec) fica estruturalmente inalcançável por HTTP aqui — não
	// é uma regressão desta migração, é a mesma limitação que já existia para
	// ExigirVinculo/ExigirVisibilidade antes dela (por isso os testes de handler de Mural/
	// Documentos/Publicação sempre passaram `ExigirVinculo: true` explícito, em vez de subir o
	// host). A função por trás (`IPermissoesDeSetor.SlugsComPermissaoAsync`) está coberta a fundo
	// em `PermissoesDeSetorTests`; o que fica sem prova direta por HTTP é só a chamada dela pelo
	// controller sob essa condição — registrado para o revisor, não escondido atrás de um teste
	// que passaria de qualquer jeito.
}
