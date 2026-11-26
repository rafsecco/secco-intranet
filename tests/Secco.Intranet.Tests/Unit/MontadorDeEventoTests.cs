using AwesomeAssertions;
using Secco.Intranet.Application.Salas.Agenda;
using Secco.Intranet.Domain.Salas;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Como uma reserva vira o evento enviado à agenda externa.</summary>
public class MontadorDeEventoTests
{
	private static readonly DateTimeOffset Inicio = new(2027, 3, 16, 13, 0, 0, TimeSpan.Zero);

	private static Reserva NovaReserva(string? descricao = null, string? emailDoDono = "ana@exemplo.test", params ConvidadoDaReserva[] convidados) =>
		new(Guid.NewGuid(), Inicio, Inicio.AddHours(1), "Alinhamento", descricao, Guid.NewGuid(), "Ana Souza", emailDoDono, convidados, espelhar: true);

	private static Sala NovaSala(string? email = "sala@exemplo.test") => new("Sala Aurora", null, null, null, email, 0);

	[Fact]
	public void Monta_AssuntoCorpoLocal()
	{
		var reserva = NovaReserva(descricao: "Pauta do dia");

		var evento = MontadorDeEvento.Montar(reserva, NovaSala());

		evento.Assunto.Should().Be("Alinhamento");
		evento.Corpo.Should().Be("Reservado por Ana Souza\n\nPauta do dia");
		evento.Local.Should().Be("Sala Aurora");
		evento.Inicio.Should().Be(Inicio);
		evento.Fim.Should().Be(Inicio.AddHours(1));
	}

	[Fact]
	public void SemDescricao_CorpoTemSoQuemReservou()
	{
		MontadorDeEvento.Montar(NovaReserva(), NovaSala()).Corpo.Should().Be("Reservado por Ana Souza");
	}

	[Fact]
	public void ConvidadoSemEmail_FicaFora()
	{
		var reserva = NovaReserva(convidados: [new(Guid.NewGuid(), "Beto", "beto@exemplo.test"), new(Guid.NewGuid(), "Carla", null)]);

		var evento = MontadorDeEvento.Montar(reserva, NovaSala());

		evento.Participantes.Select(p => p.Email).Should().Equal("ana@exemplo.test", "beto@exemplo.test");
		evento.Participantes.Select(p => p.Nome).Should().Equal("Ana Souza", "Beto");
	}

	[Fact]
	public void EmailRepetido_EntraUmaVez()
	{
		var reserva = NovaReserva(convidados:
			[new(Guid.NewGuid(), "Beto", "beto@exemplo.test"), new(Guid.NewGuid(), "Beto de novo", "BETO@exemplo.test"), new(Guid.NewGuid(), "Ana", "ana@exemplo.test")]);

		var evento = MontadorDeEvento.Montar(reserva, NovaSala());

		evento.Participantes.Select(p => p.Email).Should().Equal("ana@exemplo.test", "beto@exemplo.test");
	}

	[Fact]
	public void DonoSemEmail_FicaFora()
	{
		var reserva = NovaReserva(emailDoDono: null, convidados: [new(Guid.NewGuid(), "Beto", "beto@exemplo.test")]);

		MontadorDeEvento.Montar(reserva, NovaSala()).Participantes.Select(p => p.Email).Should().Equal("beto@exemplo.test");
	}

	[Fact]
	public void SalaSemEmail_EmailDaSalaNulo()
	{
		MontadorDeEvento.Montar(NovaReserva(), NovaSala(email: null)).EmailDaSala.Should().BeNull();
		MontadorDeEvento.Montar(NovaReserva(), NovaSala()).EmailDaSala.Should().Be("sala@exemplo.test");
	}

	[Fact]
	public void Chave_EOIdDaReserva()
	{
		var reserva = NovaReserva();

		MontadorDeEvento.Montar(reserva, NovaSala()).ChaveDeIdempotencia.Should().Be(reserva.Id.ToString());
		MontadorDeEvento.Montar(reserva, NovaSala()).ChaveDeIdempotencia.Should().Be(MontadorDeEvento.Montar(reserva, NovaSala()).ChaveDeIdempotencia);
	}
}
