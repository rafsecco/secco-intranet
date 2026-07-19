using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.Tenancy;

/// <summary>
/// Suprimento interino de tenant para navegação manual em Development. Este monolito
/// ainda não registra autenticação (ADR-0002 — a integração OIDC com o Secco.SecureGate
/// é item futuro do roadmap), então não há como a claim <c>tenant_id</c> chegar num token
/// real. Quando a requisição não carrega nem a claim nem o header <c>X-Tenant-Id</c>, este
/// middleware anexa ao <see cref="HttpContext.User"/> uma identidade com a claim de tenant
/// configurada em <see cref="TenantIdConfigurationKey"/>, para que o
/// <c>SeccoTenancyMiddleware</c> a resolva normalmente em seguida.
/// ADR-0020 (segurança transversal): este middleware só pode ser registrado dentro de
/// <c>if (app.Environment.IsDevelopment())</c> — nunca em produção, onde o tenant precisa
/// vir de um token real emitido pelo SecureGate.
/// </summary>
/// <param name="next">Próximo delegate do pipeline.</param>
/// <param name="configuration">Configuração da aplicação.</param>
public sealed class DevelopmentTenantMiddleware(RequestDelegate next, IConfiguration configuration)
{
	/// <summary>Chave de configuração do tenant de desenvolvimento (guid do catálogo de tenants).</summary>
	public const string TenantIdConfigurationKey = "Intranet:Development:TenantId";

	/// <summary>
	/// Anexa a claim de tenant de desenvolvimento quando aplicável e segue o pipeline.
	/// Se a configuração estiver ausente, não faz nada — a requisição segue sem tenant,
	/// como qualquer requisição fora de um contexto de tenant resolvido.
	/// </summary>
	/// <param name="context">Contexto HTTP da requisição atual.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var hasClaim = context.User.FindFirst(SeccoClaims.TenantId) is not null;
		var hasHeader = !string.IsNullOrEmpty(context.Request.Headers[SeccoHeaders.TenantId].ToString());

		if (!hasClaim && !hasHeader)
		{
			var tenantId = configuration[TenantIdConfigurationKey];

			if (!string.IsNullOrWhiteSpace(tenantId))
			{
				var identity = new ClaimsIdentity([new Claim(SeccoClaims.TenantId, tenantId)]);
				context.User.AddIdentity(identity);
			}
		}

		await next(context).ConfigureAwait(false);
	}
}
