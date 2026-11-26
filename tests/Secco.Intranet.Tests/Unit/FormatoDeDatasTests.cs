using AwesomeAssertions;
using Secco.Intranet.Web.Hosting;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Datas mostradas e lidas no padrão da cultura configurada, com o ISO sempre aceito para links.</summary>
public class FormatoDeDatasTests
{
	private static readonly FormatoDeDatas PtBr = new("pt-BR");

	private static readonly FormatoDeDatas EnUs = new("en-US");

	[Fact]
	public void PtBr_FormataEDaADica()
	{
		PtBr.Data(new DateOnly(2030, 1, 3)).Should().Be("03/01/2030");
		PtBr.DataEHora(new DateTime(2030, 1, 3, 9, 5, 0)).Should().Be("03/01/2030 09:05");
		PtBr.Hora(new TimeOnly(14, 0)).Should().Be("14:00");
		PtBr.DicaDeData.Should().Be("dd/mm/aaaa");
		PtBr.DicaDeDataEHora.Should().Be("dd/mm/aaaa hh:mm");
		PtBr.DiaPorExtenso(new DateOnly(2030, 1, 3)).Should().Be("Quinta-feira, 03/01/2030");
	}

	[Theory]
	[InlineData("03/01/2030")]
	[InlineData("3/1/2030")]
	[InlineData(" 03/01/2030 ")]
	[InlineData("2030-01-03")]
	public void PtBr_LeDataNoPadraoOuIso(string texto)
	{
		PtBr.TentarLerData(texto, out var data).Should().BeTrue();
		data.Should().Be(new DateOnly(2030, 1, 3));
	}

	[Theory]
	[InlineData("31/02/2030")]
	[InlineData("01/13/2030")]
	[InlineData("amanhã")]
	[InlineData("")]
	public void PtBr_DataInvalida_NaoLe(string texto) =>
		PtBr.TentarLerData(texto, out _).Should().BeFalse();

	[Theory]
	[InlineData("03/01/2030 09:05")]
	[InlineData("3/1/2030 9:05")]
	[InlineData("2030-01-03T09:05")]
	public void PtBr_LeDataEHora(string texto)
	{
		PtBr.TentarLerDataEHora(texto, out var instante).Should().BeTrue();
		instante.Should().Be(new DateTime(2030, 1, 3, 9, 5, 0));
	}

	[Fact]
	public void EnUs_SegueOPadraoDaCultura()
	{
		EnUs.Data(new DateOnly(2030, 1, 3)).Should().Be("01/03/2030");
		EnUs.DicaDeData.Should().Be("mm/dd/yyyy");
		EnUs.TentarLerData("01/03/2030", out var data).Should().BeTrue();
		data.Should().Be(new DateOnly(2030, 1, 3));
		EnUs.DiaPorExtenso(new DateOnly(2030, 1, 3)).Should().Be("Thursday, 01/03/2030");
	}

	[Fact]
	public void CulturaInexistente_Lanca()
	{
		var criar = () => new FormatoDeDatas("xx-NAO-EXISTE");

		criar.Should().Throw<InvalidOperationException>().WithMessage("*Intranet:Formatos:Cultura*");
	}
}
