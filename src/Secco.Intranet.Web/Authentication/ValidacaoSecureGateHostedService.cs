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
/// modo aberto).
/// </summary>
/// <param name="configuration">Configuração do host.</param>
/// <param name="environment">Ambiente de hospedagem.</param>
internal sealed class ValidacaoSecureGateHostedService(
	IConfiguration configuration, IHostEnvironment environment) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (environment.IsProduction() && !IntranetAuthenticationExtensions.IsConfigured(configuration))
		{
			throw new InvalidOperationException(
				"Secco:SecureGate:Authority ausente em Production. Sem ela a Intranet sobe no " +
				"modo aberto de DEV local — sem autenticacao nem autorizacao — para qualquer " +
				"visitante, em Mural, Documentos e Setor.");
		}

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
