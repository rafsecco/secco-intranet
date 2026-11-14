using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Domain.Publicacoes;

namespace Secco.Intranet.Tests.Support;

/// <summary>Repositório de imagens no texto em memória que registra inclusões e remoções.</summary>
public sealed class ImagemNoTextoRepositorioFalso : IImagemNoTextoRepository
{
	/// <summary>Imagens guardadas.</summary>
	public List<ImagemNoTexto> Guardadas { get; } = [];

	/// <summary>Imagens marcadas para remoção, na ordem.</summary>
	public List<ImagemNoTexto> Removidas { get; } = [];

	/// <inheritdoc />
	public void Adicionar(ImagemNoTexto imagem) => Guardadas.Add(imagem);

	/// <inheritdoc />
	public void Remover(ImagemNoTexto imagem)
	{
		Guardadas.Remove(imagem);
		Removidas.Add(imagem);
	}

	/// <inheritdoc />
	public Task<IReadOnlyList<ImagemNoTexto>> ListarDaPublicacaoAsync(Guid publicacaoId, CancellationToken ct = default) =>
		Task.FromResult<IReadOnlyList<ImagemNoTexto>>(Guardadas.Where(i => i.PublicacaoId == publicacaoId).ToList());

	/// <inheritdoc />
	public Task<ImagemNoTexto?> GetAsync(Guid publicacaoId, Guid imagemId, CancellationToken ct = default) =>
		Task.FromResult(Guardadas.FirstOrDefault(i => i.Id == imagemId && i.PublicacaoId == publicacaoId));
}
