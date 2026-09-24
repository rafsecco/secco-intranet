using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Infrastructure.Notificacao;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// O adaptador do diretório de usuários. O que importa aqui é a mesma garantia dos demais
/// adaptadores de borda: um timeout do <c>HttpClient</c> não pode escapar como exceção — o
/// SecureGateDiretorioDeUsuarios é chamado a partir de <c>PublicarPublicacaoHandler.AvisarAsync</c>,
/// depois que a publicação já foi persistida e auditada, então deixar o timeout escapar
/// derrubaria uma resposta que já devia ser de sucesso.
/// </summary>
public class SecureGateDiretorioDeUsuariosTests
{
	private sealed class TenantContextFalso(Guid tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => true;
	}

	/// <summary>
	/// Herda do client gerado em vez de implementar <c>ISecureGateClient</c> à mão: a interface
	/// ganha métodos a cada release da plataforma, e um fake que a implementa inteira quebra a
	/// compilação a cada bump de pacote. Aqui só a listagem de usuários importa.
	/// </summary>
	private sealed class ClientQueEstoura() : SecureGateClient(new HttpClient())
	{
		public override Task<ICollection<UserDto>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken) =>
			throw new TaskCanceledException("Timeout do HttpClient.");
	}

	[Fact]
	public async Task ListarDoTenantAtual_ComTimeout_DevolveListaVazia()
	{
		var diretorio = new SecureGateDiretorioDeUsuarios(
			new ClientQueEstoura(),
			new TenantContextFalso(Guid.NewGuid()),
			NullLogger<SecureGateDiretorioDeUsuarios>.Instance);

		var usuarios = await diretorio.ListarDoTenantAtualAsync();

		usuarios.Should().BeEmpty(
			"timeout do HttpClient é falha de rede como outra qualquer — o SecureGate aceitou " +
			"a conexão e não respondeu a tempo, e a publicação já aconteceu");
	}

	[Fact]
	public async Task ListarDoTenantAtual_ComCancelamentoDoChamador_Relanca()
	{
		var diretorio = new SecureGateDiretorioDeUsuarios(
			new ClientQueEstoura(),
			new TenantContextFalso(Guid.NewGuid()),
			NullLogger<SecureGateDiretorioDeUsuarios>.Instance);

		using var cts = new CancellationTokenSource();
		cts.Cancel();

		var listar = async () => await diretorio.ListarDoTenantAtualAsync(cts.Token);

		await listar.Should().ThrowAsync<OperationCanceledException>(
			"cancelamento pedido pelo chamador não é falha de infraestrutura — a requisição " +
			"está sendo abandonada de qualquer forma");
	}
}
