using AwesomeAssertions;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Application.Tenants.Credenciais;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class RegrasDeCredencialTests
{
	[Theory]
	[InlineData("cli_abcdefghijklmnop", true)]
	[InlineData("cli_abcdefghijklmno2", true)]
	[InlineData("cli_ABCDEFGHIJKLMNOP", false)]
	[InlineData("cli_abcdefghijklmno1", false)] // 1 não é base32
	[InlineData("cli_abcdefghijklmnop\n", false)]
	[InlineData("cli_abcdefghijklmnop/../x", false)]
	[InlineData("cli_abc", false)]
	[InlineData("secco-intranet", false)]
	[InlineData("../cli_abcdefghijklmnop", false)]
	[InlineData(null, false)]
	public void ClientIdValido(string? clientId, bool esperado) =>
		RegrasDeCredencial.ClientIdValido(clientId).Should().Be(esperado);

	[Theory]
	[InlineData("https://compras.empresa.com/signin-oidc", false, true)]
	[InlineData("http://localhost:5001/signin-oidc", false, false)]
	[InlineData("http://localhost:5001/signin-oidc", true, true)]
	[InlineData("https://user:pw@compras.empresa.com/cb", false, false)]
	[InlineData("https://compras.empresa.com/cb#x", false, false)]
	[InlineData("/signin-oidc", false, false)]
	[InlineData("javascript:alert(1)", true, false)]
	public void EnderecoValido(string uri, bool permitirHttp, bool esperado) =>
		RegrasDeCredencial.EnderecoValido(uri, permitirHttp).Should().Be(esperado);

	[Fact]
	public void EnderecoValido_AcimaDe2000_Recusa() =>
		RegrasDeCredencial.EnderecoValido("https://a.com/" + new string('x', 2000), false).Should().BeFalse();

	[Fact]
	public void Linhas_AparaDescartaVaziasEDuplicadas() =>
		RegrasDeCredencial.Linhas(" https://a.com/cb \r\n\r\nhttps://a.com/cb\nhttps://b.com/cb\n")
			.Should().Equal("https://a.com/cb", "https://b.com/cb");

	[Theory]
	[InlineData("Integração do Compras", true)]
	[InlineData("", false)]
	[InlineData("nome\u0007com controle", false)]
	public void NomeValido(string nome, bool esperado) => RegrasDeCredencial.NomeValido(nome).Should().Be(esperado);

	[Fact]
	public void NomeValido_101_Recusa() => RegrasDeCredencial.NomeValido(new string('a', 101)).Should().BeFalse();

	[Fact]
	public void EscoposLigados_SoRecursoComBancoENaoDesligado()
	{
		RecursoDoTenantDto[] recursos =
		[
			new(RecursoDaPlataforma.SecureGate, SituacaoDoRecurso.Ligado, null),
			new(RecursoDaPlataforma.LogStream, SituacaoDoRecurso.LigadoSemResponder, "x"),
			new(RecursoDaPlataforma.NotificationHub, SituacaoDoRecurso.NaoLigado, null),
		];

		RegrasDeCredencial.EscoposLigados(recursos).Should().Equal("logstream");
		RegrasDeCredencial.SecureGateLigado(recursos).Should().BeTrue();
	}

	[Fact]
	public void SecretDaCredencial_ToString_NaoTrazOValor()
	{
		var secret = new SecretDaCredencial("cli_abcdefghijklmnop", "s3cr3t-valor");

		secret.ToString().Should().NotContain("s3cr3t-valor").And.Contain("cli_abcdefghijklmnop");
		new CredencialEmitidaDto(Credencial(), secret).ToString().Should().NotContain("s3cr3t-valor");
	}

	private static CredencialDto Credencial() =>
		new("cli_abcdefghijklmnop", "Integração", TipoDeCredencial.Maquina, ["logstream"], [], [], [], null);
}
