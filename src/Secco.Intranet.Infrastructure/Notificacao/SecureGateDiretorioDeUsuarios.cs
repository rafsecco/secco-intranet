using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter real de <see cref="IDiretorioDeUsuarios"/>: uma chamada a <c>ListUsers</c> devolve
/// os usuários do tenant já com os roles, então o filtro por setor acontece em memória, sem
/// N consultas.
/// </summary>
/// <param name="client">Client administrativo do SecureGate.</param>
/// <param name="tenantContext">Tenant da requisição atual (ADR-0005).</param>
/// <param name="logger">Log de falhas, sem dado sensível (ADR-0020).</param>
public sealed class SecureGateDiretorioDeUsuarios(
	ISecureGateClient client,
	ITenantContext tenantContext,
	ILogger<SecureGateDiretorioDeUsuarios> logger) : IDiretorioDeUsuarios
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(
		CancellationToken cancellationToken = default)
	{
		if (!tenantContext.IsResolved)
		{
			return [];
		}

		try
		{
			var usuarios = await client
				.ListUsersAsync(tenantContext.TenantId!.Value, cancellationToken)
				.ConfigureAwait(false);

			return [.. usuarios.Select(usuario => new UsuarioDoTenant(
				usuario.Id,
				usuario.Email,
				usuario.Roles is null ? [] : [.. usuario.Roles]))];
		}
		catch (ApiException apiException)
		{
			// Lista vazia significa "ninguém a avisar", e o relatório sai zerado. Falhar aqui
			// derrubaria a publicação, que é o oposto da regra deste recurso.
			logger.LogWarning(
				"Falha ao listar usuários do tenant no SecureGate (status {StatusCode}).",
				apiException.StatusCode);

			return [];
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao listar usuários do tenant no SecureGate.");

			return [];
		}
		catch (OperationCanceledException operationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Timeout do HttpClient, não cancelamento pedido pelo chamador: mesma falha
			// aberta das demais — o SecureGate aceitou a conexão e não respondeu a tempo.
			logger.LogWarning(operationCanceledException, "Timeout ao listar usuários do tenant no SecureGate.");

			return [];
		}
	}
}
