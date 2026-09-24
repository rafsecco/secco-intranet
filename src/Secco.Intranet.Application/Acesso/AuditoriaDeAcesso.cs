using System.Text.Json;
using Secco.Intranet.Application.Auditoria;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Registro na trilha das ações da gestão de acesso — só depois que a plataforma aceitou a ação.</summary>
internal static class AuditoriaDeAcesso
{
	/// <summary>Registra uma ação sobre um perfil.</summary>
	public static Task PerfilAsync(
		ITrilhaDeAuditoria trilha, string verbo, string perfil, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Acesso, perfil, JsonSerializer.Serialize(new { perfil })),
			cancellationToken);

	/// <summary>Registra uma ação sobre um usuário, com o e-mail em cache (o SecureGate não guarda nome).</summary>
	public static Task UsuarioAsync(
		ITrilhaDeAuditoria trilha, string verbo, Guid usuarioId, string? email, string? perfil, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(
				verbo,
				RecursosDeAuditoria.Acesso,
				usuarioId.ToString(),
				JsonSerializer.Serialize(new { perfil, usuarioId, email })),
			cancellationToken);
}
