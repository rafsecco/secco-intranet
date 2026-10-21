using System.Text.Json;
using Secco.Intranet.Application.Auditoria;

namespace Secco.Intranet.Application.Tenants.Credenciais;

/// <summary>
/// Registro na trilha da Intranet das ações sobre as credenciais de um tenant administrado — só depois
/// que a plataforma aceitou. Nunca recebe o secret.
/// </summary>
internal static class AuditoriaDeCredenciais
{
	/// <summary>Emissão: quem é, o que alcança e quantos endereços de retorno tem.</summary>
	public static Task EmitidaAsync(
		ITrilhaDeAuditoria trilha, Guid tenantId, string clientId, DadosDaCredencial dados, CancellationToken cancellationToken) =>
		Registrar(
			trilha, VerbosDeAuditoria.TenantCredencialEmitir, tenantId,
			new { tenantId, clientId, tipo = dados.Tipo.ToString(), escopos = dados.Escopos, perfis = dados.Perfis, enderecos = dados.RedirectUris.Count },
			cancellationToken);

	/// <summary>Alteração: os valores novos.</summary>
	public static Task AlteradaAsync(
		ITrilhaDeAuditoria trilha, Guid tenantId, string clientId, DadosDaCredencial dados, CancellationToken cancellationToken) =>
		Registrar(
			trilha, VerbosDeAuditoria.TenantCredencialAlterar, tenantId,
			new { tenantId, clientId, escopos = dados.Escopos, perfis = dados.Perfis, redirectUris = dados.RedirectUris, redirectUrisDeLogout = dados.RedirectUrisDeLogout },
			cancellationToken);

	/// <summary>Rotação do secret: só a credencial.</summary>
	public static Task RotacionadaAsync(ITrilhaDeAuditoria trilha, Guid tenantId, string clientId, CancellationToken cancellationToken) =>
		Registrar(trilha, VerbosDeAuditoria.TenantCredencialRotacionar, tenantId, new { tenantId, clientId }, cancellationToken);

	/// <summary>Revogação: a credencial e o nome que ela tinha.</summary>
	public static Task RevogadaAsync(
		ITrilhaDeAuditoria trilha, Guid tenantId, string clientId, string nome, CancellationToken cancellationToken) =>
		Registrar(trilha, VerbosDeAuditoria.TenantCredencialRevogar, tenantId, new { tenantId, clientId, nome }, cancellationToken);

	private static Task Registrar(ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, object dados, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Tenant, tenantId.ToString(), JsonSerializer.Serialize(dados)),
			cancellationToken);
}
