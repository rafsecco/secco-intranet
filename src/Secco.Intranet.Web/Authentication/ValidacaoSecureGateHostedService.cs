using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Confere no startup que <c>Production</c> nunca sobe no modo aberto (ADR-0020): sem
/// <c>Secco:SecureGate:Authority</c> configurada, <see cref="IntranetAuthenticationExtensions"/>
/// não registra autenticação nem autorização, e os controllers que dependem de
/// <see cref="Navigation.AcessoAdministrativo.ModoAbertoDeDev"/> caem no bypass de
/// Development — liberando leitura e escrita para qualquer visitante. Mesmo risco que
/// <c>ValidacaoChaveMestraHostedService</c> cobre para a chave mestra de documentos; aqui é a
/// seção que liga a autenticação inteira. Não se aplica a <c>Testing</c>, que nunca configura
/// SecureGate de propósito (a autorização real é exercida com claims falsas, não com este
/// modo aberto). Também exige o client do login interativo, que é separado do de máquina, e o client de
/// gestão do tenant (subseção <c>Secco:SecureGate:TenantManagement</c>).
/// </summary>
/// <param name="configuration">Configuração do host.</param>
/// <param name="environment">Ambiente de hospedagem.</param>
internal sealed class ValidacaoSecureGateHostedService(
	IConfiguration configuration, IHostEnvironment environment) : IHostedService
{
	/// <summary>Chave do <c>ClientId</c> do client de gestão do tenant (ADR-0052 da plataforma).</summary>
	internal const string TenantManagementClientIdKey = "Secco:SecureGate:TenantManagement:ClientId";

	/// <summary>Chave do <c>ClientSecret</c> do client de gestão do tenant.</summary>
	internal const string TenantManagementClientSecretKey = "Secco:SecureGate:TenantManagement:ClientSecret";

	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (environment.IsProduction() && !IntranetAuthenticationExtensions.IsConfigured(configuration))
		{
			throw new InvalidOperationException(
				"Secco:SecureGate:Authority ausente em Production. Sem ela a Intranet sobe no " +
				"modo aberto de DEV local — sem autenticacao nem autorizacao — para qualquer " +
				"visitante, em Mural, Documentos e Setor.");
		}

		// Sem o client de login, a Intranet sobe e o primeiro acesso falha dentro do SecureGate,
		// longe da causa. O Secco:SecureGate:ClientId não serve de reserva: é client credentials,
		// e o SecureGate não dá authorization code a ele (ADR-0037 da plataforma).
		if (environment.IsProduction()
			&& IntranetAuthenticationExtensions.IsConfigured(configuration)
			&& string.IsNullOrWhiteSpace(configuration[IntranetAuthenticationExtensions.LoginClientIdKey]))
		{
			throw new InvalidOperationException(
				$"{IntranetAuthenticationExtensions.LoginClientIdKey} ausente em Production. O login " +
				"interativo usa um client proprio (AuthorizationCode), separado do client de maquina " +
				"de Secco:SecureGate.");
		}

		// A gestão de acesso usa o client de produto do tenant (securegate:tenant-manage, ADR-0052
		// da plataforma), com credencial própria. Sem ela a tela de acesso só falharia na primeira
		// chamada. Só as chaves são citadas, nunca os valores.
		if (environment.IsProduction() && IntranetAuthenticationExtensions.IsConfigured(configuration))
		{
			foreach (var chave in new[] { TenantManagementClientIdKey, TenantManagementClientSecretKey })
			{
				if (string.IsNullOrWhiteSpace(configuration[chave]))
				{
					throw new InvalidOperationException(
						$"{chave} ausente em Production. A gestao de acesso do tenant usa um client de " +
						"produto proprio (securegate:tenant-manage), criado pelo operador da instalacao " +
						"no tenant da Intranet, separado do client de maquina de Secco:SecureGate.");
				}
			}
		}

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
