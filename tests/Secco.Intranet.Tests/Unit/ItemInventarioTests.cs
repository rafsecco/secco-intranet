using AwesomeAssertions;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Item de inventário: sem setor dono (revisão da ADR-0001 em 2026-09-12), com a máquina de
/// estado da spec — Baixar() é terminal, o resto é regra local de cada transição.
/// </summary>
public class ItemInventarioTests
{
	private static ItemInventario Criar(string nome = "Notebook Dell Latitude") =>
		new(nome, descricao: null, categoria: null, codigoPatrimonio: null, setorId: null);

	[Fact]
	public void Criar_NomeVazio_Lanca()
	{
		var acao = () => new ItemInventario(" ", null, null, null, null);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_NasceDisponivel()
	{
		Criar().Status.Should().Be(StatusDoItem.Disponivel);
	}

	[Fact]
	public void Atribuir_APartirDeDisponivel_MudaParaEmUso()
	{
		var item = Criar();
		var usuarioId = Guid.NewGuid();

		item.Atribuir(usuarioId, "ana@exemplo.local");

		item.Status.Should().Be(StatusDoItem.EmUso);
		item.AtribuidoAUsuarioId.Should().Be(usuarioId);
		item.AtribuidoANome.Should().Be("ana@exemplo.local");
	}

	[Fact]
	public void Atribuir_APartirDeEmUso_Reatribui()
	{
		var item = Criar();
		item.Atribuir(Guid.NewGuid(), "ana@exemplo.local");
		var novoUsuarioId = Guid.NewGuid();

		item.Atribuir(novoUsuarioId, "bruno@exemplo.local");

		item.AtribuidoAUsuarioId.Should().Be(novoUsuarioId);
		item.AtribuidoANome.Should().Be("bruno@exemplo.local");
	}

	[Fact]
	public void Atribuir_UsuarioVazio_Lanca()
	{
		var item = Criar();

		var acao = () => item.Atribuir(Guid.Empty, "ana@exemplo.local");

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Atribuir_APartirDeEmManutencao_Lanca()
	{
		var item = Criar();
		item.EnviarParaManutencao();

		var acao = () => item.Atribuir(Guid.NewGuid(), "ana@exemplo.local");

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Desatribuir_APartirDeEmUso_VoltaADisponivel()
	{
		var item = Criar();
		item.Atribuir(Guid.NewGuid(), "ana@exemplo.local");

		item.Desatribuir();

		item.Status.Should().Be(StatusDoItem.Disponivel);
		item.AtribuidoAUsuarioId.Should().BeNull();
		item.AtribuidoANome.Should().BeNull();
	}

	[Fact]
	public void Desatribuir_APartirDeDisponivel_Lanca()
	{
		var item = Criar();

		var acao = item.Desatribuir;

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void EnviarParaManutencao_MantemAtribuido()
	{
		var item = Criar();
		item.Atribuir(Guid.NewGuid(), "ana@exemplo.local");

		item.EnviarParaManutencao();

		item.Status.Should().Be(StatusDoItem.EmManutencao);
		item.AtribuidoANome.Should().Be("ana@exemplo.local");
	}

	[Fact]
	public void VoltarDaManutencao_ComAtribuido_VoltaAEmUso()
	{
		var item = Criar();
		item.Atribuir(Guid.NewGuid(), "ana@exemplo.local");
		item.EnviarParaManutencao();

		item.VoltarDaManutencao();

		item.Status.Should().Be(StatusDoItem.EmUso);
	}

	[Fact]
	public void VoltarDaManutencao_SemAtribuido_VoltaADisponivel()
	{
		var item = Criar();
		item.EnviarParaManutencao();

		item.VoltarDaManutencao();

		item.Status.Should().Be(StatusDoItem.Disponivel);
	}

	[Fact]
	public void VoltarDaManutencao_SemEstarEmManutencao_Lanca()
	{
		var item = Criar();

		var acao = item.VoltarDaManutencao;

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Baixar_ETerminal()
	{
		var item = Criar();

		item.Baixar();

		item.Status.Should().Be(StatusDoItem.Baixado);
	}

	[Theory]
	[InlineData(nameof(ItemInventario.Desatribuir))]
	[InlineData(nameof(ItemInventario.EnviarParaManutencao))]
	[InlineData(nameof(ItemInventario.VoltarDaManutencao))]
	[InlineData(nameof(ItemInventario.Baixar))]
	public void QualquerMetodo_ApósBaixado_Lanca(string metodo)
	{
		var item = Criar();
		item.Baixar();

		Action acao = metodo switch
		{
			nameof(ItemInventario.Desatribuir) => item.Desatribuir,
			nameof(ItemInventario.EnviarParaManutencao) => item.EnviarParaManutencao,
			nameof(ItemInventario.VoltarDaManutencao) => item.VoltarDaManutencao,
			nameof(ItemInventario.Baixar) => item.Baixar,
			_ => throw new InvalidOperationException(),
		};

		acao.Should().Throw<DomainInvariantException>("um item baixado é terminal");
	}

	[Fact]
	public void Editar_AposBaixado_Lanca()
	{
		var item = Criar();
		item.Baixar();

		var acao = () => item.Editar("Novo nome", null, null, null, null);

		acao.Should().Throw<DomainInvariantException>();
	}
}
