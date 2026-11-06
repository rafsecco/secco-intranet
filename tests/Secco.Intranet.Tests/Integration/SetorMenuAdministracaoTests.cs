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

		// Sem rota e sem filhos, o item só agrupa: não há página para ele.
		(await client.GetAsync($"/{slug}/relatorios")).StatusCode.Should().Be(HttpStatusCode.NotFound);

		// Excluir é irreversível: o formulário do item pede confirmação (theme.js).
		var menu = await client.GetStringAsync($"/Setores/Menu/{setorId}");
		var form = Regex.Match(menu, "<form[^>]*action=\"/Setores/ExcluirItemDeMenu\"[^>]*>");
		form.Success.Should().BeTrue();
		WebUtility.HtmlDecode(form.Value).Should().Contain("data-confirmar=").And.Contain("Relatórios");
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

		(await client.GetAsync($"/{slug}/documentos")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	private async Task<ItemMenu> ItemAsync(Guid setorId, TipoDeItemMenu tipo, string? slug = null)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var arvore = await escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>().ListarPorSetorAsync(setorId);

		return arvore.Single(item => item.Tipo == tipo && (slug is null || item.Slug == slug));
	}

	private static FormUrlEncodedContent Formulario(params (string Nome, string Valor)[] campos) =>
		new(campos.Select(campo => new KeyValuePair<string, string>(campo.Nome, campo.Valor)));

	[Fact]
	public async Task CriarItem_ComNivelDeEscrita_GravaONivel()
	{
		var (setorId, _, raizId) = await CriarSetorAsync();
		var client = CriarCliente();
		var token = await TokenAsync(client, $"/Setores/Menu/{setorId}");

		await client.PostAsync("/Setores/CriarItemDeMenu", Formulario(
			("setorId", setorId.ToString()),
			("parentId", raizId.ToString()),
			("nome", "Inscritos"),
			("slug", "inscritos"),
			("tipo", "Personalizado"),
			("rota", "/inscritos"),
			("nivelDeAcesso", "Escrita"),
			("__RequestVerificationToken", token)));

		(await ItemAsync(setorId, TipoDeItemMenu.Personalizado, "inscritos")).NivelDeAcesso.Should().Be(NivelDeAcessoItemMenu.Escrita);
	}

	[Fact]
	public async Task CriarItem_ComNivelInvalido_NaoCria()
	{
		var (setorId, _, raizId) = await CriarSetorAsync();
		var client = CriarCliente();
		var token = await TokenAsync(client, $"/Setores/Menu/{setorId}");

		await client.PostAsync("/Setores/CriarItemDeMenu", Formulario(
			("setorId", setorId.ToString()),
			("parentId", raizId.ToString()),
			("nome", "Inscritos"),
			("slug", "inscritos"),
			("tipo", "Personalizado"),
			("nivelDeAcesso", "99"),
			("__RequestVerificationToken", token)));

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var arvore = await escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>().ListarPorSetorAsync(setorId);
		arvore.Should().NotContain(item => item.Slug == "inscritos", "um valor inválido não pode virar o padrão em silêncio");
	}

	[Fact]
	public async Task DefinirNivel_PeloFormulario_AlteraOItemEmbutido()
	{
		var (setorId, _, _) = await CriarSetorAsync();
		var client = CriarCliente();
		var documentos = await ItemAsync(setorId, TipoDeItemMenu.Documentos);
		var token = await TokenAsync(client, $"/Setores/Menu/{setorId}");

		var resposta = await client.PostAsync("/Setores/DefinirNivelDeItemDeMenu", Formulario(
			("setorId", setorId.ToString()),
			("itemId", documentos.Id.ToString()),
			("nivelDeAcesso", "Escrita"),
			("__RequestVerificationToken", token)));

		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/Setores/Menu/{setorId}");
		(await ItemAsync(setorId, TipoDeItemMenu.Documentos)).NivelDeAcesso.Should().Be(NivelDeAcessoItemMenu.Escrita);
	}

	[Theory]
	[InlineData("99")]
	[InlineData(null)]
	public async Task DefinirNivel_InvalidoOuAusente_NaoAbreOItem(string? valor)
	{
		var (setorId, _, _) = await CriarSetorAsync();
		var client = CriarCliente();
		var documentos = await ItemAsync(setorId, TipoDeItemMenu.Documentos);
		var token = await TokenAsync(client, $"/Setores/Menu/{setorId}");

		await client.PostAsync("/Setores/DefinirNivelDeItemDeMenu", Formulario(
			("setorId", setorId.ToString()),
			("itemId", documentos.Id.ToString()),
			("nivelDeAcesso", "Escrita"),
			("__RequestVerificationToken", token)));

		var campos = new List<(string, string)>
		{
			("setorId", setorId.ToString()),
			("itemId", documentos.Id.ToString()),
			("__RequestVerificationToken", token),
		};

		if (valor is not null)
		{
			campos.Add(("nivelDeAcesso", valor));
		}

		await client.PostAsync("/Setores/DefinirNivelDeItemDeMenu", Formulario([.. campos]));

		(await ItemAsync(setorId, TipoDeItemMenu.Documentos)).NivelDeAcesso.Should().Be(NivelDeAcessoItemMenu.Escrita);
	}

	[Fact]
	public async Task DefinirNivel_SemTokenAntifalsificacao_Recusa()
	{
		var (setorId, _, _) = await CriarSetorAsync();
		var documentos = await ItemAsync(setorId, TipoDeItemMenu.Documentos);

		var resposta = await CriarCliente().PostAsync("/Setores/DefinirNivelDeItemDeMenu", Formulario(
			("setorId", setorId.ToString()),
			("itemId", documentos.Id.ToString()),
			("nivelDeAcesso", "Escrita")));

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await ItemAsync(setorId, TipoDeItemMenu.Documentos)).NivelDeAcesso.Should().Be(NivelDeAcessoItemMenu.Leitura);
	}

	[Fact]
	public async Task TelaDoMenu_OfereceONivelEmCadaItem_MenosNaRaiz()
	{
		var (setorId, _, raizId) = await CriarSetorAsync();

		var html = await CriarCliente().GetStringAsync($"/Setores/Menu/{setorId}");

		var ids = Regex.Matches(html, "action=\"/Setores/DefinirNivelDeItemDeMenu\".*?name=\"itemId\" value=\"([^\"]+)\"", RegexOptions.Singleline)
			.Select(match => Guid.Parse(match.Groups[1].Value))
			.ToList();

		ids.Should().NotBeEmpty();
		ids.Should().NotContain(raizId);
		html.Should().Contain("não protege a página");
	}

	[Fact]
	public async Task TelaDoMenu_ItemDebaixoDeRestrito_AvisaQueFicaEscondido()
	{
		var (setorId, _, raizId) = await CriarSetorAsync();

		using (var escopo = factory.Services.CreateScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
			var criar = escopo.ServiceProvider.GetRequiredService<CriarItemMenuHandler>();

			var grupo = await criar.HandleAsync(new CriarItemMenuCommand(
				setorId, raizId, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, NivelDeAcessoItemMenu.Escrita));
			var filho = await criar.HandleAsync(new CriarItemMenuCommand(
				setorId, grupo.Value.Id, "Mensais", "mensais", TipoDeItemMenu.Personalizado, "/mensais", null));

			filho.IsSuccess.Should().BeTrue();
		}

		var html = await CriarCliente().GetStringAsync($"/Setores/Menu/{setorId}");

		html.Should().Contain("Escondido pelo item acima");
	}

	[Fact]
	public async Task DetalheDoSetor_TemLinkParaOMenu()
	{
		var (setorId, _, _) = await CriarSetorAsync();

		var html = await CriarCliente().GetStringAsync($"/Setores/Details/{setorId}");

		html.Should().Contain($"href=\"/Setores/Menu/{setorId}\"");
	}
}
