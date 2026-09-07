namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>
/// Configuração da notificação, seção <c>Intranet:Notificacao</c>. Bind lazy feito pela
/// Infrastructure — a Application não conhece configuração.
/// </summary>
public sealed class NotificacaoOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	public const string SectionKey = "Intranet:Notificacao";

	/// <summary>
	/// URL pública da Intranet, sem barra final — por exemplo <c>https://intranet.exemplo.com</c>.
	/// O link do aviso precisa ser absoluto: quem recebe por e-mail está fora da aplicação, e
	/// um caminho relativo não abre. Vazia significa link relativo, que serve ao sino e
	/// degrada no e-mail.
	/// </summary>
	public string UrlBase { get; set; } = string.Empty;

	/// <summary>Teto do resumo enviado na notificação, em caracteres.</summary>
	public int TamanhoDoResumo { get; set; } = 300;
}
