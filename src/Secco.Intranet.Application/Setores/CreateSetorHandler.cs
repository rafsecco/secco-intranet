using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Setores;

/// <summary>Comando de criação de um setor.</summary>
/// <param name="Nome">Nome de exibição. Obrigatório.</param>
/// <param name="Slug">Identificador curto. Obrigatório, único por tenant.</param>
/// <param name="Fixo">Se o setor nasce como fixo do sistema. Default <c>false</c>.</param>
public sealed record CreateSetorCommand(string? Nome, string? Slug, bool Fixo = false);

/// <summary>
/// Caso de uso: valida unicidade do slug (ADR-0020), provisiona as Roles do setor no
/// SecureGate (ADR-0001) e só então persiste a entidade — erros de negócio fluem por
/// <see cref="Result{T}"/>, nunca por exceção (ADR-0004).
/// </summary>
public sealed class CreateSetorHandler(
	ISetorRepository repository,
	IntranetOptions options,
	ISetorAccessProvisioner accessProvisioner)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de criação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<SetorDto>> HandleAsync(CreateSetorCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Nome))
		{
			return IntranetErrors.Setores.NomeRequired;
		}

		if (command.Nome.Length > options.MaxNameLength)
		{
			return IntranetErrors.Setores.NomeTooLong(options.MaxNameLength);
		}

		if (string.IsNullOrWhiteSpace(command.Slug))
		{
			return IntranetErrors.Setores.SlugRequired;
		}

		if (await repository.ExistsBySlugAsync(command.Slug, cancellationToken).ConfigureAwait(false))
		{
			return IntranetErrors.Setores.SlugAlreadyExists(command.Slug);
		}

		// ADR-0001: as Roles do SecureGate são provisionadas ANTES de persistir o setor.
		// Roles órfãs no SecureGate (se algo falhar depois) são inofensivas e a operação é
		// idempotente — tentar de novo não duplica nada. O inverso — setor persistido sem as
		// Roles — quebraria o modelo de acesso da ADR-0001: não haveria como vincular um
		// usuário a esse setor.
		var provisioningResult = await accessProvisioner
			.EnsureSetorRolesAsync(command.Slug, cancellationToken)
			.ConfigureAwait(false);

		if (provisioningResult.IsFailure)
		{
			return provisioningResult.Error;
		}

		var setor = new Setor(command.Nome, command.Slug, command.Fixo);

		await repository.AddAsync(setor, cancellationToken).ConfigureAwait(false);

		return SetorDto.FromEntity(setor);
	}
}
