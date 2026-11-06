using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.SecurityHeaders;

namespace Secco.Intranet.Web.Hosting;

/// <summary>
/// Cabeçalhos de segurança de resposta pelo SDK da plataforma (ADR-0057, secco-platform#53), com os
/// ajustes da Intranet: modo da CSP e coletor de relatórios por configuração, e <c>form-action</c>
/// com a origem do SecureGate.
/// </summary>
/// <remarks>
/// A política não abre exceção para script inline nem para estilo inline escrito pela Intranet: os
/// temas carregam o script de preferências como arquivo e pintam a cor do setor por classe
/// (<c>sc-hue-{matiz}</c>), então nenhuma view emite <c>style="..."</c> nem <c>&lt;script&gt;</c> sem
/// <c>src</c>. A única exceção é o valor exato de <see cref="EstilosDeTerceirosPermitidos"/>.
/// </remarks>
public static class IntranetSecurityHeadersExtensions
{
	/// <summary>Modo da CSP: <c>Enforce</c> (padrão) ou <c>ReportOnly</c>, para investigar uma violação nova.</summary>
	public const string ChaveDoModo = "Intranet:Csp:Modo";

	/// <summary>Coletor externo de relatórios de violação, HTTPS. Opcional.</summary>
	public const string ChaveDoReportUri = "Intranet:Csp:ReportUri";

	/// <summary>Atributos style liberados pelo valor exato.</summary>
	public static readonly IReadOnlyList<string> EstilosDeTerceirosPermitidos =
		["display:none"];

	/// <summary>
	/// Registra a política. O padrão é <c>Enforce</c>: navegando a Intranet nesse modo (2026-11-01), nada
	/// legítimo ficou bloqueado. Valor desconhecido derruba a subida em vez de cair num modo por engano.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddIntranetSecurityHeaders(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddSeccoSecurityHeaders(options =>
		{
			// Valor exato de style que o HTML da Intranet ainda emite, achado navegando com a CSP em
			// Enforce: "display:none", o <li> vazio que o asp-validation-summary do ASP.NET Core escreve
			// sem erro. Liberado só pelo hash do valor: qualquer outro style continua bloqueado.
			foreach (var valor in EstilosDeTerceirosPermitidos)
			{
				options.AllowStyleAttribute(valor);
			}
		});

		// Lido na montagem da política, não no registro: fontes de configuração tardias (testes) também valem.
		services.AddOptions<SeccoSecurityHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
		{
			var modo = configuration[ChaveDoModo];

			options.Mode = string.IsNullOrWhiteSpace(modo)
				? SeccoCspMode.Enforce
				: Enum.TryParse<SeccoCspMode>(modo, ignoreCase: true, out var lido) && Enum.IsDefined(lido)
					? lido
					: throw new InvalidOperationException($"{ChaveDoModo} aceita ReportOnly ou Enforce.");

			if (Uri.TryCreate(configuration[ChaveDoReportUri], UriKind.Absolute, out var coletor))
			{
				options.ReportUri = coletor;
			}

			// O logout posta em /Conta/Sair e termina no end_session do SecureGate, e o navegador aplica
			// form-action aos redirects do POST: a origem dele entra na lista. Authority HTTP (só em
			// Development) fica sem a diretiva, como no AdminPortal.
			if (Uri.TryCreate(configuration["Secco:SecureGate:Authority"], UriKind.Absolute, out var authority)
				&& authority.Scheme == Uri.UriSchemeHttps)
			{
				options.FormActions.Add("'self'");
				options.FormActions.Add(authority.GetLeftPart(UriPartial.Authority));
			}
		});

		return services;
	}
}
