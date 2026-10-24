using Secco.Intranet.Domain.Vencimentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Detalha um vencimento: estado, próximos avisos, renovações e avisos tratados.</summary>
public sealed class ObterVencimentoHandler(IItemVencimentoRepository repository, RelogioDeVencimentos relogio)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Identificador do item.</param>
	/// <param name="setorId">Setor da rota; item de outro setor não é encontrado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<VencimentoDetalheDto>> HandleAsync(Guid id, Guid setorId, CancellationToken cancellationToken = default)
	{
		var item = await repository.GetParaEdicaoAsync(id, cancellationToken).ConfigureAwait(false);

		if (item is null || item.SetorId != setorId)
		{
			return IntranetErrors.Vencimentos.NotFound;
		}

		var renovacoes = await repository.ListarRenovacoesAsync(id, cancellationToken).ConfigureAwait(false);
		var avisos = await repository.ListarAvisosAsync(id, cancellationToken).ConfigureAwait(false);
		var hoje = relogio.Hoje();

		var tratados = avisos.Where(aviso => aviso.Ciclo == item.Ciclo).Select(aviso => aviso.Marco).ToHashSet();

		return new VencimentoDetalheDto(
			item.Id,
			item.Titulo,
			item.Descricao,
			item.DataVencimento,
			RegrasDeAviso.Estado(item, hoje),
			item.ResponsavelId,
			item.Antecedencias,
			item.Ciclo,
			RegrasDeAviso.ProximosAvisos(item, hoje, tratados),
			renovacoes,
			avisos);
	}
}
