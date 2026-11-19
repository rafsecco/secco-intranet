using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência dos links do menu principal no banco do tenant atual.</summary>
internal sealed class LinkDoMenuPrincipalRepository(IntranetDbContext context) : ILinkDoMenuPrincipalRepository
{
	public async Task<IReadOnlyList<LinkDoMenuPrincipal>> ListarAsync(CancellationToken cancellationToken = default) =>
		await context.LinksDoMenuPrincipal
			.AsNoTracking()
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<LinkDoMenuPrincipal?> ObterAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.LinksDoMenuPrincipal
			.FirstOrDefaultAsync(link => link.Id == id, cancellationToken)
			.ConfigureAwait(false);

	public async Task AddAsync(LinkDoMenuPrincipal link, CancellationToken cancellationToken = default)
	{
		context.LinksDoMenuPrincipal.Add(link);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task SalvarAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

	public async Task RemoverAsync(LinkDoMenuPrincipal link, CancellationToken cancellationToken = default)
	{
		var chave = link.Chave;
		var posicoes = await context.PosicoesMenu
			.Where(posicao => posicao.Grupo == GrupoDoMenu.Principal && posicao.Chave == chave)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		context.PosicoesMenu.RemoveRange(posicoes);
		context.LinksDoMenuPrincipal.Remove(link);

		// Um único SaveChanges: o link e a posição saem na mesma transação.
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}
}
