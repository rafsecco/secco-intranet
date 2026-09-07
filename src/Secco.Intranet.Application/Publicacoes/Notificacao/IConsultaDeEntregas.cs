namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>Como foi a entrega dos avisos de uma publicação.</summary>
/// <param name="Enviadas">Notificações entregues.</param>
/// <param name="Falharam">Notificações que o Hub registrou como falha.</param>
public sealed record ResumoDeEntrega(int Enviadas, int Falharam);

/// <summary>
/// Porta de consulta de entrega. É <b>preguiçosa por desenho</b>: a pergunta só é feita
/// quando alguém abre a publicação, e nunca no instante de publicar.
/// </summary>
public interface IConsultaDeEntregas
{
	/// <summary>Resumo da entrega dos avisos de uma publicação.</summary>
	/// <param name="publicacaoId">Identificador da publicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<ResumoDeEntrega?> DaPublicacaoAsync(Guid publicacaoId, CancellationToken cancellationToken = default);
}
