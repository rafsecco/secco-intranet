using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Secco.Intranet.Web.Navigation;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Restringe uma rota a quem tem a permissão informada (ADR-0021), com <c>intranet-admin</c>
/// sempre liberado (bypass por identidade, ADR-0008 — nunca precisa ganhar a permissão gravada).
/// Mesmo desenho de <see cref="SomenteIntranetAdminAttribute"/>: filtro declarativo,
/// <see cref="Order"/> antes do antifalsificação, checa o modo aberto de DEV primeiro.
/// </summary>
/// <param name="permissao">Permissão no formato canônico <c>recurso:acao</c>.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ExigePermissaoAttribute(string permissao) : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
	/// <inheritdoc />
	public int Order => int.MinValue;

	/// <inheritdoc />
	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
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

		var authorizationService = (IAuthorizationService)servicos.GetService(typeof(IAuthorizationService))!;
		var resultado = await authorizationService.AuthorizeAsync(context.HttpContext.User, permissao);

		if (!resultado.Succeeded)
		{
			context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
		}
	}
}
