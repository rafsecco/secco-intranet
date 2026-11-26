using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Application.Salas.Agenda;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Ciclo de espelhamento das reservas na agenda externa, sem banco (as corridas com o banco real estão em EspelhamentoCorridasTests).</summary>
public class ProcessarEspelhamentoTests
{
	private static readonly DateTimeOffset Agora = new(2027, 3, 15, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Inicio = new(2027, 3, 16, 13, 0, 0, TimeSpan.Zero);

	private readonly ReservaRepositorioFalso _reservas = new();
	private readonly SalaRepositorioFalso _salas = new();
	private readonly AgendaFalsa _agenda = new();
	private readonly FakeTimeProvider _tempo = new(Agora);
	private readonly Sala _sala = new("Sala Aurora", null, null, null, "sala@exemplo.test", 0);

	public ProcessarEspelhamentoTests() => _salas.Salas.Add(_sala);

	private ProcessarEspelhamentoHandler Handler() =>
		new(_reservas, _salas, _agenda, new RelogioDeSalas(_tempo, new SalasOptions()), NullLogger<ProcessarEspelhamentoHandler>.Instance);

	private Reserva NovaReserva(string titulo = "Alinhamento")
	{
		var reserva = new Reserva(
			_sala.Id, Inicio, Inicio.AddHours(1), titulo, null, Guid.NewGuid(), "Ana Souza", "ana@exemplo.test", [], espelhar: true);
		_reservas.Reservas.Add(reserva);

		return reserva;
	}

	[Fact]
	public async Task SemEvento_Cria_EMarcaEspelhada()
	{
		var reserva = NovaReserva();

		var tratadas = await Handler().HandleAsync();

		tratadas.Should().Be(1);
		_agenda.Chamadas.Should().ContainSingle().Which.Operacao.Should().Be("criar");
		_agenda.Chamadas[0].Evento!.ChaveDeIdempotencia.Should().Be(reserva.Id.ToString());
		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
		reserva.EventoExternoId.Should().StartWith("evento-");
	}

	[Fact]
	public async Task ComEvento_Atualiza()
	{
		var reserva = NovaReserva();
		reserva.MarcarEspelhada("evt-9");
		reserva.Alterar(_sala.Id, Inicio, Inicio.AddHours(2), "Novo titulo", null, [], espelhar: true);

		await Handler().HandleAsync();

		var chamada = _agenda.Chamadas.Should().ContainSingle().Which;
		chamada.Operacao.Should().Be("atualizar");
		chamada.EventoId.Should().Be("evt-9");
		chamada.Evento!.Assunto.Should().Be("Novo titulo");
		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
		reserva.EventoExternoId.Should().Be("evt-9");
	}

	[Fact]
	public async Task FalhaPassageira_AgendaNovaTentativa_ENaoApareceAntesDaHora()
	{
		var reserva = NovaReserva();
		_agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.FalhaPassageira, Motivo: "Serviço fora do ar."));

		await Handler().HandleAsync();

		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		reserva.TentativasDeEspelhamento.Should().Be(1);
		reserva.MotivoDaFalha.Should().Be("Serviço fora do ar.");
		reserva.ProximaTentativaEm.Should().Be(Agora.AddMinutes(1));

		(await Handler().HandleAsync()).Should().Be(0, "a espera ainda não venceu");
		_agenda.Chamadas.Should().ContainSingle();

		_tempo.Advance(TimeSpan.FromMinutes(1));

