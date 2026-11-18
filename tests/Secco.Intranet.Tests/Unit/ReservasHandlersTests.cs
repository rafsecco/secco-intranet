using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Casos de uso de reservar, alterar, cancelar e consultar reservas de sala.</summary>
public class ReservasHandlersTests
{
	// 09:00 em São Paulo
	private static readonly DateTimeOffset Agora = new(2027, 3, 15, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateOnly Amanha = new(2027, 3, 16);

	private readonly Guid _anaId = Guid.NewGuid();
	private readonly Guid _betoId = Guid.NewGuid();
	private readonly Guid _carlaId = Guid.NewGuid();
	private readonly ReservaRepositorioFalso _reservas = new();
	private readonly SalaRepositorioFalso _salas = new();
	private readonly UsuariosParaDiretorioFalso _diretorio = new();
	private readonly TrilhaDeAcessoFalsa _trilha = new();
	private readonly EspelhamentoFalso _espelhamento = new();
	private readonly RelogioDeSalas _relogio = new(new FakeTimeProvider(Agora), new SalasOptions());
	private readonly Sala _sala;

	public ReservasHandlersTests()
	{
		_diretorio
			.Com(_anaId, "ana@exemplo.test", "Ana Souza")
			.Com(_betoId, "beto@exemplo.test", "Beto Lima")
			.Com(_carlaId, "carla@exemplo.test");
		_sala = NovaSala("Sala Aurora");
	}

	private QuemPede Ana => new(_anaId, false);

	private Sala NovaSala(string nome, bool ativa = true)
	{
		var sala = new Sala(nome, null, null, null, null, 0);

		if (!ativa)
		{
			sala.Desativar();
		}

		_salas.Salas.Add(sala);

		return sala;
	}

	private ReservarSalaHandler Reservar() => new(_reservas, _salas, _diretorio, _relogio, _espelhamento, _trilha);

	private AlterarReservaHandler Alterar() => new(_reservas, _salas, _diretorio, _relogio, _espelhamento, _trilha);

	private CancelarReservaHandler Cancelar() => new(_reservas, _relogio, _trilha);

	private ObterReservaHandler Obter() => new(_reservas, _salas, _relogio);

	private ReservarSalaCommand Pedido(
		DateOnly? dia = null,
		int inicioH = 10,
		int inicioM = 0,
		int fimH = 11,
		int fimM = 0,
		string? titulo = "Alinhamento",
		string? descricao = null,
		IReadOnlyList<Guid>? convidados = null,
		Guid? salaId = null,
		QuemPede? quem = null) =>
		new(
			salaId ?? _sala.Id,
			dia ?? Amanha,
			new TimeOnly(inicioH, inicioM),
			new TimeOnly(fimH, fimM),
			titulo,
			descricao,
			convidados ?? [],
			quem ?? Ana);

	private static AlterarReservaCommand PedidoDeAlteracao(
		Reserva reserva,
		QuemPede quem,
		Guid? salaId = null,
		int inicioH = 10,
		int fimH = 11,
		string? titulo = "Novo título",
		IReadOnlyList<Guid>? convidados = null) =>
		new(
			reserva.Id,
			salaId ?? reserva.SalaId,
			Amanha,
			new TimeOnly(inicioH, 0),
			new TimeOnly(fimH, 0),
			titulo,
			null,
			convidados ?? [],
			quem);

	/// <summary>Reserva já existente de Ana, amanhã das 10h às 11h (local), salvo outro horário.</summary>
	private Reserva Existente(Sala? sala = null, Guid? donoId = null, DateTimeOffset? inicio = null, string nome = "Ana Souza")
	{
		var ini = inicio ?? new DateTimeOffset(2027, 3, 16, 13, 0, 0, TimeSpan.Zero);
		var reserva = new Reserva(
			(sala ?? _sala).Id, ini, ini.AddHours(1), "Reunião antiga", null, donoId ?? _anaId, nome, null, [], false);
		_reservas.Reservas.Add(reserva);

		return reserva;
	}

	// ---------- Reservar ----------

	[Fact]
	public async Task Reservar_Valida_Grava()
	{
		var resultado = await Reservar().HandleAsync(
			Pedido(titulo: "  Alinhamento  ", descricao: "Pauta", convidados: [_betoId, _carlaId]));

		resultado.IsSuccess.Should().BeTrue();
		var reserva = _reservas.Reservas.Should().ContainSingle().Subject;
		reserva.Id.Should().Be(resultado.Value);
		reserva.Titulo.Should().Be("Alinhamento");
		reserva.Descricao.Should().Be("Pauta");
		reserva.Inicio.Should().Be(new DateTimeOffset(2027, 3, 16, 13, 0, 0, TimeSpan.Zero));
		reserva.Fim.Should().Be(new DateTimeOffset(2027, 3, 16, 14, 0, 0, TimeSpan.Zero));
		reserva.ReservadoPorId.Should().Be(_anaId);
		reserva.ReservadoPorNome.Should().Be("Ana Souza");
		reserva.ReservadoPorEmail.Should().Be("ana@exemplo.test");
		reserva.Convidados.Select(c => c.Nome).Should().BeEquivalentTo("Beto Lima", "carla@exemplo.test");
		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.NaoSeAplica);
		_reservas.InstantesRecebidos.Should().OnlyContain(i => i.Offset == TimeSpan.Zero);
		_reservas.Travas.Should().ContainSingle().Which.Should().Be(_sala.Id);
		_reservas.SobreposicoesForaDaTrava.Should().Be(0);
		var registro = _trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be("reserva.criar");
		registro.RecursoId.Should().Be(reserva.Id.ToString());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("   ")]
	public async Task Reservar_TituloVazio_TituloRequired(string? titulo)
	{
		var resultado = await Reservar().HandleAsync(Pedido(titulo: titulo));

		resultado.Error.Should().Be(IntranetErrors.Salas.TituloRequired);
		_reservas.Reservas.Should().BeEmpty();
		_reservas.Travas.Should().BeEmpty();
	}

