using AwesomeAssertions;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Web.Models.Salas;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Montagem da grade do dia: faixa de horas, células livres e reservas que cobrem linhas.</summary>
public class GradeDoDiaTests
{
	private static readonly DateOnly Dia = new(2030, 3, 12);

	private static readonly SalaDto Azul = new(Guid.NewGuid(), "Sala Azul", null, 6, null, null, 1, true);

	private static readonly SalaDto Verde = new(Guid.NewGuid(), "Sala Verde", null, 4, null, null, 2, true);

	private static ReservaDto Reserva(SalaDto sala, TimeOnly inicio, TimeOnly fim) =>
		new(
			Guid.NewGuid(), sala.Id, sala.Nome, Dia, inicio, fim, "Alinhamento", null,
			new PessoaDaReservaDto(Guid.NewGuid(), "Ana Teste"), [], false, EstadoDoEspelhamento.NaoSeAplica, null);

	private static OcupacaoDoDiaDto Ocupacao(params ReservaDto[] reservas) =>
		new(Dia, [Azul, Verde], reservas);

	[Fact]
	public void SemReservas_Das7As20_SoCelulasLivres()
	{
		var grade = GradeDoDiaViewModel.Montar(Ocupacao(), Dia.AddDays(-1), new TimeOnly(9, 0), null);

		grade.Abre.Should().Be(new TimeOnly(7, 0));
		grade.Fecha.Should().Be(new TimeOnly(20, 0));
		grade.Colunas.Should().HaveCount(2);
		grade.Colunas.Should().OnlyContain(coluna => coluna.Celulas.Count == 52);
		grade.Colunas.SelectMany(coluna => coluna.Celulas).Should().OnlyContain(celula => celula.Livre && celula.Reserva == null);
		grade.Colunas[0].Celulas[0].Hora.Should().Be(new TimeOnly(7, 0));
		grade.Colunas[0].Celulas[^1].Hora.Should().Be(new TimeOnly(19, 45));
	}

	[Fact]
	public void ReservaDe90Min_AbreComSeisLinhas_ECobreAsSeguintes()
	{
		var reserva = Reserva(Azul, new TimeOnly(10, 0), new TimeOnly(11, 30));

		var grade = GradeDoDiaViewModel.Montar(Ocupacao(reserva), Dia.AddDays(-1), new TimeOnly(9, 0), null);

		var celulas = grade.Colunas[0].Celulas;
		var abertura = celulas.Single(celula => celula.Reserva != null);

		abertura.Hora.Should().Be(new TimeOnly(10, 0));
		abertura.Linhas.Should().Be(6);
		abertura.Livre.Should().BeFalse();
		celulas.Select(celula => celula.Hora).Should().NotContain(
			[new TimeOnly(10, 15), new TimeOnly(10, 30), new TimeOnly(10, 45), new TimeOnly(11, 0), new TimeOnly(11, 15)]);
		celulas.Should().Contain(celula => celula.Hora == new TimeOnly(11, 30) && celula.Livre);
		celulas.Count.Should().Be(52 - 5);
		grade.Colunas[1].Celulas.Should().HaveCount(52);
	}

	[Fact]
	public void ReservaAs6_AlargaAFaixa()
	{
		var reserva = Reserva(Verde, new TimeOnly(6, 15), new TimeOnly(7, 0));

		var grade = GradeDoDiaViewModel.Montar(Ocupacao(reserva), Dia.AddDays(-1), new TimeOnly(9, 0), null);

		grade.Abre.Should().Be(new TimeOnly(6, 0));
		grade.Fecha.Should().Be(new TimeOnly(20, 0));
		grade.Colunas[0].Celulas[0].Hora.Should().Be(new TimeOnly(6, 0));
	}

	[Fact]
	public void ReservaDepoisDas20_AlargaAteAHoraCheia()
	{
		var reserva = Reserva(Azul, new TimeOnly(20, 0), new TimeOnly(21, 15));

		var grade = GradeDoDiaViewModel.Montar(Ocupacao(reserva), Dia.AddDays(-1), new TimeOnly(9, 0), null);

		grade.Fecha.Should().Be(new TimeOnly(22, 0));
	}

	[Fact]
	public void Hoje_CelulasPassadasNaoSaoLivres()
	{
		var grade = GradeDoDiaViewModel.Montar(Ocupacao(), Dia, new TimeOnly(10, 5), null);

		var celulas = grade.Colunas[0].Celulas;

		celulas.Where(celula => celula.Hora < new TimeOnly(10, 5)).Should().OnlyContain(celula => !celula.Livre);
		celulas.Where(celula => celula.Hora >= new TimeOnly(10, 5)).Should().OnlyContain(celula => celula.Livre);
	}

	[Fact]
	public void DiaPassado_NenhumaCelulaELivre()
	{
		var grade = GradeDoDiaViewModel.Montar(Ocupacao(), Dia.AddDays(1), new TimeOnly(9, 0), null);

		grade.Colunas.SelectMany(coluna => coluna.Celulas).Should().OnlyContain(celula => !celula.Livre);
	}

	[Fact]
	public void SalaEmFoco_Padrao_PrimeiraColuna()
	{
		GradeDoDiaViewModel.Montar(Ocupacao(), Dia, new TimeOnly(9, 0), null).SalaEmFoco.Should().Be(Azul.Id);
		GradeDoDiaViewModel.Montar(Ocupacao(), Dia, new TimeOnly(9, 0), Verde.Id).SalaEmFoco.Should().Be(Verde.Id);
		GradeDoDiaViewModel.Montar(Ocupacao(), Dia, new TimeOnly(9, 0), Guid.NewGuid()).SalaEmFoco.Should().Be(Azul.Id);
	}

	[Fact]
	public void SemSalas_SemFoco()
	{
		var grade = GradeDoDiaViewModel.Montar(new OcupacaoDoDiaDto(Dia, [], []), Dia, new TimeOnly(9, 0), null);

		grade.Colunas.Should().BeEmpty();
		grade.SalaEmFoco.Should().BeNull();
	}
}
