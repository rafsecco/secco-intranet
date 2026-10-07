using AwesomeAssertions;
using Secco.Intranet.Application.Tenants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class RecursosDaPlataformaTests
{
	[Theory]
	[InlineData("securegate", RecursoDaPlataforma.SecureGate)]
	[InlineData("logstream", RecursoDaPlataforma.LogStream)]
	[InlineData("NotificationHub", RecursoDaPlataforma.NotificationHub)]
	public void TentarLer_ValorDaLista_Reconhece(string valor, RecursoDaPlataforma esperado)
	{
		RecursosDaPlataforma.TentarLer(valor, out var recurso).Should().BeTrue();
		recurso.Should().Be(esperado);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("intranet")]
	[InlineData("compras")]
	[InlineData("logstream ")]
	[InlineData("0")]
	[InlineData("1")]
	public void TentarLer_ForaDaLista_Recusa(string? valor)
	{
		RecursosDaPlataforma.TentarLer(valor, out _).Should().BeFalse("o texto da rota nunca escolhe produto arbitrário");
	}

	[Fact]
	public void Produto_SoLogStreamENotificationHubTemBanco()
	{
		RecursosDaPlataforma.Produto(RecursoDaPlataforma.SecureGate).Should().BeNull();
		RecursosDaPlataforma.Produto(RecursoDaPlataforma.LogStream).Should().Be("logstream");
		RecursosDaPlataforma.Produto(RecursoDaPlataforma.NotificationHub).Should().Be("notificationhub");
	}

	[Fact]
	public void Rota_VoltaParaOMesmoRecurso()
	{
		foreach (var recurso in RecursosDaPlataforma.Todos)
		{
			RecursosDaPlataforma.TentarLer(RecursosDaPlataforma.Rota(recurso), out var lido).Should().BeTrue();
			lido.Should().Be(recurso);
		}
	}
}
