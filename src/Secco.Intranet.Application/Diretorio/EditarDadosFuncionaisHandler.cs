using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Pedido de edição dos dados funcionais: cargo, setor de lotação e gestor. Valores nulos limpam o campo.</summary>
/// <param name="UsuarioId">Pessoa cujos dados mudam.</param>
/// <param name="Cargo">Cargo.</param>
/// <param name="SetorId">Setor de lotação.</param>
/// <param name="GestorUsuarioId">Id do gestor no SecureGate.</param>
public sealed record EditarDadosFuncionaisCommand(Guid UsuarioId, string? Cargo, Guid? SetorId, Guid? GestorUsuarioId);

/// <summary>
/// Edita cargo, setor e gestor. Só o admin do diretório chama isto (o controller garante). Aplica as
/// regras que dependem do conjunto: gestor ativo, sem autogestão e sem ciclo; setor existente e
/// ativo para lotação nova.
/// </summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarDadosFuncionaisHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(EditarDadosFuncionaisCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure(ativos.Error);
		}

		if (!ativos.Value.Any(usuario => usuario.Id == command.UsuarioId))
		{
			return Result.Failure(IntranetErrors.Diretorio.PessoaNaoEncontrada);
		}

		var todosOsSetores = await ListarPessoasHandler.CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);
		var pessoas = MontadorDePessoas.Montar(ativos.Value, todosOsPerfis, todosOsSetores);
		var editada = pessoas.First(candidata => candidata.UsuarioId == command.UsuarioId);
		var armazenado = todosOsPerfis.FirstOrDefault(perfil => perfil.UsuarioId == command.UsuarioId);

		// Campo do Entra é só leitura: o valor postado nunca vale, e o guardado fica intacto como reserva
		// para quando o Entra esvaziar o campo.
		var cargo = editada.CargoDoEntra ? armazenado?.Cargo : command.Cargo;
		var setor = editada.SetorDoEntra ? armazenado?.SetorId : (command.SetorId == Guid.Empty ? null : command.SetorId);
		var gestor = editada.GestorDoEntra ? armazenado?.GestorUsuarioId : (command.GestorUsuarioId == Guid.Empty ? null : command.GestorUsuarioId);

		if (!editada.CargoDoEntra && (command.Cargo?.Trim().Length ?? 0) > PerfilColaborador.CargoMaxLength)
		{
			return Result.Failure(IntranetErrors.Diretorio.CargoTooLong(PerfilColaborador.CargoMaxLength));
		}

		if (!editada.GestorDoEntra && gestor is { } gestorId)
		{
			var erroDeGestor = ValidarGestor(command.UsuarioId, gestorId, ativos.Value, pessoas);

			if (erroDeGestor is not null)
			{
				return Result.Failure(erroDeGestor);
			}
		}

		if (!editada.SetorDoEntra && setor is { } setorId)
		{
			var encontrado = await setores.GetByIdAsync(setorId, cancellationToken).ConfigureAwait(false);

			// Setor desativado depois da lotação: quem já estava lá pode ser editado sem trocar de setor.
			if (encontrado is null || (!encontrado.Ativo && armazenado?.SetorId != setorId))
			{
				return Result.Failure(IntranetErrors.Diretorio.SetorInvalido);
			}
		}

		var campos = await EdicaoDePerfil
			.AplicarAsync(perfis, command.UsuarioId, perfil => perfil.EditarDadosFuncionais(cargo, setor, gestor), cancellationToken)
			.ConfigureAwait(false);

		if (campos.Count > 0)
		{
			await trilha.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DiretorioDadosFuncionaisEditar,
					RecursosDeAuditoria.Diretorio,
					command.UsuarioId.ToString(),
					JsonSerializer.Serialize(new { usuarioId = command.UsuarioId, campos })),
				cancellationToken).ConfigureAwait(false);
		}

		return Result.Success();
	}

	private static Error? ValidarGestor(
		Guid usuarioId, Guid gestorId, IReadOnlyList<UsuarioParaDiretorio> ativos, IReadOnlyList<PessoaDto> pessoas)
	{
		if (gestorId == usuarioId)
		{
			return IntranetErrors.Diretorio.GestorEhOProprio;
		}

		if (!ativos.Any(usuario => usuario.Id == gestorId))
		{
			return IntranetErrors.Diretorio.GestorInvalido;
		}

		// Grafo do gestor efetivo (Entra ou manual); a edição candidata entra por cima em CriariaCiclo.
		var mapa = pessoas
			.Where(pessoa => pessoa.GestorUsuarioId is not null)
			.ToDictionary(pessoa => pessoa.UsuarioId, pessoa => pessoa.GestorUsuarioId!.Value);

		return RegrasDeGestor.CriariaCiclo(mapa, usuarioId, gestorId) ? IntranetErrors.Diretorio.GestorCriariaCiclo : null;
	}
}
