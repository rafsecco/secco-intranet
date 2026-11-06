using System.Text;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Publicacoes;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Limite de células de tabela: a tabela em Markdown completa cada linha até a largura do
/// cabeçalho, então 20 KB chegariam a 122 MB de HTML. A recusa vem de uma varredura do texto,
/// sem parsear o Markdown.
/// </summary>
public class ValidacaoDePublicacaoTests
{
	private static Secco.SharedKernel.Results.Error? Validar(string corpo) =>
		ValidacaoDePublicacao.Validar("Titulo", corpo, DateTimeOffset.UtcNow, null, 200);

	private static string Cortar(string corpo) =>
		corpo.Length > ValidacaoDePublicacao.LimiteDoCorpo ? corpo[..ValidacaoDePublicacao.LimiteDoCorpo] : corpo;

	private static string Repetir(string trecho, int vezes) =>
		new StringBuilder(trecho.Length * vezes).Insert(0, trecho, vezes).ToString();

	[Fact]
	public void Validar_TabelaAcimaDoLimiteDeCelulas_Recusa()
	{
		var corpo = Cortar("|" + Repetir("a|", 2500) + "\n|" + Repetir("-|", 2500) + "\n" + Repetir("|\n", 5000));

		Validar(corpo).Should().Be(IntranetErrors.Publicacoes.TabelaGrandeDemais);
	}

	[Fact]
	public void Validar_TabelaSemBordasAcimaDoLimite_Recusa()
	{
		var corpo = Repetir("a|", 999) + "a\n" + Repetir("x|\n", 5000);

		Validar(corpo).Should().Be(IntranetErrors.Publicacoes.TabelaGrandeDemais);
	}

	[Fact]
	public void Validar_PipeEscapadoAcimaDoLimite_Recusa()
	{
		var corpo = Repetir("a\\|", 1000) + "\n|-|\n" + Repetir("|\n", 5000);

		Validar(corpo).Should().Be(IntranetErrors.Publicacoes.TabelaGrandeDemais);
	}

	[Fact]
	public void Validar_TabelaNoLimite_Aceita()
	{
		var corpo = new string('|', 99) + "\n" + Repetir("x\n", 98) + "x";

		Validar(corpo).Should().BeNull("(99 + 1) * 100 = 10.000 não passa do limite");
	}

	[Fact]
	public void Validar_UmaCelulaAcimaDoLimite_Recusa()
	{
		var corpo = new string('|', 100) + "\n" + Repetir("x\n", 98) + "x";

		Validar(corpo).Should().Be(IntranetErrors.Publicacoes.TabelaGrandeDemais, "(100 + 1) * 100 = 10.100");
	}

	[Fact]
	public void Validar_TabelaComCrSozinho_Recusa()
	{
		var corpo = "|" + Repetir("a|", 1000) + "\r|" + Repetir("-|", 1000) + "\r" + Repetir("|\r", 7000);

		Validar(corpo).Should().Be(IntranetErrors.Publicacoes.TabelaGrandeDemais, "o Markdig trata \\r sozinho como fim de linha");
	}

	[Fact]
	public void Validar_TabelaNoLimiteComCrLf_Aceita()
	{
		var corpo = new string('|', 99) + "\r\n" + Repetir("x\r\n", 98) + "x";

		Validar(corpo).Should().BeNull("\\r\\n é uma quebra só: (99 + 1) * 100 = 10.000");
	}

	[Fact]
	public void Validar_UmaCelulaAcimaDoLimiteComCrLf_Recusa()
	{
		var corpo = new string('|', 100) + "\r\n" + Repetir("x\r\n", 98) + "x";

		Validar(corpo).Should().Be(IntranetErrors.Publicacoes.TabelaGrandeDemais, "(100 + 1) * 100 = 10.100");
	}

	[Fact]
	public void Validar_TextoSemPipeComMuitasLinhas_Aceita()
	{
		var corpo = Repetir("x\n", 10_000);

		Validar(corpo).Should().BeNull("sem pipe não há tabela, por mais linhas que existam");
	}

	[Fact]
	public void Validar_TabelaLegitimaGrande_Aceita()
	{
		var corpo = Repetir("| texto | mais texto | 123 | fim |\n", 500);

		Validar(corpo).Should().BeNull();
	}
}
