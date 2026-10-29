using AwesomeAssertions;
using Secco.Intranet.Application.Menu;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class OrdemDoMenuTests
{
	private static IReadOnlyList<string> Ordenar(string[] entrada, Dictionary<string, int>? posicoes) =>
		OrdemDoMenu.Ordenar(entrada, c => c, posicoes);

	[Fact]
	public void Ordenar_SemPosicoes_MantemAOrdemPadrao() =>
		Ordenar(["a", "b", "c"], null).Should().Equal("a", "b", "c");

	[Fact]
	public void Ordenar_ComTodasAsPosicoes_SegueAsPosicoes() =>
		Ordenar(["a", "b", "c"], new() { ["c"] = 0, ["a"] = 1, ["b"] = 2 }).Should().Equal("c", "a", "b");

	[Fact]
	public void Ordenar_ItemSemPosicao_VaiParaOFim_NaOrdemPadrao() =>
		Ordenar(["a", "b", "novo", "c"], new() { ["c"] = 0, ["a"] = 1, ["b"] = 2 }).Should().Equal("c", "a", "b", "novo");

	[Fact]
	public void Ordenar_PosicaoOrfa_EIgnorada_SemMexerNosVizinhos() =>
		Ordenar(["a", "c"], new() { ["c"] = 0, ["b"] = 1, ["a"] = 2 }).Should().Equal("c", "a");

	[Fact]
	public void Ordenar_ChaveSemDiferenciarCaixa() =>
		Ordenar(["b", "a"], new() { ["A"] = 0 }).Should().Equal("a", "b");
}
