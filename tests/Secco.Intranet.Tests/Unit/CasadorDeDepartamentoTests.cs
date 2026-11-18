using AwesomeAssertions;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Setores;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class CasadorDeDepartamentoTests
{
	private static SetorDto Setor(string nome, bool ativo = true) =>
		new(Guid.NewGuid(), nome, nome.ToLowerInvariant(), "bi-x", false, ativo, DateTimeOffset.UtcNow);

	[Fact]
	public void Casar_IgnoraMaiusculasAcentosEEspacos()
	{
		var financas = Setor("FINANCAS");

		CasadorDeDepartamento.Casar("  Finanças ", [financas, Setor("TI")]).Should().Be(financas);
	}

	[Fact]
	public void Casar_SetorInativo_NaoCasa()
	{
		CasadorDeDepartamento.Casar("TI", [Setor("TI", ativo: false)]).Should().BeNull();
	}

	[Fact]
	public void Casar_Nenhum_Nulo()
	{
		CasadorDeDepartamento.Casar("RH", [Setor("TI")]).Should().BeNull();
	}

	[Fact]
	public void Casar_DoisSetoresComMesmoNomeNormalizado_NaoCasa()
	{
		var setores = new[] { Setor("Finanças"), Setor("FINANCAS") };

		CasadorDeDepartamento.Casar("financas", setores).Should().BeNull();
		CasadorDeDepartamento.EhAmbiguo("financas", setores).Should().BeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Casar_DepartamentoNuloOuBranco_Nulo(string? departamento)
	{
		CasadorDeDepartamento.Casar(departamento, [Setor("TI")]).Should().BeNull();
	}
}
