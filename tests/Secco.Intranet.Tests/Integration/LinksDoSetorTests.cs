using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Links;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A tela "Gerenciar links" em <c>/{setor}/links</c> sobre o host completo: só quem escreve no setor
/// chega nela, o menu de quem lê mostra o resultado, e item de outra árvore responde 404.
/// </summary>
public class LinksDoSetorTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.ResolvedorDePermissoes = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(Guid? tenant = null, params string[] roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, (tenant ?? factory.TenantAlfa).ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private HttpClient CriarClienteAdmin() => CriarCliente(null, "intranet-admin");

	private async Task<string> NovoSetorAsync(bool comLinks = true)
	{
		var slug = $"lnk-{Guid.NewGuid():N}"[..20];

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criado = await escopo.ServiceProvider
			.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug, HabilitarLinks: comLinks));

		criado.IsSuccess.Should().BeTrue();

		return slug;
	}

	private async Task<T> NoEscopoAsync<T>(string slug, Func<IServiceProvider, Guid, Task<T>> acao)
	{
		await using var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var setor = await escopo.ServiceProvider.GetRequiredService<GetSetorBySlugHandler>().HandleAsync(slug);

		return await acao(escopo.ServiceProvider, setor.Value.Id);
	}

	private Task<LinksDoSetorDto> ListarAsync(string slug) =>
		NoEscopoAsync(slug, async (sp, setorId) =>
			(await sp.GetRequiredService<ListarLinksDoSetorHandler>().HandleAsync(setorId)).Value);

	private async Task<Guid> IdDoItemDeAsync(string slug, TipoDeItemMenu tipo) =>
		await NoEscopoAsync(slug, async (sp, setorId) =>
			(await sp.GetRequiredService<IItemMenuRepository>().ListarPorSetorAsync(setorId)).Single(i => i.Tipo == tipo).Id);

	private async Task<Guid> CriarGrupoAsync(HttpClient client, string slug, string nome)
	{
		var token = await TokenAsync(client, $"/{slug}/links");
		var resposta = await client.PostAsync($"/{slug}/links/grupos", Form(("nome", nome), ("__RequestVerificationToken", token)));
		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		return (await ListarAsync(slug)).Grupos.Single(g => g.Nome == nome).Id;
	}

	private static async Task<HttpResponseMessage> PostarLinkAsync(
		HttpClient client, string slug, string nome, string endereco, bool novaAba = true, Guid? grupoId = null)
	{
		var token = await TokenAsync(client, $"/{slug}/links/novo");

		// Como o navegador: a caixa marcada manda "true" e o campo oculto do asp-for manda "false".
		return await client.PostAsync($"/{slug}/links/novo", Form(
			("Form.Nome", nome),
			("Form.Rota", endereco),
			("Form.GrupoId", grupoId?.ToString() ?? string.Empty),
			("Form.NovaAba", novaAba ? "true" : "false"),
			("Form.NovaAba", "false"),
			("__RequestVerificationToken", token)));
	}

	private static async Task CriarLinkAsync(
		HttpClient client, string slug, string nome, string endereco, Guid? grupoId = null) =>
		(await PostarLinkAsync(client, slug, nome, endereco, grupoId: grupoId)).StatusCode.Should().Be(HttpStatusCode.OK);

	private async Task<Guid> CriarLinkEIdAsync(
		HttpClient client, string slug, string nome, string endereco, Guid? grupoId = null)
	{
		await CriarLinkAsync(client, slug, nome, endereco, grupoId);

		var lista = await ListarAsync(slug);
		var todos = lista.Entradas.Where(e => e.Link is not null).Select(e => e.Link!)
			.Concat(lista.Grupos.SelectMany(g => g.Links));

		return todos.Single(l => l.Nome == nome).Id;
	}

	[Fact]
	public async Task Get_SemEscrita_404()
	{
		var slug = await NovoSetorAsync();
		var role = $"leitor-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(role, IntranetPermissoes.Setor.Read(slug));

		var leitor = CriarCliente(null, role);

		(await leitor.GetAsync($"/{slug}/links")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await leitor.GetAsync($"/{slug}/links/novo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await leitor.GetAsync($"/{slug}/links/{Guid.NewGuid()}/editar")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Get_SetorSemNoLinks_404()
	{
		var slug = await NovoSetorAsync(comLinks: false);

		(await CriarClienteAdmin().GetAsync($"/{slug}/links")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Get_NoLinksDesativado_404()
	{
		var slug = await NovoSetorAsync();
		var no = await IdDoItemDeAsync(slug, TipoDeItemMenu.Links);

		await NoEscopoAsync(slug, async (sp, _) =>
		{
			(await sp.GetRequiredService<AtivarDesativarItemMenuHandler>().HandleAsync(no, ativar: false)).IsSuccess.Should().BeTrue();

			return 0;
		});

		var client = CriarClienteAdmin();

		(await client.GetAsync($"/{slug}/links")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.GetAsync($"/{slug}/links/novo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Theory]
	[InlineData("grupos")]
	[InlineData("novo")]
	[InlineData("{id}/editar")]
	[InlineData("{id}/mover")]
	[InlineData("{id}/excluir")]
	public async Task Post_SemAntiforgery_400(string rota)
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();

		var resposta = await client.PostAsync(
			$"/{slug}/links/{rota.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal)}",
			Form(("nome", "X"), ("Form.Nome", "X"), ("Form.Rota", "https://exemplo.test/x"), ("paraCima", "true")));

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task CriarGrupoELink_ApareceNoMenuDeQuemLe()
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();

		var grupoId = await CriarGrupoAsync(client, slug, "Sistemas internos");
		await CriarLinkAsync(client, slug, "ERP da empresa", "https://exemplo.test/erp", grupoId);

		var leitorRole = $"leitor-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(leitorRole, IntranetPermissoes.Setor.Read(slug));

		var mural = Decodificar(await CriarCliente(null, leitorRole).GetStringAsync("/"));

		mural.Should().Contain("Sistemas internos");
		mural.Should().Contain("ERP da empresa");
		mural.Should().Contain("href=\"https://exemplo.test/erp\"");
		mural.Should().NotContain("Gerenciar links", "quem só lê não tem a entrada de gestão");
	}

	[Fact]
	public async Task CriarLink_NovaAba_MenuTemTarget()
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();

		await CriarLinkAsync(client, slug, "Painel externo", "https://exemplo.test/painel");

		var mural = Decodificar(await client.GetStringAsync("/"));
		var ancora = Regex.Match(mural, "<a[^>]*href=\"https://exemplo.test/painel\"[^>]*>");

		ancora.Success.Should().BeTrue();
		ancora.Value.Should().Contain("target=\"_blank\"");
	}

	[Fact]
	public async Task CriarLink_CaixaDesmarcada_MesmaPagina()
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();

		(await PostarLinkAsync(client, slug, "Mesma aba", "https://exemplo.test/mesma", novaAba: false))
			.StatusCode.Should().Be(HttpStatusCode.OK);

		var mural = Decodificar(await client.GetStringAsync("/"));
		var ancora = Regex.Match(mural, "<a[^>]*href=\"https://exemplo.test/mesma\"[^>]*>");

		ancora.Success.Should().BeTrue();
		ancora.Value.Should().NotContain("target=");
		(await ListarAsync(slug)).Entradas.Single().Link!.NovaAba.Should().BeFalse();
	}

	[Fact]
	public async Task Novo_CaixaVemMarcadaEEditarMostraOGravado()
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();

		var formulario = await client.GetStringAsync($"/{slug}/links/novo");
		CaixaMarcada(formulario).Should().BeTrue("o padrão é nova página");

		(await PostarLinkAsync(client, slug, "Sem aba", "https://exemplo.test/s", novaAba: false)).StatusCode.Should().Be(HttpStatusCode.OK);
		var id = (await ListarAsync(slug)).Entradas.Single().Link!.Id;

		var edicao = await client.GetStringAsync($"/{slug}/links/{id}/editar");
		CaixaMarcada(edicao).Should().BeFalse("na edição vale o gravado");
	}

	/// <summary>Se a caixa "Abrir em nova página" vem marcada (o campo oculto do asp-for não conta).</summary>
	private static bool CaixaMarcada(string html) =>
		Regex.Matches(html, "<input[^>]*>")
			.Select(m => m.Value)
			.Single(tag => tag.Contains("name=\"Form.NovaAba\"", StringComparison.Ordinal) && tag.Contains("type=\"checkbox\"", StringComparison.Ordinal))
			.Contains("checked", StringComparison.Ordinal);

	[Fact]
	public async Task CriarLink_EnderecoInvalido_VoltaComMensagem()
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();

		var resposta = await PostarLinkAsync(client, slug, "Link ruim", "javascript:alert(1)");
		var html = Decodificar(await resposta.Content.ReadAsStringAsync());

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		html.Should().Contain(IntranetErrors.Menu.RotaInvalida.Description);
		html.Should().Contain("Link ruim", "o que foi digitado volta no formulário");
		(await ListarAsync(slug)).Entradas.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_ItemDeDocumentosPelaRotaDeLinks_404()
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();
		var documentos = await IdDoItemDeAsync(slug, TipoDeItemMenu.Documentos);
		var token = await TokenAsync(client, $"/{slug}/links");

		(await client.GetAsync($"/{slug}/links/{documentos}/editar")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.PostAsync($"/{slug}/links/{documentos}/editar", Form(
			("Form.Nome", "Invadido"), ("Form.Rota", "https://exemplo.test/x"), ("__RequestVerificationToken", token))))
			.StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.PostAsync($"/{slug}/links/{documentos}/excluir", Form(("__RequestVerificationToken", token))))
			.StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.PostAsync($"/{slug}/links/{documentos}/mover", Form(("paraCima", "true"), ("__RequestVerificationToken", token))))
			.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Editar_LinkDeOutroSetor_404()
	{
		var dono = await NovoSetorAsync();
		var outro = await NovoSetorAsync();
		var client = CriarClienteAdmin();
		var id = await CriarLinkEIdAsync(client, dono, "Do dono", "https://exemplo.test/dono");
		var token = await TokenAsync(client, $"/{outro}/links");

		(await client.GetAsync($"/{outro}/links/{id}/editar")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.PostAsync($"/{outro}/links/{id}/editar", Form(
			("Form.Nome", "Roubado"), ("Form.Rota", "https://exemplo.test/x"), ("__RequestVerificationToken", token))))
			.StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.PostAsync($"/{outro}/links/{id}/excluir", Form(("__RequestVerificationToken", token))))
			.StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await ListarAsync(dono)).Entradas.Single().Link!.Nome.Should().Be("Do dono");
	}

	[Fact]
	public async Task Excluir_GrupoComLinks_MensagemENaoExclui()
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();
		var grupoId = await CriarGrupoAsync(client, slug, "Grupo cheio");
		await CriarLinkAsync(client, slug, "Dentro", "https://exemplo.test/dentro", grupoId);
		var token = await TokenAsync(client, $"/{slug}/links");

		var resposta = await client.PostAsync($"/{slug}/links/{grupoId}/excluir", Form(("__RequestVerificationToken", token)));
		var html = Decodificar(await resposta.Content.ReadAsStringAsync());

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		html.Should().Contain(IntranetErrors.Menu.ItemComFilhos.Description);
		(await ListarAsync(slug)).Grupos.Should().ContainSingle(g => g.Id == grupoId);
	}

	[Fact]
	public async Task Mover_TrocaAOrdemNoMenu()
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();
		await CriarLinkAsync(client, slug, "Primeiro link", "https://exemplo.test/um");
		await CriarLinkAsync(client, slug, "Segundo link", "https://exemplo.test/dois");
		var segundo = (await ListarAsync(slug)).Entradas.Select(e => e.Link!).Single(l => l.Nome == "Segundo link").Id;

		var antes = Decodificar(await client.GetStringAsync("/"));
		antes.IndexOf("Primeiro link", StringComparison.Ordinal).Should().BeLessThan(antes.IndexOf("Segundo link", StringComparison.Ordinal));

		var token = await TokenAsync(client, $"/{slug}/links");
		(await client.PostAsync($"/{slug}/links/{segundo}/mover", Form(("paraCima", "true"), ("__RequestVerificationToken", token))))
			.StatusCode.Should().Be(HttpStatusCode.OK);

		var depois = Decodificar(await client.GetStringAsync("/"));
		depois.IndexOf("Segundo link", StringComparison.Ordinal).Should().BeLessThan(depois.IndexOf("Primeiro link", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Editar_Link_GravaNomeEnderecoEGrupo()
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();
		var grupoId = await CriarGrupoAsync(client, slug, "Destino");
		var id = await CriarLinkEIdAsync(client, slug, "Antigo", "https://exemplo.test/antigo");
		var token = await TokenAsync(client, $"/{slug}/links/{id}/editar");

		var resposta = await client.PostAsync($"/{slug}/links/{id}/editar", Form(
			("Form.Nome", "Novo nome"), ("Form.Rota", "https://exemplo.test/novo"), ("Form.GrupoId", grupoId.ToString()),
			("Form.NovaAba", "false"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		var link = (await ListarAsync(slug)).Grupos.Single().Links.Single();
		link.Nome.Should().Be("Novo nome");
		link.Rota.Should().Be("https://exemplo.test/novo");
		link.NovaAba.Should().BeFalse();
	}

	[Fact]
	public async Task Editar_Grupo_MostraSoONome()
	{
		var slug = await NovoSetorAsync();
		var client = CriarClienteAdmin();
		var grupoId = await CriarGrupoAsync(client, slug, "Grupo velho");

		var formulario = await client.GetStringAsync($"/{slug}/links/{grupoId}/editar");
		formulario.Should().NotContain("name=\"Form.Rota\"");

		var token = await TokenAsync(client, $"/{slug}/links/{grupoId}/editar");
		(await client.PostAsync($"/{slug}/links/{grupoId}/editar", Form(("Form.Nome", "Grupo novo"), ("__RequestVerificationToken", token))))
			.StatusCode.Should().Be(HttpStatusCode.OK);

		(await ListarAsync(slug)).Grupos.Single().Nome.Should().Be("Grupo novo");
	}

	[Fact]
	public async Task OutroTenant_NaoVeOsLinks()
	{
		var slug = await NovoSetorAsync();
		await CriarLinkAsync(CriarClienteAdmin(), slug, "Segredo do alfa", "https://exemplo.test/alfa");

		var beta = CriarCliente(factory.TenantBeta, "intranet-admin");

		(await beta.GetAsync($"/{slug}/links")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		Decodificar(await beta.GetStringAsync("/")).Should().NotContain("Segredo do alfa");
	}
}
