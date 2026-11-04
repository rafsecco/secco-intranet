using Secco.Intranet.Application.Auditoria;
using Secco.LogStream.Client;
using Secco.SDK.AspNetCore.Ambient;

namespace Secco.Intranet.Infrastructure.Auditoria;

/// <summary>
/// Monta o pedido de <c>CreateAuditEntry</c> a partir de um registro da trilha. Compartilhado pela
/// trilha fail-open (<see cref="LogStreamTrilhaDeAuditoria"/>) e pela fail-closed
/// (<see cref="LogStreamTrilhaObrigatoria"/>): o que muda entre elas é o que fazem com a falha,
/// não o que enviam.
/// </summary>
internal static class RegistroParaLogStream
{
	/// <summary>Pedido com ator, correlação da requisição e o instante de agora.</summary>
	/// <param name="registro">Ação a registrar.</param>
	/// <param name="ator">Quem fez a ação.</param>
	public static CreateAuditEntryRequest Montar(RegistroDeAuditoria registro, AtorDaAcao ator) => new()
	{
		ActorId = ator.Id,
		ActorName = ator.Nome,
		ActorType = ActorType.User,
		Action = registro.Verbo,
		ResourceType = registro.Recurso,
		ResourceId = registro.RecursoId,
		Metadata = registro.Metadata,
		CorrelationId = Guid.TryParse(SeccoAmbientContext.CorrelationId, out var correlacao) ? correlacao : null,
		OccurredAt = DateTimeOffset.UtcNow,
	};
}
