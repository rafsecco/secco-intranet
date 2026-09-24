using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Perfis da aba "Perfis".</summary>
/// <param name="Perfis">Perfis existentes, por nome.</param>
/// <param name="PerfisDoProdutoFaltando">Perfis do produto que ainda não existem no tenant e podem ser criados.</param>
public sealed record PerfisDaTelaDto(IReadOnlyList<PerfilDto> Perfis, IReadOnlyList<string> PerfisDoProdutoFaltando);

/// <summary>Lista os perfis e aponta quais perfis do produto faltam.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
public sealed class ListarPerfisHandler(IGestaoDeAcesso gestao)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PerfisDaTelaDto>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var lidos = await gestao.ListarPerfisAsync(cancellationToken).ConfigureAwait(false);

		if (lidos.IsFailure)
		{
			return Result.Failure<PerfisDaTelaDto>(lidos.Error);
		}

		IReadOnlyList<PerfilDto> perfis = [.. lidos.Value.OrderBy(perfil => perfil.Nome, StringComparer.OrdinalIgnoreCase)];

		IReadOnlyList<string> faltando =
			[.. ClassificacaoDePerfil.PerfisDoProduto.Where(produto =>
				!perfis.Any(perfil => string.Equals(perfil.Nome, produto, StringComparison.OrdinalIgnoreCase)))];

		return new PerfisDaTelaDto(perfis, faltando);
	}
}
