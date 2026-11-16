using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Secco.Intranet.Web.Diretorio;

/// <summary>Registro do link de chat do Diretório.</summary>
public static class ChatServiceCollectionExtensions
{
	private const string ChaveDoProvedor = ChatOptions.SectionKey + ":Provedor";
	private const string ChaveDoTenantId = ChatOptions.SectionKey + ":TenantId";

	/// <summary>
	/// Registra <see cref="ILinkDeChat"/>. <c>Intranet:Chat:Provedor</c> vazio ou <c>Teams</c> (sem
	/// distinguir caixa nem espaços nas pontas) e <c>Intranet:Chat:TenantId</c> GUID; qualquer outro valor
	/// derruba a subida, em vez de ligar o botão errado ou deixá-lo quebrado até alguém clicar.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	/// <param name="configuration">Configuração da aplicação.</param>
	public static IServiceCollection AddIntranetChat(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		services.AddOptions<ChatOptions>()
			.Configure(options => Ler(configuration, options))
			.ValidateOnStart();

		services.AddSingleton<ILinkDeChat>(provider => new LinkDeChat(provider.GetRequiredService<IOptions<ChatOptions>>().Value));

		return services;
	}

	private static void Ler(IConfiguration configuration, ChatOptions options)
	{
		var provedor = configuration[ChaveDoProvedor]?.Trim();

		options.Provedor = string.IsNullOrEmpty(provedor)
			? ProvedorDeChat.Nenhum
			: string.Equals(provedor, nameof(ProvedorDeChat.Teams), StringComparison.OrdinalIgnoreCase)
				? ProvedorDeChat.Teams
				: throw new InvalidOperationException($"{ChaveDoProvedor} aceita vazio ou Teams.");

		var tenantId = configuration[ChaveDoTenantId]?.Trim();

		options.TenantId = string.IsNullOrEmpty(tenantId)
			? null
			: Guid.TryParse(tenantId, out var lido) && lido != Guid.Empty
				? lido
				: throw new InvalidOperationException($"{ChaveDoTenantId} precisa ser um GUID.");
	}
}
