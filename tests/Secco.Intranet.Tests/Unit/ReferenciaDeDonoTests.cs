using AwesomeAssertions;
using Secco.Intranet.Application.Documentos;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Igualdade e normalização da referência a um dono de documentos.</summary>
public class ReferenciaDeDonoTests
{
	[Fact]
	public void Construtor_NormalizaOSlug_IgualAFabrica() =>
		new ReferenciaDeDono(TipoDeDono.Setor, " TI ").Should().Be(ReferenciaDeDono.DoSetor("ti"));

	[Fact]
	public void MesmoSlug_EmTiposDiferentes_NaoSaoIguais() =>
		ReferenciaDeDono.DoSetor("ti").Should().NotBe(ReferenciaDeDono.DaBiblioteca("ti"));

	[Fact]
	public void Fabricas_NormalizamTrimEMinusculas()
	{
		ReferenciaDeDono.DaBiblioteca("  Manuais ").Slug.Should().Be("manuais");
		ReferenciaDeDono.DaBiblioteca("MANUAIS").GetHashCode()
			.Should().Be(ReferenciaDeDono.DaBiblioteca("manuais").GetHashCode());
	}
}
