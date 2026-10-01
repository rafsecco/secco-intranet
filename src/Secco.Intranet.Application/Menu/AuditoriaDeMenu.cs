using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Menu;

/// <summary>Registro na trilha das ações da árvore de menu — só depois que a ação foi salva.</summary>
internal static class AuditoriaDeMenu
{
	/// <summary>Registra uma ação sobre um item; <paramref name="dados"/> vira o JSON do registro.</summary>
	public static Task ItemAsync(
		ITrilhaDeAuditoria trilha, string verbo, ItemMenu item, object dados, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Menu, item.Id.ToString(), JsonSerializer.Serialize(dados)),
			cancellationToken);

	/// <summary>Registra uma reconciliação em lote.</summary>
	public static Task ReconciliacaoAsync(ITrilhaDeAuditoria trilha, int setores, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(
				VerbosDeAuditoria.MenuReconciliar, RecursosDeAuditoria.Menu, "reconciliacao",
				JsonSerializer.Serialize(new { setores })),
			cancellationToken);
}
