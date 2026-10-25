using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Secco.Intranet.Infrastructure.Vencimentos;

/// <summary>
/// Registrado no lugar do <see cref="VencimentosWorker"/> quando não há NotificationHub
/// configurado: só avisa, uma vez, no startup, que os avisos de vencimento estão desligados. Mesma
/// forma de <c>ValidacaoChaveMestraHostedService</c>: um <see cref="IHostedService"/> mínimo, já
/// que a composição de DI ainda não tem logger.
/// </summary>
/// <param name="logger">Log.</param>
internal sealed class AvisoDeVencimentoDesligadoHostedService(ILogger<AvisoDeVencimentoDesligadoHostedService> logger) : IHostedService
{
	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("Avisos de vencimento desligados: Intranet:Notificacao:HubUrl não configurado.");

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
