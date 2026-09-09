using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Auditoria;
using Secco.LogStream.Client;
using Secco.SDK.AspNetCore.Ambient;

namespace Secco.Intranet.Infrastructure.Auditoria;

/// <summary>
/// Adapter real de <see cref="ITrilhaDeAuditoria"/>: uma chamada a <c>CreateAuditEntry</c> por
/// ação. A escrita do LogStream é síncrona — não há fila do lado de lá —, então a garantia de
/// não derrubar a operação é inteiramente daqui.
/// </summary>
/// <param name="client">Client do LogStream.</param>
/// <param name="ator">Resolução do usuário atual.</param>
/// <param name="logger">Log de falhas, sem dado sensível (ADR-0020).</param>
public sealed class LogStreamTrilhaDeAuditoria(
	ILogStreamClient client,
	IAtorAtual ator,
	ILogger<LogStreamTrilhaDeAuditoria> logger) : ITrilhaDeAuditoria
{
	/// <inheritdoc />
	public async Task RegistrarAsync(
		RegistroDeAuditoria registro,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(registro);

		var quem = ator.Atual();

		if (quem is null)
		{
			// Modo aberto de DEV: registrar com ator inventado não prova nada.
			logger.LogDebug("sem ator identificado — verbo {Verbo} não registrado", registro.Verbo);

			return;
		}

		var request = new CreateAuditEntryRequest
		{
			ActorId = quem.Id,
			ActorName = quem.Nome,
			ActorType = ActorType.User,
			Action = registro.Verbo,
			ResourceType = registro.Recurso,
			ResourceId = registro.RecursoId,
			Metadata = registro.Metadata,
			CorrelationId = Guid.TryParse(SeccoAmbientContext.CorrelationId, out var correlacao) ? correlacao : null,
			OccurredAt = DateTimeOffset.UtcNow,
		};

		try
		{
			await client.CreateAuditEntryAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (ApiException apiException)
		{
			// Falha aberta: a acao do usuario ja aconteceu, e desfaze-la por causa do registro
			// seria pior que o buraco na trilha. O buraco fica visivel aqui.
			logger.LogWarning(
				"O LogStream recusou o registro de {Verbo} (status {StatusCode}).",
				registro.Verbo,
				apiException.StatusCode);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(
				httpRequestException,
				"Falha de rede ao registrar {Verbo} no LogStream.",
				registro.Verbo);
		}
		catch (OperationCanceledException operationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Timeout do HttpClient, não cancelamento pedido pelo chamador: mesma falha
			// aberta das demais — o LogStream aceitou a conexão e não respondeu a tempo.
			logger.LogWarning(
				operationCanceledException,
				"Timeout ao registrar {Verbo} no LogStream.",
				registro.Verbo);
		}
	}
}
