using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>O núcleo que os dois adaptadores de acesso compartilham: tradução de erro e mapeamentos.</summary>
public class OperacoesDeAcessoNoSecureGateTests
{
	private static ApiException Api(int status, string corpo = "") =>
		new("falha", status, corpo, new Dictionary<string, IEnumerable<string>>(), null);

	[Fact]
	public async Task Ler_404ComErroDeNaoEncontrado_DevolveOErro()
	{
		var lido = await OperacoesDeAcessoNoSecureGate.LerAsync<int>(
			NullLogger.Instance, "ler", _ => throw Api(404), IntranetErrors.Acesso.PerfilNaoEncontrado, CancellationToken.None);

		lido.Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado);
	}

	[Fact]
	public async Task Ler_TimeoutDoHttpClient_ViraIndisponivel_NaoExcecao()
	{
		var lido = await OperacoesDeAcessoNoSecureGate.LerAsync<int>(
			NullLogger.Instance, "ler", _ => throw new TaskCanceledException(), null, CancellationToken.None);

		lido.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task Escrever_409_UsaOMapeamentoDeConflitoPeloCodigo()
	{
		var escrito = await OperacoesDeAcessoNoSecureGate.EscreverAsync(
			NullLogger.Instance,
			"excluir",
			_ => throw Api(409, "{\"code\":\"SecureGate.Role.UsedByClients\"}"),
			null,
			excecao => OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Role.UsedByClients")
				? IntranetErrors.Acesso.PerfilUsadoPorClient
				: IntranetErrors.Acesso.PerfilComMembros,
			CancellationToken.None);

		escrito.Error.Should().Be(IntranetErrors.Acesso.PerfilUsadoPorClient);
	}

	[Fact]
	public async Task Escrever_CancelamentoDoChamador_ERelancado()
	{
		using var cancelado = new CancellationTokenSource();
		await cancelado.CancelAsync();

		var escrever = () => OperacoesDeAcessoNoSecureGate.EscreverAsync(
			NullLogger.Instance, "x", token => Task.FromCanceled(token), null, null, cancelado.Token);

		await escrever.Should().ThrowAsync<OperationCanceledException>();
	}

	[Theory]
	[InlineData("Active", SituacaoDoUsuario.Ativo)]
	[InlineData("deactivated", SituacaoDoUsuario.Desativado)]
	[InlineData("LockedOut", SituacaoDoUsuario.Bloqueado)]
	[InlineData(null, SituacaoDoUsuario.Desconhecida)]
	public void Situacao_MapeiaOStatusDaPlataforma(string? status, SituacaoDoUsuario esperada) =>
		OperacoesDeAcessoNoSecureGate.Situacao(status).Should().Be(esperada);

	[Fact]
	public async Task Escrever_400ComMotivoConhecido_UsaOMapeamento_E400SemMotivo_Indisponivel()
	{
		static Error? Mapear(ApiException excecao) =>
			OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Role.PermissionsNotInCatalog")
				? IntranetErrors.Acesso.PermissoesForaDoCatalogo
				: null;

		var conhecido = await OperacoesDeAcessoNoSecureGate.EscreverAsync(
			NullLogger.Instance, "x", _ => throw Api(400, "{\"title\":\"SecureGate.Role.PermissionsNotInCatalog\"}"),
			null, null, CancellationToken.None, Mapear);
		var desconhecido = await OperacoesDeAcessoNoSecureGate.EscreverAsync(
			NullLogger.Instance, "x", _ => throw Api(400, "{\"title\":\"Outro\"}"),
			null, null, CancellationToken.None, Mapear);

		conhecido.Error.Should().Be(IntranetErrors.Acesso.PermissoesForaDoCatalogo);
		desconhecido.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}
}
