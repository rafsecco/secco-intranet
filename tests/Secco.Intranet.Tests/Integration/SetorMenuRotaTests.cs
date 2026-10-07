using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A página do setor mora na raiz: /{setor}/{item}/…, sem prefixo, sem abas e sem
/// redirecionamento — quem navega pela árvore é o menu principal. Cada teste cria o próprio
/// setor com slug sufixado por GUID — não existe setor fixo na fixture.
/// </summary>
/// <remarks>
/// PodeLerAsync e PodePublicarAsync só abrem sem a permissão de verdade em Development sem
/// SecureGate configurado (ADR-0020) — Testing passa pelo caminho de permissão real, por isso
/// os testes que leem uma página real de setor usam <c>intranet-admin</c> em
/// <see cref="CriarCliente"/>. O "usuário sem setor-{slug}:read recebe 404" propriamente dito
/// está coberto em <see cref="MenuVisibilidadeDeSetorTests"/>; a permissão em si, em
/// PermissoesDeSetorTests.
/// </remarks>
public class SetorMenuRotaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

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

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private async Task<string> CriarSetorAsync(bool habilitarDocumentos = true, bool habilitarAvisos = true)
	{
		var slug = $"menu-{Guid.NewGuid():N}"[..20];

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criado = await escopo.ServiceProvider
			.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand(
				$"Setor {slug}", slug, HabilitarDocumentos: habilitarDocumentos, HabilitarAvisos: habilitarAvisos));

		criado.IsSuccess.Should().BeTrue();

		return slug;
	}

	private async Task DesativarAsync(string slug, TipoDeItemMenu tipo)
	{
		await using var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var setor = await escopo.ServiceProvider.GetRequiredService<GetSetorBySlugHandler>().HandleAsync(slug);
		var arvore = await escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>().ListarPorSetorAsync(setor.Value.Id);
		var item = arvore.Single(i => i.Tipo == tipo);

		(await escopo.ServiceProvider.GetRequiredService<AtivarDesativarItemMenuHandler>()
			.HandleAsync(item.Id, ativar: false)).IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task Documentos_AbreNaRaiz_SemAbas()
	{
		var slug = await CriarSetorAsync();

		var html = await CriarCliente("intranet-admin").GetStringAsync($"/{slug}/documentos");

		html.Should().Contain($"action=\"/{slug}/documentos\"", "o formulário de publicação posta na rota nova");
		// A ausência das abas é garantida pela remoção da partial (grep do Step 9): checar
		// href aqui quebraria na Task 5, quando o próprio menu passa a ter o link de Avisos.
	}

	[Fact]
	public async Task RaizDoSetor_Da404_PorqueSoAgrupa()
	{
		var slug = await CriarSetorAsync();

		(await CriarCliente("intranet-admin").GetAsync($"/{slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task PrefixoAntigo_Da404()
	{
		var slug = await CriarSetorAsync();

		(await CriarCliente().GetAsync($"/setor/{slug}/documentos")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task ItemDesativado_Da404()
	{
		var slug = await CriarSetorAsync();
		await DesativarAsync(slug, TipoDeItemMenu.Documentos);

		(await CriarCliente("intranet-admin").GetAsync($"/{slug}/documentos")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task SegmentoSemMatchNenhum_Da404()
	{
		var slug = await CriarSetorAsync();

		(await CriarCliente("intranet-admin").GetAsync($"/{slug}/caminho-que-nao-existe")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Theory]
	[InlineData("/nao-existe-setor-assim")]
	[InlineData("/nao-existe/nem/isto")]
	[InlineData("/favicon-inexistente.ico")]
	public async Task CaminhoDesconhecidoNaRaiz_Da404_NaoErro(string caminho) =>
		(await CriarCliente().GetAsync(caminho)).StatusCode.Should().Be(HttpStatusCode.NotFound);

	[Theory]
	[InlineData("/Setores")]
	[InlineData("/Acesso")]
	[InlineData("/diretorio")]
	[InlineData("/inventario")]
	[InlineData("/health/live")]
	public async Task RotaFixa_ContinuaVencendoOSetor(string caminho)
	{
		var resposta = await CriarCliente("intranet-admin").GetAsync(caminho);

		// 200, não só "não 404": um 500 ou um redirecionamento para o setor também seria falha.
		resposta.StatusCode.Should().Be(HttpStatusCode.OK, $"{caminho} é rota do produto, não setor");
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().BeEquivalentTo(caminho);
	}

	[Fact]
	public async Task Documentos_TrazOTrilhoDoSetorNoCabecalho()
	{
		var slug = await CriarSetorAsync();

		var html = await CriarCliente("intranet-admin").GetStringAsync($"/{slug}/documentos");

		html.Should().Contain($"Setor {slug}", "o subtítulo mostra de que setor é a página");
	}

	[Fact]
	public async Task RecursoDesligado_RecusaPost_MesmoComTokenValido()
	{
		var slug = await CriarSetorAsync();
		await DesativarAsync(slug, TipoDeItemMenu.Avisos);
		var client = CriarCliente("intranet-admin");
		// O token antifalsificação não é por ação: o da página de Documentos (ainda ligada) vale.
		var token = await TokenAsync(client, $"/{slug}/documentos");

		var resposta = await client.PostAsync($"/{slug}/avisos", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Form.Titulo", "Não deveria entrar"),
			new KeyValuePair<string, string>("Form.Corpo", "Avisos está desligado neste setor."),
			new KeyValuePair<string, string>("Form.Tipo", "0"),
			new KeyValuePair<string, string>("Form.Visibilidade", "1"),
			new KeyValuePair<string, string>("Form.Prioridade", "0"),
			new KeyValuePair<string, string>("Form.PublicadoEm", DateTime.Now.ToString("yyyy-MM-ddTHH:mm")),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound, "desligar o recurso fecha a escrita também");
	}

	[Theory]
	[InlineData("/nao-existe-setor-assim")]
	[InlineData("/robots.txt")]
	public async Task CaminhoDesconhecido_SemTenant_RecusaSemErroDeServidor(string caminho)
	{
		// A página do setor mora na raiz: todo caminho desconhecido chega ao SetorController,
		// inclusive numa requisição sem tenant resolvido — que não tem banco a consultar. A
		// tenancy do SDK recusa com 4xx antes de qualquer consulta; o que não pode é virar 500.
		var resposta = await factory.CreateClient().GetAsync(caminho);

		((int)resposta.StatusCode).Should().BeInRange(400, 499);
	}

	[Fact]
	public async Task PersonalizadoComFilhos_SoAgrupa_EOFilhoComRotaRedireciona()
	{
		var slug = await CriarSetorAsync();

		await using (var escopo = factory.Services.CreateAsyncScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
			var setor = await escopo.ServiceProvider.GetRequiredService<GetSetorBySlugHandler>().HandleAsync(slug);
			var arvore = await escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>().ListarPorSetorAsync(setor.Value.Id);
			var criar = escopo.ServiceProvider.GetRequiredService<CriarItemMenuHandler>();

			var relatorios = await criar.HandleAsync(new CriarItemMenuCommand(
				setor.Value.Id, arvore.Single(i => i.Tipo == TipoDeItemMenu.Setor).Id, "Relatórios", "relatorios",
				TipoDeItemMenu.Personalizado, null, null));
			(await criar.HandleAsync(new CriarItemMenuCommand(
				setor.Value.Id, relatorios.Value.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/diretorio", null)))
				.IsSuccess.Should().BeTrue();
		}

		var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		(await client.GetAsync($"/{slug}/relatorios")).StatusCode.Should().Be(HttpStatusCode.NotFound, "Relatórios só agrupa");

		var vendas = await client.GetAsync($"/{slug}/relatorios/vendas");
		vendas.StatusCode.Should().Be(HttpStatusCode.Redirect);
		vendas.Headers.Location!.OriginalString.Should().Be("/diretorio");

		// No menu, Relatórios é agrupador (botão) e Vendas, debaixo dele, é link direto para a rota.
		var menu = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
		Regex.IsMatch(menu, @"<button[^>]*data-sc-submenu[^>]*>\s*<span class=""sc-nav__label"">Relatórios</span>")
			.Should().BeTrue();
		Regex.IsMatch(menu, @"<a[^>]*href=""/diretorio""[^>]*>\s*<span class=""sc-nav__label"">Vendas</span>")
			.Should().BeTrue();
	}
}
