using AwesomeAssertions;
using Secco.Intranet.Domain.Salas;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Regras do cadastro de sala.</summary>
public class SalaTests
{
	[Fact]
	public void Construtor_NomeVazio_Lanca()
	{
		var act = () => new Sala(" ", null, null, null, null, 1);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_CapacidadeZero_Lanca()
	{
		var act = () => new Sala("Sala Azul", null, 0, null, null, 1);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_NasceAtiva()
	{
		var sala = new Sala("Sala Azul", "2º andar", 8, "TV", "azul@exemplo.com", 1);

		sala.Ativa.Should().BeTrue();
	}

	[Fact]
	public void Editar_TrocaOsCampos()
	{
		var sala = new Sala("Sala Azul", null, null, null, null, 1);

		sala.Editar("Sala Verde", "3º andar", 10, "Projetor", "verde@exemplo.com", 2);

		sala.Nome.Should().Be("Sala Verde");
		sala.Localizacao.Should().Be("3º andar");
		sala.Capacidade.Should().Be(10);
		sala.Recursos.Should().Be("Projetor");
		sala.EmailNaAgenda.Should().Be("verde@exemplo.com");
		sala.Ordem.Should().Be(2);
	}

	[Fact]
	public void Desativar_E_Ativar_AlternamOEstado()
	{
		var sala = new Sala("Sala Azul", null, null, null, null, 1);

		sala.Desativar();
		sala.Ativa.Should().BeFalse();

		sala.Ativar();
		sala.Ativa.Should().BeTrue();
	}
}
