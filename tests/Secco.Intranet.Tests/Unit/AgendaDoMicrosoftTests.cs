using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application.Salas.Agenda;
using Secco.Intranet.Infrastructure.Salas.Microsoft;
using Xunit;
using static Secco.Intranet.Tests.Unit.TokenDoMicrosoftTests;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Chamadas ao Microsoft Graph e a tradução das respostas em desfechos, sem rede de verdade.</summary>
public class AgendaDoMicrosoftTests
{
	private static readonly EventoDeAgenda Evento = new(
		"7e163156-7762-4beb-a1c6-729ea81755a7",
		"Alinhamento",
		"Reservado por Ana\n\nPauta",
		"Sala Azul",
		new DateTimeOffset(2030, 3, 12, 14, 0, 0, TimeSpan.FromHours(-3)),
		new DateTimeOffset(2030, 3, 12, 15, 30, 0, TimeSpan.FromHours(-3)),
		[new ParticipanteDeAgenda("Ana Teste", "ana@exemplo.test"), new ParticipanteDeAgenda("Bia", "bia@exemplo.test")],
		"sala.azul@exemplo.test");

	// Um único manipulador atende token e Graph, na ordem do roteiro.
	private static (AgendaDoMicrosoft Agenda, ManipuladorGravador Manipulador) Monta(Action<ManipuladorGravador> roteiro)
	{
		var manipulador = new ManipuladorGravador();
		roteiro(manipulador);
		var http = new HttpClient(manipulador);
		var opcoes = Opcoes();
		return (new AgendaDoMicrosoft(http, new TokenDoMicrosoft(http, opcoes), opcoes), manipulador);
	}

	private static void ComToken(ManipuladorGravador m, string token = "token-1") =>
		m.Responde(HttpStatusCode.OK, RespostaDeToken(token));

	[Fact]
	public async Task Criar_MontaOPedido()
	{
		var (agenda, m) = Monta(r => { ComToken(r); r.Responde(HttpStatusCode.Created, """{"id":"AAMk-1="}"""); });

		await agenda.CriarAsync(Evento);

		var pedido = m.Pedidos[1];
		pedido.Verbo.Should().Be(HttpMethod.Post);
		pedido.Endereco.AbsoluteUri.Should().Be("https://graph.microsoft.com/v1.0/users/reservas%40exemplo.test/events");
		pedido.Autorizacao.Should().Be("Bearer token-1");

		using var json = JsonDocument.Parse(pedido.Corpo);
		var raiz = json.RootElement;
		raiz.GetProperty("subject").GetString().Should().Be("Alinhamento");
		raiz.GetProperty("body").GetProperty("contentType").GetString().Should().Be("text");
		raiz.GetProperty("body").GetProperty("content").GetString().Should().Be("Reservado por Ana\n\nPauta");
		raiz.GetProperty("start").GetProperty("dateTime").GetString().Should().Be("2030-03-12T17:00:00");
		raiz.GetProperty("start").GetProperty("timeZone").GetString().Should().Be("UTC");
		raiz.GetProperty("end").GetProperty("dateTime").GetString().Should().Be("2030-03-12T18:30:00");
		raiz.GetProperty("end").GetProperty("timeZone").GetString().Should().Be("UTC");
		raiz.GetProperty("location").GetProperty("displayName").GetString().Should().Be("Sala Azul");
		raiz.GetProperty("transactionId").GetString().Should().Be("7e163156-7762-4beb-a1c6-729ea81755a7");

		var participantes = raiz.GetProperty("attendees").EnumerateArray().ToList();
		participantes.Should().HaveCount(3);
		participantes[0].GetProperty("type").GetString().Should().Be("required");
		participantes[0].GetProperty("emailAddress").GetProperty("address").GetString().Should().Be("ana@exemplo.test");
		participantes[1].GetProperty("type").GetString().Should().Be("required");
		participantes[2].GetProperty("type").GetString().Should().Be("resource");
		participantes[2].GetProperty("emailAddress").GetProperty("address").GetString().Should().Be("sala.azul@exemplo.test");
	}

	[Fact]
	public async Task Criar_SemEmailDaSala_NaoLevaRecurso()
	{
		var (agenda, m) = Monta(r => { ComToken(r); r.Responde(HttpStatusCode.Created, """{"id":"x"}"""); });

		await agenda.CriarAsync(Evento with { EmailDaSala = null });

		m.Pedidos[1].Corpo.Should().NotContain("\"resource\"");
	}

	[Fact]
	public async Task Criar_DevolveOIdDoEvento()
	{
		var (agenda, _) = Monta(r => { ComToken(r); r.Responde(HttpStatusCode.Created, """{"id":"AAMk-1=","subject":"x"}"""); });

		var resultado = await agenda.CriarAsync(Evento);

		resultado.Should().Be(new ResultadoDaAgenda(DesfechoDaAgenda.Feito, "AAMk-1="));
	}