	[Fact]
	public async Task Reservar_TituloAcimaDoLimite_TituloTooLong()
	{
		var resultado = await Reservar().HandleAsync(Pedido(titulo: new string('a', Reserva.TituloMaximo + 1)));

		resultado.Error.Code.Should().Be("Intranet.Salas.TituloTooLong");
		_reservas.Reservas.Should().BeEmpty();
	}

	[Fact]
	public async Task Reservar_DescricaoAcimaDoLimite_DescricaoTooLong()
	{
		var resultado = await Reservar().HandleAsync(Pedido(descricao: new string('a', Reserva.DescricaoMaxima + 1)));

		resultado.Error.Code.Should().Be("Intranet.Salas.DescricaoTooLong");
		_reservas.Reservas.Should().BeEmpty();
	}

	[Theory]
	[InlineData(10, 0, 10, 0)]
	[InlineData(11, 0, 10, 0)]
	[InlineData(10, 10, 11, 0)]
	[InlineData(10, 0, 11, 5)]
	public async Task Reservar_HorarioInvalido_HorarioInvalido(int inicioH, int inicioM, int fimH, int fimM)
	{
		var resultado = await Reservar().HandleAsync(Pedido(inicioH: inicioH, inicioM: inicioM, fimH: fimH, fimM: fimM));

		resultado.Error.Should().Be(IntranetErrors.Salas.HorarioInvalido);
		_reservas.Reservas.Should().BeEmpty();
	}

	[Fact]
	public async Task Reservar_HoraLocalInexistente_HorarioInvalido()
	{
		var relogio = new RelogioDeSalas(new FakeTimeProvider(Agora), new SalasOptions { FusoHorario = "America/New_York" });
		var handler = new ReservarSalaHandler(_reservas, _salas, _diretorio, relogio, _espelhamento, _trilha);

		var resultado = await handler.HandleAsync(Pedido(dia: new DateOnly(2028, 3, 12), inicioH: 2, inicioM: 30, fimH: 3));

		resultado.Error.Should().Be(IntranetErrors.Salas.HorarioInvalido);
		_reservas.Reservas.Should().BeEmpty();
	}

	[Fact]
	public async Task Reservar_HoraLocalAmbigua_HorarioInvalido()
	{
		var relogio = new RelogioDeSalas(new FakeTimeProvider(Agora), new SalasOptions { FusoHorario = "America/New_York" });
		var handler = new ReservarSalaHandler(_reservas, _salas, _diretorio, relogio, _espelhamento, _trilha);

		var resultado = await handler.HandleAsync(Pedido(dia: new DateOnly(2027, 11, 7), inicioH: 1, inicioM: 30, fimH: 2));

		resultado.Error.Should().Be(IntranetErrors.Salas.HorarioInvalido);
	}

