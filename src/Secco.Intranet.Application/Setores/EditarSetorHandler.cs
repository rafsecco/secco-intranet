using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Setores;

/// <summary>Pedido de edição de setor.</summary>
/// <param name="Id">Identificador do setor.</param>
/// <param name="Nome">Novo nome de exibição.</param>
/// <param name="Icone">Nova classe do Bootstrap Icons; vazio volta ao ícone padrão.</param>
/// <param name="Ativo">Situação desejada.</param>
/// <remarks>
/// O slug não entra: ele é a base das Roles <c>{slug}-admin</c>/<c>{slug}-user</c> no
/// SecureGate (ADR-0001). Trocá-lo romperia o vínculo de todos os usuários do setor, e as
/// Roles antigas ficariam órfãs sem que nada aqui percebesse.
/// </remarks>
public sealed record EditarSetorCommand(Guid Id, string? Nome, string? Icone, bool Ativo);

/// <summary>
/// Altera nome, ícone e situação de um setor existente. Toda invariante que o domínio recusa
/// com exceção é checada antes (ADR-0004): a exceção fica como rede de segurança para
/// chamador interno, e o usuário recebe <see cref="Result{T}"/>.
/// </summary>
/// <param name="repository">Persistência de setores.</param>
/// <param name="options">Limites de entrada do produto.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarSetorHandler(ISetorRepository repository, IntranetOptions options, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<SetorDto>> HandleAsync(
		EditarSetorCommand command,
		CancellationToken cancellationToken = default)
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

		if (!Setor.IconeEhValido(command.Icone))
		{
			return IntranetErrors.Setores.IconeInvalido;
		}

		var setor = await repository.GetParaEdicaoAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (setor is null)
		{
			return IntranetErrors.Setores.NotFound;
		}

		// Checado ANTES de qualquer alteração: um setor fixo continua editável no resto, e
		// gravar o nome novo junto de uma recusa deixaria a tela mentindo sobre o que salvou.
		if (!command.Ativo && setor.Fixo)
		{
			return IntranetErrors.Setores.FixoNaoDesativa;
		}

		var estavaAtivo = setor.Ativo;

		setor.Renomear(command.Nome);
		setor.DefinirIcone(command.Icone);

		if (command.Ativo)
		{
			setor.Ativar();
		}
		else
		{
			setor.Desativar();
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		// Desativar e reativar ganham verbo proprio porque mudam quem enxerga o que; um
		// "setor.editar" generico esconderia exatamente a mudanca que alguem vai procurar.
		var verbo = (estavaAtivo, setor.Ativo) switch
		{
			(true, false) => VerbosDeAuditoria.SetorDesativar,
			(false, true) => VerbosDeAuditoria.SetorReativar,
			_ => VerbosDeAuditoria.SetorEditar,
		};

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					verbo,
					RecursosDeAuditoria.Setor,
					setor.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						nome = setor.Nome,
						slug = setor.Slug,
						icone = setor.Icone,
						ativo = setor.Ativo,
					})),
				cancellationToken)
			.ConfigureAwait(false);

		return SetorDto.FromEntity(setor);
	}
}
