using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
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
/// A página de leitura dos documentos com grupos e filhos, nos dois temas oficiais: o filho aparece
/// recuado abaixo do pai, grupo vazio some da leitura e o "Abaixo de" só existe para quem escreve.
/// </summary>
public class DocumentoHierarquiaTelaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private const string MarcadorDeFilhos = "sc-doclist__filhos";

	private static readonly byte[] ConteudoPdf = Encoding.UTF8.GetBytes("%PDF-1.7\nhierarquia\n%%EOF");

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

	public static TheoryData<string> Temas => new() { "Vertical", "Horizontal" };

	private HttpClient CriarCliente(string tema, params string[] roles)
	{
		var client = factory
			.WithWebHostBuilder(builder =>
			{
				builder.UseSetting("Intranet:Theme:Nome", tema);
				builder.ConfigureAppConfiguration((_, configuration) =>
					configuration.AddInMemoryCollection(new Dictionary<string, string?>
					{
						["Intranet:Documentos:Armazenamento:Raiz"] = _raiz,
					}));
			})
			.CreateClient();

		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));

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

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		return slug;
	}

	private async Task<Guid> CriarGrupoAsync(string slug, string titulo)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var resultado = await escopo.ServiceProvider.GetRequiredService<CriarGrupoDeDocumentosHandler>()
			.HandleAsync(new CriarGrupoDeDocumentosCommand(slug, titulo, null, "teste"), CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();

		return resultado.Value.Id;
	}

	private static async Task PublicarAsync(HttpClient admin, string slug, string titulo, Guid? abaixoDe = null)
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

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>O trecho da página que é a lista de leitura, sem o formulário acima dela.</summary>
	private static string Lista(string html)
	{
		var inicio = html.IndexOf("class=\"sc-doclist", StringComparison.Ordinal);

		inicio.Should().BePositive("a página precisa renderizar o parcial _ListaDeDocumentos");

		return html[inicio..];
	}

	[Theory]
	[MemberData(nameof(Temas))]
	public async Task Pagina_MostraFilhoRecuadoAbaixoDoPai(string tema)
	{
		var admin = CriarCliente(tema, "intranet-admin");
		var slug = await CriarSetorAsync(admin);
		var grupo = await CriarGrupoAsync(slug, "Grupo Pai");
		await PublicarAsync(admin, slug, "Documento Filho", grupo);

		var lista = Lista(await admin.GetStringAsync($"/{slug}/documentos"));

		var pai = lista.IndexOf("Grupo Pai", StringComparison.Ordinal);
		var filhos = lista.IndexOf(MarcadorDeFilhos, StringComparison.Ordinal);
		var filho = lista.IndexOf("Documento Filho", StringComparison.Ordinal);

		pai.Should().BeGreaterThanOrEqualTo(0);
		filhos.Should().BeGreaterThan(pai, "os filhos ficam dentro do marcador, depois do pai");
		filho.Should().BeGreaterThan(filhos);
	}

	[Theory]
	[MemberData(nameof(Temas))]
	public async Task Pagina_GrupoVazio_NaoAparece(string tema)
	{
		var admin = CriarCliente(tema, "intranet-admin");
		var slug = await CriarSetorAsync(admin);
		await CriarGrupoAsync(slug, "Grupo Sem Nada");
		await PublicarAsync(admin, slug, "Documento Solto");

		var lista = Lista(await admin.GetStringAsync($"/{slug}/documentos"));

		lista.Should().Contain("Documento Solto").And.NotContain("Grupo Sem Nada");
	}

	[Theory]
	[MemberData(nameof(Temas))]
	public async Task Publicar_AbaixoDe_ViraFilho(string tema)
	{
		var admin = CriarCliente(tema, "intranet-admin");
		var slug = await CriarSetorAsync(admin);
		var grupo = await CriarGrupoAsync(slug, "Grupo Destino");

		await PublicarAsync(admin, slug, "Entrou No Grupo", grupo);

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var nos = await escopo.ServiceProvider.GetRequiredService<ListarDocumentosHandler>()
			.HandleAsync(new ListarDocumentosQuery(slug), CancellationToken.None);

		var no = nos.Value.Single();

		no.Documento.Id.Should().Be(grupo);
		no.Filhos.Should().ContainSingle().Which.Titulo.Should().Be("Entrou No Grupo");
	}

	[Theory]
	[MemberData(nameof(Temas))]
	public async Task Pagina_SemEscrita_NaoMostraAbaixoDe(string tema)
	{
		var admin = CriarCliente(tema, "intranet-admin");
		var slug = await CriarSetorAsync(admin);
		var grupo = await CriarGrupoAsync(slug, "Grupo Visivel");
		await PublicarAsync(admin, slug, "Documento Visivel", grupo);

		// Role nova a cada chamada: o resolvedor do SDK guarda o resultado por (tenant, role).
		var role = $"leitor-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(role, IntranetPermissoes.Setor.Read(slug));

		try
		{
			var leitor = CriarCliente(tema, role);

			var comEscrita = await admin.GetStringAsync($"/{slug}/documentos");
			var semEscrita = await leitor.GetStringAsync($"/{slug}/documentos");

			comEscrita.Should().Contain("Form.ParentId");
			semEscrita.Should().Contain("Documento Visivel").And.NotContain("Form.ParentId");
		}
		finally
		{
			factory.ResolvedorDePermissoes = null;
		}
	}
}
