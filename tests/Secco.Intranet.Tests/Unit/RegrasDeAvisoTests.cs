using AwesomeAssertions;
using Secco.Intranet.Domain.Vencimentos;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Regra dos marcos de aviso: estado do vencimento e decisão do que enviar hoje.</summary>
public class RegrasDeAvisoTests
{
	private static readonly DateOnly Vencimento = new(2026, 12, 31);

	private static ItemVencimento Criar(IReadOnlyList<int>? antecedencias = null) =>
		new(Guid.NewGuid(), "Licenca", null, Vencimento, null, antecedencias);

	private static HashSet<int> Tratados(params int[] marcos) => [.. marcos];

	[Theory]
	[InlineData("2026-11-30", EstadoDoVencimento.EmDia)]
	[InlineData("2026-12-01", EstadoDoVencimento.AVencer)]
	[InlineData("2026-12-31", EstadoDoVencimento.VenceHoje)]
	[InlineData("2027-01-01", EstadoDoVencimento.Vencido)]
	public void Estado_NasFronteiras(string hoje, EstadoDoVencimento esperado)
	{
		RegrasDeAviso.Estado(Criar(), DateOnly.Parse(hoje)).Should().Be(esperado);
	}

	[Fact]
	public void Estado_Encerrado_EEncerrado()
	{
		var item = Criar();
		item.Encerrar("ana@x.com", DateTimeOffset.UtcNow);

		RegrasDeAviso.Estado(item, new DateOnly(2027, 5, 1)).Should().Be(EstadoDoVencimento.Encerrado);
	}

	[Fact]
	public void Decidir_NoPrimeiroDiaDaJanela_EnviaMarco30()
	{
		var decisao = RegrasDeAviso.Decidir(Criar(), new DateOnly(2026, 12, 1), Tratados());

		decisao.MarcoAEnviar.Should().Be(30);
		decisao.DiasRestantes.Should().Be(30);
		decisao.MarcosAPular.Should().BeEmpty();
	}

	[Fact]
	public void Decidir_VariosMarcosDevidos_EnviaSoOMaisProximo_EPulaOsOutros()
	{
		var decisao = RegrasDeAviso.Decidir(Criar(), new DateOnly(2026, 12, 26), Tratados());

		decisao.MarcoAEnviar.Should().Be(7);
		decisao.MarcosAPular.Should().BeEquivalentTo([30]);
		decisao.DiasRestantes.Should().Be(5);
	}

	[Fact]
	public void Decidir_MarcoJaTratado_NaoRepete()
	{
		var decisao = RegrasDeAviso.Decidir(Criar(), new DateOnly(2026, 12, 10), Tratados(30));

		decisao.MarcoAEnviar.Should().BeNull();
	}

	[Fact]
	public void Decidir_CadastradoNoDia_EnviaMarcoZero()
	{
		var decisao = RegrasDeAviso.Decidir(Criar(), Vencimento, Tratados());

		decisao.MarcoAEnviar.Should().Be(0);
		decisao.MarcosAPular.Should().BeEquivalentTo([30, 7, 1]);
	}

	[Fact]
	public void Decidir_CadastradoJaVencido_PulaTudo()
	{
		var decisao = RegrasDeAviso.Decidir(Criar(), new DateOnly(2027, 1, 15), Tratados());

		decisao.MarcoAEnviar.Should().BeNull();
		decisao.MarcosAPular.Should().BeEquivalentTo([0, 1, 7, 30]);
	}

	[Fact]
	public void Decidir_MarcoZeroDepoisDoDia_Pula()
	{
		var decisao = RegrasDeAviso.Decidir(Criar(), new DateOnly(2027, 1, 1), Tratados(30, 7, 1));

		decisao.MarcoAEnviar.Should().BeNull();
		decisao.MarcosAPular.Should().BeEquivalentTo([0]);
	}

	[Fact]
	public void Decidir_MarcoMaisDistanteQueUmJaTratado_Pula()
	{
		var decisao = RegrasDeAviso.Decidir(Criar([30, 7]), new DateOnly(2026, 12, 26), Tratados(7));

		decisao.MarcoAEnviar.Should().BeNull();
		decisao.MarcosAPular.Should().BeEquivalentTo([30]);
	}

	[Fact]
	public void Decidir_MarcoNovoSemNadaTratado_Envia()
	{
		var decisao = RegrasDeAviso.Decidir(Criar([30]), new DateOnly(2026, 12, 21), Tratados());

		decisao.MarcoAEnviar.Should().Be(30);
		decisao.DiasRestantes.Should().Be(10);
	}

	[Fact]
	public void Decidir_Encerrado_NaoEnviaNemPula()
	{
		var item = Criar();
		item.Encerrar("ana@x.com", DateTimeOffset.UtcNow);

		var decisao = RegrasDeAviso.Decidir(item, new DateOnly(2026, 12, 26), Tratados());

		decisao.MarcoAEnviar.Should().BeNull();
		decisao.MarcosAPular.Should().BeEmpty();
	}

	[Fact]
	public void ProximosAvisos_ListaOsNaoTratadosFuturos_EmOrdemDeData()
	{
		var proximos = RegrasDeAviso.ProximosAvisos(Criar(), new DateOnly(2026, 12, 10), Tratados(30));

		proximos.Should().Equal(
			(7, new DateOnly(2026, 12, 24)),
			(1, new DateOnly(2026, 12, 30)),
			(0, new DateOnly(2026, 12, 31)));
	}
}
