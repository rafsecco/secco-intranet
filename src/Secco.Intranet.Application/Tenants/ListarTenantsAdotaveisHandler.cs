using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Tenants da instalação que podem ser adotados: fora do cadastro e não protegidos.</summary>
/// <param name="gestao">API de tenants.</param>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="protegidos">Tenants que nunca são administrados.</param>
public sealed class ListarTenantsAdotaveisHandler(IGestaoDeTenants gestao, ITenantsAdministrados cadastro, ITenantsProtegidos protegidos)
{
	/// <summary>Executa a consulta.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<TenantDaPlataformaDto>>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var todos = await gestao.ListarTenantsAsync(cancellationToken).ConfigureAwait(false);

		if (todos.IsFailure)
		{
			return todos;
		}

		var bloqueados = await protegidos.ListarAsync(cancellationToken).ConfigureAwait(false);
		var administrados = (await cadastro.ListarAsync(cancellationToken).ConfigureAwait(false))
			.Select(r => r.TenantId)
			.ToHashSet();

		IReadOnlyList<TenantDaPlataformaDto> adotaveis =
			[.. todos.Value.Where(t => !bloqueados.Contains(t.Id) && !administrados.Contains(t.Id)).OrderBy(t => t.Nome)];

		return Result.Success(adotaveis);
	}
}
