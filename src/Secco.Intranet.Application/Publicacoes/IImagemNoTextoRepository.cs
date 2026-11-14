using Secco.Intranet.Domain.Publicacoes;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>
/// Porta de persistência das imagens no texto. Sem <c>SaveChanges</c> próprio: o de
/// <see cref="IPublicacaoRepository"/> grava tudo, pois ambos usam o mesmo contexto com escopo.
/// </summary>
public interface IImagemNoTextoRepository
{
	/// <summary>Marca a imagem para inclusão.</summary>
	/// <param name="imagem">Imagem a incluir.</param>
	void Adicionar(ImagemNoTexto imagem);

	/// <summary>Marca a imagem para remoção.</summary>
	/// <param name="imagem">Imagem a remover.</param>
	void Remover(ImagemNoTexto imagem);

	/// <summary>Imagens de uma publicação.</summary>
	/// <param name="publicacaoId">Publicação dona.</param>
	/// <param name="ct">Token de cancelamento.</param>
	Task<IReadOnlyList<ImagemNoTexto>> ListarDaPublicacaoAsync(Guid publicacaoId, CancellationToken ct = default);

	/// <summary>Busca uma imagem da publicação; nulo se não existir ou for de outra publicação.</summary>
	/// <param name="publicacaoId">Publicação dona.</param>
	/// <param name="imagemId">Identificador da imagem.</param>
	/// <param name="ct">Token de cancelamento.</param>
	Task<ImagemNoTexto?> GetAsync(Guid publicacaoId, Guid imagemId, CancellationToken ct = default);
}
