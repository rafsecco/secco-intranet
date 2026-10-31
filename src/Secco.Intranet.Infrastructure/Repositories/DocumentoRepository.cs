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
			.OrderBy(par => par.Documento.Ordem)
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
				par.Documento.CreatedAt,
				par.Documento.Tipo,
				par.Documento.ParentId,
				par.Documento.Ordem))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<Documento>> ListarIrmaosAsync(
		Guid setorId,
		Guid? parentId,
		CancellationToken cancellationToken = default) =>
		await context.Documentos
			.Where(documento => documento.SetorId == setorId && documento.ParentId == parentId && documento.Ativo)
			.OrderBy(documento => documento.Ordem)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<bool> TemFilhosAtivosAsync(Guid documentoId, CancellationToken cancellationToken = default) =>
		await context.Documentos
			.AsNoTracking()
			.AnyAsync(documento => documento.ParentId == documentoId && documento.Ativo, cancellationToken)
			.ConfigureAwait(false);
}
