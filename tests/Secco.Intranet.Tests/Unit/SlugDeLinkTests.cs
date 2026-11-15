using System.Text.RegularExpressions;
using AwesomeAssertions;
using Secco.Intranet.Application.Links;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class SlugDeLinkTests
{
	[Theory]
	[InlineData("Internet Banking", "internet-banking")]
	[InlineData("Portal de Notas (NF-e)", "portal-de-notas-nf-e")]
	[InlineData("Conciliação Ação", "conciliacao-acao")]
	[InlineData("★★★", "link")]
	[InlineData("   ", "link")]
	public void Gerar_Normaliza(string nome, string esperado) =>
		SlugDeLink.Gerar(nome, []).Should().Be(esperado);

	[Fact]
	public void Gerar_Repetido_AcrescentaSufixo()
	{
		SlugDeLink.Gerar("ERP", ["erp"]).Should().Be("erp-2");
		SlugDeLink.Gerar("ERP", ["erp", "erp-2"]).Should().Be("erp-3");
	}

	[Fact]
	public void Gerar_NomeDe300Letras_CortaEm120()
	{
		var slug = SlugDeLink.Gerar(new string('a', 300), []);

		slug.Length.Should().Be(120);
	}

	[Fact]
	public void Gerar_CorteNoHifen_NaoTerminaEmHifen()
	{
		var slug = SlugDeLink.Gerar(new string('a', 119) + " b", []);

		slug.Should().Be(new string('a', 119));
	}

	[Fact]
	public void Gerar_SempreNoFormatoDoMenu() =>
		Regex.IsMatch(SlugDeLink.Gerar("  --Olá, Mundo!! 2026--  ", []), "^[a-z0-9]+(-[a-z0-9]+)*$").Should().BeTrue();
}
