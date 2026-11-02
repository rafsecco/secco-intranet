using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Bibliotecas;

/// <summary>
/// Leitura de uma biblioteca ativa pelo slug — a chave da URL e das permissões. Inexistente e
/// inativa dão o mesmo erro de não encontrada (404 indistinto, ADR-0005).
/// </summary>
/// <param name="repository">Persistência de bibliotecas.</param>
public sealed class ObterBibliotecaPorSlugHandler(IBibliotecaRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<BibliotecaDto>> HandleAsync(string? slug, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(slug))
		{
			return IntranetErrors.Bibliotecas.NotFound;
		}

		var biblioteca = await repository.GetBySlugAsync(slug.Trim(), cancellationToken).ConfigureAwait(false);

		return biblioteca is not { Ativo: true }
			? IntranetErrors.Bibliotecas.NotFound
			: BibliotecaDto.FromEntity(biblioteca);
	}
}
