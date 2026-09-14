using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Leitura pontual de um item de inventário.</summary>
public sealed class GetItemInventarioByIdHandler(IItemInventarioRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ItemInventarioDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var item = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

		return item is null ? IntranetErrors.Inventario.NotFound : ItemInventarioDto.FromEntity(item);
	}
}
