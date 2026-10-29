using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Setores;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Página de uma "RCL externa" na raiz: o assembly de testes é application part do host
/// (<see cref="IntranetWebFactory"/>), então este controller entra nas rotas como entraria o de um
/// projeto referenciado.
/// </summary>
[AllowAnonymous]
public sealed class PaginaDeRclDeTesteController : Controller
{
	/// <summary>Caminho da página, cujo primeiro segmento precisa ficar reservado.</summary>
	public const string Slug = "pagina-de-rcl-de-teste";

	[HttpGet("/" + Slug)]
	public IActionResult Index() => Content("pagina da rcl de teste");
}

/// <summary>
/// O setor mora na raiz (/{slug}/…), então o primeiro segmento fixo de toda rota registrada fica
/// reservado — os do core e os de uma RCL referenciada, sem lista escrita à mão.
/// </summary>
public class SlugsReservadosTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private ISlugsReservados Reservados => factory.Services.GetRequiredService<ISlugsReservados>();

	[Theory]
	// Rota de atributo, rota convencional ({controller}) e health check. Se a leitura das rotas
	// quebrar e voltar vazia, a proteção some calada: estes garantem que ela enxerga as três.
	[InlineData("setores")]
	[InlineData("acesso")]
	[InlineData("menu")]
	[InlineData("home")]
	[InlineData("health")]
	public void RotasDoCore_SaoReservadas(string slug) => Reservados.Contem(slug).Should().BeTrue();

	[Theory]
	[InlineData("setor")]
	[InlineData("_content")]
	[InlineData("favicon.ico")]
	[InlineData("api")]
	[InlineData(".well-known")]
	[InlineData("signin-oidc")]
	[InlineData("signout-callback-oidc")]
	public void CaminhosForaDasRotas_ContinuamReservados(string slug) => Reservados.Contem(slug).Should().BeTrue();

	[Fact]
	public void SlugComum_NaoEReservado() => Reservados.Contem("financeiro").Should().BeFalse();

	[Fact]
	public void RotaDeRclReferenciada_FicaReservada()
	{
		Reservados.Contem(PaginaDeRclDeTesteController.Slug).Should().BeTrue();
		Reservados.Contem(" Pagina-De-Rcl-De-Teste ").Should().BeTrue("sem diferenciar caixa e ignorando espaços");
	}

	[Fact]
	public async Task RotaDeRclReferenciada_ChegaAoControllerDela()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		var html = await client.GetStringAsync("/" + PaginaDeRclDeTesteController.Slug);

		html.Should().Be("pagina da rcl de teste");
	}

	[Fact]
	public async Task CriarSetor_ComSlugDeRotaDeRcl_RecusaComoReservado()
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var resultado = await escopo.ServiceProvider
			.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand("Institucional", PaginaDeRclDeTesteController.Slug));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Code.Should().Be("Intranet.Setor.SlugReservado");
	}
}
