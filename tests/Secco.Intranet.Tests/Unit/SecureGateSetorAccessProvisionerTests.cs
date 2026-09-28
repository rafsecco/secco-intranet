using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// O provisionador de Roles de setor (ADR-0001) agora também garante a permissão de cada Role
/// (ADR-0021) — mesma ideia dos outros adaptadores de borda: dublê herdando o client gerado, não
/// implementando a interface (ela cresce a cada release).
/// </summary>
public class SecureGateSetorAccessProvisionerTests
{
	private static readonly Guid Tenant = Guid.NewGuid();

	private sealed class TenantContextFalso(Guid? tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => tenantId is not null;
	}

	private sealed class ClientFalso() : SecureGateClient(new HttpClient())
	{
		public Dictionary<string, List<string>> PermissoesPorRole { get; } = [];

		public override Task<RoleDto> CreateRoleAsync(Guid tenantId, CreateRoleRequest body, CancellationToken cancellationToken) =>
			Task.FromResult(new RoleDto { Name = body.Name });

		// A mesclagem lê pelo GetRole administrativo, não pelo GetRolePermissions (esse fala com
		// /api/v1/authorization/..., escopo authorization:read — que o client admin não tem;
		// achado pela fumaça real, 2026-09-27).
		public override Task<RoleDetailDto> GetRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken) =>
			Task.FromResult(new RoleDetailDto
			{
				Name = role,
				Permissions = PermissoesPorRole.TryGetValue(role, out var permissoes) ? permissoes : [],
			});

		public override Task SetRolePermissionsAsync(
			Guid tenantId, string role, SetRolePermissionsRequest body, CancellationToken cancellationToken)
		{
			PermissoesPorRole[role] = [.. body.Permissions];

			return Task.CompletedTask;
		}
	}

	private static SecureGateSetorAccessProvisioner Montar(ClientFalso client) =>
		new(client, new TenantContextFalso(Tenant), NullLogger<SecureGateSetorAccessProvisioner>.Instance);

	[Fact]
	public async Task EnsureSetorRolesAsync_GarantePermissaoDeLeituraEEscritaNasDuasRoles()
	{
		var client = new ClientFalso();

		var resultado = await Montar(client).EnsureSetorRolesAsync("financeiro");

		resultado.IsSuccess.Should().BeTrue();
		client.PermissoesPorRole["financeiro-user"].Should().BeEquivalentTo(IntranetPermissoes.Setor.Read("financeiro"));
		client.PermissoesPorRole["financeiro-admin"].Should().BeEquivalentTo(
			IntranetPermissoes.Setor.Read("financeiro"), IntranetPermissoes.Setor.Write("financeiro"));
	}

	[Fact]
	public async Task EnsureSetorRolesAsync_PreservaPermissaoExtraJaGravada()
	{
		var client = new ClientFalso();
		client.PermissoesPorRole["financeiro-admin"] = ["extra-que-o-admin-somou:read"];

		await Montar(client).EnsureSetorRolesAsync("financeiro");

		client.PermissoesPorRole["financeiro-admin"].Should().BeEquivalentTo(
			"extra-que-o-admin-somou:read", IntranetPermissoes.Setor.Read("financeiro"), IntranetPermissoes.Setor.Write("financeiro"));
	}

	[Fact]
	public async Task EnsureSetorRolesAsync_NormalizaOSlug()
	{
		var client = new ClientFalso();

		await Montar(client).EnsureSetorRolesAsync("  Financeiro ");

		client.PermissoesPorRole.Should().ContainKey("financeiro-user").And.ContainKey("financeiro-admin");
	}
}
