using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Dados de adoção.</summary>
/// <param name="TenantId">Tenant escolhido (vem do formulário — tratado como não confiável).</param>
/// <param name="Sistema">Sistema que o tenant representa.</param>
/// <param name="Responsavel">Responsável.</param>
public sealed record AdotarTenantCommand(Guid TenantId, string? Sistema, string? Responsavel);

/// <summary>
/// Traz para o cadastro um tenant criado por fora. As exclusões valem aqui, e não só na lista da
/// tela: um POST com Guid forjado também é recusado.
/// </summary>
/// <param name="gestao">API de tenants.</param>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="protegidos">Tenants que nunca são administrados.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="ator">Quem age.</param>
public sealed class AdotarTenantHandler(
	IGestaoDeTenants gestao, ITenantsAdministrados cadastro, ITenantsProtegidos protegidos, ITrilhaDeAuditoria trilha, IAtorAtual ator)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Dados de adoção.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Sucesso ou o motivo da recusa.</returns>
	public async Task<Result> HandleAsync(AdotarTenantCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (RegrasDeTenant.ValidarRegistro(command.Sistema, command.Responsavel) is { } invalido)
		{
			return Result.Failure(invalido);
		}

		if (command.TenantId == Guid.Empty
			|| (await protegidos.ListarAsync(cancellationToken).ConfigureAwait(false)).Contains(command.TenantId))
		{
			return Result.Failure(IntranetErrors.Tenants.NaoAdotavel);
		}

		if (await cadastro.ObterAsync(command.TenantId, cancellationToken).ConfigureAwait(false) is not null)
		{
			return Result.Failure(IntranetErrors.Tenants.JaAdministrado);
		}

		var tenant = await gestao.ObterTenantAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

		if (tenant.IsFailure)
		{
			return Result.Failure(tenant.Error);
		}

		var sistema = command.Sistema!.Trim();
		var registro = new TenantAdministrado(
			command.TenantId, sistema, command.Responsavel!.Trim(), OrigemDoTenant.Adotado, RegrasDeTenant.RotuloDoAtor(ator));

		if (!await cadastro.TentarAdicionarAsync(registro, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(IntranetErrors.Tenants.JaAdministrado);
		}

		await AuditoriaDeTenants.TenantAsync(trilha, VerbosDeAuditoria.TenantAdotar, command.TenantId, sistema, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
