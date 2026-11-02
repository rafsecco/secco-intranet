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

	public async Task<DocumentoComDono?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		// Um documento tem setor OU biblioteca: left join nas duas tabelas, uma única consulta.
		var linha = await (
			from documento in context.Documentos
			where documento.Id == id
			join setor in context.Setores on documento.SetorId equals setor.Id into setores
			from setor in setores.DefaultIfEmpty()
			join biblioteca in context.Bibliotecas on documento.BibliotecaId equals biblioteca.Id into bibliotecas
			from biblioteca in bibliotecas.DefaultIfEmpty()
			select new
			{
				Documento = documento,
				SetorSlug = setor == null ? null : setor.Slug,
				SetorNome = setor == null ? null : setor.Nome,
				SetorAtivo = setor != null && setor.Ativo,
				BibliotecaSlug = biblioteca == null ? null : biblioteca.Slug,
				BibliotecaNome = biblioteca == null ? null : biblioteca.Nome,
				BibliotecaAtiva = biblioteca != null && biblioteca.Ativo,
			})
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);

		if (linha is null)
		{
			return null;
		}

		return linha.BibliotecaSlug is not null
			? new DocumentoComDono(
				linha.Documento,
				ReferenciaDeDono.DaBiblioteca(linha.BibliotecaSlug),
				linha.BibliotecaNome!,
				linha.BibliotecaAtiva)
			: new DocumentoComDono(
				linha.Documento,
				ReferenciaDeDono.DoSetor(linha.SetorSlug!),
				linha.SetorNome!,
				linha.SetorAtivo);
	}

	public async Task<IReadOnlyList<DocumentoDto>> ListarPorDonoAsync(
		ReferenciaDeDono dono,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(dono);

		var slug = dono.Slug;

		var ativos = context.Documentos.AsNoTracking().Where(documento => documento.Ativo);

		// A ordenação vem ANTES da projeção: ordenar por um campo do DTO faria o provider
		// tentar traduzir o próprio construtor, que ele não sabe traduzir.
		if (dono.Tipo == TipoDeDono.Biblioteca)
		{
			var daBiblioteca = await ativos
				.Join(
					context.Bibliotecas.Where(biblioteca => biblioteca.Slug == slug),
					documento => documento.BibliotecaId,
					biblioteca => biblioteca.Id,
					(documento, biblioteca) => new { Documento = documento, DonoNome = biblioteca.Nome })
				.OrderBy(par => par.Documento.Ordem)
				.ThenByDescending(par => par.Documento.CreatedAt)
				.ThenBy(par => par.Documento.Id)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			return [.. daBiblioteca.Select(par => Projetar(par.Documento, dono, par.DonoNome))];
		}

		var doSetor = await ativos
			.Join(
				context.Setores.Where(setor => setor.Slug == slug),
				documento => documento.SetorId,
				setor => setor.Id,
				(documento, setor) => new { Documento = documento, DonoNome = setor.Nome })
			.OrderBy(par => par.Documento.Ordem)
			.ThenByDescending(par => par.Documento.CreatedAt)
			.ThenBy(par => par.Documento.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return [.. doSetor.Select(par => Projetar(par.Documento, dono, par.DonoNome))];
	}

	public async Task<IReadOnlyList<Documento>> ListarIrmaosAsync(
		DonoDeDocumento dono,
		Guid? parentId,
		CancellationToken cancellationToken = default) =>
		await context.Documentos
			.Where(documento => documento.SetorId == dono.SetorId
				&& documento.BibliotecaId == dono.BibliotecaId
				&& documento.ParentId == parentId
				&& documento.Ativo)
			.OrderBy(documento => documento.Ordem)
			.ThenByDescending(documento => documento.CreatedAt)
			.ThenBy(documento => documento.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<bool> TemFilhosAtivosAsync(Guid documentoId, CancellationToken cancellationToken = default) =>
		await context.Documentos
			.AsNoTracking()
			.AnyAsync(documento => documento.ParentId == documentoId && documento.Ativo, cancellationToken)
			.ConfigureAwait(false);

	private static DocumentoDto Projetar(Documento documento, ReferenciaDeDono dono, string donoNome) =>
		new(
			documento.Id,
			documento.Titulo,
			documento.Descricao,
			documento.NomeArquivo,
			documento.ContentType,
			documento.Tamanho,
			documento.Visibilidade,
			dono,
			donoNome,
			documento.CriadoPor,
			documento.CreatedAt,
			documento.Tipo,
			documento.ParentId,
			documento.Ordem);
}
