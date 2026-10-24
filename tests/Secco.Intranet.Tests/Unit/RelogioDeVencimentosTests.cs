using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Secco.Intranet.Application.Vencimentos;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>"Hoje" do produto: a data no fuso configurado, não a data UTC.</summary>
public class RelogioDeVencimentosTests
{
	[Fact]
	public void Hoje_UsaOFusoConfigurado_NaViradaDoDiaUtc()
	{
		// 02:30Z de 31/12 ainda é 23:30 de 30/12 em São Paulo (UTC-3).
		var tempo = new FakeTimeProvider(DateTimeOffset.Parse("2026-12-31T02:30:00Z"));
		var relogio = new RelogioDeVencimentos(tempo, new VencimentosOptions());

		relogio.Hoje().Should().Be(new DateOnly(2026, 12, 30));
		relogio.Agora().Should().Be(DateTimeOffset.Parse("2026-12-31T02:30:00Z"));
	}

	[Fact]
	public void FusoInvalido_Lanca()
	{
		var criar = () => new RelogioDeVencimentos(
			new FakeTimeProvider(), new VencimentosOptions { FusoHorario = "Marte/Olympus" });

		criar.Should().Throw<TimeZoneNotFoundException>();
	}
}
