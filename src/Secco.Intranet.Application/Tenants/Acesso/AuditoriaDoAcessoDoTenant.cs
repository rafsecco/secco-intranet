using System.Text.Json;
using Secco.Intranet.Application.Auditoria;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>
/// Registro na trilha da Intranet das ações sobre o acesso de um tenant administrado — só depois que
/// a plataforma aceitou. Nunca recebe e-mail de terceiro nem a lista de permissões.
/// </summary>
internal static class AuditoriaDoAcessoDoTenant
{
	/// <summary>Ação sobre um perfil.</summary>
	public static Task PerfilAsync(ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, string perfil, CancellationToken cancellationToken) =>
		Registrar(trilha, verbo, tenantId, new { tenantId, perfil }, cancellationToken);

	/// <summary>Edição de permissões: só as contagens.</summary>
	public static Task PermissoesAsync(
		ITrilhaDeAuditoria trilha, Guid tenantId, string perfil, int antes, int depois, CancellationToken cancellationToken) =>
		Registrar(trilha, VerbosDeAuditoria.TenantAcessoPermissoesEditar, tenantId, new { tenantId, perfil, antes, depois }, cancellationToken);

	/// <summary>Ação sobre uma pessoa, opcionalmente com o perfil envolvido.</summary>
	public static Task UsuarioAsync(
		ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, Guid usuarioId, string? perfil, CancellationToken cancellationToken) =>
		Registrar(trilha, verbo, tenantId, new { tenantId, usuarioId, perfil }, cancellationToken);

	/// <summary>Cadastro: id devolvido pela plataforma, modo de entrada e perfis iniciais.</summary>
	public static Task CadastroAsync(
		ITrilhaDeAuditoria trilha, Guid tenantId, Guid usuarioId, bool entraPorSenha, IReadOnlyList<string> perfis,
		CancellationToken cancellationToken) =>
		Registrar(
			trilha, VerbosDeAuditoria.TenantAcessoUsuarioCadastrar, tenantId,
			new { tenantId, usuarioId, modo = entraPorSenha ? "convite" : "entra", perfis }, cancellationToken);

	/// <summary>Federação definida; o directory id não é segredo (ADR-0026 da plataforma).</summary>
	public static Task FederacaoAsync(
		ITrilhaDeAuditoria trilha, Guid tenantId, Guid directoryId, bool habilitada, CancellationToken cancellationToken) =>
		Registrar(trilha, VerbosDeAuditoria.TenantAcessoFederacaoDefinir, tenantId, new { tenantId, directoryId, habilitada }, cancellationToken);

	private static Task Registrar(ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, object dados, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Tenant, tenantId.ToString(), JsonSerializer.Serialize(dados)),
			cancellationToken);
}
