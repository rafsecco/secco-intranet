using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Web.Models;

namespace Secco.Intranet.Web.Controllers;

/// <summary>Tratamento de erro padrão do monolito (ADR-0002).</summary>
public sealed class HomeController : Controller
{
	/// <summary>
	/// Mantida para os redirecionamentos existentes: a página inicial da Intranet é o Mural.
	/// </summary>
	public IActionResult Index() => RedirectToAction("Index", "Mural");

	/// <summary>
	/// Página de erro padrão (ADR-0020): nunca expõe stack trace ou mensagem de exceção,
	/// mesmo em Development — só o identificador de correlação da requisição, quando
	/// disponível.
	/// </summary>
	[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
	public IActionResult Error() =>
		View(new ErrorViewModel(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}
