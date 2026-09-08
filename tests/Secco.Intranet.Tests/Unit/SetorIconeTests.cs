using AwesomeAssertions;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// O ícone do setor vai direto para o atributo <c>class</c> do menu. Por isso ele é validado
/// no domínio: sem a guarda, um valor qualquer viraria classe CSS arbitrária — digitar
/// <c>d-none</c> sumiria com o próprio item do menu, e o culpado seria invisível.
/// </summary>
public class SetorIconeTests
{
	[Fact]
	public void SemIcone_UsaOPadrao()
	{
		var setor = new Setor("Financeiro", "financeiro");

		setor.Icone.Should().Be(Setor.IconePadrao);
	}

	[Theory]
	[InlineData("bi-cash-coin")]
	[InlineData("bi-people")]
	[InlineData("bi-123")]
	public void IconeValido_EAceito(string icone)
	{
		var setor = new Setor("Financeiro", "financeiro", icone: icone);

		setor.Icone.Should().Be(icone);
	}

	[Fact]
	public void Icone_ENormalizadoParaMinusculas()
	{
		var setor = new Setor("Financeiro", "financeiro", icone: "  BI-Cash-Coin  ");

		setor.Icone.Should().Be("bi-cash-coin");
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void IconeVazio_CaiNoPadrao(string icone)
	{
		var setor = new Setor("Financeiro", "financeiro", icone: icone);

		setor.Icone.Should().Be(Setor.IconePadrao, "campo em branco significa 'use o de sempre'");
	}

	[Theory]
	[InlineData("d-none")]
	[InlineData("cash-coin")]
	[InlineData("bi bi-cash-coin")]
	[InlineData("bi-cash coin")]
	[InlineData("bi-cash\"onload=x")]
	public void IconeForaDoPadrao_Recusa(string icone)
	{
		var criar = () => new Setor("Financeiro", "financeiro", icone: icone);

		criar.Should().Throw<DomainInvariantException>(
			"o valor vira classe CSS do menu, então só o formato do Bootstrap Icons entra");
	}

	[Fact]
	public void Menu_UsaOIconeDoSetor()
	{
		var setor = new SetorDto(
			Guid.NewGuid(), "Financeiro", "financeiro", "bi-cash-coin", false, true, DateTimeOffset.UtcNow);

		var menu = IntranetNavigation.Build(new NavigationRequest([setor], "/", false, false));

		menu.Grupos
			.SelectMany(grupo => grupo.Itens)
			.Should().ContainSingle(item => item.Texto == "Financeiro")
			.Which.Icone.Should().Be("bi-cash-coin", "o menu deixa de usar um ícone fixo para todos");
	}
}
