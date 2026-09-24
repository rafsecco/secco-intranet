using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Secco.Intranet.Web.Navigation;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Exige um nível mínimo de acesso ao Diretório (<see cref="AcessoAoDiretorio.Nivel"/>). Aplicado
/// na classe do controller (nível <c>Usuario</c>) e nas actions administrativas (nível
/// <c>Administrador</c>): nenhuma rota nova nasce aberta. Mesmo desenho do
/// <see cref="SomenteIntranetAdminAttribute"/> — <c>Order</c> menor que o do antifalsificação, para
/// o <c>POST</c> sem permissão dar 403 e não 400.
/// </summary>
/// <param name="minimo">Nível mínimo exigido.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio minimo) : Attribute, IAuthorizationFilter, IOrderedFilter
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
			|| AcessoAoDiretorio.TemNivel(context.HttpContext.User, minimo))
		{
			return;
		}

		context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
	}
}
