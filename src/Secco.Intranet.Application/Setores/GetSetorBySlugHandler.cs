using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Setores;

/// <summary>Leitura de um setor pelo slug — a chave que aparece nas rotas e nas Roles.</summary>
/// <param name="repository">Persistência de setores.</param>
public sealed class GetSetorBySlugHandler(ISetorRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<SetorDto>> HandleAsync(string? slug, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(slug))
		{
			return IntranetErrors.Setores.NotFound;
		}

		var setor = await repository.GetBySlugAsync(slug, cancellationToken).ConfigureAwait(false);

		return setor is null ? IntranetErrors.Setores.NotFound : SetorDto.FromEntity(setor);
	}
}
