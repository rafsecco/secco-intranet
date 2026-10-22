using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Client.Catalog;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Decide se o client administrativo do tenant está disponível (ADR-0052 da plataforma). O
/// <c>ISecureGateClient</c> registrado por <c>AddSecureGateTenantManagementClient()</c> usa a
/// credencial da subseção <c>Secco:SecureGate:TenantManagement</c> (client de produto criado
/// pelo operador no tenant da Intranet); sem ela, o adapter real só falharia na primeira
/// chamada, longe da causa — então a composição escolhe o adapter "indisponível".
/// </summary>
internal static class SecureGateTenantManagement
{
	/// <summary>Chave do <c>ClientId</c> do client de gestão do tenant.</summary>
	internal const string ClientIdKey = "Secco:SecureGate:TenantManagement:ClientId";

	/// <summary>
	/// Indica se a seção <c>Secco:SecureGate</c> está configurada <b>e</b> o client de gestão
	/// do tenant tem <c>ClientId</c>.
	/// </summary>
	/// <param name="serviceProvider">Provedor de serviços do escopo.</param>
	internal static bool EstaDisponivel(IServiceProvider serviceProvider)
	{
		var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();
		var configuration = serviceProvider.GetRequiredService<IConfiguration>();

		return credenciais.IsConfigured && !string.IsNullOrWhiteSpace(configuration[ClientIdKey]);
	}
}
