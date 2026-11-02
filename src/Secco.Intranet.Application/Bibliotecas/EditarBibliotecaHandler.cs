using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Bibliotecas;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Bibliotecas;

/// <summary>Pedido de edição de biblioteca.</summary>
/// <param name="Id">Identificador da biblioteca.</param>
/// <param name="Nome">Novo nome de exibição.</param>
/// <param name="Icone">Nova classe do Bootstrap Icons; vazio volta ao ícone padrão.</param>
/// <param name="Ativo">Situação desejada.</param>
/// <remarks>
/// O slug não entra: ele é a base das permissões <c>biblioteca-{slug}:read/write</c> e da URL.
/// Trocá-lo deixaria as permissões já atribuídas apontando para nada.
/// </remarks>
public sealed record EditarBibliotecaCommand(Guid Id, string? Nome, string? Icone, bool Ativo);

/// <summary>
/// Altera nome, ícone e situação de uma biblioteca existente. Toda invariante que o domínio
/// recusa com exceção é checada antes (ADR-0004): a exceção fica como rede de segurança para
/// chamador interno, e o usuário recebe <see cref="Result{T}"/>.
/// </summary>
/// <param name="repository">Persistência de bibliotecas.</param>
/// <param name="options">Limites de entrada do produto.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarBibliotecaHandler(
	IBibliotecaRepository repository, IntranetOptions options, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<BibliotecaDto>> HandleAsync(
		EditarBibliotecaCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Nome))
		{
			return IntranetErrors.Bibliotecas.NomeRequired;
		}

		if (command.Nome.Length > options.MaxNameLength)
		{
			return IntranetErrors.Bibliotecas.NomeTooLong(options.MaxNameLength);
		}

		if (!Biblioteca.IconeEhValido(command.Icone))
		{
			return IntranetErrors.Bibliotecas.IconeInvalido;
		}

		var biblioteca = await repository.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (biblioteca is null)
		{
			return IntranetErrors.Bibliotecas.NotFound;
		}

		var estavaAtiva = biblioteca.Ativo;

		biblioteca.Renomear(command.Nome);
		biblioteca.DefinirIcone(command.Icone);

		if (command.Ativo)
		{
			biblioteca.Ativar();
		}
		else
		{
			biblioteca.Desativar();
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		// Desativar e reativar ganham verbo próprio porque mudam quem enxerga o quê; um
		// "biblioteca.editar" genérico esconderia exatamente a mudança que alguém vai procurar.
		var verbo = (estavaAtiva, biblioteca.Ativo) switch
		{
			(true, false) => VerbosDeAuditoria.BibliotecaDesativar,
			(false, true) => VerbosDeAuditoria.BibliotecaReativar,
			_ => VerbosDeAuditoria.BibliotecaEditar,
		};

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					verbo,
					RecursosDeAuditoria.Biblioteca,
					biblioteca.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						nome = biblioteca.Nome,
						slug = biblioteca.Slug,
						icone = biblioteca.Icone,
						ativo = biblioteca.Ativo,
					})),
				cancellationToken)
			.ConfigureAwait(false);

		return BibliotecaDto.FromEntity(biblioteca);
	}
}
