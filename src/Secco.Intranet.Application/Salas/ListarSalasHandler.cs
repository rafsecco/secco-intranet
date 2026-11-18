namespace Secco.Intranet.Application.Salas;

/// <summary>Lista as salas por ordem e nome.</summary>
public sealed class ListarSalasHandler(ISalaRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="somenteAtivas">Se verdadeiro, omite as salas desativadas.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlyList<SalaDto>> HandleAsync(bool somenteAtivas, CancellationToken cancellationToken = default)
	{
		var salas = await repository.ListarAsync(somenteAtivas, cancellationToken).ConfigureAwait(false);

		return [.. salas.Select(SalaDto.De)];
	}
}
