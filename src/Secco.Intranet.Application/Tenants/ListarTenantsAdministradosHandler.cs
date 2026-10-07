using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>
/// Lista do cadastro, com nome, slug, situação e recursos vindos da plataforma. Uma chamada por
/// tenant: a lista de uma empresa tem poucos sistemas, e o resumo da plataforma não traz produtos.
/// </summary>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="gestao">API de tenants.</param>
public sealed class ListarTenantsAdministradosHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao)
{
	/// <summary>Executa a consulta.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<TenantAdministradoResumoDto>>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var registros = await cadastro.ListarAsync(cancellationToken).ConfigureAwait(false);
		var linhas = new List<TenantAdministradoResumoDto>(registros.Count);

		foreach (var registro in registros)
		{
			var tenant = await gestao.ObterTenantAsync(registro.TenantId, cancellationToken).ConfigureAwait(false);

			if (tenant.IsFailure && tenant.Error != IntranetErrors.Tenants.NaoEncontrado)
			{
				return Result.Failure<IReadOnlyList<TenantAdministradoResumoDto>>(tenant.Error);
			}

			var recursos = new List<RecursoDaPlataforma>();

			if (registro.SecureGateHabilitado)
			{
				recursos.Add(RecursoDaPlataforma.SecureGate);
			}

			if (tenant.IsSuccess)
			{
				recursos.AddRange(RecursosDaPlataforma.Todos.Where(r =>
					RecursosDaPlataforma.Produto(r) is { } produto
					&& tenant.Value.Produtos.Contains(produto, StringComparer.OrdinalIgnoreCase)));
			}

			linhas.Add(new TenantAdministradoResumoDto(
				registro.TenantId,
				registro.Sistema,
				tenant.IsSuccess ? tenant.Value.Nome : "(não encontrado no SecureGate)",
				tenant.IsSuccess ? tenant.Value.Slug : string.Empty,
				tenant.IsSuccess && tenant.Value.Ativo,
				tenant.IsSuccess,
				registro.Origem,
				recursos));
		}

		return Result.Success<IReadOnlyList<TenantAdministradoResumoDto>>(linhas);
	}
}
