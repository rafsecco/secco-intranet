using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Tenants.Acesso;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Barra o acesso de tenant administrado cujo SecureGate não foi ligado no cadastro. Responde 404,
/// como <see cref="TenantAdministradoAttribute"/>, e roda logo depois dele.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SecureGateLigadoAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
	/// <inheritdoc />
	public int Order => int.MinValue + 3;

	/// <inheritdoc />
	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!Guid.TryParse(context.RouteData.Values[TenantAdministradoAttribute.ValorDeRota]?.ToString(), out var tenantId))
		{
			context.Result = new NotFoundResult();

			return;
		}

		var verificar = context.HttpContext.RequestServices.GetRequiredService<VerificarSecureGateLigadoHandler>();

		if (!await verificar.HandleAsync(tenantId, context.HttpContext.RequestAborted).ConfigureAwait(false))
		{
			context.Result = new NotFoundResult();
		}
	}
}
