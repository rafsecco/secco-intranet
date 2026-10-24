using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Vencimentos;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Vencimentos;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de itens de vencimento no banco do tenant atual.</summary>
internal sealed class ItemVencimentoRepository(IntranetDbContext context) : IItemVencimentoRepository
{
	public async Task AddAsync(ItemVencimento item, CancellationToken ct = default)
	{
		context.ItensVencimento.Add(item);
		await context.SaveChangesAsync(ct).ConfigureAwait(false);
	}

	public async Task<ItemVencimento?> GetParaEdicaoAsync(Guid id, CancellationToken ct = default) =>
		await context.ItensVencimento
			.FirstOrDefaultAsync(item => item.Id == id, ct)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<ItemVencimento>> ListarDoSetorAsync(
		Guid setorId, bool incluirEncerrados, CancellationToken ct = default)
	{
		var query = context.ItensVencimento
			.AsNoTracking()
			.Where(item => item.SetorId == setorId);

		if (!incluirEncerrados)
		{
			query = query.Where(item => item.EncerradoEm == null);
		}

		return await query
			.OrderBy(item => item.DataVencimento)
			.ThenBy(item => item.Titulo)
			.ToListAsync(ct)
			.ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<RenovacaoDeVencimento>> ListarRenovacoesAsync(Guid itemId, CancellationToken ct = default) =>
		await context.RenovacoesDeVencimento
			.AsNoTracking()
			.Where(renovacao => renovacao.ItemVencimentoId == itemId)
			.OrderByDescending(renovacao => renovacao.RenovadoEm)
			.ToListAsync(ct)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<AvisoDeVencimentoEnviado>> ListarAvisosAsync(Guid itemId, CancellationToken ct = default) =>
		await context.AvisosDeVencimentoEnviados
			.AsNoTracking()
			.Where(aviso => aviso.ItemVencimentoId == itemId)
			.OrderBy(aviso => aviso.Ciclo)
			.ThenByDescending(aviso => aviso.Marco)
			.ToListAsync(ct)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<ItemParaAviso>> ListarAtivosParaAvisoAsync(CancellationToken ct = default)
	{
		var setores = await context.Setores
			.AsNoTracking()
			.Where(setor => setor.Ativo)
			.Select(setor => new { setor.Id, setor.Slug, setor.Nome })
			.ToListAsync(ct)
			.ConfigureAwait(false);

		if (setores.Count == 0)
		{
			return [];
		}

		var setorIds = setores.Select(setor => setor.Id).ToList();

		// A árvore de menu é pequena: a checagem de ancestrais ativos é feita em memória.
		var menu = await context.ItensMenu
			.AsNoTracking()
			.Where(item => setorIds.Contains(item.SetorId))
			.ToListAsync(ct)
			.ConfigureAwait(false);

		var habilitados = setores
			.Where(setor => RecursoEstaLigado(menu.Where(item => item.SetorId == setor.Id).ToList()))
			.ToDictionary(setor => setor.Id);

		if (habilitados.Count == 0)
		{
			return [];
		}

		var habilitadosIds = habilitados.Keys.ToList();

		var itens = await context.ItensVencimento
			.AsNoTracking()
			.Where(item => item.EncerradoEm == null && habilitadosIds.Contains(item.SetorId))
			.OrderBy(item => item.DataVencimento)
			.ToListAsync(ct)
			.ConfigureAwait(false);

		if (itens.Count == 0)
		{
			return [];
		}

		var itemIds = itens.Select(item => item.Id).ToList();

		var avisos = await context.AvisosDeVencimentoEnviados
			.AsNoTracking()
			.Where(aviso => itemIds.Contains(aviso.ItemVencimentoId))
			.Select(aviso => new { aviso.ItemVencimentoId, aviso.Ciclo, aviso.Marco })
			.ToListAsync(ct)
			.ConfigureAwait(false);

		var avisosPorItem = avisos
			.GroupBy(aviso => aviso.ItemVencimentoId)
			.ToDictionary(grupo => grupo.Key, grupo => grupo.ToList());

		return itens
			.Select(item => new ItemParaAviso(
				item,
				habilitados[item.SetorId].Slug,
				habilitados[item.SetorId].Nome,
				avisosPorItem.TryGetValue(item.Id, out var tratados)
					? tratados.Where(aviso => aviso.Ciclo == item.Ciclo).Select(aviso => aviso.Marco).ToHashSet()
					: new HashSet<int>()))
			.ToList();
	}

	public async Task<bool> TentarRegistrarAvisoAsync(AvisoDeVencimentoEnviado aviso, CancellationToken ct = default)
	{
		context.AvisosDeVencimentoEnviados.Add(aviso);

		try
		{
			await context.SaveChangesAsync(ct).ConfigureAwait(false);

			return true;
		}
		catch (DbUpdateException)
		{
			context.Entry(aviso).State = EntityState.Detached;

			var jaExiste = await context.AvisosDeVencimentoEnviados
				.AsNoTracking()
				.AnyAsync(
					outro => outro.ItemVencimentoId == aviso.ItemVencimentoId
						&& outro.Ciclo == aviso.Ciclo
						&& outro.Marco == aviso.Marco,
					ct)
				.ConfigureAwait(false);

			// Outro processo tratou o marco um instante antes: não é falha, o chamador não reenvia.
			// Qualquer outra causa de DbUpdateException continua subindo.
			if (jaExiste)
			{
				return false;
			}

			throw;
		}
	}

	public async Task RemoverAvisoAsync(Guid avisoId, CancellationToken ct = default) =>
		await context.AvisosDeVencimentoEnviados
			.Where(aviso => aviso.Id == avisoId)
			.ExecuteDeleteAsync(ct)
			.ConfigureAwait(false);

	public async Task AdicionarRenovacaoAsync(RenovacaoDeVencimento renovacao, CancellationToken ct = default)
	{
		context.RenovacoesDeVencimento.Add(renovacao);
		await context.SaveChangesAsync(ct).ConfigureAwait(false);
	}

	public async Task SaveChangesAsync(CancellationToken ct = default) =>
		await context.SaveChangesAsync(ct).ConfigureAwait(false);

	/// <summary>
	/// O recurso está ligado quando há item de menu <c>Vencimentos</c> e ele e todos os seus
	/// ancestrais, até a raiz do setor, estão ativos (mesma regra de <c>CaminhoDoTipoAsync</c>).
	/// </summary>
	private static bool RecursoEstaLigado(List<ItemMenu> arvore)
	{
		var atual = arvore.FirstOrDefault(item => item.Tipo == TipoDeItemMenu.Vencimentos);

		while (atual is not null && atual.Tipo != TipoDeItemMenu.Setor)
		{
			if (!atual.Ativo)
			{
				return false;
			}

			var paiId = atual.ParentId;
			atual = arvore.FirstOrDefault(item => item.Id == paiId);
		}

		// Sem item do tipo, ou cadeia que não termina na raiz (dado corrompido): desligado.
		return atual is not null;
	}
}
