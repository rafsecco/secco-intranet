using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A tela de organizar os documentos de um setor: acesso, botões habilitados pelas regras de
/// movimento, criação de grupo, movimentos pelo formulário e as recusas devolvidas como mensagem.
/// </summary>
public class DocumentoOrganizarTelaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private const string MensagemItemComFilhos =
		"Este item tem documentos abaixo dele. Mova ou arquive esses documentos primeiro.";

	private static readonly byte[] ConteudoPdf = Encoding.UTF8.GetBytes("%PDF-1.7\norganizar\n%%EOF");

	private readonly string _raiz = Path.Combine(
		Path.GetTempPath(), "secco-intranet-testes", Guid.NewGuid().ToString("N"));

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		if (Directory.Exists(_raiz))
		{
			Directory.Delete(_raiz, recursive: true);
		}

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(string role = "intranet-admin", bool seguirRedirects = true)
	{
		var client = factory
			.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Intranet:Documentos:Armazenamento:Raiz"] = _raiz,
				})))
			.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = seguirRedirects });

		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, role);

		return client;
	}

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private static async Task<string> CriarSetorAsync(HttpClient admin)
	{
		var slug = $"setor-{Guid.NewGuid():N}"[..20];
		var token = await TokenAsync(admin, "/Setores/Create");

		var resposta = await admin.PostAsync("/Setores/Create", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", "Setor de Teste"),
			new KeyValuePair<string, string>("Slug", slug),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);

		return slug;
	}

	private async Task<Guid> CriarGrupoAsync(string slug, string titulo)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var resultado = await escopo.ServiceProvider.GetRequiredService<CriarGrupoDeDocumentosHandler>()
			.HandleAsync(new CriarGrupoDeDocumentosCommand(ReferenciaDeDono.DoSetor(slug), titulo, null, "teste"), CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();

		return resultado.Value.Id;
	}

	private async Task<Guid> PublicarAsync(HttpClient admin, string slug, string titulo, Guid? abaixoDe = null)
	{
		var token = await TokenAsync(admin, $"/{slug}/documentos");

		using var arquivo = new ByteArrayContent(ConteudoPdf);
		using var form = new MultipartFormDataContent
		{
			{ new StringContent(titulo), "Form.Titulo" },
			{ new StringContent("0"), "Form.Visibilidade" },
			{ new StringContent(abaixoDe?.ToString() ?? string.Empty), "Form.ParentId" },
			{ new StringContent(token), "__RequestVerificationToken" },
			{ arquivo, "arquivo", "politica.pdf" },
		};

		var resposta = await admin.PostAsync($"/{slug}/documentos", form);

		resposta.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);

		return await IdPorTituloAsync(slug, titulo);
	}

	private async Task<IReadOnlyList<NoDeDocumentoDto>> ArvoreAsync(string slug)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var nos = await escopo.ServiceProvider.GetRequiredService<ListarDocumentosHandler>()
			.HandleAsync(new ListarDocumentosQuery(ReferenciaDeDono.DoSetor(slug)), CancellationToken.None);

		return nos.Value;
	}

	private async Task<Guid> IdPorTituloAsync(string slug, string titulo)
	{
		var arvore = await ArvoreAsync(slug);

		return arvore
			.SelectMany(no => no.Filhos.Prepend(no.Documento))
			.Single(documento => documento.Titulo == titulo)
			.Id;
	}

	private static async Task<HttpResponseMessage> PostarAsync(
		HttpClient client, string slug, string caminho, params (string Nome, string Valor)[] campos)
	{
		var token = await TokenAsync(client, $"/{slug}/documentos/organizar");
		var conteudo = campos
			.Select(campo => new KeyValuePair<string, string>(campo.Nome, campo.Valor))
			.Append(new KeyValuePair<string, string>("__RequestVerificationToken", token));

		return await client.PostAsync($"/{slug}/documentos/{caminho}", new FormUrlEncodedContent(conteudo));
	}

	/// <summary>A linha (li) da tela de organizar que traz o título dado.</summary>
	private static string Linha(string html, string titulo)
	{
		var linhas = Regex.Matches(html, "<li class=\"sc-list__item[^\"]*\"[\\s\\S]*?</li>");

		return linhas.Select(linha => linha.Value).Single(linha => linha.Contains(titulo, StringComparison.Ordinal));
	}

	private static bool Desabilitado(string linha, string rotulo)
	{
		var botao = Regex.Match(linha, $"<button[^>]*aria-label=\"{rotulo}[^\"]*\"[^>]*>");

		botao.Success.Should().BeTrue($"a linha precisa ter o botão '{rotulo}'");

		return botao.Value.Contains("disabled", StringComparison.Ordinal);
	}

	private static string Texto(string html) => WebUtility.HtmlDecode(html);

	[Fact]
	public async Task Organizar_SemEscrita_NaoEncontrado()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var grupo = await CriarGrupoAsync(slug, "Grupo");
		var outro = await CriarSetorAsync(admin);

		// Lê o setor do teste e escreve em outro: o token antifalsificação vem da página do outro
		// setor, e a recusa tem de ser a de permissão no setor do teste.
		var role = $"leitor-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao(role, IntranetPermissoes.Setor.Read(slug))
			.ComPermissao(role, IntranetPermissoes.Setor.Read(outro))
			.ComPermissao(role, IntranetPermissoes.Setor.Write(outro));

		try
		{
			var leitor = CriarCliente(role);

			(await leitor.GetAsync($"/{slug}/documentos/organizar")).StatusCode.Should().Be(HttpStatusCode.NotFound);

			var token = await TokenAsync(leitor, $"/{outro}/documentos");
			var campos = new[] { new KeyValuePair<string, string>("__RequestVerificationToken", token) };

			(await leitor.PostAsync(
				$"/{slug}/documentos/{grupo}/mover",
				new FormUrlEncodedContent(campos.Append(new("acao", "1"))))).StatusCode.Should().Be(HttpStatusCode.NotFound);
			(await leitor.PostAsync(
				$"/{slug}/documentos/grupos",
				new FormUrlEncodedContent(campos.Append(new("Titulo", "X"))))).StatusCode.Should().Be(HttpStatusCode.NotFound);
			(await leitor.PostAsync(
				$"/{slug}/documentos/{grupo}/arquivar",
				new FormUrlEncodedContent(campos))).StatusCode.Should().Be(HttpStatusCode.NotFound);
		}
		finally
		{
			factory.ResolvedorDePermissoes = null;
		}
	}

	[Fact]
	public async Task Pagina_SemEscrita_NaoMostraOrganizar()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var grupo = await CriarGrupoAsync(slug, "Grupo");
		await PublicarAsync(admin, slug, "Documento", grupo);

		var role = $"leitor-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(role, IntranetPermissoes.Setor.Read(slug));

		try
		{
			var html = await CriarCliente(role).GetStringAsync($"/{slug}/documentos");

			html.Should().Contain("Documento").And.NotContain("/documentos/organizar");
		}
		finally
		{
			factory.ResolvedorDePermissoes = null;
		}
	}

	[Fact]
	public async Task Pagina_ComEscrita_MostraOrganizar()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);

		var html = await admin.GetStringAsync($"/{slug}/documentos");

		html.Should().Contain($"/{slug}/documentos/organizar");
	}

	[Fact]
	public async Task Organizar_GrupoVazio_Aparece()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		await CriarGrupoAsync(slug, "Grupo Sem Nada");

		var html = await admin.GetStringAsync($"/{slug}/documentos/organizar");

		html.Should().Contain("Grupo Sem Nada");
	}

	[Fact]
	public async Task Organizar_BotoesDesabilitados()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);

		// Raiz de cima para baixo: Pai (com Filho), Grupo, Ultimo.
		await PublicarAsync(admin, slug, "Ultimo");
		await CriarGrupoAsync(slug, "Grupo");
		var pai = await PublicarAsync(admin, slug, "Pai");
		await PublicarAsync(admin, slug, "Filho", pai);

		var html = await admin.GetStringAsync($"/{slug}/documentos/organizar");

		var primeiro = Linha(html, ">Pai<");
		Desabilitado(primeiro, "Subir").Should().BeTrue("o primeiro da raiz não sobe");
		Desabilitado(primeiro, "Arquivar").Should().BeTrue("item com filhos não arquiva");
		Desabilitado(primeiro, "Recuar").Should().BeTrue("o primeiro da raiz não recua");
		Desabilitado(primeiro, "Descer").Should().BeFalse();

		var grupo = Linha(html, ">Grupo<");
		Desabilitado(grupo, "Recuar").Should().BeTrue("grupo nunca é filho");
		Desabilitado(grupo, "Subir").Should().BeFalse();
		Desabilitado(grupo, "Arquivar").Should().BeFalse();

		var ultimo = Linha(html, ">Ultimo<");
		Desabilitado(ultimo, "Descer").Should().BeTrue("o último da raiz não desce");
		Desabilitado(ultimo, "Recuar").Should().BeFalse("tem irmão acima e nada embaixo");
		Desabilitado(ultimo, "Desrecuar").Should().BeTrue("já está na raiz");

		var filho = Linha(html, ">Filho<");
		Desabilitado(filho, "Recuar").Should().BeTrue("já é filho");
		Desabilitado(filho, "Desrecuar").Should().BeFalse();
		Desabilitado(filho, "Subir").Should().BeTrue("único filho");
		Desabilitado(filho, "Descer").Should().BeTrue("único filho");
	}

	[Fact]
	public async Task CriarGrupo_EntraNoTopo()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		await PublicarAsync(admin, slug, "Documento Antigo");

		var resposta = await PostarAsync(admin, slug, "grupos", ("Titulo", "Grupo Novo"), ("Descricao", string.Empty));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		var arvore = await ArvoreAsync(slug);

		arvore.Select(no => no.Documento.Titulo).Should().Equal("Grupo Novo", "Documento Antigo");
	}

	[Fact]
	public async Task Mover_Recuar_EAPaginaReflete()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var pai = await PublicarAsync(admin, slug, "Pai Da Vez");
		var item = await PublicarAsync(admin, slug, "Item Que Recua");

		// Item Que Recua foi publicado depois, então está acima de Pai Da Vez; desce para ficar abaixo.
		(await PostarAsync(admin, slug, $"{item}/mover", ("acao", "2"))).StatusCode.Should().Be(HttpStatusCode.OK);
		(await PostarAsync(admin, slug, $"{item}/mover", ("acao", "3"))).StatusCode.Should().Be(HttpStatusCode.OK);

		var arvore = await ArvoreAsync(slug);

		arvore.Should().ContainSingle().Which.Documento.Id.Should().Be(pai);
		arvore[0].Filhos.Should().ContainSingle().Which.Id.Should().Be(item);

		var html = await admin.GetStringAsync($"/{slug}/documentos");

		html.IndexOf("sc-doclist__filhos", StringComparison.Ordinal).Should().BePositive();
		html.IndexOf("Item Que Recua", StringComparison.Ordinal)
			.Should().BeGreaterThan(html.IndexOf("sc-doclist__filhos", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Mover_Invalido_VoltaComMensagem()
	{
		var admin = CriarCliente(seguirRedirects: false);
		var slug = await CriarSetorAsync(admin);
		await PublicarAsync(admin, slug, "Qualquer Um");
		var grupo = await CriarGrupoAsync(slug, "Grupo Forjado");
		var antes = await ArvoreAsync(slug);

		var resposta = await PostarAsync(admin, slug, $"{grupo}/mover", ("acao", "3"));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.ToString().Should().EndWith($"/{slug}/documentos/organizar");

		var html = Texto(await admin.GetStringAsync(resposta.Headers.Location));

		html.Should().Contain("Não foi possível mover este item.");
		(await ArvoreAsync(slug)).Select(no => (no.Documento.Id, no.Documento.Ordem))
			.Should().Equal(antes.Select(no => (no.Documento.Id, no.Documento.Ordem)));
	}

	[Fact]
	public async Task Arquivar_ItemComFilhos_MostraMensagem()
	{
		var admin = CriarCliente(seguirRedirects: false);
		var slug = await CriarSetorAsync(admin);
		var grupo = await CriarGrupoAsync(slug, "Grupo Com Filho");
		await PublicarAsync(admin, slug, "Filho Ativo", grupo);

		var resposta = await PostarAsync(admin, slug, $"{grupo}/arquivar");

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.ToString().Should().EndWith($"/{slug}/documentos/organizar");

		Texto(await admin.GetStringAsync(resposta.Headers.Location)).Should().Contain(MensagemItemComFilhos);
		(await ArvoreAsync(slug)).Should().ContainSingle().Which.Documento.Id.Should().Be(grupo);
	}

	[Fact]
	public async Task Arquivar_ItemSemFilhos_VoltaParaOrganizar()
	{
		var admin = CriarCliente(seguirRedirects: false);
		var slug = await CriarSetorAsync(admin);
		var item = await PublicarAsync(admin, slug, "Folha");

		var resposta = await PostarAsync(admin, slug, $"{item}/arquivar");

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.ToString().Should().EndWith($"/{slug}/documentos/organizar");
		(await ArvoreAsync(slug)).Should().BeEmpty();
	}

	[Fact]
	public async Task Mover_SemAntiforgery_Recusa()
	{
		var admin = CriarCliente();
		var slug = await CriarSetorAsync(admin);
		var item = await PublicarAsync(admin, slug, "Documento");

		var resposta = await admin.PostAsync(
			$"/{slug}/documentos/{item}/mover",
			new FormUrlEncodedContent([new KeyValuePair<string, string>("acao", "2")]));

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}
}
