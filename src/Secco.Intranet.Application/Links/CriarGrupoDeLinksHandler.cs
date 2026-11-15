using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Links;

/// <summary>Cria um grupo de links: filho direto do nó de links, sem rota, por último.</summary>
/// <param name="repository">Persistência da árvore.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class CriarGrupoDeLinksHandler(IItemMenuRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorId">Setor da tela.</param>
	/// <param name="nome">Nome do grupo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(Guid setorId, string? nome, CancellationToken cancellationToken = default)
	{
		var arvore = await repository.ListarPorSetorAsync(setorId, cancellationToken).ConfigureAwait(false);
		var trecho = TrechoDeLinks.De(arvore);

		if (trecho is null)
		{
			return Result.Failure<Guid>(IntranetErrors.Links.NotFound);
		}

		if (CamposDeLink.ValidarNome(nome) is { } erro)
		{
			return Result.Failure<Guid>(erro);
		}

		var nomeAparado = nome!.Trim();
		var irmaos = trecho.FilhosDe(trecho.No.Id);
		var slug = SlugDeLink.Gerar(nomeAparado, irmaos.Select(irmao => irmao.Slug));
		var ordem = irmaos.Select(irmao => irmao.Ordem).DefaultIfEmpty(-1).Max() + 1;

		var grupo = new ItemMenu(setorId, trecho.No.Id, nomeAparado, slug, TipoDeItemMenu.Personalizado, null, null, ordem);

		await repository.AddAsync(grupo, cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.ItemAsync(
			trilha, VerbosDeAuditoria.MenuItemCriar, grupo,
			new { setorId, parentId = grupo.ParentId, nome = grupo.Nome, slug = grupo.Slug, tipo = grupo.Tipo.ToString(), grupo = true },
			cancellationToken).ConfigureAwait(false);

		return grupo.Id;
	}
}
