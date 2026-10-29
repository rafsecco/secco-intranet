using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Devolve um grupo do menu à ordem padrão, apagando as posições salvas.</summary>
/// <param name="repository">Persistência das posições.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class RestaurarOrdemDoMenuHandler(IPosicaoMenuRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="grupo">Grupo a restaurar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(GrupoDoMenu grupo, CancellationToken cancellationToken = default)
	{
		await repository.RemoverGrupoAsync(grupo, cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.OrdemAsync(
			trilha, VerbosDeAuditoria.MenuOrdemRestaurar, grupo.ToString(),
			new { grupo = grupo.ToString() }, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
