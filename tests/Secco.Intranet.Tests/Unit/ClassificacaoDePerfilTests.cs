using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ClassificacaoDePerfilTests
{
	[Theory]
	[InlineData("intranet-admin", TipoDePerfil.Produto)]
	[InlineData("Intranet-Admin", TipoDePerfil.Produto)]
	[InlineData("inventario-admin", TipoDePerfil.Produto)]
	[InlineData("financeiro-admin", TipoDePerfil.Setor)]
	[InlineData("financeiro-user", TipoDePerfil.Setor)]
	[InlineData("gerente-de-compras", TipoDePerfil.Comum)]
	[InlineData("-admin", TipoDePerfil.Comum)]
	[InlineData("all-users", TipoDePerfil.Comum)]
	public void Tipo_ClassificaPeloNome(string nome, TipoDePerfil esperado)
	{
		ClassificacaoDePerfil.Tipo(nome).Should().Be(esperado);
	}

	[Theory]
	[InlineData("installation-operator")]
	[InlineData("Installation-Operator")]
	[InlineData("platform-operator")]
	[InlineData("installation-log-reader")]
	[InlineData("installation-auditor")]
	public void EhReservado_ReconheceOsReservadosDaPlataforma(string nome)
	{
		ClassificacaoDePerfil.EhReservado(nome).Should().BeTrue();
	}

	[Fact]
	public void EhReservado_PerfilComum_Falso()
	{
		ClassificacaoDePerfil.EhReservado("gerente-de-compras").Should().BeFalse();
	}

	[Fact]
	public void DoSetor_ExtraiSlugEPapel()
	{
		var admin = ClassificacaoDePerfil.DoSetor("Marketing-Admin");
		var usuario = ClassificacaoDePerfil.DoSetor("rh-user");

		admin.Should().NotBeNull();
		admin!.Value.Slug.Should().Be("Marketing");
		admin.Value.Papel.Should().Be(PapelNoSetor.Administrador);
		usuario!.Value.Slug.Should().Be("rh");
		usuario.Value.Papel.Should().Be(PapelNoSetor.Usuario);
	}

	[Theory]
	[InlineData("gerente-de-compras")]
	[InlineData("intranet-admin")]
	[InlineData("-user")]
	public void DoSetor_NaoEDeSetor_Nulo(string nome)
	{
		ClassificacaoDePerfil.DoSetor(nome).Should().BeNull();
	}

	[Theory]
	[InlineData("gerente-de-compras", true)]
	[InlineData("a", true)]
	[InlineData("a.b_c-d", true)]
	[InlineData("Equipe1", true)]
	[InlineData("gerente de compras", false)]
	[InlineData("-x", false)]
	[InlineData("x-", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	[InlineData("ação", false)]
	public void NomeValido_SegueARegraDaPlataforma(string? nome, bool esperado)
	{
		ClassificacaoDePerfil.NomeValido(nome).Should().Be(esperado);
	}

	[Fact]
	public void NomeValido_Com101Caracteres_Falso()
	{
		ClassificacaoDePerfil.NomeValido(new string('a', 101)).Should().BeFalse();
		ClassificacaoDePerfil.NomeValido(new string('a', 100)).Should().BeTrue();
	}
}
