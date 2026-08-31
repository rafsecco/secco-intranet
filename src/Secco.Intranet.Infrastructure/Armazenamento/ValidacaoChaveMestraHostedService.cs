using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Secco.Intranet.Infrastructure.Armazenamento;

/// <summary>
/// Resolve o cifrador no startup só para que uma chave mestra ausente ou malformada derrube a
/// aplicação imediatamente, e não no primeiro upload (ADR-0020, fail-fast). Sem isto, um
/// deploy de produção sem a chave subiria saudável e só falharia quando alguém tentasse
/// publicar um documento.
/// </summary>
/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
internal sealed class ValidacaoChaveMestraHostedService(IServiceProvider serviceProvider) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		using var scope = serviceProvider.CreateScope();
		_ = scope.ServiceProvider.GetRequiredService<EnvelopeCipher>();

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
