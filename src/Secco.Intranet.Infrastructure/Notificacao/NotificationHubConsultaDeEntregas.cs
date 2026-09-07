using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.NotificationHub.Client;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter real de <see cref="IConsultaDeEntregas"/>: uma busca filtrada por <c>Source</c> e
/// <c>Type</c> — campos que o Hub declaradamente nunca interpreta e que a publicação grava
/// justamente para isto — responde "como foi o envio da publicação X" numa chamada por
/// status.
/// </summary>
/// <param name="client">Client do NotificationHub.</param>
/// <param name="logger">Log de falhas, sem dado sensível (ADR-0020).</param>
public sealed class NotificationHubConsultaDeEntregas(
	INotificationHubClient client,
	ILogger<NotificationHubConsultaDeEntregas> logger) : IConsultaDeEntregas
{
	// Só o total interessa, então a menor página possível basta: a contagem vem do
	// TotalCount, não do tamanho da lista devolvida.
	private const int TamanhoDaPagina = 1;

	/// <inheritdoc />
	public async Task<ResumoDeEntrega?> DaPublicacaoAsync(
		Guid publicacaoId,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var enviadas = await ContarAsync(publicacaoId, NotificationStatus.Sent, cancellationToken)
				.ConfigureAwait(false);
			var falharam = await ContarAsync(publicacaoId, NotificationStatus.Failed, cancellationToken)
				.ConfigureAwait(false);

			return new ResumoDeEntrega(enviadas, falharam);
		}
		catch (ApiException apiException)
		{
			// Nulo esconde o painel. O relatório é conveniência: não pode derrubar a página
			// da publicação, que é o destino do próprio aviso.
			logger.LogWarning(
				"Falha ao consultar entregas no NotificationHub (status {StatusCode}).",
				apiException.StatusCode);

			return null;
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao consultar entregas no NotificationHub.");

			return null;
		}
	}

	private async Task<int> ContarAsync(
		Guid publicacaoId,
		NotificationStatus status,
		CancellationToken cancellationToken)
	{
		var pagina = await client
			.SearchNotificationsAsync(
				from: null,
				to: null,
				status: status,
				channel: null,
				source: "mural",
				type: publicacaoId.ToString(),
				scheduledFrom: null,
				scheduledTo: null,
				page: 1,
				size: TamanhoDaPagina,
				cancellationToken)
			.ConfigureAwait(false);

		return (int)pagina.TotalCount;
	}
}
