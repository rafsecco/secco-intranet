using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter no-op de <see cref="ICaixaDeNotificacoes"/> — sem NotificationHub configurado não
/// há inbox a consultar, e o sino não aparece.
/// </summary>
public sealed class CaixaVazia : ICaixaDeNotificacoes
{
	/// <inheritdoc />
	public Task<IReadOnlyList<NotificacaoDaCaixa>> NaoLidasAsync(
		Guid usuarioId,
		CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<NotificacaoDaCaixa>>([]);

	/// <inheritdoc />
	public Task MarcarComoLidaAsync(Guid notificacaoId, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;
}
