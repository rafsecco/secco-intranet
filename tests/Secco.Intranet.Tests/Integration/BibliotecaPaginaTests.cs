using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A página de uma biblioteca e as ações de documento dela: leitura, escrita, o 404 indistinto
/// (ADR-0005) e o download pelas permissões da biblioteca.
/// </summary>
public class BibliotecaPaginaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly byte[] PdfOriginal = Encoding.UTF8.GetBytes("%PDF-1.7\noriginal da biblioteca\n%%EOF");
	private static readonly byte[] PdfNovo = Encoding.UTF8.GetBytes("%PDF-1.7\nsubstituto da biblioteca\n%%EOF");

	private readonly string _raiz = Path.Combine(
		Path.GetTempPath(), "secco-intranet-testes", Guid.NewGuid().ToString("N"));

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.GestaoDeAcesso = null;
		factory.ResolvedorDePermissoes = null;

		if (Directory.Exists(_raiz))
		{
			Directory.Delete(_raiz, recursive: true);
		}

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(string role = "intranet-admin")
	{
		var client = factory
			.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Intranet:Documentos:Armazenamento:Raiz"] = _raiz,
				})))
			.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

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

	private static string Texto(string html) => WebUtility.HtmlDecode(html);

	private async Task<string> CriarBibliotecaAsync(HttpClient admin, string nome = "Manuais")
	{
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa().ComPerfilImplicito();

		var slug = $"bib-{Guid.NewGuid():N}"[..16];
		var token = await TokenAsync(admin, "/bibliotecas/nova");

		var resposta = await admin.PostAsync("/bibliotecas/nova", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", nome),
			new KeyValuePair<string, string>("Slug", slug),
			new KeyValuePair<string, string>("TodosLeem", "true"),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		return slug;
	}

	private async Task DesativarBibliotecaAsync(HttpClient admin, string nome, string slug)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var todas = await escopo.ServiceProvider.GetRequiredService<ListarBibliotecasHandler>()
			.HandleAsync(new ListarBibliotecasQuery(SomenteAtivas: false), CancellationToken.None);
		var guid = todas.Value.Single(b => b.Slug == slug).Id.ToString();
		var editar = $"/bibliotecas/{guid}/editar";
		var token = await TokenAsync(admin, editar);

		(await admin.PostAsync(editar, new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Id", guid),
			new KeyValuePair<string, string>("Nome", nome),
			new KeyValuePair<string, string>("Slug", slug),
			new KeyValuePair<string, string>("Icone", string.Empty),
			new KeyValuePair<string, string>("Ativo", "false"),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]))).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	private async Task<string> CriarSetorAsync(HttpClient admin)
	{
		var slug = $"setor-{Guid.NewGuid():N}"[..20];
		var token = await TokenAsync(admin, "/Setores/Create");

		(await admin.PostAsync("/Setores/Create", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", "Setor de Teste"),
			new KeyValuePair<string, string>("Slug", slug),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]))).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);

		return slug;
	}

	private static async Task<HttpResponseMessage> PublicarAsync(
		HttpClient client, string prefixo, string titulo, string tokenDe, byte[]? conteudo = null)
	{
		var token = await TokenAsync(client, tokenDe);

		using var arquivo = new ByteArrayContent(conteudo ?? PdfOriginal);
		using var form = new MultipartFormDataContent
		{
			{ new StringContent(titulo), "Form.Titulo" },
			{ new StringContent("0"), "Form.Visibilidade" },
			{ new StringContent(string.Empty), "Form.ParentId" },
			{ new StringContent(token), "__RequestVerificationToken" },
			{ arquivo, "arquivo", "manual.pdf" },
		};

		return await client.PostAsync($"{prefixo}/documentos", form);
	}

	private async Task<Guid> IdDoDocumentoAsync(ReferenciaDeDono dono, string titulo)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var nos = await escopo.ServiceProvider.GetRequiredService<ListarDocumentosHandler>()
			.HandleAsync(new ListarDocumentosQuery(dono), CancellationToken.None);

		return nos.Value.SelectMany(no => no.Filhos.Prepend(no.Documento)).Single(documento => documento.Titulo == titulo).Id;
	}

	private async Task<IReadOnlyList<string>> TitulosAsync(ReferenciaDeDono dono)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var nos = await escopo.ServiceProvider.GetRequiredService<ListarDocumentosHandler>()
			.HandleAsync(new ListarDocumentosQuery(dono), CancellationToken.None);

		return [.. nos.Value.Select(no => no.Documento.Titulo)];
	}

	private static string NovaRole(string prefixo) => $"{prefixo}-{Guid.NewGuid():N}";

	[Fact]
	public async Task Sem_leitura_inexistente_e_inativa_dao_o_mesmo_404()
	{
		var admin = CriarCliente();
		var semPermissao = await CriarBibliotecaAsync(admin, "Fechada");
		var inativa = await CriarBibliotecaAsync(admin, "Velha");

		await DesativarBibliotecaAsync(admin, "Velha", inativa);

		var role = NovaRole("leitor");
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao(role, IntranetPermissoes.Biblioteca.Read(inativa));

		var cliente = CriarCliente(role);
		var respostas = new List<(HttpStatusCode Status, string Corpo)>();

		foreach (var slug in new[] { semPermissao, "nao-existe", inativa })
		{
			var resposta = await cliente.GetAsync($"/bibliotecas/{slug}");

			respostas.Add((resposta.StatusCode, await resposta.Content.ReadAsStringAsync()));
		}

		respostas.Select(r => r.Status).Should().OnlyContain(status => status == HttpStatusCode.NotFound);
		respostas.Select(r => r.Corpo).Distinct().Should().HaveCount(1, "o corpo não pode distinguir os três casos");
	}

	[Fact]
	public async Task Nova_continua_no_cadastro_do_admin()
	{
		var admin = CriarCliente();

		var html = await admin.GetStringAsync("/bibliotecas/nova");

		html.Should().Contain("name=\"Slug\"", "a rota literal do cadastro vence a da página da biblioteca");
	}

	[Fact]
	public async Task Leitor_ve_a_lista_sem_acoes_de_escrita()
	{
		var admin = CriarCliente();
		var slug = await CriarBibliotecaAsync(admin);

		(await PublicarAsync(admin, $"/bibliotecas/{slug}", "Manual de Conduta", $"/bibliotecas/{slug}")).StatusCode
			.Should().Be(HttpStatusCode.OK);

		var role = NovaRole("leitor");
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(role, IntranetPermissoes.Biblioteca.Read(slug));

		var leitor = CriarCliente(role);
		var html = Texto(await leitor.GetStringAsync($"/bibliotecas/{slug}"));

		html.Should().Contain("Manual de Conduta");
		html.Should().NotContain("Organizar").And.NotContain("Publicar documento").And.NotContain("name=\"arquivo\"");

		(await leitor.GetAsync($"/bibliotecas/{slug}/documentos/organizar")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Escritor_publica_cria_grupo_organiza_edita_e_arquiva()
	{
		var admin = CriarCliente();
		var slug = await CriarBibliotecaAsync(admin);
		var dono = ReferenciaDeDono.DaBiblioteca(slug);
		var pagina = $"/bibliotecas/{slug}";

		var role = NovaRole("escritor");
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(role, IntranetPermissoes.Biblioteca.Write(slug));

		var escritor = CriarCliente(role);

		// Publica: volta para a página, que já lista o documento e oferece "Organizar".
		var publicada = await PublicarAsync(escritor, pagina, "Manual A", pagina);

		publicada.StatusCode.Should().Be(HttpStatusCode.OK);
		publicada.RequestMessage!.RequestUri!.AbsolutePath.Should().Be(pagina);

		var html = Texto(await publicada.Content.ReadAsStringAsync());

		html.Should().Contain("Manual A").And.Contain("Documento \"Manual A\" publicado.");
		html.Should().Contain($"{pagina}/documentos/organizar");

		(await PublicarAsync(escritor, pagina, "Manual B", pagina)).StatusCode.Should().Be(HttpStatusCode.OK);

		// Cria o grupo na tela de organizar.
		var organizar = $"{pagina}/documentos/organizar";
		var tokenGrupo = await TokenAsync(escritor, organizar);
		var grupo = await escritor.PostAsync($"{pagina}/documentos/grupos", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Titulo", "Pasta"),
			new KeyValuePair<string, string>("__RequestVerificationToken", tokenGrupo),
		]));

		grupo.StatusCode.Should().Be(HttpStatusCode.OK);
		(await TitulosAsync(dono)).Should().Equal("Pasta", "Manual B", "Manual A");

		// Organiza: desce o grupo (o mais novo fica no topo).
		var idGrupo = await IdDoDocumentoAsync(dono, "Pasta");
		var mover = await escritor.PostAsync($"{pagina}/documentos/{idGrupo}/mover", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("acao", "2"),
			new KeyValuePair<string, string>("__RequestVerificationToken", await TokenAsync(escritor, organizar)),
		]));

		mover.StatusCode.Should().Be(HttpStatusCode.OK);
		(await TitulosAsync(dono)).Should().Equal("Manual B", "Pasta", "Manual A");

		// Edita título e arquivo.
		var idA = await IdDoDocumentoAsync(dono, "Manual A");
		var editar = $"{pagina}/documentos/{idA}/editar";
		var formEdicao = Texto(await escritor.GetStringAsync(editar));

		formEdicao.Should().Contain("value=\"Manual A\"").And.Contain("Arquivo atual: manual.pdf.");

		using var arquivoNovo = new ByteArrayContent(PdfNovo);
		using var edicao = new MultipartFormDataContent
		{
			{ new StringContent("Manual A2"), "Form.Titulo" },
			{ new StringContent(string.Empty), "Form.Descricao" },
			{ new StringContent(await TokenAsync(escritor, editar)), "__RequestVerificationToken" },
			{ arquivoNovo, "arquivo", "novo.pdf" },
		};

		(await escritor.PostAsync(editar, edicao)).StatusCode.Should().Be(HttpStatusCode.OK);
		(await TitulosAsync(dono)).Should().Contain("Manual A2").And.NotContain("Manual A");

		var baixado = await escritor.GetAsync($"/Documentos/{idA}/Download");

		baixado.StatusCode.Should().Be(HttpStatusCode.OK);
		(await baixado.Content.ReadAsByteArrayAsync()).Should().Equal(PdfNovo);

		// Arquiva.
		var arquivar = await escritor.PostAsync($"{pagina}/documentos/{idA}/arquivar", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("__RequestVerificationToken", await TokenAsync(escritor, organizar)),
		]));

		arquivar.StatusCode.Should().Be(HttpStatusCode.OK);
		(await TitulosAsync(dono)).Should().NotContain("Manual A2");
	}

	[Fact]
	public async Task Formulario_da_biblioteca_nao_tem_quem_pode_ver()
	{
		var admin = CriarCliente();
		var slug = await CriarBibliotecaAsync(admin);
		var pagina = $"/bibliotecas/{slug}";

		(await PublicarAsync(admin, pagina, "Manual", pagina)).StatusCode.Should().Be(HttpStatusCode.OK);

		var id = await IdDoDocumentoAsync(ReferenciaDeDono.DaBiblioteca(slug), "Manual");

		(await admin.GetStringAsync(pagina)).Should().Contain("Publicar documento").And.NotContain("Quem pode ver");
		(await admin.GetStringAsync($"{pagina}/documentos/{id}/editar")).Should().NotContain("Quem pode ver");

		// O setor continua oferecendo o seletor.
		var setor = await CriarSetorAsync(admin);

		(await admin.GetStringAsync($"/{setor}/documentos")).Should().Contain("Quem pode ver");
	}

	[Fact]
	public async Task Lista_da_biblioteca_nao_tem_badge()
	{
		var admin = CriarCliente();
		var slug = await CriarBibliotecaAsync(admin);
		var pagina = $"/bibliotecas/{slug}";

		(await PublicarAsync(admin, pagina, "Manual", pagina)).StatusCode.Should().Be(HttpStatusCode.OK);

		var setor = await CriarSetorAsync(admin);

		(await PublicarAsync(admin, $"/{setor}", "Do setor", $"/{setor}/documentos")).StatusCode.Should().Be(HttpStatusCode.OK);

		(await admin.GetStringAsync(pagina)).Should().Contain("Manual").And.NotContain("sc-badge");
		(await admin.GetStringAsync($"/{setor}/documentos")).Should().Contain("sc-badge--setor");
	}

	[Fact]
	public async Task Leitor_baixa_documento_e_quem_le_so_o_setor_de_mesmo_slug_recebe_404()
	{
		var admin = CriarCliente();
		var slug = await CriarBibliotecaAsync(admin);
		var pagina = $"/bibliotecas/{slug}";

		(await PublicarAsync(admin, pagina, "Manual", pagina)).StatusCode.Should().Be(HttpStatusCode.OK);

		var id = await IdDoDocumentoAsync(ReferenciaDeDono.DaBiblioteca(slug), "Manual");

		var leitorRole = NovaRole("leitor");
		var setorRole = NovaRole("setor");
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao(leitorRole, IntranetPermissoes.Biblioteca.Read(slug))
			// Leitura de um setor com o mesmo slug da biblioteca não vale para a biblioteca.
			.ComPermissao(setorRole, IntranetPermissoes.Setor.Read(slug));

		var baixado = await CriarCliente(leitorRole).GetAsync($"/Documentos/{id}/Download");

		baixado.StatusCode.Should().Be(HttpStatusCode.OK);
		(await baixado.Content.ReadAsByteArrayAsync()).Should().Equal(PdfOriginal);

		(await CriarCliente(setorRole).GetAsync($"/Documentos/{id}/Download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task POST_forjado_sem_write_e_404()
	{
		var admin = CriarCliente();
		var slug = await CriarBibliotecaAsync(admin);
		var outra = await CriarBibliotecaAsync(admin, "Outra");
		var pagina = $"/bibliotecas/{slug}";
		var dono = ReferenciaDeDono.DaBiblioteca(slug);

		(await PublicarAsync(admin, pagina, "Manual", pagina)).StatusCode.Should().Be(HttpStatusCode.OK);

		var id = await IdDoDocumentoAsync(dono, "Manual");

		// Lê a biblioteca e escreve em outra: o token vem da página da outra, e a recusa tem de ser
		// a da falta de escrita na biblioteca alvo.
		var role = NovaRole("leitor");
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao(role, IntranetPermissoes.Biblioteca.Read(slug))
			.ComPermissao(role, IntranetPermissoes.Biblioteca.Write(outra));

		var leitor = CriarCliente(role);
		var token = await TokenAsync(leitor, $"/bibliotecas/{outra}");
		var campos = new[] { new KeyValuePair<string, string>("__RequestVerificationToken", token) };

		using var arquivo = new ByteArrayContent(PdfOriginal);
		using var publicar = new MultipartFormDataContent
		{
			{ new StringContent("Forjado"), "Form.Titulo" },
			{ new StringContent(token), "__RequestVerificationToken" },
			{ arquivo, "arquivo", "forjado.pdf" },
		};

		(await leitor.PostAsync($"{pagina}/documentos", publicar)).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await leitor.PostAsync(
			$"{pagina}/documentos/{id}/mover",
			new FormUrlEncodedContent(campos.Append(new("acao", "1"))))).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await leitor.PostAsync(
			$"{pagina}/documentos/{id}/arquivar",
			new FormUrlEncodedContent(campos))).StatusCode.Should().Be(HttpStatusCode.NotFound);

		// E quem escreve em outra biblioteca não arquiva o documento desta pela rota da sua.
		(await leitor.PostAsync(
			$"/bibliotecas/{outra}/documentos/{id}/arquivar",
			new FormUrlEncodedContent(campos))).StatusCode.Should().Be(HttpStatusCode.NotFound);

		(await TitulosAsync(dono)).Should().Equal("Manual");
	}
}
