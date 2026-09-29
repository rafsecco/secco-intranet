using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Secco.Intranet.Web.Hosting;

/// <summary>
/// Confiança em cabeçalho de reverse proxy (<c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c>),
/// opt-in por configuração — sem ela, a aplicação não confia em nenhum header desse tipo, e o
/// <c>UseForwardedHeaders()</c> do pipeline vira um no-op (comportamento padrão do framework).
/// </summary>
/// <remarks>
/// Habilitar isto só faz sentido quando a porta da aplicação **não é alcançável diretamente** —
/// só o proxy a alcança (rede do Docker, security group, etc.). Sem essa garantia de rede,
/// qualquer chamador poderia forjar <c>X-Forwarded-Proto: https</c> e enganar a aplicação sobre o
/// esquema real da requisição (ADR-0020: nunca confiar em claim não validada). Por isso
/// <see cref="ForwardedHeadersOptions.KnownIPNetworks"/>/<see cref="ForwardedHeadersOptions.KnownProxies"/>
/// ficam vazios (confia em quem quer que alcance a aplicação) em vez de listar uma faixa — não dá
/// para saber a priori o endereço do proxy de cada instalação.
/// </remarks>
public static class IntranetProxyExtensions
{
	private const string ChaveDeConfiguracao = "Secco:ReverseProxy:Habilitado";

	/// <summary>Indica se a aplicação deve confiar em cabeçalho de reverse proxy.</summary>
	/// <param name="configuration">Configuração do host.</param>
	public static bool Habilitado(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		return configuration.GetValue<bool>(ChaveDeConfiguracao);
	}

	/// <summary>
	/// Registra a confiança em <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> quando habilitado
	/// (ver <see cref="Habilitado"/>). Sem a chave, não registra nada — o pipeline segue vendo a
	/// requisição exatamente como o Kestrel a recebeu.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	/// <param name="configuration">Configuração do host.</param>
	public static IServiceCollection AddIntranetProxyForwarding(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);

		if (!Habilitado(configuration))
		{
			return services;
		}

		services.Configure<ForwardedHeadersOptions>(options =>
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
			options.KnownIPNetworks.Clear();
			options.KnownProxies.Clear();
		});

		return services;
	}
}
