using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Secco.Intranet.Web.Navigation;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Restringe o controller (ou a action) ao <c>intranet-admin</c> e a mais ninguém (ADR-0008).
/// É um filtro de autorização declarativo, aplicado na classe: nenhuma action nova nasce
/// desprotegida, e a regra não depende de cada action lembrar de chamar um método.
/// </summary>
/// <remarks>
/// <see cref="Order"/> é menor que o do <c>[ValidateAntiForgeryToken]</c> (1000), então quem não
/// é admin recebe 403 mesmo num <c>POST</c> sem token — sem isso o filtro antifalsificação
/// responderia 400 antes, e o teste de "bloqueado" provaria o filtro errado.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SomenteIntranetAdminAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
	/// <inheritdoc />
	public int Order => int.MinValue;

	/// <inheritdoc />
	public void OnAuthorization(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var servicos = context.HttpContext.RequestServices;
		var ambiente = (IWebHostEnvironment)servicos.GetService(typeof(IWebHostEnvironment))!;
		var configuracao = (IConfiguration)servicos.GetService(typeof(IConfiguration))!;

		if (AcessoAdministrativo.ModoAbertoDeDev(ambiente, configuracao)
			|| AcessoAdministrativo.SomenteIntranetAdmin(context.HttpContext.User))
		{
			return;
		}

		context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
	}
}
