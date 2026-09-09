using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.NotificationHub.Client;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter real de <see cref="INotificadorDeMensagens"/>: um lote vira uma chamada a
/// <c>DispatchNotificationBatch</c>. Fila, retry e entrega são do Hub (ADR-0006).
/// </summary>
/// <param name="client">Client do NotificationHub.</param>
/// <param name="logger">Log de falhas, sem dado sensível (ADR-0020).</param>
public sealed class NotificationHubNotificador(
	INotificationHubClient client,
	ILogger<NotificationHubNotificador> logger) : INotificadorDeMensagens
{
	/// <inheritdoc />
	public async Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(mensagem);

		var request = new DispatchNotificationBatchRequest
		{
			Title = mensagem.Titulo,
			Message = mensagem.Resumo,
			Link = mensagem.Link,
			Source = mensagem.Origem,
			Type = mensagem.Tipo,
			Channels = [.. mensagem.Canais],
			// Nulo entrega agora; preenchido, o Hub segura e-mail E item do sino até a hora.
			ScheduledFor = mensagem.ProgramadaPara,
			Destinations =
			[
				.. mensagem.Destinos.Select(destino => new NotificationDestination
				{
					UserId = destino.UsuarioId,
					Recipient = destino.Email,
				}),
			],
		};

		try
		{
			await client.DispatchNotificationBatchAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (ApiException apiException)
		{
			logger.LogWarning(
				"O NotificationHub recusou um lote de {Quantidade} destino(s) (status {StatusCode}).",
				mensagem.Destinos.Count,
				apiException.StatusCode);

			throw new NotificacaoIndisponivelException(
				"O serviço de notificação recusou o envio.", apiException);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(
				httpRequestException,
				"Falha de rede ao enviar um lote de {Quantidade} destino(s) ao NotificationHub.",
				mensagem.Destinos.Count);

			throw new NotificacaoIndisponivelException(
				"O serviço de notificação está indisponível.", httpRequestException);
		}
		catch (OperationCanceledException operationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Timeout do HttpClient, não cancelamento pedido pelo chamador: mesma falha
			// aberta das demais — o Hub aceitou a conexão e não respondeu a tempo.
			logger.LogWarning(
				operationCanceledException,
				"Timeout ao enviar um lote de {Quantidade} destino(s) ao NotificationHub.",
				mensagem.Destinos.Count);

			throw new NotificacaoIndisponivelException(
				"O serviço de notificação está indisponível.", operationCanceledException);
		}
	}
}
