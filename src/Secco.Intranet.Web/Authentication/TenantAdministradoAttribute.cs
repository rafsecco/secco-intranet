using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Tenants;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Barra qualquer action com <c>{tenantId}</c> fora do cadastro de tenants administrados — a
/// defesa contra IDOR da spec. Responde 404, nunca 403: 403 confirmaria que o tenant existe. Roda
/// depois de <see cref="ExigeSegundoFatorAttribute"/> e antes do antifalsificação.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TenantAdministradoAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
	/// <summary>Nome do route value lido.</summary>
	public const string ValorDeRota = "tenantId";

	/// <inheritdoc />
	public int Order => int.MinValue + 2;

	/// <inheritdoc />
	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!Guid.TryParse(context.RouteData.Values[ValorDeRota]?.ToString(), out var tenantId))
		{
			context.Result = new NotFoundResult();

			return;
		}

		var verificar = context.HttpContext.RequestServices.GetRequiredService<VerificarTenantAdministradoHandler>();

		if (!await verificar.HandleAsync(tenantId, context.HttpContext.RequestAborted).ConfigureAwait(false))
		{
			context.Result = new NotFoundResult();
		}
	}
}
