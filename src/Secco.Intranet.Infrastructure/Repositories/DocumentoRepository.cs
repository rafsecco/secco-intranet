using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de documentos no banco do tenant atual.</summary>
internal sealed class DocumentoRepository(IntranetDbContext context) : IDocumentoRepository
{
	public async Task AddAsync(Documento documento, CancellationToken cancellationToken = default)
	{
		context.Documentos.Add(documento);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

	public async Task<DocumentoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.Documentos
			.Where(documento => documento.Id == id)
			.Join(
				context.Setores,
				documento => documento.SetorId,
				setor => setor.Id,
				(documento, setor) => new DocumentoComSetor(documento, setor.Nome, setor.Slug))
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<DocumentoDto>> ListarPorSetorAsync(
		string setorSlug,
		CancellationToken cancellationToken = default) =>
		await context.Documentos
			.AsNoTracking()
			.Where(documento => documento.Ativo)
			.Join(
				context.Setores.Where(setor => setor.Slug == setorSlug),
				documento => documento.SetorId,
				setor => setor.Id,
				(documento, setor) => new { Documento = documento, Setor = setor })
			// A ordenação vem ANTES da projeção: ordenar por um campo do DTO faria o provider
			// tentar traduzir o próprio construtor, que ele não sabe traduzir.
			.OrderByDescending(par => par.Documento.CreatedAt)
			.Select(par => new DocumentoDto(
				par.Documento.Id,
				par.Documento.Titulo,
				par.Documento.Descricao,
				par.Documento.NomeArquivo,
				par.Documento.ContentType,
				par.Documento.Tamanho,
				par.Documento.Visibilidade,
				par.Setor.Id,
				par.Setor.Nome,
				par.Setor.Slug,
				par.Documento.CriadoPor,
				par.Documento.CreatedAt))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
}