	[Fact]
	public async Task Reservar_InicioNoPassado_NoPassado()
	{
		var resultado = await Reservar().HandleAsync(Pedido(dia: new DateOnly(2027, 3, 15), inicioH: 8, fimH: 9));

		resultado.Error.Should().Be(IntranetErrors.Salas.NoPassado);
		_reservas.Reservas.Should().BeEmpty();
	}

	[Fact]
	public async Task Reservar_InicioExatamenteAgora_NoPassado()
	{
		var resultado = await Reservar().HandleAsync(Pedido(dia: new DateOnly(2027, 3, 15), inicioH: 9, fimH: 10));

		resultado.Error.Should().Be(IntranetErrors.Salas.NoPassado);
	}

	[Fact]
	public async Task Reservar_SalaInexistente_NotFound()
	{
		var resultado = await Reservar().HandleAsync(Pedido(salaId: Guid.NewGuid()));

		resultado.Error.Should().Be(IntranetErrors.Salas.NotFound);
		_reservas.Reservas.Should().BeEmpty();
	}

	[Fact]
	public async Task Reservar_SalaInativa_NotFound()
	{
		var inativa = NovaSala("Sala Inativa", ativa: false);

		var resultado = await Reservar().HandleAsync(Pedido(salaId: inativa.Id));

		resultado.Error.Should().Be(IntranetErrors.Salas.NotFound);
	}

	[Fact]
	public async Task Reservar_ConvidadoForaDoDiretorio_ConvidadoInvalido()
	{
		var resultado = await Reservar().HandleAsync(Pedido(convidados: [_betoId, Guid.NewGuid()]));

		resultado.Error.Should().Be(IntranetErrors.Salas.ConvidadoInvalido);
		_reservas.Reservas.Should().BeEmpty();
		_reservas.Travas.Should().BeEmpty();
	}

	[Fact]
	public async Task Reservar_MaisDe50Convidados_ConvidadosDemais()
	{
		var ids = Enumerable.Range(0, Reserva.MaximoDeConvidados + 1).Select(_ => Guid.NewGuid()).ToList();
		foreach (var id in ids)
		{
			_diretorio.Com(id, $"{id:N}@exemplo.test");
		}

		var resultado = await Reservar().HandleAsync(Pedido(convidados: ids));

		resultado.Error.Code.Should().Be("Intranet.Salas.ConvidadosDemais");
		_reservas.Reservas.Should().BeEmpty();
	}

	[Fact]
	public async Task Reservar_50ConvidadosMaisRepetidosEODono_ContaDepoisDeNormalizar()
	{
		var ids = Enumerable.Range(0, Reserva.MaximoDeConvidados).Select(_ => Guid.NewGuid()).ToList();
		foreach (var id in ids)
		{
			_diretorio.Com(id, $"{id:N}@exemplo.test");
		}

		var resultado = await Reservar().HandleAsync(Pedido(convidados: [.. ids, ids[0], ids[1], _anaId]));

		resultado.IsSuccess.Should().BeTrue();
		_reservas.Reservas.Single().Convidados.Should().HaveCount(Reserva.MaximoDeConvidados);
	}

	[Fact]
	public async Task Reservar_DonoEntreOsConvidadosERepetido_AceitoNormalizado()
	{
		var resultado = await Reservar().HandleAsync(Pedido(convidados: [_anaId, _betoId, _betoId]));

		resultado.IsSuccess.Should().BeTrue();
		_reservas.Reservas.Single().Convidados.Should().ContainSingle().Which.UsuarioId.Should().Be(_betoId);
	}

	[Fact]
	public async Task Reservar_Sobreposicao_Conflito()
	{
		Existente();

		var resultado = await Reservar().HandleAsync(Pedido(inicioH: 10, inicioM: 30, fimH: 11, fimM: 30, quem: new QuemPede(_betoId, false)));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Code.Should().Be("Intranet.Salas.Conflito");
		resultado.Error.Description.Should().Be("Sala ocupada das 10:00 às 11:00 por Ana Souza.");
		_reservas.Reservas.Should().HaveCount(1);
		_trilha.Registros.Should().BeEmpty();
		_reservas.SobreposicoesForaDaTrava.Should().Be(0);
	}

