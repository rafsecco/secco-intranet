using Secco.Intranet.Application.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Links;

/// <summary>Lista os links de um setor: grupos com seus links e links soltos, na ordem do menu.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ListarLinksDoSetorHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorId">Setor da tela.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<LinksDoSetorDto>> HandleAsync(Guid setorId, CancellationToken cancellationToken = default)
	{
		var arvore = await repository.ListarPorSetorAsync(setorId, cancellationToken).ConfigureAwait(false);
		var trecho = TrechoDeLinks.De(arvore);

		if (trecho is null)
		{
			return Result.Failure<LinksDoSetorDto>(IntranetErrors.Links.NotFound);
		}

		var entradas = new List<EntradaDeLinksDto>();
		var grupos = new List<GrupoDeLinksDto>();

		foreach (var filho in trecho.FilhosDe(trecho.No.Id))
		{
			if (filho.Rota is null)
			{
				var links = trecho.FilhosDe(filho.Id)
					.Where(item => item.Rota is not null)
					.Select(item => new LinkDto(item.Id, item.Nome, item.Rota!, item.NovaAba, filho.Id))
					.ToList();
				var grupo = new GrupoDeLinksDto(filho.Id, filho.Nome, links);

				grupos.Add(grupo);
				entradas.Add(new EntradaDeLinksDto(grupo, null));
			}
			else
			{
				entradas.Add(new EntradaDeLinksDto(null, new LinkDto(filho.Id, filho.Nome, filho.Rota, filho.NovaAba, null)));
			}
		}

		return new LinksDoSetorDto(entradas, grupos);
	}
}
