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
/// A tela de administração da árvore (SetoresController.Menu) de ponta a ponta: o formulário
/// real chega ao handler e o item aparece na página do setor. Os handlers têm testes próprios;
/// aqui o que se prova é a ligação — nomes de campo, enum por nome, rotas e redirecionamentos.
/// </summary>
public class SetorMenuAdministracaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		return client;
	}

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private async Task<(Guid SetorId, string Slug, Guid RaizId)> CriarSetorAsync()
	{
		var slug = $"adm-{Guid.NewGuid():N}"[..20];

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criado = await escopo.ServiceProvider
			.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));

		criado.IsSuccess.Should().BeTrue();

		var arvore = await escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>().ListarPorSetorAsync(criado.Value.Id);

		return (criado.Value.Id, slug, arvore.Single(item => item.Tipo == TipoDeItemMenu.Setor).Id);
	}

	[Fact]
	public async Task TelaDoMenu_MostraAArvore_ENaoOfereceTipoEmbutidoQueJaExiste()
	{
		var (setorId, _, _) = await CriarSetorAsync();

		var html = await CriarCliente().GetStringAsync($"/Setores/Menu/{setorId}");

		html.Should().Contain("Documentos").And.Contain("Avisos");
		html.Should().Contain("<option value=\"Personalizado\">");
		html.Should().NotContain("<option value=\"Documentos\">", "o setor já tem Documentos — um segundo seria recusado");
		html.Should().NotContain("<option value=\"Avisos\">");
	}

	[Fact]
	public async Task CriarItemPersonalizado_PeloFormulario_AparecenaPaginaDoSetor()
	{
		var (setorId, slug, raizId) = await CriarSetorAsync();
		var client = CriarCliente();
		var token = await TokenAsync(client, $"/Setores/Menu/{setorId}");

		var resposta = await client.PostAsync("/Setores/CriarItemDeMenu", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("setorId", setorId.ToString()),
			new KeyValuePair<string, string>("parentId", raizId.ToString()),
			new KeyValuePair<string, string>("nome", "Relatórios"),
			new KeyValuePair<string, string>("slug", "relatorios"),
			new KeyValuePair<string, string>("tipo", "Personalizado"),
			new KeyValuePair<string, string>("icone", "bi-graph-up"),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/Setores/Menu/{setorId}");

		// Sem rota, o item personalizado abre a página "sem conteúdo ainda".
		var pagina = WebUtility.HtmlDecode(await client.GetStringAsync($"/setor/{slug}/relatorios"));
		pagina.Should().Contain("Sem conteúdo ainda");

		// E entra na barra de abas dos irmãos.
		var documentos = await client.GetStringAsync($"/setor/{slug}/documentos");
		documentos.Should().Contain($"href=\"/setor/{slug}/relatorios\"");
	}

	[Fact]
	public async Task DesativarPeloFormulario_TiraOItemDaPaginaDoSetor()
	{
		var (setorId, slug, _) = await CriarSetorAsync();
		var client = CriarCliente();
		Guid documentosId;

		using (var escopo = factory.Services.CreateScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
			var arvore = await escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>().ListarPorSetorAsync(setorId);
			documentosId = arvore.Single(item => item.Tipo == TipoDeItemMenu.Documentos).Id;
		}

		var token = await TokenAsync(client, $"/Setores/Menu/{setorId}");

		await client.PostAsync("/Setores/AlternarItemDeMenu", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("setorId", setorId.ToString()),
			new KeyValuePair<string, string>("itemId", documentosId.ToString()),
			new KeyValuePair<string, string>("ativar", "false"),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		(await client.GetAsync($"/setor/{slug}/documentos")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task DetalheDoSetor_TemLinkParaOMenu()
	{
		var (setorId, _, _) = await CriarSetorAsync();

		var html = await CriarCliente().GetStringAsync($"/Setores/Details/{setorId}");

		html.Should().Contain($"href=\"/Setores/Menu/{setorId}\"");
	}
}
