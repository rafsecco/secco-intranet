using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Web.Authentication;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Login/logout via SecureGate (relying party OIDC, ADR-0023). Anônimo por natureza — é a
/// porta de entrada de quem ainda não está autenticado.
/// </summary>
/// <param name="configuration">Configuração do host, usada para checar se a autenticação está ativa.</param>
[AllowAnonymous]
public sealed class ContaController(IConfiguration configuration) : Controller
{
	/// <summary>
	/// Inicia o login via SecureGate. Se a autenticação não estiver configurada (modo aberto de
	/// DEV/Testing), redireciona para a Home com um aviso em vez de quebrar.
	/// </summary>
	/// <param name="returnUrl">URL para onde voltar após o login — validada contra open redirect (ADR-0020).</param>
	[HttpGet]
	public IActionResult Entrar(string? returnUrl)
	{
		if (!IntranetAuthenticationExtensions.IsConfigured(configuration))
		{
			TempData["Aviso"] = "Login não está configurado neste ambiente.";
			return RedirectToAction("Index", "Home");
		}

		// ADR-0020: só aceita RedirectUri local — nunca repassar uma URL externa ao Challenge.
		var redirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action("Index", "Home")!;

		return Challenge(
			new AuthenticationProperties { RedirectUri = redirectUri },
			OpenIdConnectDefaults.AuthenticationScheme);
	}

	/// <summary>Encerra a sessão local e no SecureGate.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public IActionResult Sair() =>
		SignOut(
			new AuthenticationProperties { RedirectUri = Url.Action("Index", "Home") },
			CookieAuthenticationDefaults.AuthenticationScheme,
			OpenIdConnectDefaults.AuthenticationScheme);
}
