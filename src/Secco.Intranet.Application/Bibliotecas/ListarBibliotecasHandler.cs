using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Bibliotecas;

/// <summary>Pedido de listagem de bibliotecas.</summary>
/// <param name="SomenteAtivas">Quando <c>true</c>, omite as desativadas.</param>
public sealed record ListarBibliotecasQuery(bool SomenteAtivas);

/// <summary>Lista as bibliotecas por nome.</summary>
/// <param name="repository">Persistência de bibliotecas.</param>
public sealed class ListarBibliotecasHandler(IBibliotecaRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<BibliotecaDto>>> HandleAsync(
		ListarBibliotecasQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var bibliotecas = await repository.ListarAsync(query.SomenteAtivas, cancellationToken).ConfigureAwait(false);

		IReadOnlyList<BibliotecaDto> dtos = [.. bibliotecas.Select(BibliotecaDto.FromEntity)];

		return Result.Success(dtos);
	}
}
