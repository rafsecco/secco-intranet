using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência das imagens no texto no banco do tenant atual.</summary>
internal sealed class ImagemNoTextoRepository(IntranetDbContext context) : IImagemNoTextoRepository
{
	public void Adicionar(ImagemNoTexto imagem) => context.ImagensNoTexto.Add(imagem);

	public void Remover(ImagemNoTexto imagem) => context.ImagensNoTexto.Remove(imagem);

	public async Task<IReadOnlyList<ImagemNoTexto>> ListarDaPublicacaoAsync(Guid publicacaoId, CancellationToken ct = default) =>
		await context.ImagensNoTexto
			.Where(imagem => imagem.PublicacaoId == publicacaoId)
			.OrderBy(imagem => imagem.Id)
			.ToListAsync(ct)
			.ConfigureAwait(false);

	public async Task<ImagemNoTexto?> GetAsync(Guid publicacaoId, Guid imagemId, CancellationToken ct = default) =>
		await context.ImagensNoTexto
			.FirstOrDefaultAsync(imagem => imagem.Id == imagemId && imagem.PublicacaoId == publicacaoId, ct)
			.ConfigureAwait(false);
}
