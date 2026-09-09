namespace Secco.Intranet.Application.Auditoria;

/// <summary>Uma ação a registrar na trilha.</summary>
/// <param name="Verbo">Verbo canônico, de <see cref="VerbosDeAuditoria"/>.</param>
/// <param name="Recurso">Tipo do recurso, de <see cref="RecursosDeAuditoria"/>.</param>
/// <param name="RecursoId">Identificador do recurso afetado.</param>
/// <param name="Metadata">
/// JSON curto com o que identifica a ação. Nunca conteúdo: nem corpo de publicação, nem bytes
/// de documento — a trilha diz o que aconteceu, e duplicar dado sensível num serviço de
/// observabilidade cria um segundo lugar de onde ele pode vazar (ADR-0020).
/// </param>
public sealed record RegistroDeAuditoria(string Verbo, string Recurso, string RecursoId, string? Metadata);

/// <summary>
/// Porta de escrita da trilha de auditoria. A trilha vive no <c>Secco.LogStream</c> (ADR-0006);
/// este produto não guarda cópia.
/// </summary>
public interface ITrilhaDeAuditoria
{
	/// <summary>
	/// Registra uma ação. <b>Nunca lança</b>: auditar não pode derrubar o que o usuário pediu,
	/// e concentrar essa garantia aqui evita oito <c>try/catch</c> espalhados pelos handlers.
	/// Falha vira aviso no log e a trilha fica com um buraco — decisão registrada no spec.
	/// </summary>
	/// <param name="registro">Ação a registrar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default);
}
