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
/// A árvore de itens de menu substitui as duas abas fixas de /setor/{slug}. Cada teste cria o
/// próprio setor com slug sufixado por GUID — não existe setor fixo na fixture.
/// </summary>
/// <remarks>
/// Sem teste de "usuário sem setor-{slug}:read recebe 404": PodeLerAsync tem o mesmo bypass de
/// PodePublicarAsync (sem autenticação configurada, libera), e o ambiente Testing nunca liga a
/// autenticação — o ramo restritivo não é alcançável por HTTP aqui. Mesma limitação registrada
/// em MenuVisibilidadeDeSetorTests; a permissão em si está coberta em PermissoesDeSetorTests.
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
	public async Task CaminhoVazio_RedirecionaParaOPrimeiroFilhoAtivo()
	{
		var slug = await CriarSetorAsync();

		var resposta = await CriarCliente().GetAsync($"/setor/{slug}");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/setor/{slug}/avisos",
			"Avisos vem antes de Documentos em ordem alfabética (Task 7)");
	}

	[Fact]
	public async Task Documentos_ContinuaNaMesmaUrl_ComAbaDinamica()
	{
		var slug = await CriarSetorAsync();

		var html = await CriarCliente().GetStringAsync($"/setor/{slug}/documentos");

		html.Should().Contain($"href=\"/setor/{slug}/avisos\"", "a aba de Avisos vem da árvore, não mais de um link fixo");
	}

	[Fact]
	public async Task SetorSemNenhumItemAtivo_MostraOEstadoVazio()
	{
		var slug = await CriarSetorAsync(habilitarDocumentos: false, habilitarAvisos: false);

		var resposta = await CriarCliente().GetAsync($"/setor/{slug}");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("Nenhum item ainda");
	}

	[Fact]
	public async Task ItemDesativado_Da404_NoCaminhoQueAntesFuncionava()
	{
		var slug = await CriarSetorAsync();
		await DesativarAsync(slug, TipoDeItemMenu.Documentos);

		var resposta = await CriarCliente().GetAsync($"/setor/{slug}/documentos");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task ItemDesativado_SaiDaBarraDeAbasDosIrmaos()
	{
		var slug = await CriarSetorAsync();
		await DesativarAsync(slug, TipoDeItemMenu.Documentos);

		var html = await CriarCliente().GetStringAsync($"/setor/{slug}/avisos");

		html.Should().NotContain($"href=\"/setor/{slug}/documentos\"");
	}

	[Fact]
	public async Task RecursoDesligado_RecusaPost_MesmoComTokenValido()
	{
		var slug = await CriarSetorAsync();
		await DesativarAsync(slug, TipoDeItemMenu.Avisos);
		var client = CriarCliente();
		// O token antifalsificação não é por ação: o da página de Documentos (ainda ligada) vale.
		var token = await TokenAsync(client, $"/setor/{slug}/documentos");

		var resposta = await client.PostAsync($"/setor/{slug}/avisos", new FormUrlEncodedContent(
		[
			// Mesmo conjunto de PublicacaoFluxoTests.PublicarAsync: com Avisos ligado este POST
			// publicaria — o 404 só pode vir do recurso desligado.
			new KeyValuePair<string, string>("Form.Titulo", "Não deveria entrar"),
			new KeyValuePair<string, string>("Form.Corpo", "Avisos está desligado neste setor."),
			new KeyValuePair<string, string>("Form.Tipo", "0"),
			new KeyValuePair<string, string>("Form.Visibilidade", "1"),
			new KeyValuePair<string, string>("Form.Prioridade", "0"),
			new KeyValuePair<string, string>("Form.PublicadoEm", DateTime.Now.ToString("yyyy-MM-ddTHH:mm")),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound,
			"desligar o recurso precisa fechar a escrita também, não só esconder a aba");
	}

	[Fact]
	public async Task SegmentoSemMatchNenhum_Da404()
	{
		var slug = await CriarSetorAsync();

		var resposta = await CriarCliente().GetAsync($"/setor/{slug}/caminho-que-nao-existe");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
}
