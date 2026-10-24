using Secco.Intranet.Domain.Vencimentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Lista os vencimentos de um setor por data, com o estado de hoje.</summary>
public sealed class ListarVencimentosDoSetorHandler(IItemVencimentoRepository repository, RelogioDeVencimentos relogio)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorId">Setor dono.</param>
	/// <param name="incluirEncerrados">Quando <c>false</c>, omite os encerrados.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<VencimentoResumoDto>>> HandleAsync(
		Guid setorId, bool incluirEncerrados, CancellationToken cancellationToken = default)
	{
		var itens = await repository.ListarDoSetorAsync(setorId, incluirEncerrados, cancellationToken).ConfigureAwait(false);
		var hoje = relogio.Hoje();

		IReadOnlyList<VencimentoResumoDto> lista = itens
			.Select(item => new VencimentoResumoDto(
				item.Id, item.Titulo, item.DataVencimento, RegrasDeAviso.Estado(item, hoje), item.ResponsavelId))
			.ToList();

		return Result.Success(lista);
	}
}