	[Fact]
	public async Task Criar_SemIdNaResposta_FalhaPassageira()
	{
		var (agenda, _) = Monta(r => { ComToken(r); r.Responde(HttpStatusCode.Created, "{}"); });

		(await agenda.CriarAsync(Evento)).Desfecho.Should().Be(DesfechoDaAgenda.FalhaPassageira);
	}

	[Fact]
	public async Task Atualizar_UsaPatchNoEvento()
	{
		var (agenda, m) = Monta(r => { ComToken(r); r.Responde(HttpStatusCode.OK, """{"id":"AAMk-1="}"""); });

		var resultado = await agenda.AtualizarAsync("AAMk-1=", Evento);

		resultado.Desfecho.Should().Be(DesfechoDaAgenda.Feito);
		var pedido = m.Pedidos[1];
		pedido.Verbo.Should().Be(HttpMethod.Patch);
		pedido.Endereco.AbsoluteUri.Should().Be("https://graph.microsoft.com/v1.0/users/reservas%40exemplo.test/events/AAMk-1%3D");
		// A documentação não deixa alterar a transactionId depois de criado.
		pedido.Corpo.Should().NotContain("transactionId");
	}

	[Fact]
	public async Task Apagar_UsaDelete()
	{
		var (agenda, m) = Monta(r => { ComToken(r); r.Responde(HttpStatusCode.NoContent, string.Empty); });

		var resultado = await agenda.ApagarAsync("AAMk-1=");

		resultado.Desfecho.Should().Be(DesfechoDaAgenda.Feito);
		m.Pedidos[1].Verbo.Should().Be(HttpMethod.Delete);
		m.Pedidos[1].Corpo.Should().BeEmpty();
	}

	[Fact]
	public async Task Apagar_404_EhFeito()
	{
		var (agenda, _) = Monta(r => { ComToken(r); r.Responde(HttpStatusCode.NotFound); });

		(await agenda.ApagarAsync("x")).Desfecho.Should().Be(DesfechoDaAgenda.Feito);
	}

	[Theory]
	[InlineData(HttpStatusCode.RequestTimeout, DesfechoDaAgenda.FalhaPassageira)]
	[InlineData(HttpStatusCode.TooManyRequests, DesfechoDaAgenda.FalhaPassageira)]
	[InlineData(HttpStatusCode.InternalServerError, DesfechoDaAgenda.FalhaPassageira)]
	[InlineData(HttpStatusCode.BadGateway, DesfechoDaAgenda.FalhaPassageira)]
	[InlineData(HttpStatusCode.ServiceUnavailable, DesfechoDaAgenda.FalhaPassageira)]
	[InlineData(HttpStatusCode.BadRequest, DesfechoDaAgenda.FalhaDefinitiva)]
	[InlineData(HttpStatusCode.Forbidden, DesfechoDaAgenda.FalhaDefinitiva)]
	[InlineData(HttpStatusCode.NotFound, DesfechoDaAgenda.FalhaDefinitiva)]
	public async Task Resposta_ViraDesfecho_NaCriacaoEAtualizacao(HttpStatusCode codigo, DesfechoDaAgenda esperado)
	{
		var (agenda, _) = Monta(r => { ComToken(r); r.Responde(codigo); r.Responde(codigo); });

		var criar = await agenda.CriarAsync(Evento);
		var atualizar = await agenda.AtualizarAsync("x", Evento);

		criar.Desfecho.Should().Be(esperado);
		atualizar.Desfecho.Should().Be(esperado);
		criar.Motivo.Should().NotBeNullOrWhiteSpace();
	}

	[Theory]
	[InlineData(HttpStatusCode.Forbidden, DesfechoDaAgenda.FalhaDefinitiva)]
	[InlineData(HttpStatusCode.InternalServerError, DesfechoDaAgenda.FalhaPassageira)]
	[InlineData(HttpStatusCode.TooManyRequests, DesfechoDaAgenda.FalhaPassageira)]
	public async Task Resposta_ViraDesfecho_NaExclusao(HttpStatusCode codigo, DesfechoDaAgenda esperado)
	{
		var (agenda, _) = Monta(r => { ComToken(r); r.Responde(codigo); });

		(await agenda.ApagarAsync("x")).Desfecho.Should().Be(esperado);
	}

	[Fact]
	public async Task Motivo_DaPermissaoNegada_EhLegivel()
	{
		var (agenda, _) = Monta(r => { ComToken(r); r.Responde(HttpStatusCode.Forbidden); });

		(await agenda.CriarAsync(Evento)).Motivo.Should().Be("A agenda recusou a permissão (403).");
	}

