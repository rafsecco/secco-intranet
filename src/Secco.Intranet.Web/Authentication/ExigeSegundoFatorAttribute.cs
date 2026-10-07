using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Web.Models.Tenants;
using Secco.Intranet.Web.Navigation;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Exige segundo fator ativo na conta de quem acessa (spec da área de tenants, ponto crítico). O
/// token não traz <c>amr</c>, então a prova é o cadastro da conta no SecureGate
/// (<c>DoisFatoresAtivo</c>). <b>Fail-closed:</b> só passa com 2FA confirmado, ou com o SecureGate
/// não configurado (aí a área inteira já responde "não configurado"). Roda depois de
/// <see cref="SomenteIntranetAdminAttribute"/> e antes do antifalsificação.
/// </summary>
/// <remarks>
/// Resíduo conhecido: no login federado pelo Entra, o segundo fator é do Entra e a Intranet não o
/// enxerga — a conta pode não ter 2FA local. Correção completa depende da plataforma.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ExigeSegundoFatorAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
	private static readonly TimeSpan DuracaoDoPositivo = TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	public int Order => int.MinValue + 1;

	/// <inheritdoc />
	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var servicos = context.HttpContext.RequestServices;
		var configuracao = servicos.GetRequiredService<IConfiguration>();

		if (AcessoAdministrativo.ModoAbertoDeDev(servicos.GetRequiredService<IWebHostEnvironment>(), configuracao))
		{
			return;
		}

		if (!Guid.TryParse(context.HttpContext.User.FindFirst(SeccoClaims.Subject)?.Value, out var usuarioId))
		{
			context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);

			return;
		}

		var cache = servicos.GetRequiredService<IMemoryCache>();
		var chave = $"tenants:2fa:{servicos.GetRequiredService<ITenantContext>().TenantId}:{usuarioId}";

		if (cache.TryGetValue(chave, out bool confirmado) && confirmado)
		{
			return;
		}

		var usuario = await servicos.GetRequiredService<IGestaoDeAcesso>()
			.ObterUsuarioAsync(usuarioId, context.HttpContext.RequestAborted)
			.ConfigureAwait(false);

		if (usuario.IsSuccess && usuario.Value.DoisFatoresAtivo)
		{
			cache.Set(chave, true, DuracaoDoPositivo);

			return;
		}

		if (usuario.IsSuccess)
		{
			context.Result = Tela(context, servicos, configuracao, "SegundoFatorObrigatorio", StatusCodes.Status403Forbidden);

			return;
		}

		if (usuario.Error == IntranetErrors.Acesso.NaoConfigurado)
		{
			return;
		}

		context.Result = usuario.Error.Type == ErrorType.NotFound
			? new StatusCodeResult(StatusCodes.Status403Forbidden)
			: Tela(context, servicos, configuracao, "SegundoFatorIndisponivel", StatusCodes.Status503ServiceUnavailable);
	}

	private static ViewResult Tela(
		AuthorizationFilterContext context, IServiceProvider servicos, IConfiguration configuracao, string view, int status)
	{
		var authority = configuracao["Secco:SecureGate:Authority"]?.TrimEnd('/');
		var modelo = new SegundoFatorViewModel(string.IsNullOrWhiteSpace(authority) ? null : $"{authority}/Account/TwoFactor");

		return new ViewResult
		{
			ViewName = view,
			StatusCode = status,
			ViewData = new ViewDataDictionary<SegundoFatorViewModel>(
				servicos.GetRequiredService<IModelMetadataProvider>(), context.ModelState) { Model = modelo },
		};
	}
}
