using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Composição da autenticação da Intranet como relying party OIDC (molde ADR-0023 da
/// plataforma, mesmo usado pelo Secco.AdminPortal): cookie de sessão + authorization
/// code/PKCE contra o SecureGate. NÃO usa <c>AddSeccoAuthentication()</c> (validação JWT de
/// resource server) — a Intranet é um CLIENTE, não um resource server.
/// </summary>
public static class IntranetAuthenticationExtensions
{
	/// <summary>Nome do cookie de sessão da Intranet.</summary>
	private const string CookieName = "secco.intranet.auth";

	/// <summary>
	/// Scopes solicitados no login interativo do usuário humano. Deliberadamente SEM
	/// <c>securegate:admin</c> — logins interativos nem recebem esse scope (é exclusivo de
	/// client credentials, usado pelo provisionamento automático de Roles, ver
	/// <c>Secco.Intranet.Infrastructure.Access</c>) — e sem custódia de token (ver
	/// <see cref="AddIntranetAuthentication"/>, comentário do <c>SaveTokens</c>).
	/// </summary>
	private static readonly string[] Scopes = ["openid", "profile", "roles"];

	/// <summary>
	/// Indica se a autenticação está configurada: a chave <c>Secco:SecureGate:Authority</c>
	/// precisa estar presente e não vazia. Usado tanto na composição quanto no pipeline do
	/// <c>Program.cs</c> e no <c>ContaController</c> para decidir se a autenticação está ativa.
	/// </summary>
	/// <param name="configuration">Configuração do host.</param>
	public static bool IsConfigured(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		return !string.IsNullOrWhiteSpace(configuration["Secco:SecureGate:Authority"]);
	}

	/// <summary>
	/// Registra cookie + OpenIdConnect e o <c>FallbackPolicy</c> de autenticação obrigatória —
	/// só quando <see cref="IsConfigured"/> for <c>true</c> (seção <c>Secco:SecureGate</c>). Sem
	/// Authority configurada, não registra nada: é o modo aberto de DEV local (sem SecureGate
	/// disponível) e do ambiente Testing. ADR-0020: produção exige a seção — o modo aberto
	/// nunca deve chegar lá.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	/// <param name="configuration">Configuração do host (seção <c>Secco:SecureGate</c>).</param>
	/// <param name="environment">Ambiente de hospedagem.</param>
	public static IServiceCollection AddIntranetAuthentication(
		this IServiceCollection services,
		IConfiguration configuration,
		IHostEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(environment);

		if (!IsConfigured(configuration))
		{
			return services;
		}

		services.AddAuthentication(options =>
			{
				options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
				options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
			})
			.AddCookie(options =>
			{
				options.Cookie.Name = CookieName;
				options.Cookie.HttpOnly = true;
				options.Cookie.SameSite = SameSiteMode.Lax;

				// Só Production força HTTPS — hosts locais/TestServer normalmente são HTTP puro.
				options.Cookie.SecurePolicy = environment.IsProduction()
					? CookieSecurePolicy.Always
					: CookieSecurePolicy.SameAsRequest;

				options.SlidingExpiration = true;
			})
			.AddOpenIdConnect(options =>
			{
				options.Authority = configuration["Secco:SecureGate:Authority"];
				options.ClientId = configuration["Secco:SecureGate:ClientId"];
				options.ClientSecret = configuration["Secco:SecureGate:ClientSecret"];

				options.ResponseType = OpenIdConnectResponseType.Code;
				options.UsePkce = true;

				// Diferente do AdminPortal (ADR-0023): a Intranet não chama nenhuma API da
				// plataforma on-behalf-of do usuário logado hoje — não há necessidade de
				// custodiar o access token no cookie.
				options.SaveTokens = false;
				options.GetClaimsFromUserInfoEndpoint = false; // as claims já vêm no id_token

				// ADR-0007: claims curtas sem remapeamento; name = sub/username, role = 'role'
				options.MapInboundClaims = false;
				options.TokenValidationParameters.NameClaimType = "name";
				options.TokenValidationParameters.RoleClaimType = SeccoClaims.Role;

				// Fora de Production o discovery pode ser HTTP (dev local)
				options.RequireHttpsMetadata = environment.IsProduction();

				options.Scope.Clear();
				foreach (var scope in Scopes)
				{
					options.Scope.Add(scope);
				}
			});

		services.AddAuthorization(options =>
			options.FallbackPolicy = new AuthorizationPolicyBuilder()
				.RequireAuthenticatedUser()
				.Build());

		return services;
	}
}
