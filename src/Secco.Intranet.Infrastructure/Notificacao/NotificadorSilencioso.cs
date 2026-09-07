using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter no-op de <see cref="INotificadorDeMensagens"/> — modo DEV/Testing, quando o
/// NotificationHub não está configurado. Publicar continua funcionando e o relatório sai
/// zerado, que é a verdade: não há para onde enviar.
/// </summary>
/// <param name="logger">Log em nível Debug.</param>
public sealed class NotificadorSilencioso(ILogger<NotificadorSilencioso> logger) : INotificadorDeMensagens
{
	/// <inheritdoc />
	public Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default)
	{
		logger.LogDebug("notificação desativada — NotificationHub não configurado");

		return Task.CompletedTask;
	}
}
