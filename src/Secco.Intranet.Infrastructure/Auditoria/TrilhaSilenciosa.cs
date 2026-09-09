using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Auditoria;

namespace Secco.Intranet.Infrastructure.Auditoria;

/// <summary>
/// Adapter no-op de <see cref="ITrilhaDeAuditoria"/> — modo DEV/Testing, quando o
/// <c>Secco.LogStream</c> não está configurado.
/// </summary>
/// <param name="logger">Log em nível Debug.</param>
public sealed class TrilhaSilenciosa(ILogger<TrilhaSilenciosa> logger) : ITrilhaDeAuditoria
{
	/// <inheritdoc />
	public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(registro);

		logger.LogDebug(
			"auditoria desativada — LogStream não configurado; verbo {Verbo} não registrado",
			registro.Verbo);

		return Task.CompletedTask;
	}
}
