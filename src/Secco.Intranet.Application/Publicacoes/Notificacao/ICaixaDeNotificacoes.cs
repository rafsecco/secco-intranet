namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>Uma notificação não lida do inbox in-app.</summary>
/// <param name="Id">Identificador, usado para marcar como lida.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Mensagem">Resumo.</param>
/// <param name="Link">Destino do clique, quando houver.</param>
/// <param name="CriadaEm">Quando foi criada.</param>
public sealed record NotificacaoDaCaixa(
	Guid Id,
	string Titulo,
	string Mensagem,
	string? Link,
	DateTimeOffset CriadaEm);

/// <summary>
/// Porta de leitura do inbox in-app. O Hub é dono do estado de lida — a Intranet não guarda
/// nada disso (ADR-0006).
/// </summary>
public interface ICaixaDeNotificacoes
{
	/// <summary>Notificações ainda não lidas do usuário.</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<NotificacaoDaCaixa>> NaoLidasAsync(
		Guid usuarioId,
		CancellationToken cancellationToken = default);

	/// <summary>Marca uma notificação como lida.</summary>
	/// <param name="notificacaoId">Identificador da notificação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task MarcarComoLidaAsync(Guid notificacaoId, CancellationToken cancellationToken = default);
}
