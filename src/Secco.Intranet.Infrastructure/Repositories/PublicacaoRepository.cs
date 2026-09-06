using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de publicações no banco do tenant atual.</summary>
internal sealed class PublicacaoRepository(IntranetDbContext context) : IPublicacaoRepository
{
	public async Task AddAsync(Publicacao publicacao, CancellationToken cancellationToken = default)
	{
		context.Publicacoes.Add(publicacao);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

	public async Task<PublicacaoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.Publicacoes
			.Where(publicacao => publicacao.Id == id)
			.Join(
				context.Setores,
				publicacao => publicacao.SetorId,
				setor => setor.Id,
				(publicacao, setor) => new PublicacaoComSetor(publicacao, setor.Nome, setor.Slug))
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<PagedResult<PublicacaoDto>> ListarNoMuralAsync(
		MuralCriteria criteria,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(criteria);

		var consulta = context.Publicacoes
			.AsNoTracking()
			.Where(publicacao => publicacao.Ativo)
			.Where(publicacao => publicacao.PublicadoEm <= criteria.Agora)
			.Where(publicacao => publicacao.ExpiraEm == null || publicacao.ExpiraEm > criteria.Agora)
			.Join(
				context.Setores,
				publicacao => publicacao.SetorId,
				setor => setor.Id,
				(publicacao, setor) => new { Publicacao = publicacao, Setor = setor });

		if (criteria.Tipo is not null)
		{
			consulta = consulta.Where(par => par.Publicacao.Tipo == criteria.Tipo);
		}

		if (criteria.ExigirVisibilidade)
		{
			var slugs = criteria.SetoresDoLeitor.ToList();

			consulta = consulta.Where(par =>
				par.Publicacao.Visibilidade == Visibilidade.Empresa || slugs.Contains(par.Setor.Slug));
		}

		var total = await consulta.LongCountAsync(cancellationToken).ConfigureAwait(false);

		// A ordenação vem ANTES da projeção: ordenar por campo do DTO faria o provider tentar
		// traduzir o próprio construtor, que ele não sabe traduzir.
		var itens = await consulta
			.OrderByDescending(par => par.Publicacao.PublicadoEm)
			.ThenByDescending(par => par.Publicacao.Id)
			.Skip(criteria.Page.Skip)
			.Take(criteria.Page.Size)
			.Select(par => new PublicacaoDto(
				par.Publicacao.Id,
				par.Publicacao.Titulo,
				par.Publicacao.Corpo,
				par.Publicacao.Tipo,
				par.Publicacao.Visibilidade,
				par.Publicacao.Prioridade,
				par.Publicacao.PublicadoEm,
				par.Publicacao.ExpiraEm,
				par.Publicacao.Ativo,
				par.Setor.Id,
				par.Setor.Nome,
				par.Setor.Slug,
				par.Publicacao.CriadoPor))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return PagedResult.Create(itens, criteria.Page, total);
	}

	public async Task<IReadOnlyList<PublicacaoDto>> ListarDoSetorAsync(
		string setorSlug,
		CancellationToken cancellationToken = default) =>
		await context.Publicacoes
			.AsNoTracking()
			.Join(
				context.Setores.Where(setor => setor.Slug == setorSlug),
				publicacao => publicacao.SetorId,
				setor => setor.Id,
				(publicacao, setor) => new { Publicacao = publicacao, Setor = setor })
			.OrderByDescending(par => par.Publicacao.PublicadoEm)
			.Select(par => new PublicacaoDto(
				par.Publicacao.Id,
				par.Publicacao.Titulo,
				par.Publicacao.Corpo,
				par.Publicacao.Tipo,
				par.Publicacao.Visibilidade,
				par.Publicacao.Prioridade,
				par.Publicacao.PublicadoEm,
				par.Publicacao.ExpiraEm,
				par.Publicacao.Ativo,
				par.Setor.Id,
				par.Setor.Nome,
				par.Setor.Slug,
				par.Publicacao.CriadoPor))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
}