	[Fact]
	public async Task Reservar_Encostada_Aceita()
	{
		Existente();

		var resultado = await Reservar().HandleAsync(Pedido(inicioH: 11, fimH: 12));

		resultado.IsSuccess.Should().BeTrue();
		_reservas.Reservas.Should().HaveCount(2);
	}

	[Fact]
	public async Task Reservar_QuemPedeForaDoDiretorio_GravaComoVisitante()
	{
		var estranho = new QuemPede(Guid.NewGuid(), false);

		var resultado = await Reservar().HandleAsync(Pedido(quem: estranho));

		resultado.IsSuccess.Should().BeTrue();
		var reserva = _reservas.Reservas.Single();
		reserva.ReservadoPorNome.Should().Be("Visitante");
		reserva.ReservadoPorEmail.Should().BeNull();
		reserva.ReservadoPorId.Should().Be(estranho.UsuarioId);
	}

	[Fact]
	public async Task Reservar_DiretorioIndisponivel_NaoGrava()
	{
		_diretorio.FalharCom = Error.Failure("Teste.Falha", "fora do ar");

		var resultado = await Reservar().HandleAsync(Pedido(convidados: [_betoId]));

		resultado.Error.Should().Be(IntranetErrors.Salas.DiretorioIndisponivel);
		_reservas.Reservas.Should().BeEmpty();
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Reservar_ComEspelhamentoLigado_NasceAguardando()
	{
		_espelhamento.Ligado = true;

		var resultado = await Reservar().HandleAsync(Pedido());

		resultado.IsSuccess.Should().BeTrue();
		_reservas.Reservas.Single().Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
	}

	// ---------- Alterar ----------

	[Fact]
	public async Task Alterar_Valida_Grava()
	{
		var reserva = Existente();

		var resultado = await Alterar().HandleAsync(PedidoDeAlteracao(reserva, Ana, inicioH: 14, fimH: 15, convidados: [_betoId]));

		resultado.IsSuccess.Should().BeTrue();
		reserva.Titulo.Should().Be("Novo título");
		reserva.Inicio.Should().Be(new DateTimeOffset(2027, 3, 16, 17, 0, 0, TimeSpan.Zero));
		reserva.Versao.Should().Be(2);
		reserva.Convidados.Should().ContainSingle().Which.UsuarioId.Should().Be(_betoId);
		_reservas.Gravacoes.Should().Be(1);
		_reservas.Travas.Should().ContainSingle().Which.Should().Be(_sala.Id);
		_reservas.SobreposicoesForaDaTrava.Should().Be(0);
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be("reserva.alterar");
	}

	[Fact]
	public async Task Alterar_SoOTitulo_NaoConflitaConsigoMesma()
	{
		var reserva = Existente();

		var resultado = await Alterar().HandleAsync(PedidoDeAlteracao(reserva, Ana));

		resultado.IsSuccess.Should().BeTrue();
		reserva.Titulo.Should().Be("Novo título");
	}

	[Fact]
	public async Task Alterar_TrocaDeSala_ConfereConflitoNaNova()
	{
		var reserva = Existente();
		var outra = NovaSala("Sala Boreal");
		Existente(outra, _betoId, nome: "Beto Lima");

		var resultado = await Alterar().HandleAsync(PedidoDeAlteracao(reserva, Ana, salaId: outra.Id));

		resultado.Error.Code.Should().Be("Intranet.Salas.Conflito");
		resultado.Error.Description.Should().Contain("Beto Lima");
		reserva.SalaId.Should().Be(_sala.Id);
		_reservas.Travas.Should().ContainSingle().Which.Should().Be(outra.Id);
	}

	[Fact]
	public async Task Alterar_TrocaDeSalaLivre_Move()
	{
		var reserva = Existente();
		var outra = NovaSala("Sala Boreal");

		var resultado = await Alterar().HandleAsync(PedidoDeAlteracao(reserva, Ana, salaId: outra.Id));

		resultado.IsSuccess.Should().BeTrue();
		reserva.SalaId.Should().Be(outra.Id);
	}

	[Fact]
	public async Task Alterar_SemSerDonoNemAdmin_ReservaNotFound()
	{
		var reserva = Existente();

		var resultado = await Alterar().HandleAsync(PedidoDeAlteracao(reserva, new QuemPede(_betoId, false)));

		resultado.Error.Should().Be(IntranetErrors.Salas.ReservaNotFound);
		reserva.Titulo.Should().Be("Reunião antiga");
	}

	[Fact]
	public async Task Alterar_Inexistente_ReservaNotFound()
	{
		var reserva = Existente();
		var pedido = PedidoDeAlteracao(reserva, Ana) with { Id = Guid.NewGuid() };

		var resultado = await Alterar().HandleAsync(pedido);

		resultado.Error.Should().Be(IntranetErrors.Salas.ReservaNotFound);
	}

	[Fact]
	public async Task Alterar_QueJaComecou_JaComecou()
	{
		var reserva = Existente(inicio: new DateTimeOffset(2027, 3, 15, 11, 0, 0, TimeSpan.Zero));

		var resultado = await Alterar().HandleAsync(PedidoDeAlteracao(reserva, Ana));

		resultado.Error.Should().Be(IntranetErrors.Salas.JaComecou);
	}

	[Fact]
	public async Task Alterar_ParaOPassado_NoPassado()
	{
		var reserva = Existente();
		var pedido = PedidoDeAlteracao(reserva, Ana) with { Dia = new DateOnly(2027, 3, 15), Inicio = new TimeOnly(8, 0), Fim = new TimeOnly(8, 30) };

		var resultado = await Alterar().HandleAsync(pedido);

		resultado.Error.Should().Be(IntranetErrors.Salas.NoPassado);
	}

	[Fact]
	public async Task Alterar_Admin_MantemODono()
	{
		var reserva = Existente();
		var admin = new QuemPede(_carlaId, true);

		var resultado = await Alterar().HandleAsync(PedidoDeAlteracao(reserva, admin, convidados: [_anaId, _carlaId]));

		resultado.IsSuccess.Should().BeTrue();
		reserva.ReservadoPorId.Should().Be(_anaId);
		reserva.Convidados.Should().ContainSingle().Which.UsuarioId.Should().Be(_carlaId);
	}

	[Fact]
	public async Task Alterar_ComEspelhamentoLigado_VoltaAguardando()
	{
		var reserva = Existente();
		_espelhamento.Ligado = true;

		await Alterar().HandleAsync(PedidoDeAlteracao(reserva, Ana));

		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
	}

	// ---------- Cancelar ----------

	[Fact]
	public async Task Cancelar_SemEvento_NaoDeixaPendencia()
	{
		var reserva = Existente();

		var resultado = await Cancelar().HandleAsync(reserva.Id, Ana);

		resultado.IsSuccess.Should().BeTrue();
		_reservas.Reservas.Should().BeEmpty();
		_reservas.Exclusoes.Should().BeEmpty();
		_reservas.Gravacoes.Should().Be(1);
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be("reserva.cancelar");
	}

	[Fact]
	public async Task Cancelar_ComEvento_DeixaExclusaoPendente()
	{
		var reserva = Existente();
		reserva.MarcarEspelhada("evento-123");

		var resultado = await Cancelar().HandleAsync(reserva.Id, Ana);

		resultado.IsSuccess.Should().BeTrue();
		_reservas.Reservas.Should().BeEmpty();
		_reservas.Exclusoes.Should().ContainSingle().Which.EventoExternoId.Should().Be("evento-123");
		_reservas.Gravacoes.Should().Be(1);
	}

	[Fact]
	public async Task Cancelar_SemSerDonoNemAdmin_ReservaNotFound()
	{
		var reserva = Existente();

		var resultado = await Cancelar().HandleAsync(reserva.Id, new QuemPede(_betoId, false));

		resultado.Error.Should().Be(IntranetErrors.Salas.ReservaNotFound);
		_reservas.Reservas.Should().ContainSingle();
	}

	[Fact]
	public async Task Cancelar_Inexistente_ReservaNotFound()
	{
		var resultado = await Cancelar().HandleAsync(Guid.NewGuid(), Ana);

		resultado.Error.Should().Be(IntranetErrors.Salas.ReservaNotFound);
	}

	[Fact]
	public async Task Cancelar_QueJaComecou_JaComecou()
	{
		var reserva = Existente(inicio: new DateTimeOffset(2027, 3, 15, 11, 0, 0, TimeSpan.Zero));

		var resultado = await Cancelar().HandleAsync(reserva.Id, Ana);

		resultado.Error.Should().Be(IntranetErrors.Salas.JaComecou);
		_reservas.Reservas.Should().ContainSingle();
	}

	[Fact]
	public async Task Admin_CancelaReservaDeOutro()
	{
		var reserva = Existente();

		var resultado = await Cancelar().HandleAsync(reserva.Id, new QuemPede(_betoId, true));

		resultado.IsSuccess.Should().BeTrue();
		_reservas.Reservas.Should().BeEmpty();
	}

	// ---------- Consultas ----------

	[Fact]
	public async Task Obter_MostraHorarioLocalEPodeAlterar()
	{
		var reserva = Existente();

		var doDono = await Obter().HandleAsync(reserva.Id, Ana);
		var deOutro = await Obter().HandleAsync(reserva.Id, new QuemPede(_betoId, false));
		var doAdmin = await Obter().HandleAsync(reserva.Id, new QuemPede(_betoId, true));

		doDono.Value.Dia.Should().Be(Amanha);
		doDono.Value.Inicio.Should().Be(new TimeOnly(10, 0));
		doDono.Value.Fim.Should().Be(new TimeOnly(11, 0));
		doDono.Value.SalaNome.Should().Be("Sala Aurora");
		doDono.Value.PodeAlterar.Should().BeTrue();
		deOutro.Value.PodeAlterar.Should().BeFalse();
		doAdmin.Value.PodeAlterar.Should().BeTrue();
	}

	[Fact]
	public async Task Obter_QueJaComecou_NaoPodeAlterar()
	{
		var reserva = Existente(inicio: new DateTimeOffset(2027, 3, 15, 11, 0, 0, TimeSpan.Zero));

		var resultado = await Obter().HandleAsync(reserva.Id, Ana);

		resultado.Value.PodeAlterar.Should().BeFalse();
	}

	[Fact]
	public async Task Obter_Inexistente_ReservaNotFound()
	{
		var resultado = await Obter().HandleAsync(Guid.NewGuid(), Ana);

		resultado.Error.Should().Be(IntranetErrors.Salas.ReservaNotFound);
	}

	[Fact]
	public async Task ListarOcupacaoDoDia_SoSalasAtivasEReservasDoDia()
	{
		var inativa = NovaSala("Sala Inativa", ativa: false);
		Existente();
		Existente(inativa, _betoId);
		Existente(inicio: new DateTimeOffset(2027, 3, 17, 13, 0, 0, TimeSpan.Zero));

		var ocupacao = await new ListarOcupacaoDoDiaHandler(_reservas, _salas, _relogio).HandleAsync(Amanha, Ana);

		ocupacao.Dia.Should().Be(Amanha);
		ocupacao.Salas.Should().ContainSingle().Which.Id.Should().Be(_sala.Id);
		ocupacao.Reservas.Should().ContainSingle().Which.SalaId.Should().Be(_sala.Id);
	}

	[Fact]
	public async Task ListarMinhasReservas_DonaEConvidada()
	{
		Existente();
		var deBeto = Existente(inicio: new DateTimeOffset(2027, 3, 16, 15, 0, 0, TimeSpan.Zero), donoId: _betoId, nome: "Beto Lima");
		deBeto.Alterar(_sala.Id, deBeto.Inicio, deBeto.Fim, "Com Ana", null, [new ConvidadoDaReserva(_anaId, "Ana Souza", null)], false);
		Existente(inicio: new DateTimeOffset(2027, 3, 16, 17, 0, 0, TimeSpan.Zero), donoId: _carlaId, nome: "Carla");

		var minhas = await new ListarMinhasReservasHandler(_reservas, _salas, _relogio).HandleAsync(Ana);

		minhas.Should().HaveCount(2);
		minhas.Select(m => m.Titulo).Should().BeEquivalentTo("Reunião antiga", "Com Ana");
	}

	[Fact]
	public async Task ListarConvidadosPossiveis_SemQuemPedeEPorNome()
	{
		var lista = await new ListarConvidadosPossiveisHandler(_diretorio).HandleAsync(_anaId);

		lista.Select(p => p.Nome).Should().Equal("Beto Lima", "carla@exemplo.test");
	}

	[Fact]
	public async Task ListarConvidadosPossiveis_DiretorioFalha_Vazio()
	{
		_diretorio.FalharCom = Error.Failure("Teste.Falha", "fora do ar");

		var lista = await new ListarConvidadosPossiveisHandler(_diretorio).HandleAsync(_anaId);

		lista.Should().BeEmpty();
	}

	private sealed class EspelhamentoFalso : IEspelhamentoLigado
	{
		public bool Ligado { get; set; }
	}
}
