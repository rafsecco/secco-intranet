using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Consulta de uma imagem no texto de uma publicação, para entrega.</summary>
/// <param name="PublicacaoId">Identificador da publicação.</param>
/// <param name="ImagemId">Identificador da imagem.</param>
/// <param name="SetoresDoLeitor">Slugs dos setores aos quais o leitor pertence.</param>
/// <param name="SetoresComEscrita">Slugs dos setores em que o leitor escreve.</param>
/// <param name="ExigirVisibilidade">Quando <c>false</c>, dispensa o filtro — modo aberto de DEV.</param>
public sealed record ObterImagemNoTextoQuery(
	Guid PublicacaoId,
	Guid ImagemId,
	IReadOnlyList<string> SetoresDoLeitor,
	IReadOnlySet<string> SetoresComEscrita,
	bool ExigirVisibilidade);

/// <summary>Localiza uma imagem no texto para quem pode ler a publicação dona.</summary>
/// <param name="repository">Persistência de publicações.</param>
/// <param name="imagens">Persistência das imagens no texto.</param>
public sealed class ObterImagemNoTextoHandler(IPublicacaoRepository repository, IImagemNoTextoRepository imagens)
{
	private const string ContentTypeServido = "image/webp";

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Consulta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ImagemGuardada>> HandleAsync(
		ObterImagemNoTextoQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var encontrada = await repository.GetByIdAsync(query.PublicacaoId, cancellationToken).ConfigureAwait(false);

		if (encontrada?.Publicacao is not { Ativo: true } publicacao
			|| !ObterImagemDaPublicacaoHandler.PodeVer(
				publicacao, encontrada.SetorSlug, query.SetoresDoLeitor, query.SetoresComEscrita, query.ExigirVisibilidade))
		{
			return IntranetErrors.Publicacoes.NotFound;
		}

		var imagem = await imagens.GetAsync(query.PublicacaoId, query.ImagemId, cancellationToken).ConfigureAwait(false);

		return imagem is null
			? IntranetErrors.Publicacoes.NotFound
			: new ImagemGuardada(
				imagem.CaminhoRelativo, imagem.ChaveEmbrulhada, ETagDeArquivo.De(imagem.CaminhoRelativo), ContentTypeServido);
	}
}
