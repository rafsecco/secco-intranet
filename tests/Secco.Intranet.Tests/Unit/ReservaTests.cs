using AwesomeAssertions;
using Secco.Intranet.Domain.Salas;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Regras de horário, convidados e espelhamento da reserva.</summary>
public class ReservaTests
{
	private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3);
	private static readonly Guid SalaId = Guid.NewGuid();
	private static readonly Guid DonoId = Guid.NewGuid();

	private static DateTimeOffset Hora(int hora, int minuto = 0) =>
		new(2030, 5, 10, hora, minuto, 0, Fuso);

	private static ConvidadoDaReserva Convidado(Guid? id = null) =>
		new(id ?? Guid.NewGuid(), "Convidada Fictícia", "convidada@exemplo.com");

	private static Reserva Nova(
		DateTimeOffset? inicio = null,
		DateTimeOffset? fim = null,
		string titulo = "Reunião de alinhamento",
		IEnumerable<ConvidadoDaReserva>? convidados = null,
		bool espelhar = false) =>
		new(SalaId, inicio ?? Hora(10), fim ?? Hora(11), titulo, null,
			DonoId, "Dona Fictícia", "dona@exemplo.com", convidados ?? [], espelhar);

	[Fact]
	public void Construtor_FimAntesDoInicio_Lanca()
	{
		var act = () => Nova(inicio: Hora(11), fim: Hora(10));

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_MinutoForaDoPasso_Lanca()
	{
		var act = () => Nova(inicio: Hora(10, 7), fim: Hora(11));

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_TituloCom121_Lanca()
	{
		var act = () => Nova(titulo: new string('a', 121));

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_ConvidadoRepetidoEODono_FicaUmSemODono()
	{
		var outro = Guid.NewGuid();

		var repetido = () => Nova(convidados: [Convidado(outro), Convidado(outro)]);
		repetido.Should().Throw<DomainInvariantException>();

		var reserva = Nova(convidados: [Convidado(outro), Convidado(DonoId)]);

		reserva.Convidados.Should().ContainSingle().Which.UsuarioId.Should().Be(outro);
		reserva.Convidados[0].ReservaId.Should().Be(reserva.Id);
	}

	[Fact]
	public void Construtor_51Convidados_Lanca()
	{
		var convidados = Enumerable.Range(0, 51).Select(_ => Convidado()).ToList();

		var act = () => Nova(convidados: convidados);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_SemEspelhar_NaoSeAplica_ComEspelhar_Aguardando()
	{
		Nova(espelhar: false).Espelhamento.Should().Be(EstadoDoEspelhamento.NaoSeAplica);
		Nova(espelhar: true).Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		Nova().Versao.Should().Be(1);
	}

	[Theory]
	[InlineData(11, 0, 12, 0, false)]
	[InlineData(10, 30, 11, 30, true)]
	[InlineData(9, 0, 10, 0, false)]
	[InlineData(9, 0, 12, 0, true)]
	public void Sobrepoe_ConfereOsLimites(int hi, int mi, int hf, int mf, bool esperado)
	{
		var reserva = Nova(Hora(10), Hora(11));

		reserva.Sobrepoe(Hora(hi, mi), Hora(hf, mf)).Should().Be(esperado);
	}

	[Fact]
	public void JaComecou_ComparaComOInicio()
	{
		var reserva = Nova();

		reserva.JaComecou(Hora(9, 59)).Should().BeFalse();
		reserva.JaComecou(Hora(10)).Should().BeTrue();
	}

	[Fact]
	public void Alterar_ComEspelhar_VoltaParaAguardando_ZeraTentativas_MantemEvento_SomaVersao()
	{
		var reserva = Nova(espelhar: true);
		reserva.MarcarEspelhada("evento-1");
		reserva.RegistrarFalhaPassageira("erro", Hora(9));

		reserva.Alterar(SalaId, Hora(14), Hora(15), "Novo título", "desc", [Convidado()], espelhar: true);

		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		reserva.TentativasDeEspelhamento.Should().Be(0);
		reserva.ProximaTentativaEm.Should().BeNull();
		reserva.EventoExternoId.Should().Be("evento-1");
		reserva.Versao.Should().Be(2);
		reserva.Titulo.Should().Be("Novo título");
		reserva.Convidados.Should().HaveCount(1);
	}

	[Fact]
	public void Alterar_SemEspelhar_NaoTocaNoEstado()
	{
		var reserva = Nova(espelhar: true);
		reserva.MarcarEspelhada("evento-1");

		reserva.Alterar(SalaId, Hora(14), Hora(15), "Novo título", null, [], espelhar: false);

		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
		reserva.Versao.Should().Be(2);
	}

	[Fact]
	public void RegistrarFalhaPassageira_AgendaAEspera_1_2_4_8_16_32_60()
	{
		var reserva = Nova(espelhar: true);
		var agora = Hora(9);

		foreach (var minutos in new[] { 1, 2, 4, 8, 16, 32, 60 })
		{
			reserva.RegistrarFalhaPassageira("erro", agora);

			reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
			reserva.ProximaTentativaEm.Should().Be(agora.AddMinutes(minutos));
		}

		reserva.TentativasDeEspelhamento.Should().Be(7);
	}

	[Fact]
	public void RegistrarFalhaPassageira_NaOitava_ViraFalhou()
	{
		var reserva = Nova(espelhar: true);

		for (var i = 1; i <= 7; i++)
		{
			reserva.RegistrarFalhaPassageira("erro", Hora(9));
		}

		reserva.RegistrarFalhaPassageira("último motivo", Hora(9));

		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Falhou);
		reserva.MotivoDaFalha.Should().Be("último motivo");
	}

	[Fact]
	public void RegistrarFalhaPassageira_CortaOMotivoEm500()
	{
		var reserva = Nova(espelhar: true);

		reserva.RegistrarFalhaPassageira(new string('x', 600), Hora(9));

		reserva.MotivoDaFalha.Should().HaveLength(500);
	}

	[Fact]
	public void MarcarFalhaDefinitiva_ViraFalhou()
	{
		var reserva = Nova(espelhar: true);

		reserva.MarcarFalhaDefinitiva("sem permissão");

		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Falhou);
		reserva.MotivoDaFalha.Should().Be("sem permissão");
	}

	[Fact]
	public void TentarDeNovo_DeFalhou_VoltaParaAguardando()
	{
		var reserva = Nova(espelhar: true);
		reserva.RegistrarFalhaPassageira("erro", Hora(9));
		reserva.MarcarFalhaDefinitiva("erro");

		reserva.TentarDeNovo();

		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		reserva.TentativasDeEspelhamento.Should().Be(0);
		reserva.ProximaTentativaEm.Should().BeNull();
	}

	[Fact]
	public void MarcarEspelhada_GravaOEventoELimpaAFalha()
	{
		var reserva = Nova(espelhar: true);
		reserva.RegistrarFalhaPassageira("erro", Hora(9));

		reserva.MarcarEspelhada("evento-9");

		reserva.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
		reserva.EventoExternoId.Should().Be("evento-9");
		reserva.MotivoDaFalha.Should().BeNull();
		reserva.ProximaTentativaEm.Should().BeNull();
	}

	[Fact]
	public void ExclusaoDeEventoPendente_UsaAMesmaEspera()
	{
		var exclusao = new ExclusaoDeEventoPendente("evento-1");
		var agora = Hora(9);

		exclusao.RegistrarFalhaPassageira("erro", agora);
		exclusao.ProximaTentativaEm.Should().Be(agora.AddMinutes(1));

		exclusao.RegistrarFalhaPassageira("erro", agora);
		exclusao.ProximaTentativaEm.Should().Be(agora.AddMinutes(2));
	}
}
