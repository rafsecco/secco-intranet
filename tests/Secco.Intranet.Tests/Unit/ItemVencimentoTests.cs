using AwesomeAssertions;
using Secco.Intranet.Domain.Vencimentos;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Item de vencimento: invariantes, ciclos de aviso, renovação e encerramento.</summary>
public class ItemVencimentoTests
{
	private static readonly DateOnly Vencimento = new(2026, 12, 31);
	private static readonly DateOnly Hoje = new(2026, 10, 24);

	private static ItemVencimento Criar(IReadOnlyList<int>? antecedencias = null, DateOnly? data = null) =>
		new(Guid.NewGuid(), "Certificado digital", null, data ?? Vencimento, null, antecedencias);

	[Fact]
	public void Criar_SemAntecedencias_UsaPadrao30_7_1()
	{
		var item = Criar();

		item.Antecedencias.Should().Equal(30, 7, 1);
		item.Ciclo.Should().Be(1);
	}

	[Fact]
	public void Criar_AntecedenciasDesordenadas_GuardaDecrescente()
	{
		Criar([1, 30, 7]).Antecedencias.Should().Equal(30, 7, 1);
	}

	[Fact]
	public void Criar_TituloVazio_Lanca()
	{
		var acao = () => new ItemVencimento(Guid.NewGuid(), " ", null, Vencimento, null, null);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(366)]
	public void Criar_AntecedenciaForaDaFaixa_Lanca(int valor)
	{
		var acao = () => Criar([valor]);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_AntecedenciasRepetidas_Lanca()
	{
		var acao = () => Criar([7, 7]);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_MaisDeSeisAntecedencias_Lanca()
	{
		var acao = () => Criar([1, 2, 3, 4, 5, 6, 7]);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_DataNoPassado_Aceita()
	{
		var item = Criar(data: new DateOnly(2020, 1, 1));

		item.DataVencimento.Should().Be(new DateOnly(2020, 1, 1));
	}

	[Fact]
	public void Editar_MudandoAData_AbreCicloNovo()
	{
		var item = Criar();

		var mudou = item.Editar("Certificado", null, Vencimento.AddDays(10), null, [30, 7, 1]);

		mudou.Should().BeTrue();
		item.Ciclo.Should().Be(2);
	}

	[Fact]
	public void Editar_SemMudarAData_MantemOCiclo()
	{
		var item = Criar();

		var mudou = item.Editar("Outro titulo", "desc", Vencimento, null, [15]);

		mudou.Should().BeFalse();
		item.Ciclo.Should().Be(1);
		item.Titulo.Should().Be("Outro titulo");
		item.Antecedencias.Should().Equal(15);
	}

	[Fact]
	public void Renovar_DataNaoPosteriorAAtual_Lanca()
	{
		var item = Criar();

		var acao = () => item.Renovar(Vencimento, Hoje, "ana@x.com", null);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Renovar_DataNaoPosteriorAHoje_Lanca()
	{
		var item = Criar(data: new DateOnly(2026, 1, 1));

		var acao = () => item.Renovar(new DateOnly(2026, 10, 24), Hoje, "ana@x.com", null);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Renovar_RegistraHistoricoEAbreCiclo()
	{
		var item = Criar();
		var nova = Vencimento.AddYears(1);

		var renovacao = item.Renovar(nova, Hoje, "ana@x.com", "renovado");

		renovacao.ItemVencimentoId.Should().Be(item.Id);
		renovacao.CicloAnterior.Should().Be(1);
		renovacao.DataAnterior.Should().Be(Vencimento);
		renovacao.DataNova.Should().Be(nova);
		renovacao.RenovadoPor.Should().Be("ana@x.com");
		renovacao.Observacao.Should().Be("renovado");
		item.Ciclo.Should().Be(2);
		item.DataVencimento.Should().Be(nova);
	}

	[Fact]
	public void Encerrado_NaoEditaNemRenova()
	{
		var item = Criar();
		item.Encerrar("ana@x.com", DateTimeOffset.UtcNow);

		var editar = () => item.Editar("x", null, Vencimento, null, [7]);
		var renovar = () => item.Renovar(Vencimento.AddYears(1), Hoje, "ana@x.com", null);

		editar.Should().Throw<DomainInvariantException>();
		renovar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Encerrar_DuasVezes_NaoMudaOPrimeiroRegistro()
	{
		var item = Criar();
		var primeiro = new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);

		item.Encerrar("ana@x.com", primeiro);
		item.Encerrar("bia@x.com", primeiro.AddDays(1));

		item.EncerradoEm.Should().Be(primeiro);
		item.EncerradoPor.Should().Be("ana@x.com");
	}

	[Fact]
	public void Reativar_ExigeDataFutura_EAbreCiclo()
	{
		var item = Criar();
		item.Encerrar("ana@x.com", DateTimeOffset.UtcNow);

		var passada = () => item.Reativar(Hoje, Hoje);
		passada.Should().Throw<DomainInvariantException>();

		item.Reativar(Hoje.AddDays(5), Hoje);

		item.EncerradoEm.Should().BeNull();
		item.EncerradoPor.Should().BeNull();
		item.DataVencimento.Should().Be(Hoje.AddDays(5));
		item.Ciclo.Should().Be(2);
	}
}
