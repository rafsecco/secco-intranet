using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Lê a federação de login via Microsoft Entra ID do tenant atual.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
public sealed class ObterFederacaoHandler(IGestaoDeAcesso gestao)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public Task<Result<FederacaoDto>> HandleAsync(CancellationToken cancellationToken = default) =>
		gestao.ObterFederacaoAsync(cancellationToken);
}
