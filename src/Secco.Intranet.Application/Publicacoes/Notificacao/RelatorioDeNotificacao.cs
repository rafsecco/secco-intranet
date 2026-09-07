namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>
/// O que aconteceu com o aviso de uma publicação. Conta em vez de nomear: o cadastro de
/// usuários do SecureGate não expõe nome.
/// </summary>
/// <param name="Notificados">Quantas pessoas entraram nos lotes enviados.</param>
/// <param name="SemEmail">Quantas ficaram fora do canal de e-mail por não ter endereço cadastrado.</param>
/// <param name="ProgramadaPara">
/// Quando a entrega acontece, se for no futuro. Nulo significa que já saiu.
/// </param>
/// <param name="Indisponivel">O Hub não aceitou o lote; a publicação foi gravada mesmo assim.</param>
public sealed record RelatorioDeNotificacao(
	int Notificados,
	int SemEmail,
	DateTimeOffset? ProgramadaPara,
	bool Indisponivel)
{
	/// <summary>Nada foi enviado porque o Hub não respondeu.</summary>
	/// <param name="semEmail">Quantos já haviam sido descartados por falta de e-mail.</param>
	public static RelatorioDeNotificacao DeIndisponivel(int semEmail) =>
		new(0, semEmail, ProgramadaPara: null, Indisponivel: true);

	/// <summary>Nenhum aviso a dar: publicação sem destinatários e sem falha.</summary>
	public static RelatorioDeNotificacao Silencioso() => new(0, 0, null, false);
}
