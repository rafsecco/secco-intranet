using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Extensibility;
using Secco.Intranet.Web.Navigation;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Avaliação dos gates dos itens extras do menu principal.</summary>
public class GatesDoMenuPrincipalTests
{
	private static readonly ClaimsPrincipal Usuario = new(new ClaimsIdentity());

	[Fact]
	public async Task FiltrarAsync_ModoAberto_DevolveTodos_SemChamarOGate()
	{
		var gate = new ItemFalso("a", () => false);

		var resultado = await GatesDoMenuPrincipal.FiltrarAsync(
			[gate], Usuario, modoAberto: true, new LoggerCapturador<GatesDoMenuPrincipalTests>(), CancellationToken.None);

		resultado.Select(i => i.Chave).Should().Equal("a");
		gate.Chamadas.Should().Be(0);
	}

	[Fact]
	public async Task FiltrarAsync_GateFalso_TiraOItem()
	{
		var resultado = await GatesDoMenuPrincipal.FiltrarAsync(
			[new ItemFalso("a", () => false), new ItemFalso("b", () => true)],
			Usuario, modoAberto: false, new LoggerCapturador<GatesDoMenuPrincipalTests>(), CancellationToken.None);

		resultado.Select(i => i.Chave).Should().Equal("b");
	}

	[Fact]
	public async Task FiltrarAsync_GateQueLanca_TiraOItem_EAvisa()
	{
		var log = new LoggerCapturador<GatesDoMenuPrincipalTests>();

		var resultado = await GatesDoMenuPrincipal.FiltrarAsync(
			[new ItemFalso("quebrado", () => throw new InvalidOperationException("boom")), new ItemFalso("b", () => true)],
			Usuario, modoAberto: false, log, CancellationToken.None);

		resultado.Select(i => i.Chave).Should().Equal("b");
		log.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Warning && e.Mensagem.Contains("quebrado", StringComparison.Ordinal));
	}

	private sealed class ItemFalso(string chave, Func<bool> gate) : IItemDeMenuPrincipal
	{
		public int Chamadas { get; private set; }

		public string Chave => chave;

		public string Rotulo => chave;

		public string? Icone => null;

		public string Url => "/" + chave;

		public int Ordem => 0;

		public Task<bool> VisivelParaAsync(ClaimsPrincipal usuario, CancellationToken cancellationToken)
		{
			Chamadas++;
			return Task.FromResult(gate());
		}
	}
}
