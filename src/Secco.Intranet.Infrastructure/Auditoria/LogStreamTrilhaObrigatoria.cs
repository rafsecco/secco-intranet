using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.LogStream.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Auditoria;

/// <summary>
/// Adapter real da <see cref="ITrilhaObrigatoria"/>: uma chamada a <c>CreateAuditEntry</c>, e
/// sucesso só quando o LogStream aceitou. Recusa, rede e timeout viram
/// <see cref="IntranetErrors.Auditoria.TrilhaIndisponivel"/>, ao contrário da
/// <see cref="LogStreamTrilhaDeAuditoria"/>, que engole.
/// </summary>
/// <param name="client">Client do LogStream.</param>
/// <param name="ator">Resolução do usuário atual.</param>
/// <param name="ambiente">Ambiente do host, para o caso sem usuário identificado.</param>
/// <param name="logger">Log de falhas, sem dado sensível (ADR-0020).</param>
public sealed class LogStreamTrilhaObrigatoria(
	ILogStreamClient client,
	IAtorAtual ator,
	IHostEnvironment ambiente,
	ILogger<LogStreamTrilhaObrigatoria> logger) : ITrilhaObrigatoria
{
	/// <inheritdoc />
	public async Task<Result> RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(registro);

		var quem = ator.Atual();

		if (quem is null)
		{
			// Modo aberto de DEV: registrar com ator inventado não prova nada. Fora de DEV, sem
			// usuário não há trilha possível, e sem trilha a ação não acontece.
			if (EscolhaDaTrilhaObrigatoria.EhDesenvolvimento(ambiente))
			{
				logger.LogWarning("{Verbo} de {Recurso} não auditado: sem usuário identificado (modo aberto).", registro.Verbo, registro.RecursoId);

				return Result.Success();
			}

			logger.LogWarning("{Verbo} de {Recurso} recusado: sem usuário identificado.", registro.Verbo, registro.RecursoId);

			return Result.Failure(IntranetErrors.Auditoria.TrilhaIndisponivel);
		}

		try
		{
			await client.CreateAuditEntryAsync(RegistroParaLogStream.Montar(registro, quem), cancellationToken).ConfigureAwait(false);

			return Result.Success();
		}
		catch (ApiException apiException)
		{
			logger.LogWarning(
				"O LogStream recusou o registro de {Verbo} (status {StatusCode}); a ação foi recusada.",
				registro.Verbo,
				apiException.StatusCode);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(
				httpRequestException,
				"Falha de rede ao registrar {Verbo} no LogStream; a ação foi recusada.",
				registro.Verbo);
		}
		catch (OperationCanceledException operationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Timeout do HttpClient, não cancelamento pedido pelo chamador, que propaga.
			logger.LogWarning(
				operationCanceledException,
				"Timeout ao registrar {Verbo} no LogStream; a ação foi recusada.",
				registro.Verbo);
		}

		return Result.Failure(IntranetErrors.Auditoria.TrilhaIndisponivel);
	}
}
