using System.Net;
using System.Text;
using AwesomeAssertions;
using Secco.Intranet.Application.Salas.Agenda;
using Secco.Intranet.Infrastructure.Salas.Microsoft;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Pedido do token de aplicativo ao Microsoft 365, com cache em memória até um minuto antes de vencer.</summary>
public class TokenDoMicrosoftTests
{
	internal const string Segredo = "segredo-super-secreto-123";
	internal const string Tenant = "11111111-1111-1111-1111-111111111111";
	internal const string Cliente = "22222222-2222-2222-2222-222222222222";

	internal sealed class ManipuladorGravador : HttpMessageHandler
	{
		private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _roteiro = new();

		public List<(HttpMethod Verbo, Uri Endereco, string? Autorizacao, string Corpo)> Pedidos { get; } = [];

		public ManipuladorGravador Responde(HttpStatusCode codigo, string corpo = "{}")
		{
			_roteiro.Enqueue(_ => new HttpResponseMessage(codigo) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
			return this;
		}

		public ManipuladorGravador Lanca(Exception excecao)
		{
			_roteiro.Enqueue(_ => throw excecao);
			return this;
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var corpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
			Pedidos.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), corpo));

			return _roteiro.Dequeue()(request);
		}
	}

	// Entrega sempre um cliente sobre o mesmo manipulador gravador e anota os nomes pedidos.
	internal sealed class FabricaFalsa(HttpMessageHandler manipulador) : IHttpClientFactory
	{
		public List<string> Nomes { get; } = [];

		public HttpClient CreateClient(string name)
		{
			Nomes.Add(name);
			return new HttpClient(manipulador, disposeHandler: false);
		}
	}

	internal sealed class RelogioFalso(DateTimeOffset inicio) : TimeProvider
	{
		private DateTimeOffset _agora = inicio;

		public override DateTimeOffset GetUtcNow() => _agora;

		public void Avanca(TimeSpan tempo) => _agora += tempo;
	}

	internal static AgendaOptions Opcoes() => new()
	{
		Provedor = "Microsoft",
		Microsoft = new MicrosoftAgendaOptions
		{
			TenantId = Tenant,
			ClientId = Cliente,
			ClientSecret = Segredo,
			CaixaOrganizadora = "reservas@exemplo.test"
		}
	};

	internal static TokenDoMicrosoft Criar(HttpMessageHandler manipulador, AgendaOptions? opcoes = null, TimeProvider? relogio = null) =>
		new(new FabricaFalsa(manipulador), opcoes ?? Opcoes(), relogio);

	internal static string RespostaDeToken(string token = "token-1", int expira = 3600) =>
		$$"""{"token_type":"Bearer","expires_in":{{expira}},"access_token":"{{token}}"}""";

	[Fact]
	public async Task Token_PedeComCredenciaisDeCliente()
	{
		var manipulador = new ManipuladorGravador().Responde(HttpStatusCode.OK, RespostaDeToken());
		var token = Criar(manipulador);

		var resultado = await token.ObterAsync(CancellationToken.None);

		resultado.Valor.Should().Be("token-1");
		var pedido = manipulador.Pedidos.Single();
		pedido.Verbo.Should().Be(HttpMethod.Post);
		pedido.Endereco.AbsoluteUri.Should().Be($"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/token");
		var campos = pedido.Corpo.Split('&').Select(c => c.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
		campos.Should().Contain("grant_type", "client_credentials")
			.And.Contain("scope", "https://graph.microsoft.com/.default")
			.And.Contain("client_id", Cliente)
			.And.Contain("client_secret", Segredo);
	}

	[Fact]
	public async Task Token_PegaClienteDaFabricaAPedido()
	{
		var manipulador = new ManipuladorGravador()
			.Responde(HttpStatusCode.OK, RespostaDeToken("token-1", 3600))
			.Responde(HttpStatusCode.OK, RespostaDeToken("token-2", 3600));
		var fabrica = new FabricaFalsa(manipulador);
		var relogio = new RelogioFalso(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
		var token = new TokenDoMicrosoft(fabrica, Opcoes(), relogio);

		await token.ObterAsync(CancellationToken.None);
		relogio.Avanca(TimeSpan.FromHours(1));
		await token.ObterAsync(CancellationToken.None);

		fabrica.Nomes.Should().HaveCount(2).And.AllBeEquivalentTo(TokenDoMicrosoft.NomeDoHttpClient);
	}

	[Fact]
	public async Task Token_ReusaAteVencer()
	{
		var relogio = new RelogioFalso(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
		var manipulador = new ManipuladorGravador()
			.Responde(HttpStatusCode.OK, RespostaDeToken("token-1", 3600))
			.Responde(HttpStatusCode.OK, RespostaDeToken("token-2", 3600));
		var token = Criar(manipulador, relogio: relogio);

		(await token.ObterAsync(CancellationToken.None)).Valor.Should().Be("token-1");
		relogio.Avanca(TimeSpan.FromMinutes(58));
		(await token.ObterAsync(CancellationToken.None)).Valor.Should().Be("token-1");
		manipulador.Pedidos.Should().HaveCount(1);

		// Um minuto antes de vencer já conta como vencido.
		relogio.Avanca(TimeSpan.FromMinutes(1));
		(await token.ObterAsync(CancellationToken.None)).Valor.Should().Be("token-2");
		manipulador.Pedidos.Should().HaveCount(2);
	}

	[Fact]
	public async Task Token_Descartado_PedeOutro()
	{
		var manipulador = new ManipuladorGravador()
			.Responde(HttpStatusCode.OK, RespostaDeToken("token-1"))
			.Responde(HttpStatusCode.OK, RespostaDeToken("token-2"));
		var token = Criar(manipulador);

		await token.ObterAsync(CancellationToken.None);
		token.Descartar("token-1");

		(await token.ObterAsync(CancellationToken.None)).Valor.Should().Be("token-2");
	}

	[Fact]
	public async Task Token_DescartarOUltimoRecusado_MantemTokenJaRenovado()
	{
		var relogio = new RelogioFalso(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
		var manipulador = new ManipuladorGravador()
			.Responde(HttpStatusCode.OK, RespostaDeToken("token-1", 3600))
			.Responde(HttpStatusCode.OK, RespostaDeToken("token-2", 3600));
		var token = Criar(manipulador, relogio: relogio);

		(await token.ObterAsync(CancellationToken.None)).Valor.Should().Be("token-1");
		relogio.Avanca(TimeSpan.FromHours(1));
		(await token.ObterAsync(CancellationToken.None)).Valor.Should().Be("token-2");

		// A agenda recusou o token-1 tarde demais: o token-2, já renovado, não pode ser descartado.
		token.Descartar("token-1");

		(await token.ObterAsync(CancellationToken.None)).Valor.Should().Be("token-2");
		manipulador.Pedidos.Should().HaveCount(2);
	}

	[Theory]
	[InlineData(HttpStatusCode.BadRequest)]
	[InlineData(HttpStatusCode.Unauthorized)]
	public async Task Token_CredencialRecusada_FalhaDefinitiva(HttpStatusCode codigo)
	{
		var corpo = $$"""{"error":"invalid_client","error_description":"AADSTS7000215: segredo {{Segredo}} invalido"}""";
		var manipulador = new ManipuladorGravador().Responde(codigo, corpo);
		var token = Criar(manipulador);

		var resultado = await token.ObterAsync(CancellationToken.None);

		resultado.Valor.Should().BeNull();
		resultado.Falha!.Desfecho.Should().Be(DesfechoDaAgenda.FalhaDefinitiva);
		resultado.Falha.Motivo.Should().NotContain(Segredo).And.NotContain("AADSTS");
	}

	[Theory]
	[InlineData(HttpStatusCode.TooManyRequests)]
	[InlineData(HttpStatusCode.ServiceUnavailable)]
	public async Task Token_ServicoFora_FalhaPassageira(HttpStatusCode codigo)
	{
		var token = Criar(new ManipuladorGravador().Responde(codigo));

		(await token.ObterAsync(CancellationToken.None)).Falha!.Desfecho.Should().Be(DesfechoDaAgenda.FalhaPassageira);
	}

	[Fact]
	public async Task Token_RespostaSemToken_FalhaPassageira()
	{
		var token = Criar(new ManipuladorGravador().Responde(HttpStatusCode.OK, "{}"));

		(await token.ObterAsync(CancellationToken.None)).Falha!.Desfecho.Should().Be(DesfechoDaAgenda.FalhaPassageira);
	}

	[Fact]
	public async Task Token_RedeCaida_FalhaPassageira()
	{
		var manipulador = new ManipuladorGravador().Lanca(new HttpRequestException($"conexao com {Segredo} caiu"));
		var token = Criar(manipulador);

		var resultado = await token.ObterAsync(CancellationToken.None);

		resultado.Falha!.Desfecho.Should().Be(DesfechoDaAgenda.FalhaPassageira);
		resultado.Falha.Motivo.Should().NotContain(Segredo);
	}
}
