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

	/// <summary>Registra uma reconciliação em lote de permissões de setor e do Diretório.</summary>
	public static Task ReconciliacaoAsync(
		ITrilhaDeAuditoria trilha, int setores, int perfisDoDiretorio, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(
				VerbosDeAuditoria.AcessoPermissoesReconciliar, RecursosDeAuditoria.Acesso, "reconciliacao",
				JsonSerializer.Serialize(new { setores, perfisDoDiretorio })),
			cancellationToken);

	/// <summary>Registra a edição de permissões de um perfil — a lista final, não um diff.</summary>
	public static Task PermissoesAsync(
		ITrilhaDeAuditoria trilha, string perfil, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(
				VerbosDeAuditoria.AcessoPermissoesEditar, RecursosDeAuditoria.Acesso, perfil,
				JsonSerializer.Serialize(new { perfil, permissoes })),
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
