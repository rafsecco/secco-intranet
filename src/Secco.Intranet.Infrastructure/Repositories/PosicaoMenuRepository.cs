using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência das posições salvas do menu no banco do tenant atual.</summary>
internal sealed class PosicaoMenuRepository(IntranetDbContext context) : IPosicaoMenuRepository
{
	public async Task<IReadOnlyList<PosicaoMenu>> ListarAsync(CancellationToken cancellationToken = default) =>
		await context.PosicoesMenu
			.AsNoTracking()
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task SubstituirGrupoAsync(
		GrupoDoMenu grupo, IReadOnlyList<string> chavesEmOrdem, CancellationToken cancellationToken = default)
	{
		var existentes = await context.PosicoesMenu
			.Where(posicao => posicao.Grupo == grupo)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		context.PosicoesMenu.RemoveRange(existentes);
		context.PosicoesMenu.AddRange(chavesEmOrdem.Select((chave, indice) => new PosicaoMenu(grupo, chave, indice)));

		// Um único SaveChanges: uma transação só para apagar e regravar o grupo.
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task RemoverGrupoAsync(GrupoDoMenu grupo, CancellationToken cancellationToken = default)
	{
		var existentes = await context.PosicoesMenu
			.Where(posicao => posicao.Grupo == grupo)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		context.PosicoesMenu.RemoveRange(existentes);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}
}
