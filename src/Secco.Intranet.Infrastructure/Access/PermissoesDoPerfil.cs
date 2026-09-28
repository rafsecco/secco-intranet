using Secco.SecureGate.Client;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Mesclagem de permissão de uma Role (ADR-0021): lê o que já existe, une com o mínimo exigido,
/// grava só se mudou algo. Nunca remove uma permissão que já estava lá — quem chama isso nunca
/// sabe o estado inteiro da Role, só o que quer garantir. Compartilhado por
/// <see cref="SecureGateGestaoDeAcesso"/> e <see cref="SecureGateSetorAccessProvisioner"/>; cada
/// chamador trata exceção do jeito que já trata as próprias chamadas ao client — este helper não
/// captura nada.
/// </summary>
/// <remarks>
/// Lê o estado atual por <c>GetRoleAsync</c> (o <c>GetRole</c> de administração, escopo
/// <c>securegate:admin</c> — o mesmo que já usamos), <b>não</b> por <c>GetRolePermissionsAsync</c>:
/// esse outro método do client gerado fala com <c>/api/v1/authorization/...</c>, uma superfície
/// separada para resolução de permissão em runtime (o <c>IPermissionResolver</c> da plataforma),
/// que exige o escopo <c>authorization:read</c> — o client administrativo desta Intranet não o
/// tem, e a chamada dá 403. Achado pela fumaça contra o SecureGate real (2026-09-27); só
/// <c>SetRolePermissionsAsync</c> (gravar) é mesmo do grupo administrativo.
/// </remarks>
internal static class PermissoesDoPerfil
{
	/// <summary>Garante que a Role tenha, no mínimo, as permissões informadas.</summary>
	/// <param name="client">Client administrativo do SecureGate.</param>
	/// <param name="tenantId">Tenant da Role.</param>
	/// <param name="role">Nome da Role.</param>
	/// <param name="minimas">Permissões que a Role precisa ter ao final.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task GarantirAsync(
		ISecureGateClient client,
		Guid tenantId,
		string role,
		IReadOnlyCollection<string> minimas,
		CancellationToken cancellationToken)
	{
		var papel = await client.GetRoleAsync(tenantId, role, cancellationToken).ConfigureAwait(false);
		var uniao = new HashSet<string>(papel.Permissions ?? [], StringComparer.Ordinal);
		var mudou = false;

		foreach (var permissao in minimas)
		{
			if (uniao.Add(permissao))
			{
				mudou = true;
			}
		}

		if (!mudou)
		{
			return;
		}

		await client
			.SetRolePermissionsAsync(tenantId, role, new SetRolePermissionsRequest { Permissions = [.. uniao] }, cancellationToken)
			.ConfigureAwait(false);
	}
}