	[Fact]
	public async Task Resposta401_RenovaOTokenERepeteUmaVez()
	{
		var (agenda, m) = Monta(r =>
		{
			ComToken(r, "token-1");
			r.Responde(HttpStatusCode.Unauthorized);
			ComToken(r, "token-2");
			r.Responde(HttpStatusCode.Created, """{"id":"AAMk"}""");
		});

		var resultado = await agenda.CriarAsync(Evento);

		resultado.Should().Be(new ResultadoDaAgenda(DesfechoDaAgenda.Feito, "AAMk"));
		m.Pedidos.Should().HaveCount(4);
		m.Pedidos[1].Autorizacao.Should().Be("Bearer token-1");
		m.Pedidos[3].Autorizacao.Should().Be("Bearer token-2");
		m.Pedidos[3].Corpo.Should().Be(m.Pedidos[1].Corpo);
	}

	[Fact]
	public async Task Resposta401_DepoisDaRepeticao_FalhaDefinitiva()
	{
		var (agenda, m) = Monta(r =>
		{
			ComToken(r, "token-1");
			r.Responde(HttpStatusCode.Unauthorized);
			ComToken(r, "token-2");
			r.Responde(HttpStatusCode.Unauthorized);
		});

		var resultado = await agenda.CriarAsync(Evento);

		resultado.Desfecho.Should().Be(DesfechoDaAgenda.FalhaDefinitiva);
		m.Pedidos.Should().HaveCount(4);
	}

	[Fact]
	public async Task TokenEmCache_ServeVariasChamadas()
	{
		var (agenda, m) = Monta(r =>
		{
			ComToken(r);
			r.Responde(HttpStatusCode.Created, """{"id":"a"}""");
			r.Responde(HttpStatusCode.NoContent, string.Empty);
		});

		await agenda.CriarAsync(Evento);
		await agenda.ApagarAsync("a");

		m.Pedidos.Count(p => p.Endereco.Host == "login.microsoftonline.com").Should().Be(1);
	}

	[Fact]
	public async Task Token_CredencialRecusada_FalhaDefinitiva()
	{
		var (agenda, m) = Monta(r => r.Responde(HttpStatusCode.BadRequest, """{"error":"invalid_client"}"""));

		var resultado = await agenda.CriarAsync(Evento);

		resultado.Desfecho.Should().Be(DesfechoDaAgenda.FalhaDefinitiva);
		m.Pedidos.Should().HaveCount(1);
	}

	[Fact]
	public async Task TempoEsgotado_FalhaPassageira()
	{
		var (agenda, _) = Monta(r => { ComToken(r); r.Lanca(new TaskCanceledException("tempo esgotado")); });

		var resultado = await agenda.CriarAsync(Evento);

		resultado.Should().Be(new ResultadoDaAgenda(DesfechoDaAgenda.FalhaPassageira, Motivo: "A agenda não respondeu."));
	}

	[Fact]
	public async Task ErroDeRede_FalhaPassageira()
	{
		var (agenda, _) = Monta(r => { ComToken(r); r.Lanca(new HttpRequestException("rede")); });

		(await agenda.ApagarAsync("x")).Desfecho.Should().Be(DesfechoDaAgenda.FalhaPassageira);
	}

	[Fact]
	public async Task CancelamentoDoChamador_Propaga()
	{
		using var cancelamento = new CancellationTokenSource();
		var (agenda, _) = Monta(r => { ComToken(r); r.Lanca(new OperationCanceledException(cancelamento.Token)); });
		await cancelamento.CancelAsync();

		var acao = () => agenda.CriarAsync(Evento, cancelamento.Token);

		await acao.Should().ThrowAsync<OperationCanceledException>();
	}

	[Fact]
	public async Task Motivo_NaoCarregaSegredoNemToken()
	{
		var corpoDeErro = $"{{\"error\":{{\"message\":\"token token-1 e segredo {Segredo}\"}}}}";
		var (agenda, _) = Monta(r =>
		{
			ComToken(r, "token-1");
			r.Responde(HttpStatusCode.Forbidden, corpoDeErro);
			r.Lanca(new HttpRequestException($"falha com {Segredo}"));
			r.Responde(HttpStatusCode.InternalServerError, corpoDeErro);
		});

		var resultados = new[]
		{
			await agenda.CriarAsync(Evento),
			await agenda.AtualizarAsync("x", Evento),
			await agenda.ApagarAsync("x")
		};

		foreach (var resultado in resultados)
		{
			resultado.Motivo.Should().NotBeNullOrWhiteSpace()
				.And.NotContain(Segredo).And.NotContain("token-1").And.NotContain("message");
		}
	}
}
