using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.NotificationHub.Client;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter real de <see cref="ICaixaDeNotificacoes"/>. O Hub expõe três operações in-app:
/// contar não lidas, listar não lidas e marcar <b>uma</b> como lida — por isso o sino mostra
/// só não lidas e não oferece "marcar todas", que seriam N chamadas.
/// </summary>
/// <param name="client">Client do NotificationHub.</param>
/// <param name="logger">Log de falhas, sem dado sensível (ADR-0020).</param>
public sealed class NotificationHubCaixaDeNotificacoes(
	INotificationHubClient client,
	ILogger<NotificationHubCaixaDeNotificacoes> logger) : ICaixaDeNotificacoes
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<NotificacaoDaCaixa>> NaoLidasAsync(
		Guid usuarioId,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var itens = await client
				.GetUnreadInAppNotificationsAsync(usuarioId, cancellationToken)
				.ConfigureAwait(false);

			return [.. itens.Select(item => new NotificacaoDaCaixa(
				item.Id, item.Title, item.Message, item.Link, item.CreatedAt))];
		}
		catch (ApiException apiException)
		{
			// O sino não pode derrubar o layout: ele está em toda página.
			logger.LogWarning(
				"Falha ao ler o inbox in-app (status {StatusCode}).", apiException.StatusCode);

			return [];
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao ler o inbox in-app.");

			return [];
		}
	}

	/// <inheritdoc />
	public async Task MarcarComoLidaAsync(Guid notificacaoId, CancellationToken cancellationToken = default)
	{
		try
		{
			await client.MarkInAppNotificationAsReadAsync(notificacaoId, cancellationToken).ConfigureAwait(false);
		}
		catch (ApiException apiException)
		{
			logger.LogWarning(
				"Falha ao marcar notificação como lida (status {StatusCode}).", apiException.StatusCode);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao marcar notificação como lida.");
		}
	}
}
