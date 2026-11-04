using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Auditoria;

/// <summary>
/// Adapter da <see cref="ITrilhaObrigatoria"/> fora de Development/Testing sem LogStream: recusa
/// sempre. Exportar fora de DEV exige <c>Intranet:Auditoria:LogStreamUrl</c> (ADR-0013).
/// </summary>
/// <param name="logger">Log da recusa.</param>
public sealed class TrilhaObrigatoriaIndisponivel(ILogger<TrilhaObrigatoriaIndisponivel> logger) : ITrilhaObrigatoria
{
	/// <inheritdoc />
	public Task<Result> RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(registro);

		logger.LogWarning(
			"Exportação de {Recurso} recusada: LogStream não configurado fora de Development ({Verbo}).",
			registro.RecursoId,
			registro.Verbo);

		return Task.FromResult(Result.Failure(IntranetErrors.Auditoria.TrilhaIndisponivel));
	}
}
