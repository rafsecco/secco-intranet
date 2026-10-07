using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Detalhe de um tenant do cadastro, com o painel dos três recursos.</summary>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="gestao">API de tenants.</param>
public sealed class ObterTenantAdministradoHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao)
{
	/// <summary>Executa a consulta. Tenant fora do cadastro é <c>NaoEncontrado</c> sem chamar a plataforma.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<TenantAdministradoDetalheDto>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var registro = await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (registro is null)
		{
			return Result.Failure<TenantAdministradoDetalheDto>(IntranetErrors.Tenants.NaoEncontrado);
		}

		var tenant = await gestao.ObterTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (tenant.IsFailure)
		{
			return Result.Failure<TenantAdministradoDetalheDto>(tenant.Error);
		}

		var status = await gestao.ObterStatusDosBancosAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (status.IsFailure)
		{
			return Result.Failure<TenantAdministradoDetalheDto>(status.Error);
		}

		var recursos = RecursosDaPlataforma.Todos
			.Select(recurso => Situacao(recurso, registro.SecureGateHabilitado, tenant.Value.Produtos, status.Value))
			.ToList();

		return Result.Success(new TenantAdministradoDetalheDto(
			registro.TenantId,
			registro.Sistema,
			registro.Responsavel,
			tenant.Value.Nome,
			tenant.Value.Slug,
			tenant.Value.Ativo,
			registro.Origem,
			registro.RegistradoPor,
			registro.CreatedAt,
			recursos));
	}

	private static RecursoDoTenantDto Situacao(
		RecursoDaPlataforma recurso, bool secureGateHabilitado, IReadOnlyList<string> produtos, IReadOnlyList<StatusDoBancoDto> status)
	{
		if (RecursosDaPlataforma.Produto(recurso) is not { } produto)
		{
			return new RecursoDoTenantDto(recurso, secureGateHabilitado ? SituacaoDoRecurso.Ligado : SituacaoDoRecurso.NaoLigado, null);
		}

		if (!produtos.Contains(produto, StringComparer.OrdinalIgnoreCase))
		{
			return new RecursoDoTenantDto(recurso, SituacaoDoRecurso.NaoLigado, null);
		}

		var banco = status.FirstOrDefault(s => string.Equals(s.Produto, produto, StringComparison.OrdinalIgnoreCase));

		return banco is { Responde: true }
			? new RecursoDoTenantDto(recurso, SituacaoDoRecurso.Ligado, null)
			: new RecursoDoTenantDto(recurso, SituacaoDoRecurso.LigadoSemResponder, banco?.Motivo);
	}
}
