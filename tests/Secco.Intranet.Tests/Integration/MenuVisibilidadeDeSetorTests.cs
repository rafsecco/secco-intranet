using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O menu (<see cref="Secco.Intranet.Web.ViewComponents.NavigationViewComponent"/>) filtra
/// setores por permissão (ADR-0021), não mais por nome de Role — a lógica que
/// <c>SetorAcesso.Visiveis</c> continha antes de sair do código. O ramo restritivo roda sempre
/// em Testing (que não configura SecureGate, mas não é Development — ver
/// <c>AcessoAdministrativo.ModoAbertoDeDev</c>, ADR-0020), por isso esta classe prova os dois
/// lados por HTTP de verdade: sem permissão o setor não aparece, com ela aparece. A permissão em
/// si (<c>IPermissoesDeSetor.SlugsComPermissaoAsync</c>) está coberta a fundo em
/// <see cref="PermissoesDeSetorTests"/>; o que esta classe prova é a chamada dela pelo
/// controller/menu sob o caminho HTTP real.
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

	private HttpClient CriarCliente(params string[] roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	[Fact]
	public async Task SemPermissaoDeLeitura_OSetorNaoAparece()
	{
		var slug = $"menu-{Guid.NewGuid():N}"[..20];
		await CriarSetorAsync(slug);

		// Sem X-Test-Roles: nenhuma role — a permissão real nega por padrão (ADR-0020), e o
		// menu some com o setor em vez de listar sem controle, mesmo sem SecureGate configurado.
		var html = await CriarCliente().GetStringAsync("/");

		html.Should().NotContain($"href=\"/{slug}/avisos\"", "sem permissão de leitura o setor não deveria aparecer no menu");
	}

	[Fact]
	public async Task ComPermissaoDeLeitura_MostraOSetorComoAgrupador_ComOsItensDaArvore()
	{
		var slug = $"menu-{Guid.NewGuid():N}"[..20];
		await CriarSetorAsync(slug);

		// intranet-admin: SomenteIntranetAdmin curto-circuita PermissoesDeSetor para "tudo" (é o
		// superusuário da instalação, ADR-0008) — não precisa de um IPermissionResolver dublê só
		// para este teste, e é a mesma convenção usada em SetorMenuRotaTests/DocumentoFluxoTests.
		var html = await CriarCliente("intranet-admin").GetStringAsync("/");

		AssertarArvore(html, slug);
	}

	[Fact]
	public async Task TemaHorizontal_RenderizaAMesmaArvore()
	{
		var slug = $"menu-{Guid.NewGuid():N}"[..20];
		await CriarSetorAsync(slug);

		var client = factory
			.WithWebHostBuilder(builder => builder.UseSetting("Intranet:Theme:Nome", "Horizontal"))
			.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		var html = await client.GetStringAsync("/");

		html.Should().Contain("sc-topnav", "garante que o tema trocou de fato");
		AssertarArvore(html, slug);

		// Na barra superior não cabe uma fileira por setor: cada grupo com título vira um botão
		// expansível, e os setores ficam um nível abaixo dele.
		Regex.IsMatch(html, @"<button[^>]*data-sc-submenu[^>]*>(?:(?!</button>).)*>Setores<", RegexOptions.Singleline)
			.Should().BeTrue("o grupo Setores é um botão expansível");
		html.Should().NotContain("sc-nav__group-title", "o título do grupo deixou de ser um rótulo solto na barra");
	}

	private static void AssertarArvore(string html, string slug)
	{
		html.Should().Contain($"href=\"/{slug}/avisos\"").And.Contain($"href=\"/{slug}/documentos\"");
		html.Should().NotContain($"href=\"/{slug}\"", "o setor não é link");
		Regex.IsMatch(html, $"<button[^>]*data-sc-submenu[^>]*aria-expanded=\"false\"[^>]*>(?:(?!</button>).)*Setor {slug}",
			RegexOptions.Singleline).Should().BeTrue("o setor é um botão expansível com o nome dele");
	}
}
