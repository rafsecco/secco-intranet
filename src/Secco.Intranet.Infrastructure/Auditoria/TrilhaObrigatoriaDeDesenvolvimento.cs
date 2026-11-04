using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Auditoria;

/// <summary>
/// Adapter da <see cref="ITrilhaObrigatoria"/> em Development/Testing sem LogStream: aceita, para a
/// exportação poder ser exercitada localmente, e deixa no log que nada foi auditado (ADR-0013).
/// </summary>
/// <param name="logger">Log do aviso.</param>
public sealed class TrilhaObrigatoriaDeDesenvolvimento(ILogger<TrilhaObrigatoriaDeDesenvolvimento> logger) : ITrilhaObrigatoria
{
	/// <inheritdoc />
	public Task<Result> RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(registro);

		logger.LogWarning(
			"Exportação de {Recurso} não auditada: LogStream não configurado ({Verbo}).",
			registro.RecursoId,
			registro.Verbo);

		return Task.FromResult(Result.Success());
	}
}
