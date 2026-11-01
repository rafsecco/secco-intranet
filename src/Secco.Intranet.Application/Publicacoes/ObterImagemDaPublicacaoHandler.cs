using Secco.Intranet.Domain;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Consulta da imagem de uma publicação, para entrega.</summary>
/// <param name="Id">Identificador da publicação.</param>
/// <param name="SetoresDoLeitor">Slugs dos setores aos quais o leitor pertence.</param>
/// <param name="SetoresComEscrita">Slugs dos setores em que o leitor escreve.</param>
/// <param name="ExigirVisibilidade">Quando <c>false</c>, dispensa o filtro — modo aberto de DEV.</param>
public sealed record ObterImagemDaPublicacaoQuery(
	Guid Id,
	IReadOnlyList<string> SetoresDoLeitor,
	IReadOnlySet<string> SetoresComEscrita,
	bool ExigirVisibilidade);

/// <summary>Referência da imagem guardada, o que o endpoint de leitura precisa para servi-la.</summary>
/// <param name="CaminhoRelativo">Endereço da imagem no armazenamento.</param>
/// <param name="ChaveEmbrulhada">Chave do arquivo cifrada pela chave mestra.</param>
/// <param name="ETag">Validador entre aspas, derivado do caminho sem expô-lo.</param>
/// <param name="ContentType">Tipo do conteúdo servido.</param>
public sealed record ImagemGuardada(string CaminhoRelativo, string ChaveEmbrulhada, string ETag, string ContentType);

/// <summary>
/// Localiza a imagem de uma publicação para quem pode lê-la: a publicação no ar e visível pela
/// regra do <see cref="ObterPublicacaoHandler"/>, ou quem escreve no setor dono (que também vê o
/// que ainda não entrou no ar). Todo o resto responde o mesmo erro de inexistente.
/// </summary>
/// <param name="repository">Persistência de publicações.</param>
public sealed class ObterImagemDaPublicacaoHandler(IPublicacaoRepository repository)
{
	private const string ContentTypeServido = "image/webp";

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Consulta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ImagemGuardada>> HandleAsync(
		ObterImagemDaPublicacaoQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var encontrada = await repository.GetByIdAsync(query.Id, cancellationToken).ConfigureAwait(false);

		if (encontrada?.Publicacao is not { Ativo: true, ImagemCaminhoRelativo: { } caminho, ImagemChaveEmbrulhada: { } chave } publicacao)
		{
			return IntranetErrors.Publicacoes.NotFound;
		}

		var pode = !query.ExigirVisibilidade
			|| query.SetoresComEscrita.Contains(encontrada.SetorSlug)
			|| (publicacao.EstaNoAr(DateTimeOffset.UtcNow)
				&& (publicacao.Visibilidade == Visibilidade.Empresa
					|| query.SetoresDoLeitor.Contains(encontrada.SetorSlug, StringComparer.OrdinalIgnoreCase)));

		return pode
			? new ImagemGuardada(caminho, chave, ETagDeArquivo.De(caminho), ContentTypeServido)
			: IntranetErrors.Publicacoes.NotFound;
	}
}
