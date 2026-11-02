using AwesomeAssertions;
using Secco.Intranet.Domain.Bibliotecas;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public sealed class BibliotecaTests
{
	[Fact]
	public void Slug_e_normalizado()
	{
		var biblioteca = new Biblioteca("Manuais", " Manuais ");

		biblioteca.Slug.Should().Be("manuais");
		biblioteca.Ativo.Should().BeTrue();
	}

	[Theory]
	[InlineData("a", true)]
	[InlineData("manuais-2", true)]
	[InlineData("com espaco", false)]
	[InlineData("acentuação", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void SlugEhValido_aceita_so_o_padrao(string? slug, bool esperado) =>
		Biblioteca.SlugEhValido(slug).Should().Be(esperado);

	[Fact]
	public void SlugEhValido_respeita_o_tamanho_maximo()
	{
		Biblioteca.SlugEhValido(new string('a', 53)).Should().BeTrue();
		Biblioteca.SlugEhValido(new string('a', 54)).Should().BeFalse();
	}

	[Fact]
	public void Icone_vazio_cai_no_padrao()
	{
		new Biblioteca("Manuais", "manuais", " ").Icone.Should().Be(Biblioteca.IconePadrao);
		Biblioteca.IconePadrao.Should().Be("bi-journal-text");
	}

	[Fact]
	public void Desativar_e_Ativar()
	{
		var biblioteca = new Biblioteca("Manuais", "manuais");

		biblioteca.Desativar();
		biblioteca.Ativo.Should().BeFalse();

		biblioteca.Ativar();
		biblioteca.Ativo.Should().BeTrue();
	}
}
