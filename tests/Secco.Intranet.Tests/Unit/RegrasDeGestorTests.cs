using AwesomeAssertions;
using Secco.Intranet.Application.Diretorio;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class RegrasDeGestorTests
{
	private static readonly Guid A = Guid.NewGuid();
	private static readonly Guid B = Guid.NewGuid();
	private static readonly Guid C = Guid.NewGuid();
	private static readonly Guid D = Guid.NewGuid();

	[Fact]
	public void SemNenhumGestorDefinido_NaoCriaCiclo()
	{
		RegrasDeGestor.CriariaCiclo(new Dictionary<Guid, Guid>(), A, B).Should().BeFalse();
	}

	[Fact]
	public void CicloDireto_AReportaAB_BReportaAA()
	{
		// B já reporta a A; fazer A reportar a B fecha o ciclo.
		var mapa = new Dictionary<Guid, Guid> { [B] = A };

		RegrasDeGestor.CriariaCiclo(mapa, A, B).Should().BeTrue();
	}

	[Fact]
	public void CicloDeTres_AReportaAC_CReportaAB_BReportaAA()
	{
		var mapa = new Dictionary<Guid, Guid> { [B] = A, [C] = B };

		RegrasDeGestor.CriariaCiclo(mapa, A, C).Should().BeTrue();
	}

	[Fact]
	public void CorrenteQueNaoPassaPeloUsuario_NaoEUmCiclo()
	{
		var mapa = new Dictionary<Guid, Guid> { [C] = D };

		RegrasDeGestor.CriariaCiclo(mapa, A, C).Should().BeFalse();
	}

	[Fact]
	public void TrocarDeGestorNaMesmaArvore_NaoEUmCiclo()
	{
		// B e C reportam a A; mover C para debaixo de B é legítimo.
		var mapa = new Dictionary<Guid, Guid> { [B] = A, [C] = A };

		RegrasDeGestor.CriariaCiclo(mapa, C, B).Should().BeFalse();
	}

	[Fact]
	public void DadoJaCorrompidoEmOutroLugar_NaoTravaNemAcusaCicloAlheio()
	{
		// Ciclo gravado à mão entre C e D, que não envolve A: a regra termina e diz "não".
		var mapa = new Dictionary<Guid, Guid> { [C] = D, [D] = C };

		var resultado = () => RegrasDeGestor.CriariaCiclo(mapa, A, C);

		resultado.Should().NotThrow();
		RegrasDeGestor.CriariaCiclo(mapa, A, C).Should().BeFalse();
	}
}
