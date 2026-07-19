using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Web.Models;

namespace Secco.Intranet.Web.Controllers;

/// <summary>Página inicial e tratamento de erro padrão do monolito (ADR-0002).</summary>
public sealed class HomeController : Controller
{
	/// <summary>Página inicial com links para os módulos disponíveis.</summary>
	public IActionResult Index() => View();

	/// <summary>
	/// Página de erro padrão (ADR-0020): nunca expõe stack trace ou mensagem de exceção,
	/// mesmo em Development — só o identificador de correlação da requisição, quando
	/// disponível.
	/// </summary>
	[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
	public IActionResult Error() =>
		View(new ErrorViewModel(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}
