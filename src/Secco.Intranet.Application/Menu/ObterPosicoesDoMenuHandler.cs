using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Menu;

/// <summary>Lê as posições salvas do menu do tenant atual.</summary>
/// <param name="repository">Persistência das posições.</param>
public sealed class ObterPosicoesDoMenuHandler(IPosicaoMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<PosicoesDoMenuDto> HandleAsync(CancellationToken cancellationToken = default)
	{
		var posicoes = await repository.ListarAsync(cancellationToken).ConfigureAwait(false);

		return new PosicoesDoMenuDto(Do(posicoes, GrupoDoMenu.Principal), Do(posicoes, GrupoDoMenu.Setores));
	}

	private static Dictionary<string, int> Do(IReadOnlyList<PosicaoMenu> posicoes, GrupoDoMenu grupo) =>
		posicoes.Where(p => p.Grupo == grupo)
			.ToDictionary(p => p.Chave, p => p.Ordem, StringComparer.OrdinalIgnoreCase);
}
