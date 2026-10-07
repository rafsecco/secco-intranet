using System.Text.Json;
using Secco.Intranet.Application.Auditoria;

namespace Secco.Intranet.Application.Tenants;

/// <summary>
/// Registro na trilha das ações sobre tenants — só depois que a plataforma aceitou. Na plataforma,
/// o ator dessas chamadas é o client da Intranet; esta trilha é o único registro de <b>quem</b>.
/// Nunca recebe script, senha ou connection string.
/// </summary>
internal static class AuditoriaDeTenants
{
	/// <summary>Registra uma ação sobre o tenant.</summary>
	public static Task TenantAsync(
		ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, string sistema, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Tenant, tenantId.ToString(),
				JsonSerializer.Serialize(new { tenantId, sistema })),
			cancellationToken);

	/// <summary>Registra uma ação sobre um recurso do tenant.</summary>
	public static Task RecursoAsync(
		ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, string sistema, RecursoDaPlataforma recurso, bool aplicado,
		CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Tenant, tenantId.ToString(),
				JsonSerializer.Serialize(new { tenantId, sistema, recurso = RecursosDaPlataforma.Rota(recurso), aplicado })),
			cancellationToken);
}
