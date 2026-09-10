using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Web.Theming.Contracts;

namespace Secco.Intranet.Web.ViewComponents;

/// <summary>
/// Feedback de ação concluída. Os controllers deixam a mensagem no <c>TempData</c> antes do
/// redirect e o layout invoca este componente uma vez — nenhuma view de página escreve o
/// aviso à mão, e qualquer tela nova ganha o retorno de graça (ADR-0004).
/// </summary>
public sealed class FeedbackViewComponent : ViewComponent
{
	/// <summary>Chave onde os controllers deixam a mensagem de sucesso.</summary>
	public const string ChaveDaMensagem = "Mensagem";

	/// <summary>Renderiza o aviso, ou nada quando não há mensagem pendente.</summary>
	public IViewComponentResult Invoke()
	{
		// Ler consome: a mensagem vale para a página que veio do redirect, e não sobrevive
		// até a próxima navegação.
		var mensagem = TempData[ChaveDaMensagem] as string;

		return string.IsNullOrWhiteSpace(mensagem)
			? Content(string.Empty)
			: View(new ToastModel(mensagem));
	}
}
