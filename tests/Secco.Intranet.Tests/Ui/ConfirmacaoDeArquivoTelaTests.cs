using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Tests.Integration;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Secco.Intranet.Tests.Ui;

/// <summary>
/// Sobe a aplicação de verdade (Kestrel), uma vez por tema, com um setor que tem um documento
/// publicado, e um Chromium headless — o necessário para abrir a tela de editar documento.
/// </summary>
public sealed class ConfirmacaoDeArquivoFixture : IAsyncLifetime
{
	private readonly IntranetWebFactory _fabrica = new();
	private readonly Dictionary<string, WebApplicationFactory<Program>> _apps = [];
	private readonly string _raiz = Path.Combine(Path.GetTempPath(), "secco-intranet-testes", Guid.NewGuid().ToString("N"));
	private IPlaywright? _playwright;

	/// <summary>Navegador compartilhado pelos testes.</summary>
	public IBrowser Navegador { get; private set; } = null!;

	/// <summary>Endereço da aplicação por tema.</summary>
	public Dictionary<string, string> Enderecos { get; } = [];

	/// <summary>Slug do setor do cenário.</summary>
	public string Slug { get; } = $"arq-{Guid.NewGuid():N}"[..12];

	/// <summary>Id do documento publicado.</summary>
	public Guid DocumentoId { get; private set; }

	/// <summary>Nome do arquivo atual do documento.</summary>
	public const string NomeDoArquivoAtual = "original.pdf";

	/// <inheritdoc />
	public async Task InitializeAsync()
	{
		if (!UiTheoryAttribute.Ligados)
		{
			return;
		}

		await ((IAsyncLifetime)_fabrica).InitializeAsync();
		await _fabrica.EnsureDatabaseMigratedAsync();

		foreach (var tema in new[] { "Vertical", "Horizontal" })
		{
			var app = _fabrica.WithWebHostBuilder(builder => builder
				.UseSetting("Intranet:Theme:Nome", tema)
				.UseSetting("Intranet:Documentos:Armazenamento:Raiz", _raiz)
				.UseStaticWebAssets());
			app.UseKestrel(kestrel => kestrel.Listen(System.Net.IPAddress.Loopback, 0));
			app.StartServer();
			_apps[tema] = app;
			Enderecos[tema] = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		}

		await CriarSetorEDocumentoAsync();

		_playwright = await Playwright.CreateAsync();
		Navegador = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
	}

	/// <summary>Abre a tela de editar o documento, como administrador da intranet.</summary>
	public async Task<IPage> AbrirEdicaoAsync(string tema)
	{
		var contexto = await Navegador.NewContextAsync(new BrowserNewContextOptions
		{
			BaseURL = Enderecos[tema],
			ExtraHTTPHeaders = new Dictionary<string, string>
			{
				[SeccoHeaders.TenantId] = _fabrica.TenantAlfa.ToString(),
				[RolesDeTesteMiddleware.Header] = "intranet-admin",
			},
		});

		var pagina = await contexto.NewPageAsync();
		await pagina.GotoAsync($"/{Slug}/documentos/{DocumentoId}/editar");

		return pagina;
	}

	private async Task CriarSetorEDocumentoAsync()
	{
		// Cliente em memória do mesmo host: cria o setor e publica pela própria interface.
		var admin = _apps["Vertical"].CreateClient();
		admin.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, _fabrica.TenantAlfa.ToString());
		admin.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		var token = await TokenAsync(admin, "/Setores/Create");
		(await admin.PostAsync("/Setores/Create", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", "Setor de Arquivos"),
			new KeyValuePair<string, string>("Slug", Slug),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]))).IsSuccessStatusCode.Should().BeTrue();

		token = await TokenAsync(admin, $"/{Slug}/documentos");
		using var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.7\noriginal\n%%EOF"));
		using var form = new MultipartFormDataContent
		{
			{ new StringContent("Manual"), "Form.Titulo" },
			{ new StringContent(string.Empty), "Form.Descricao" },
			{ new StringContent("0"), "Form.Visibilidade" },
			{ new StringContent(string.Empty), "Form.ParentId" },
			{ new StringContent(token), "__RequestVerificationToken" },
			{ arquivo, "arquivo", NomeDoArquivoAtual },
		};
		(await admin.PostAsync($"/{Slug}/documentos", form)).IsSuccessStatusCode.Should().BeTrue();

		using var escopo = _fabrica.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(_fabrica.TenantAlfa);
		var nos = await escopo.ServiceProvider.GetRequiredService<ListarDocumentosHandler>()
			.HandleAsync(new ListarDocumentosQuery(ReferenciaDeDono.DoSetor(Slug)), CancellationToken.None);
		DocumentoId = nos.Value.Select(no => no.Documento).Single(d => d.Titulo == "Manual").Id;
	}

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
		token.Success.Should().BeTrue();

		return token.Groups[1].Value;
	}

	/// <inheritdoc />
	public async Task DisposeAsync()
	{
		if (_playwright is not null)
		{
			await Navegador.DisposeAsync();
			_playwright.Dispose();
		}

		foreach (var app in _apps.Values)
		{
			await app.DisposeAsync();
		}

		await _fabrica.DisposeAsync();

		if (Directory.Exists(_raiz))
		{
			Directory.Delete(_raiz, recursive: true);
		}
	}
}

