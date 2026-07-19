using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Setores;

/// <summary>Comando de criação de um setor.</summary>
/// <param name="Nome">Nome de exibição. Obrigatório.</param>
/// <param name="Slug">Identificador curto. Obrigatório, único por tenant.</param>
/// <param name="Fixo">Se o setor nasce como fixo do sistema. Default <c>false</c>.</param>
public sealed record CreateSetorCommand(string? Nome, string? Slug, bool Fixo = false);

/// <summary>
/// Caso de uso: valida unicidade do slug (ADR-0020), cria a entidade e persiste — erros
/// de negócio fluem por <see cref="Result{T}"/>, nunca por exceção (ADR-0004). A criação
/// das Roles <c>{slug}-admin</c>/<c>{slug}-user</c> no SecureGate acontece após o commit,
/// orquestrada por quem chama este handler (ver ADR de integração SecureGate pendente).
/// </summary>
public sealed class CreateSetorHandler(ISetorRepository repository, IntranetOptions options)
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

		var setor = new Setor(command.Nome, command.Slug, command.Fixo);

		await repository.AddAsync(setor, cancellationToken).ConfigureAwait(false);

		return SetorDto.FromEntity(setor);
	}
}
