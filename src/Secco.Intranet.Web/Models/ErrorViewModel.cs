namespace Secco.Intranet.Web.Models;

/// <summary>
/// Dados exibidos na página de erro padrão. Carrega só o identificador de correlação da
/// requisição — nunca stack trace ou mensagem de exceção (ADR-0020: sem vazamento de
/// detalhe interno, mesmo em Development).
/// </summary>
/// <param name="RequestId">Identificador da requisição, quando disponível.</param>
public sealed record ErrorViewModel(string? RequestId)
{
	/// <summary>Indica se há identificador de requisição para exibir.</summary>
	public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
