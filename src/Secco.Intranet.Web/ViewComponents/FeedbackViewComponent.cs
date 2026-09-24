using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Web.Theming.Contracts;

namespace Secco.Intranet.Web.ViewComponents;

/// <summary>
/// Feedback de ação concluída ou recusada. Os controllers deixam a mensagem no
/// <c>TempData</c> antes do redirect e o layout invoca este componente uma vez — nenhuma view
/// de página escreve o aviso à mão, e qualquer tela nova ganha o retorno de graça (ADR-0004).
/// </summary>
public sealed class FeedbackViewComponent : ViewComponent
{
	/// <summary>Chave onde os controllers deixam a mensagem de sucesso.</summary>
	public const string ChaveDaMensagem = "Mensagem";

	/// <summary>Chave onde os controllers deixam o texto de uma ação recusada.</summary>
	public const string ChaveDaMensagemDeErro = "MensagemDeErro";

	/// <summary>Renderiza o aviso, ou nada quando não há mensagem pendente.</summary>
	public IViewComponentResult Invoke()
	{
		// Ler consome: a mensagem vale para a página que veio do redirect, e não sobrevive
		// até a próxima navegação. As duas chaves são lidas sempre, para que nenhuma sobre.
		var erro = TempData[ChaveDaMensagemDeErro] as string;
		var sucesso = TempData[ChaveDaMensagem] as string;

		if (!string.IsNullOrWhiteSpace(erro))
		{
			return View(new ToastModel(erro, ToastVariante.Erro));
		}

		return string.IsNullOrWhiteSpace(sucesso)
			? Content(string.Empty)
			: View(new ToastModel(sucesso));
	}
}