/// <summary>
/// O <c>data-confirmar</c> num campo de arquivo: o tema só pergunta quando há arquivo escolhido,
/// nos dois temas.
/// </summary>
public class ConfirmacaoDeArquivoTelaTests(ConfirmacaoDeArquivoFixture f) : IClassFixture<ConfirmacaoDeArquivoFixture>
{
	// Mantém o nome do arquivo atual: o teste que confirma troca o conteúdo no documento
	// compartilhado, e o nome citado na pergunta precisa seguir o mesmo nos demais.
	private static FilePayload PdfNovo => new()
	{
		Name = ConfirmacaoDeArquivoFixture.NomeDoArquivoAtual,
		MimeType = "application/pdf",
		Buffer = Encoding.UTF8.GetBytes("%PDF-1.7\nsubstituto\n%%EOF"),
	};

	/// <summary>Registra o diálogo que aparecer e responde a ele.</summary>
	private static List<string> ResponderDialogos(IPage pagina, bool aceitar)
	{
		var mensagens = new List<string>();
		pagina.Dialog += async (_, dialogo) =>
		{
			mensagens.Add(dialogo.Message);
			await (aceitar ? dialogo.AcceptAsync() : dialogo.DismissAsync());
		};

		return mensagens;
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task ArquivoEscolhido_PerguntaECancelarNaoEnvia(string tema)
	{
		var pagina = await f.AbrirEdicaoAsync(tema);
		var mensagens = ResponderDialogos(pagina, aceitar: false);
		await pagina.EvaluateAsync("() => { window.__marca = 1; }");
		await pagina.Locator("#arquivo").SetInputFilesAsync(PdfNovo);

		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar" }).ClickAsync();

		mensagens.Should().ContainSingle().Which.Should().Contain(ConfirmacaoDeArquivoFixture.NomeDoArquivoAtual);
		(await pagina.EvaluateAsync<int?>("() => window.__marca")).Should().Be(1, "a página não foi recarregada: o envio foi cancelado");
		await Expect(pagina.GetByText("Documento atualizado.")).ToHaveCountAsync(0);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task ArquivoEscolhido_ConfirmarEnvia(string tema)
	{
		var pagina = await f.AbrirEdicaoAsync(tema);
		var mensagens = ResponderDialogos(pagina, aceitar: true);
		await pagina.Locator("#arquivo").SetInputFilesAsync(PdfNovo);

		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar" }).ClickAsync();

		await Expect(pagina.GetByText("Documento atualizado.")).ToBeVisibleAsync();
		mensagens.Should().ContainSingle().Which.Should().Contain(ConfirmacaoDeArquivoFixture.NomeDoArquivoAtual);
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task SemArquivo_EnviaSemPerguntar(string tema)
	{
		var pagina = await f.AbrirEdicaoAsync(tema);
		var mensagens = ResponderDialogos(pagina, aceitar: true);

		await pagina.GetByRole(AriaRole.Button, new() { Name = "Salvar" }).ClickAsync();

		await Expect(pagina.GetByText("Documento atualizado.")).ToBeVisibleAsync();
		mensagens.Should().BeEmpty();
	}
}
