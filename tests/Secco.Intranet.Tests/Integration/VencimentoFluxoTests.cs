using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Publicacoes.Notificacao;
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
/// Telas do setor para o recurso Vencimentos sobre o host completo: lista pelo caminho da
/// árvore, detalhe, cadastro, renovação e encerramento, e a recusa quando o recurso está
/// desligado, o item é de outro setor ou falta o token antifalsificação.
/// </summary>
public class VencimentoFluxoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private readonly DiretorioDeUsuariosMutavel _roles = new();
	private readonly UsuariosParaDiretorioFalso _nomes = new();
	private WebApplicationFactory<Program> _fabrica = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();

		factory.UsuariosDoDiretorio = _nomes;
		_fabrica = factory.WithWebHostBuilder(builder =>
			builder.ConfigureServices(services =>
			{
				services.AddSingleton(_roles);
				services.AddScoped<IDiretorioDeUsuarios>(provider => provider.GetRequiredService<DiretorioDeUsuariosMutavel>());
			}));
	}

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;
		factory.ResolvedorDePermissoes = null;

		return Task.CompletedTask;
	}

	private sealed class DiretorioDeUsuariosMutavel : IDiretorioDeUsuarios
	{
		public List<UsuarioDoTenant> Usuarios { get; } = [];

		public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<UsuarioDoTenant>>([.. Usuarios]);
	}

	private HttpClient CriarCliente(string role = "intranet-admin")
	{
		var client = _fabrica.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, role);

		return client;
	}

	private async Task<string> CriarSetorAsync(bool comVencimentos = true)
	{
		var slug = $"venc-{Guid.NewGuid():N}"[..20];

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criado = await escopo.ServiceProvider
			.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug, HabilitarVencimentos: comVencimentos));

		criado.IsSuccess.Should().BeTrue();

		return slug;
	}

	/// <summary>
	/// Data daqui a <paramref name="dias"/> dias no fuso do produto (o mesmo do
	/// <c>RelogioDeVencimentos</c>), não em UTC: perto da meia-noite os dois dias diferem.
	/// </summary>
	private static string Data(int dias)
	{
		var fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
		var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, fuso).DateTime);

		return hoje.AddDays(dias).ToString("yyyy-MM-dd");
	}

	/// <summary>Cadastra pela tela e devolve o id do item, tirado da URL do detalhe para onde o POST redireciona.</summary>
	private static async Task<(Guid Id, string Pagina)> CadastrarAsync(
		HttpClient client, string slug, string titulo, int dias, Guid? responsavel = null, string antecedencias = "30, 7, 1")
	{
		var token = await TokenAsync(client, $"/{slug}/vencimentos/novo");

		var resposta = await client.PostAsync($"/{slug}/vencimentos/novo", Form(
			("Form.Titulo", titulo),
			("Form.Descricao", "Descrição do item"),
			("Form.DataVencimento", Data(dias)),
			("Form.ResponsavelId", responsavel?.ToString() ?? string.Empty),
			("Form.Antecedencias", antecedencias),
			("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		var id = Guid.Parse(resposta.RequestMessage!.RequestUri!.Segments.Last());

		return (id, Decodificar(await resposta.Content.ReadAsStringAsync()));
	}

	[Fact]
	public async Task Lista_PeloCaminhoDaArvore_MostraOsItensOrdenadosPorData()
	{
		var slug = await CriarSetorAsync();
		var client = CriarCliente();

		await CadastrarAsync(client, slug, "Certificado depois", 40);
		await CadastrarAsync(client, slug, "Alvara antes", 3);

		var pagina = Decodificar(await client.GetStringAsync($"/{slug}/vencimentos"));

		var antes = pagina.IndexOf("Alvara antes", StringComparison.Ordinal);
		var depois = pagina.IndexOf("Certificado depois", StringComparison.Ordinal);

		antes.Should().BePositive();
		depois.Should().BeGreaterThan(antes, "a lista vem por data de vencimento");
		pagina.Should().Contain("A vencer", "o item de 3 dias está na janela dos 30");
		pagina.Should().Contain("Em dia", "o item de 40 dias está fora da janela");
	}

	[Fact]
	public async Task RecursoDesligado_404NaLeituraENaEscrita()
	{
		var semItem = await CriarSetorAsync(comVencimentos: false);
		var desativado = await CriarSetorAsync();
		var client = CriarCliente();

		var (id, _) = await CadastrarAsync(client, desativado, "Contrato", 20);
		var token = await TokenAsync(client, "/Setores/Create");

		await using (var escopo = factory.Services.CreateAsyncScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

			var setor = await escopo.ServiceProvider.GetRequiredService<GetSetorBySlugHandler>().HandleAsync(desativado);
			var arvore = await escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>().ListarPorSetorAsync(setor.Value.Id);
			var item = arvore.Single(i => i.Tipo == TipoDeItemMenu.Vencimentos);

			(await escopo.ServiceProvider.GetRequiredService<AtivarDesativarItemMenuHandler>()
				.HandleAsync(item.Id, ativar: false)).IsSuccess.Should().BeTrue();
		}

		foreach (var alvo in new[] { semItem, desativado })
		{
			(await client.GetAsync($"/{alvo}/vencimentos")).StatusCode.Should().Be(HttpStatusCode.NotFound);
			(await client.GetAsync($"/{alvo}/vencimentos/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
			(await client.GetAsync($"/{alvo}/vencimentos/novo")).StatusCode.Should().Be(HttpStatusCode.NotFound);

			(await client.PostAsync($"/{alvo}/vencimentos/novo", Form(
				("Form.Titulo", "X"), ("Form.DataVencimento", Data(5)), ("Form.Antecedencias", "7"),
				("__RequestVerificationToken", token)))).StatusCode.Should().Be(HttpStatusCode.NotFound);

			(await client.PostAsync($"/{alvo}/vencimentos/{id}/renovar", Form(
				("novaData", Data(60)), ("__RequestVerificationToken", token)))).StatusCode.Should().Be(HttpStatusCode.NotFound);
		}
	}

	[Fact]
	public async Task Criar_Renovar_Encerrar_FluxoCompleto()
	{
		var slug = await CriarSetorAsync();
		var client = CriarCliente();

		var (id, detalhe) = await CadastrarAsync(client, slug, "Licença \"Sanitária\"", 3);

		detalhe.Should().Contain("Vencimento \"Licença \"Sanitária\"\" cadastrado.");
		detalhe.Should().Contain("A vencer");

		var token = await TokenAsync(client, $"/{slug}/vencimentos/{id}");
		var renovado = await client.PostAsync($"/{slug}/vencimentos/{id}/renovar", Form(
			("novaData", Data(90)), ("observacao", "Renovada no cartório"), ("__RequestVerificationToken", token)));

		renovado.StatusCode.Should().Be(HttpStatusCode.OK);

		var pagina = Decodificar(await renovado.Content.ReadAsStringAsync());
		pagina.Should().Contain("Vencimento renovado");
		pagina.Should().Contain("Em dia");
		pagina.Should().Contain("Renovada no cartório");

		var encerrado = await client.PostAsync($"/{slug}/vencimentos/{id}/encerrar", Form(("__RequestVerificationToken", token)));

		encerrado.StatusCode.Should().Be(HttpStatusCode.OK);

		pagina = Decodificar(await encerrado.Content.ReadAsStringAsync());
		pagina.Should().Contain("Vencimento encerrado");
		pagina.Should().Contain("Encerrado");

		var lista = Decodificar(await client.GetStringAsync($"/{slug}/vencimentos"));
		lista.Should().NotContain("Sanitária", "encerrado sai da lista padrão");

		(await client.GetStringAsync($"/{slug}/vencimentos?encerrados=true")).Should().Contain("Sanit");
	}

	[Fact]
	public async Task Detalhe_TemOFormularioDeEncerrarComConfirmacao()
	{
		var slug = await CriarSetorAsync();
		var client = CriarCliente();

		var (_, detalhe) = await CadastrarAsync(client, slug, "Seguro", 15);

		detalhe.Should().Contain("data-confirmar=\"Encerrar este vencimento? Os avisos param.\"");
	}

	[Fact]
	public async Task ItemDeOutroSetor_404()
	{
		var dono = await CriarSetorAsync();
		var outro = await CriarSetorAsync();
		var client = CriarCliente();

		var (id, _) = await CadastrarAsync(client, dono, "Do dono", 10);
		var token = await TokenAsync(client, $"/{outro}/vencimentos/novo");

		(await client.GetAsync($"/{outro}/vencimentos/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.GetAsync($"/{outro}/vencimentos/{id}/editar")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.PostAsync($"/{outro}/vencimentos/{id}/renovar", Form(
			("novaData", Data(60)), ("__RequestVerificationToken", token)))).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.PostAsync($"/{outro}/vencimentos/{id}/encerrar", Form(
			("__RequestVerificationToken", token)))).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task PostSemAntiforgery_Recusado()
	{
		var slug = await CriarSetorAsync();
		var client = CriarCliente();

		var (id, _) = await CadastrarAsync(client, slug, "Item", 10);

		(await client.PostAsync($"/{slug}/vencimentos/novo", Form(
			("Form.Titulo", "X"), ("Form.DataVencimento", Data(5)), ("Form.Antecedencias", "7")))).StatusCode
			.Should().Be(HttpStatusCode.BadRequest);
		(await client.PostAsync($"/{slug}/vencimentos/{id}/encerrar", Form())).StatusCode
			.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Detalhe_ResponsavelSemRoleDoSetor_MostraSemAcesso()
	{
		var slug = await CriarSetorAsync();
		var client = CriarCliente();
		var responsavel = Guid.NewGuid();

		_roles.Usuarios.Add(new UsuarioDoTenant(responsavel, "maria@empresa.com", [$"{slug}-user"]));
		_nomes.Com(responsavel, "maria@empresa.com", "Maria Souza");

		var formulario = Decodificar(await client.GetStringAsync($"/{slug}/vencimentos/novo"));
		formulario.Should().Contain("Maria Souza", "o select usa o nome de exibição do diretório");

		var (id, detalhe) = await CadastrarAsync(client, slug, "Com dono", 10, responsavel);

		detalhe.Should().Contain("Maria Souza");
		detalhe.Should().NotContain("sem acesso ao setor");

		_roles.Usuarios.Clear();

		var depois = Decodificar(await client.GetStringAsync($"/{slug}/vencimentos/{id}"));
		depois.Should().Contain("sem acesso ao setor");
	}

	[Fact]
	public async Task Criar_AntecedenciaInvalida_ReapresentaOFormularioComErro()
	{
		var slug = await CriarSetorAsync();
		var client = CriarCliente();
		var token = await TokenAsync(client, $"/{slug}/vencimentos/novo");

		var resposta = await client.PostAsync($"/{slug}/vencimentos/novo", Form(
			("Form.Titulo", "Item"), ("Form.DataVencimento", Data(10)), ("Form.Antecedencias", "7, abc"),
			("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync())
			.Should().Contain("Informe os dias separados por vírgula, como 30, 7, 1.");
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().EndWith("/novo");
	}

	[Fact]
	public async Task Editar_ItemEncerrado_RedirecionaParaODetalhe()
	{
		var slug = await CriarSetorAsync();
		var client = CriarCliente();

		var (id, _) = await CadastrarAsync(client, slug, "Apólice", 20);
		var token = await TokenAsync(client, $"/{slug}/vencimentos/{id}");

		(await client.PostAsync($"/{slug}/vencimentos/{id}/encerrar", Form(("__RequestVerificationToken", token))))
			.StatusCode.Should().Be(HttpStatusCode.OK);

		var resposta = await client.GetAsync($"/{slug}/vencimentos/{id}/editar");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/{slug}/vencimentos/{id}");
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Este vencimento está encerrado.");

		// O POST direto também é recusado, sem abrir caminho para alterar o item.
		var post = await client.PostAsync($"/{slug}/vencimentos/{id}/editar", Form(
			("Form.Titulo", "Alterado"), ("Form.DataVencimento", Data(30)), ("Form.Antecedencias", "7"),
			("__RequestVerificationToken", token)));

		Decodificar(await post.Content.ReadAsStringAsync()).Should().Contain("Um vencimento encerrado não aceita alterações.");
		Decodificar(await client.GetStringAsync($"/{slug}/vencimentos/{id}")).Should().Contain("Apólice").And.NotContain("Alterado");
	}

	/// <summary>
	/// Cliente cuja única Role tem apenas as permissões pedidas, por setor. Cada chamada usa uma
	/// Role nova, porque o resolvedor do SDK guarda o resultado por (tenant, role).
	/// </summary>
	private HttpClient CriarClienteComPermissoes(params (string Slug, string Acao)[] permissoes)
	{
		var role = $"papel-{Guid.NewGuid():N}";
		var resolvedor = new PermissionResolverDeTeste();

		foreach (var (slug, acao) in permissoes)
		{
			resolvedor.ComPermissao(role, acao == "read" ? IntranetPermissoes.Setor.Read(slug) : IntranetPermissoes.Setor.Write(slug));
		}

		factory.ResolvedorDePermissoes = resolvedor;

		return CriarCliente(role);
	}

	[Fact]
	public async Task SemLeitura_404NaListaENoDetalhe()
	{
		var slug = await CriarSetorAsync();
		var (id, _) = await CadastrarAsync(CriarCliente(), slug, "Contrato", 20);
		var client = CriarClienteComPermissoes();

		(await client.GetAsync($"/{slug}/vencimentos")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.GetAsync($"/{slug}/vencimentos/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task SoLeitura_404EmNovoERenovar()
	{
		var soLeitura = await CriarSetorAsync();
		var comEscrita = await CriarSetorAsync();
		var (id, _) = await CadastrarAsync(CriarCliente(), soLeitura, "Contrato", 20);

		// A mesma Role lê o primeiro setor e escreve no segundo. O token antifalsificação sai do
		// formulário do segundo (o detalhe de quem só lê não tem formulário) e vale para o primeiro,
		// porque o token é preso à identidade, não ao setor: o 404 abaixo é da permissão, não do token.
		var client = CriarClienteComPermissoes((soLeitura, "read"), (comEscrita, "read"), (comEscrita, "write"));
		var token = await TokenAsync(client, $"/{comEscrita}/vencimentos/novo");

		(await client.GetAsync($"/{soLeitura}/vencimentos/{id}")).StatusCode.Should().Be(HttpStatusCode.OK, "a leitura vale");
		(await client.GetAsync($"/{soLeitura}/vencimentos/novo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.PostAsync($"/{soLeitura}/vencimentos/{id}/renovar", Form(
			("novaData", Data(90)), ("__RequestVerificationToken", token)))).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
}
