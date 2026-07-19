using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Setores;

/// <summary>Leitura pontual de um setor do banco do tenant atual.</summary>
public sealed class GetSetorByIdHandler(ISetorRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Identificador do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<SetorDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var setor = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

		return setor is null
			? IntranetErrors.Setores.NotFound
			: SetorDto.FromEntity(setor);
	}
}
