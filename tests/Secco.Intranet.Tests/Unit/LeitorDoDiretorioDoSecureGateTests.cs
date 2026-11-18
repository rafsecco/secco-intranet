using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Infrastructure.Diretorio;
using Secco.SDK.ClientCredentials;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LeitorDoDiretorioDoSecureGateTests
{
	private static readonly Guid TenantDaRequisicao = Guid.NewGuid();
	private static readonly Uri Base = new("https://securegate.exemplo.local/");

	private sealed record Pessoa(
		Guid Id,
		string Email,
		bool IsActive = true,
		string? JobTitle = null,
		string? Department = null,
		string? BusinessPhone = null,
		Guid? ManagerId = null,
		string? AttributesSource = null);

	/// <summary>Responde o token e as páginas da API, guardando o que recebeu.</summary>
	private sealed class ServidorFalso(IReadOnlyList<Pessoa> pessoas, long? total = null) : HttpMessageHandler
	{
		public List<string> Requisicoes { get; } = [];

		public string? CorpoDoToken { get; private set; }

		public List<string?> CabecalhosDeTenant { get; } = [];

		/// <summary>Página (1-based) que deve falhar com 500.</summary>
		public int? FalhaNaPagina { get; init; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var caminho = request.RequestUri!.PathAndQuery;
			Requisicoes.Add(caminho);

			if (caminho.StartsWith("/connect/token", StringComparison.Ordinal))
			{
				CorpoDoToken = await request.Content!.ReadAsStringAsync(cancellationToken);

				return Json("""{"access_token":"tok","expires_in":3600}""");
			}

			CabecalhosDeTenant.Add(request.Headers.TryGetValues("X-Tenant-Id", out var valores) ? valores.Single() : null);

			var consulta = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
			var pagina = int.Parse(consulta["page"] ?? "1");
			var tamanho = int.Parse(consulta["size"] ?? "100");

			if (pagina == FalhaNaPagina)
			{
				return new HttpResponseMessage(HttpStatusCode.InternalServerError);
			}

			var itens = pessoas.Skip((pagina - 1) * tamanho).Take(tamanho);
			var totalLido = total ?? pessoas.Count;

			return Json(JsonSerializer.Serialize(new
			{
				items = itens.Select(p => new
				{
					id = p.Id,
					email = p.Email,
					displayName = (string?)null,
					isActive = p.IsActive,
					isOwner = false,
					jobTitle = p.JobTitle,
					department = p.Department,
					businessPhone = p.BusinessPhone,
					managerId = p.ManagerId,
					attributesSource = p.AttributesSource,
				}),
				page = pagina,
				size = tamanho,
				totalCount = totalLido,
				totalPages = (int)Math.Ceiling(totalLido / (double)tamanho),
			}));
		}

		private static HttpResponseMessage Json(string corpo) => new(HttpStatusCode.OK)
		{
			Content = new StringContent(corpo, Encoding.UTF8, "application/json"),
		};
	}

	private static LeitorDoDiretorioDoSecureGate Montar(ServidorFalso servidor)
	{
		var handler = LeitorDoDiretorioDoSecureGate.CriarHandler(Base.ToString(), "client", "segredo", new SeccoAccessTokenStore());
		handler.InnerHandler = servidor;

		return new LeitorDoDiretorioDoSecureGate(new HttpClient(handler) { BaseAddress = Base });
	}

	private static List<Pessoa> Pessoas(int quantidade) =>
		[.. Enumerable.Range(0, quantidade).Select(i => new Pessoa(Guid.NewGuid(), $"p{i}@x.com"))];

	[Fact]
	public async Task Listar_PercorreTodasAsPaginas()
	{
		var servidor = new ServidorFalso(Pessoas(150));

		var lidas = await Montar(servidor).ListarAtivosAsync(TenantDaRequisicao, CancellationToken.None);

		lidas.Should().HaveCount(150);
		servidor.Requisicoes.Count(r => r.StartsWith("/api/v1/directory/people", StringComparison.Ordinal)).Should().Be(2);
	}

	[Fact]
	public async Task Listar_SoAtivos()
	{
		var ativa = new Pessoa(Guid.NewGuid(), "a@x.com");
		var inativa = new Pessoa(Guid.NewGuid(), "i@x.com", IsActive: false);

		var lidas = await Montar(new ServidorFalso([ativa, inativa])).ListarAtivosAsync(TenantDaRequisicao, CancellationToken.None);

		lidas.Select(u => u.Id).Should().Equal(ativa.Id);
	}

	[Fact]
	public async Task Listar_AtributosSoComOrigemDirectory()
	{
		var gestor = Guid.NewGuid();
		var doDiretorio = new Pessoa(Guid.NewGuid(), "d@x.com", JobTitle: "Analista", Department: "TI", BusinessPhone: "123", ManagerId: gestor, AttributesSource: "directory");
		var semOrigem = new Pessoa(Guid.NewGuid(), "s@x.com", JobTitle: "Analista", Department: "TI", BusinessPhone: "123", ManagerId: gestor);

		var lidas = await Montar(new ServidorFalso([doDiretorio, semOrigem])).ListarAtivosAsync(TenantDaRequisicao, CancellationToken.None);

		var com = lidas.Single(u => u.Id == doDiretorio.Id).Entra;
		com.Should().NotBeNull();
		com!.Cargo.Should().Be("Analista");
		com.Departamento.Should().Be("TI");
		com.Telefone.Should().Be("123");
		com.GestorUsuarioId.Should().Be(gestor);
		lidas.Single(u => u.Id == semOrigem.Id).Entra.Should().BeNull("sem origem directory, o campo não é do Entra");
	}

	[Fact]
	public async Task Listar_BrancoViraNulo()
	{
		var pessoa = new Pessoa(Guid.NewGuid(), "b@x.com", JobTitle: "   ", Department: "  TI  ", BusinessPhone: "", AttributesSource: "directory");

		var lidas = await Montar(new ServidorFalso([pessoa])).ListarAtivosAsync(TenantDaRequisicao, CancellationToken.None);

		var entra = lidas.Single().Entra!;
		entra.Cargo.Should().BeNull();
		entra.Departamento.Should().Be("TI");
		entra.Telefone.Should().BeNull();
	}

	[Fact]
	public async Task Listar_PedeEscopoDirectoryRead()
	{
		var servidor = new ServidorFalso(Pessoas(1));

		await Montar(servidor).ListarAtivosAsync(TenantDaRequisicao, CancellationToken.None);

		servidor.CorpoDoToken.Should().Contain("scope=directory%3Aread");
	}

	[Fact]
	public async Task Listar_FalhaNaSegundaPagina_Lanca()
	{
		var servidor = new ServidorFalso(Pessoas(150)) { FalhaNaPagina = 2 };

		var acao = () => Montar(servidor).ListarAtivosAsync(TenantDaRequisicao, CancellationToken.None);

		await acao.Should().ThrowAsync<Exception>("o leitor nunca devolve lista parcial");
	}

	[Fact]
	public async Task Listar_EnviaXTenantIdDoTenantDaRequisicao()
	{
		var servidor = new ServidorFalso(Pessoas(150));

		await Montar(servidor).ListarAtivosAsync(TenantDaRequisicao, CancellationToken.None);

		servidor.CabecalhosDeTenant.Should().HaveCount(2);
		servidor.CabecalhosDeTenant.Should().AllBeEquivalentTo(TenantDaRequisicao.ToString("D"),
			"as duas páginas precisam carregar o tenant, para o SecureGate recusar divergência com o token");
	}
}
