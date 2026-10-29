using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Menu;

/// <summary>Porta de persistência das posições salvas do menu — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IPosicaoMenuRepository
{
	/// <summary>Todas as posições salvas, de todos os grupos, desrastreadas.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<PosicaoMenu>> ListarAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Troca as posições de um grupo: apaga as existentes e grava <c>chavesEmOrdem[i]</c> com <c>Ordem = i</c>,
	/// num único <c>SaveChangesAsync</c>. Não mexe nos outros grupos.
	/// </summary>
	/// <param name="grupo">Grupo a substituir.</param>
	/// <param name="chavesEmOrdem">Chaves na ordem desejada.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SubstituirGrupoAsync(GrupoDoMenu grupo, IReadOnlyList<string> chavesEmOrdem, CancellationToken cancellationToken = default);

	/// <summary>Apaga as posições de um grupo, devolvendo-o à ordem padrão.</summary>
	/// <param name="grupo">Grupo a restaurar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemoverGrupoAsync(GrupoDoMenu grupo, CancellationToken cancellationToken = default);
}
