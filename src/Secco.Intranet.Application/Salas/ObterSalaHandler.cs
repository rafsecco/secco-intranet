using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Obtém uma sala pelo id.</summary>
public sealed class ObterSalaHandler(ISalaRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Sala procurada.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<SalaDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var sala = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);

		return sala is null ? IntranetErrors.Salas.NotFound : SalaDto.De(sala);
	}
}
