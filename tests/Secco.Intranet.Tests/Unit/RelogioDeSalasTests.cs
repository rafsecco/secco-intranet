using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Secco.Intranet.Application.Salas;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Conversão entre horário local e UTC das reservas.</summary>
public class RelogioDeSalasTests
{
	private static RelogioDeSalas Relogio(string fuso = "America/Sao_Paulo") =>
		new(new FakeTimeProvider(new DateTimeOffset(2027, 3, 15, 12, 0, 0, TimeSpan.Zero)), new SalasOptions { FusoHorario = fuso });

	[Fact]
	public void ParaUtc_SaoPaulo_Converte()
	{
		var utc = Relogio().ParaUtc(new DateOnly(2027, 3, 16), new TimeOnly(10, 0));

		utc.Should().Be(new DateTimeOffset(2027, 3, 16, 13, 0, 0, TimeSpan.Zero));
		utc!.Value.Offset.Should().Be(TimeSpan.Zero);
	}

	[Fact]
	public void ParaUtc_HoraQueNaoExiste_Nulo() =>
		Relogio("America/New_York").ParaUtc(new DateOnly(2026, 3, 8), new TimeOnly(2, 30)).Should().BeNull();

	[Fact]
	public void ParaUtc_HoraAmbigua_Nulo() =>
		Relogio("America/New_York").ParaUtc(new DateOnly(2026, 11, 1), new TimeOnly(1, 30)).Should().BeNull();

	[Fact]
	public void LimitesDoDia_CobreODiaLocal()
	{
		var (de, ate) = Relogio().LimitesDoDia(new DateOnly(2027, 3, 16));

		de.Should().Be(new DateTimeOffset(2027, 3, 16, 3, 0, 0, TimeSpan.Zero));
		ate.Should().Be(new DateTimeOffset(2027, 3, 17, 3, 0, 0, TimeSpan.Zero));
		de.Offset.Should().Be(TimeSpan.Zero);
		ate.Offset.Should().Be(TimeSpan.Zero);
	}

	[Fact]
	public void LimitesDoDia_DiaDeMudancaDeHorario_Tem23Horas()
	{
		var (de, ate) = Relogio("America/New_York").LimitesDoDia(new DateOnly(2026, 3, 8));

		(ate - de).Should().Be(TimeSpan.FromHours(23));
	}

	[Fact]
	public void ParaLocal_ConverteInstante()
	{
		var (dia, hora) = Relogio().ParaLocal(new DateTimeOffset(2027, 3, 17, 1, 30, 0, TimeSpan.Zero));

		dia.Should().Be(new DateOnly(2027, 3, 16));
		hora.Should().Be(new TimeOnly(22, 30));
	}

	[Fact]
	public void AgoraEHoje_UsamOFuso()
	{
		var relogio = new RelogioDeSalas(
			new FakeTimeProvider(new DateTimeOffset(2027, 3, 16, 1, 0, 0, TimeSpan.Zero)), new SalasOptions());

		relogio.Agora().Offset.Should().Be(TimeSpan.Zero);
		relogio.Hoje().Should().Be(new DateOnly(2027, 3, 15));
	}
}
