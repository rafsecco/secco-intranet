using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>
/// Lista todas as publicações de um setor, inclusive fora do ar: é a aba de quem administra,
/// e esconder a agendada tornaria impossível editá-la antes de entrar no ar.
/// </summary>
/// <param name="repository">Persistência de publicações.</param>
public sealed class ListarPublicacoesDoSetorHandler(IPublicacaoRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorSlug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<PublicacaoDto>>> HandleAsync(
		string? setorSlug,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(setorSlug))
		{
			return IntranetErrors.Setores.NotFound;
		}

		var publicacoes = await repository.ListarDoSetorAsync(setorSlug, cancellationToken).ConfigureAwait(false);

		return Result<IReadOnlyList<PublicacaoDto>>.Success(publicacoes);
	}
}
