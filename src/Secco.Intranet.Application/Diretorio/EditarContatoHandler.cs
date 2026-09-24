using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Pedido de edição do contato de uma pessoa: só nome, ramal e "sobre".</summary>
/// <param name="UsuarioId">Pessoa cujo contato muda.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Ramal">Ramal.</param>
/// <param name="Sobre">Texto livre.</param>
public sealed record EditarContatoCommand(Guid UsuarioId, string? Nome, string? Ramal, string? Sobre);

/// <summary>
/// Edita o contato de uma pessoa (o próprio colaborador ou um admin — quem pode é decisão do
/// controller). Não conhece cargo, setor nem gestor: é por isso que o formulário do colaborador
/// não consegue alterá-los, mesmo forjado.
/// </summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarContatoHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(EditarContatoCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (Tamanho(command.Nome) > PerfilColaborador.NomeMaxLength)
		{
			return Result.Failure(IntranetErrors.Diretorio.NomeTooLong(PerfilColaborador.NomeMaxLength));
		}

		if (Tamanho(command.Ramal) > PerfilColaborador.RamalMaxLength)
		{
			return Result.Failure(IntranetErrors.Diretorio.RamalTooLong(PerfilColaborador.RamalMaxLength));
		}

		if (Tamanho(command.Sobre) > PerfilColaborador.SobreMaxLength)
		{
			return Result.Failure(IntranetErrors.Diretorio.SobreTooLong(PerfilColaborador.SobreMaxLength));
		}

		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure(ativos.Error);
		}

		if (!ativos.Value.Any(usuario => usuario.Id == command.UsuarioId))
		{
			return Result.Failure(IntranetErrors.Diretorio.PessoaNaoEncontrada);
		}

		var campos = await EdicaoDePerfil
			.AplicarAsync(perfis, command.UsuarioId, perfil => perfil.EditarContato(command.Nome, command.Ramal, command.Sobre), cancellationToken)
			.ConfigureAwait(false);

		if (campos.Count > 0)
		{
			await trilha.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DiretorioPerfilEditar,
					RecursosDeAuditoria.Diretorio,
					command.UsuarioId.ToString(),
					JsonSerializer.Serialize(new { usuarioId = command.UsuarioId, campos })),
				cancellationToken).ConfigureAwait(false);
		}

		return Result.Success();
	}

	private static int Tamanho(string? valor) => valor?.Trim().Length ?? 0;
}
