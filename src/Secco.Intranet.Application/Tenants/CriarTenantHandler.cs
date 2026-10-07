using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Dados de criação.</summary>
/// <param name="Sistema">Sistema que o tenant representa.</param>
/// <param name="Responsavel">Responsável.</param>
/// <param name="Nome">Nome do tenant na plataforma.</param>
/// <param name="Slug">Slug do tenant na plataforma.</param>
public sealed record CriarTenantCommand(string? Sistema, string? Responsavel, string? Nome, string? Slug);

/// <summary>
/// Cria o tenant no SecureGate e o registra no cadastro. Não há transação distribuída: se a
/// gravação local falhar depois do sucesso remoto, o erro diz o slug e manda adotar — falha
/// visível e recuperável pelo próprio fluxo.
/// </summary>
/// <param name="gestao">API de tenants.</param>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="ator">Quem age.</param>
public sealed class CriarTenantHandler(IGestaoDeTenants gestao, ITenantsAdministrados cadastro, ITrilhaDeAuditoria trilha, IAtorAtual ator)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Dados de criação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>O id do tenant criado.</returns>
	public async Task<Result<Guid>> HandleAsync(CriarTenantCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (RegrasDeTenant.ValidarCriacao(command.Sistema, command.Responsavel, command.Nome, command.Slug) is { } invalido)
		{
			return Result.Failure<Guid>(invalido);
		}

		var sistema = command.Sistema!.Trim();
		var slug = command.Slug!.Trim();
		var criado = await gestao.CriarTenantAsync(command.Nome!.Trim(), slug, cancellationToken).ConfigureAwait(false);

		if (criado.IsFailure)
		{
			return Result.Failure<Guid>(criado.Error);
		}

		// A partir daqui o tenant existe na plataforma: a criação é auditada aconteça o que
		// acontecer com o cadastro local.
		await AuditoriaDeTenants.TenantAsync(trilha, VerbosDeAuditoria.TenantCriar, criado.Value.Id, sistema, cancellationToken)
			.ConfigureAwait(false);

		var registro = new TenantAdministrado(
			criado.Value.Id, sistema, command.Responsavel!.Trim(), OrigemDoTenant.Criado, RegrasDeTenant.RotuloDoAtor(ator));

		bool registrado;

		try
		{
			registrado = await cadastro.TentarAdicionarAsync(registro, cancellationToken).ConfigureAwait(false);
		}
#pragma warning disable CA1031 // Deliberado e estreito: o único passo do try é a gravação local; a mensagem de recuperação vale para qualquer causa (banco fora, timeout).
		catch (Exception excecao) when (excecao is not OperationCanceledException)
#pragma warning restore CA1031
		{
			return Result.Failure<Guid>(IntranetErrors.Tenants.RegistroLocalFalhou(slug));
		}

		return registrado
			? Result.Success(criado.Value.Id)
			: Result.Failure<Guid>(IntranetErrors.Tenants.RegistroLocalFalhou(slug));
	}
}