		(await Handler().HandleAsync()).Should().Be(1);
		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
	}

	[Fact]
	public async Task CriouMasNaoGravou_ProximoCicloUsaAMesmaChave()
	{
		// O evento pode ter sido criado e a resposta se perdido: sem id gravado, o próximo ciclo cria
		// de novo e só a chave de idempotência (a mesma) impede o evento duplicado na agenda.
		var reserva = NovaReserva();
		_agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.FalhaPassageira, Motivo: "Tempo esgotado."));

		await Handler().HandleAsync();
		_tempo.Advance(TimeSpan.FromMinutes(1));
		await Handler().HandleAsync();

		_agenda.Chamadas.Should().HaveCount(2).And.OnlyContain(c => c.Operacao == "criar");
		_agenda.Chamadas.Select(c => c.Evento!.ChaveDeIdempotencia).Distinct().Should().ContainSingle()
			.Which.Should().Be(reserva.Id.ToString());
	}

	[Fact]
	public async Task FalhaDefinitiva_MarcaFalhouComMotivo()
	{
		var reserva = NovaReserva();
		_agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.FalhaDefinitiva, Motivo: "Permissão negada."));

		await Handler().HandleAsync();

		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Falhou);
		reserva.MotivoDaFalha.Should().Be("Permissão negada.");
		(await Handler().HandleAsync()).Should().Be(0, "reserva que falhou sai da fila");
	}

	[Fact]
	public async Task UmaFalha_NaoInterrompeAsOutras()
	{
		var primeira = NovaReserva("Primeira");
		var segunda = NovaReserva("Segunda");
		var terceira = NovaReserva("Terceira");
		_agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.FalhaDefinitiva, Motivo: "Caixa inexistente."));

		var tratadas = await Handler().HandleAsync();

		tratadas.Should().Be(3);
		primeira.Espelhamento.Should().Be(EstadoDoEspelhamento.Falhou);
		segunda.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
		terceira.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
		_reservas.Gravacoes.Should().Be(3, "cada reserva é gravada logo depois de tratada");
	}

	[Fact]
	public async Task ExcecaoDaAgenda_ViraFalhaPassageira_SemVazarOTexto()
	{
		var reserva = NovaReserva();
		_agenda.Lancar = new InvalidOperationException("segredo-que-nao-pode-vazar");

		await Handler().HandleAsync();

		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		reserva.TentativasDeEspelhamento.Should().Be(1);
		reserva.MotivoDaFalha.Should().NotContain("segredo");
	}

	[Fact]
	public async Task ProcessaNoMaximoVinteReservasPorCiclo()
	{
		for (var i = 0; i < 25; i++)
		{
			NovaReserva($"Reserva {i}");
		}

		(await Handler().HandleAsync()).Should().Be(20);
		(await Handler().HandleAsync()).Should().Be(5);
	}

	[Fact]
	public async Task ExclusaoPendente_Feita_SaiDaFila()
	{
		_reservas.Exclusoes.Add(new ExclusaoDeEventoPendente("evt-1"));

		var tratadas = await Handler().HandleAsync();

		tratadas.Should().Be(1);
		var chamada = _agenda.Chamadas.Should().ContainSingle().Which;
		chamada.Operacao.Should().Be("apagar");
		chamada.EventoId.Should().Be("evt-1");
		_reservas.Exclusoes.Should().BeEmpty();
	}

	[Fact]
	public async Task ExclusaoPendente_FalhaPassageira_EsperaEVoltaDepois()
	{
		var pendencia = new ExclusaoDeEventoPendente("evt-1");
		_reservas.Exclusoes.Add(pendencia);
		_agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.FalhaPassageira, Motivo: "Limite de chamadas."));

		await Handler().HandleAsync();

		pendencia.Tentativas.Should().Be(1);
		pendencia.ProximaTentativaEm.Should().Be(Agora.AddMinutes(1));
		(await Handler().HandleAsync()).Should().Be(0);

		_tempo.Advance(TimeSpan.FromMinutes(1));
		(await Handler().HandleAsync()).Should().Be(1);
		_reservas.Exclusoes.Should().BeEmpty();
	}

	[Fact]
	public async Task ExclusaoPendente_FalhaDefinitiva_FicaMarcadaESaiDaFila()
	{
		var pendencia = new ExclusaoDeEventoPendente("evt-1");
		_reservas.Exclusoes.Add(pendencia);
		_agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.FalhaDefinitiva, Motivo: "Permissão negada."));

		await Handler().HandleAsync();

		pendencia.Falhou.Should().BeTrue();
		pendencia.MotivoDaFalha.Should().Be("Permissão negada.");
		_reservas.Exclusoes.Should().Contain(pendencia, "a pendência fica registrada como falha");
		(await Handler().HandleAsync()).Should().Be(0);
	}

	[Fact]
	public async Task TentarDeNovo_DeFalhou_VoltaParaAFila_NaoAdmin_NotFound()
	{
		var reserva = NovaReserva();
		reserva.MarcarFalhaDefinitiva("Permissão negada.");
		var handler = new TentarEspelharDeNovoHandler(_reservas);

		var negado = await handler.HandleAsync(reserva.Id, new QuemPede(reserva.ReservadoPorId, false));
		var inexistente = await handler.HandleAsync(Guid.NewGuid(), new QuemPede(Guid.NewGuid(), true));

		negado.IsFailure.Should().BeTrue();
		negado.Error.Should().Be(IntranetErrors.Salas.ReservaNotFound);
		inexistente.Error.Should().Be(IntranetErrors.Salas.ReservaNotFound);
		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Falhou);

		var feito = await handler.HandleAsync(reserva.Id, new QuemPede(Guid.NewGuid(), true));

		feito.IsSuccess.Should().BeTrue();
		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		reserva.TentativasDeEspelhamento.Should().Be(0);
		(await Handler().HandleAsync()).Should().Be(1);
	}

	[Fact]
	public async Task TentarDeNovo_ReservaQueNaoFalhou_Recusa()
	{
		var reserva = NovaReserva();

		var resultado = await new TentarEspelharDeNovoHandler(_reservas).HandleAsync(reserva.Id, new QuemPede(Guid.NewGuid(), true));

		resultado.Error.Should().Be(IntranetErrors.Salas.EspelhamentoNaoFalhou);
		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
	}
}
