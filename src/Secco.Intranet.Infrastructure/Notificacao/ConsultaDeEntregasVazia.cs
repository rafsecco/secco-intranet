using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter no-op de <see cref="IConsultaDeEntregas"/> — sem NotificationHub configurado não
/// há entrega a consultar, e o painel não aparece.
/// </summary>
public sealed class ConsultaDeEntregasVazia : IConsultaDeEntregas
{
	/// <inheritdoc />
	public Task<ResumoDeEntrega?> DaPublicacaoAsync(
		Guid publicacaoId,
		CancellationToken cancellationToken = default) =>
		Task.FromResult<ResumoDeEntrega?>(null);
}
