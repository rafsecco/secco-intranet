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

	private sealed class ClientQueEstoura : ISecureGateClient
	{
		public Task<ICollection<UserDto>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken) =>
			throw new TaskCanceledException("Timeout do HttpClient.");

		public Task<ICollection<UserDto>> ListUsersAsync(Guid tenantId) =>
			ListUsersAsync(tenantId, CancellationToken.None);

		// ISecureGateClient tem muitos membros de administração além da listagem de usuários; o
		// teste não usa nenhum deles, então cada um vira uma linha de NotImplementedException.
		public Task<ICollection<string>> GetRolePermissionsAsync(Guid tenantId, string role) => throw new NotImplementedException();
		public Task<ICollection<string>> GetRolePermissionsAsync(Guid tenantId, string role, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<TenantDto> CreateTenantAsync(CreateTenantRequest body) => throw new NotImplementedException();
		public Task<TenantDto> CreateTenantAsync(CreateTenantRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<ICollection<TenantDto>> ListTenantsAsync() => throw new NotImplementedException();
		public Task<ICollection<TenantDto>> ListTenantsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<TenantDetailDto> GetTenantAsync(Guid id) => throw new NotImplementedException();
		public Task<TenantDetailDto> GetTenantAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task ActivateTenantAsync(Guid id) => throw new NotImplementedException();
		public Task ActivateTenantAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task DeactivateTenantAsync(Guid id) => throw new NotImplementedException();
		public Task DeactivateTenantAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task UpsertTenantDatabaseAsync(Guid id, string product, UpsertTenantDatabaseRequest body) => throw new NotImplementedException();
		public Task UpsertTenantDatabaseAsync(Guid id, string product, UpsertTenantDatabaseRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<TenantDatabaseProvisioningDto> ProvisionTenantDatabaseAsync(Guid id, string product, ProvisionTenantDatabaseRequest body) => throw new NotImplementedException();
		public Task<TenantDatabaseProvisioningDto> ProvisionTenantDatabaseAsync(Guid id, string product, ProvisionTenantDatabaseRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<ICollection<TenantDatabaseStatusDto>> GetTenantDatabaseStatusAsync(Guid id) => throw new NotImplementedException();
		public Task<ICollection<TenantDatabaseStatusDto>> GetTenantDatabaseStatusAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task UpsertTenantFederationAsync(Guid id, UpsertTenantFederationRequest body) => throw new NotImplementedException();
		public Task UpsertTenantFederationAsync(Guid id, UpsertTenantFederationRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<ICollection<CatalogTenantDto>> ListCatalogTenantsAsync(string product) => throw new NotImplementedException();
		public Task<ICollection<CatalogTenantDto>> ListCatalogTenantsAsync(string product, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<CatalogTenantDto> GetCatalogTenantAsync(string product, Guid tenantId) => throw new NotImplementedException();
		public Task<CatalogTenantDto> GetCatalogTenantAsync(string product, Guid tenantId, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<RoleDto> CreateRoleAsync(Guid tenantId, CreateRoleRequest body) => throw new NotImplementedException();
		public Task<RoleDto> CreateRoleAsync(Guid tenantId, CreateRoleRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<ICollection<RoleDto>> ListRolesAsync(Guid tenantId) => throw new NotImplementedException();
		public Task<ICollection<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task SetRolePermissionsAsync(Guid tenantId, string role, SetRolePermissionsRequest body) => throw new NotImplementedException();
		public Task SetRolePermissionsAsync(Guid tenantId, string role, SetRolePermissionsRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<UserDto> CreateUserAsync(Guid tenantId, CreateUserRequest body) => throw new NotImplementedException();
		public Task<UserDto> CreateUserAsync(Guid tenantId, CreateUserRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
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
