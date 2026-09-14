using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Ação de transição de status pedida.</summary>
public enum AcaoDeStatus
{
	/// <summary>Atribui a um usuário (exige <see cref="MudarStatusItemInventarioCommand.UsuarioId"/>).</summary>
	Atribuir,

	/// <summary>Libera o item atribuído.</summary>
	Desatribuir,

	/// <summary>Envia para manutenção.</summary>
	EnviarParaManutencao,

	/// <summary>Volta da manutenção.</summary>
	VoltarDaManutencao,

	/// <summary>Dá baixa — terminal.</summary>
	Baixar,
}

/// <summary>Pedido de transição de status de um item de inventário.</summary>
/// <param name="Id">Identificador do item.</param>
/// <param name="Acao">Transição pedida.</param>
/// <param name="UsuarioId">Obrigatório quando <paramref name="Acao"/> é <see cref="AcaoDeStatus.Atribuir"/>.</param>
/// <param name="UsuarioEmail">E-mail em cache do usuário — o SecureGate não guarda nome de exibição.</param>
public sealed record MudarStatusItemInventarioCommand(
	Guid Id, AcaoDeStatus Acao, Guid? UsuarioId = null, string? UsuarioEmail = null);

/// <summary>
/// Executa uma das cinco transições de <see cref="ItemInventario"/>. Toda invariante que o
/// domínio recusaria com exceção é checada antes (ADR-0004): a exceção do domínio é rede de
/// segurança para chamador interno, não o caminho normal.
/// </summary>
public sealed class MudarStatusItemInventarioHandler(IItemInventarioRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ItemInventarioDto>> HandleAsync(
		MudarStatusItemInventarioCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var item = await repository.GetParaEdicaoAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return IntranetErrors.Inventario.NotFound;
		}

		if (item.Status == StatusDoItem.Baixado)
		{
			return IntranetErrors.Inventario.ItemBaixado;
		}

		switch (command.Acao)
		{
			case AcaoDeStatus.Atribuir:
				if (command.UsuarioId is null || command.UsuarioId == Guid.Empty)
				{
					return IntranetErrors.Inventario.UsuarioRequired;
				}

				if (item.Status is not (StatusDoItem.Disponivel or StatusDoItem.EmUso))
				{
					return IntranetErrors.Inventario.TransicaoInvalida;
				}

				item.Atribuir(command.UsuarioId.Value, command.UsuarioEmail);
				break;

			case AcaoDeStatus.Desatribuir:
				if (item.Status != StatusDoItem.EmUso)
				{
					return IntranetErrors.Inventario.TransicaoInvalida;
				}

				item.Desatribuir();
				break;

			case AcaoDeStatus.EnviarParaManutencao:
				item.EnviarParaManutencao();
				break;

			case AcaoDeStatus.VoltarDaManutencao:
				if (item.Status != StatusDoItem.EmManutencao)
				{
					return IntranetErrors.Inventario.TransicaoInvalida;
				}

				item.VoltarDaManutencao();
				break;

			case AcaoDeStatus.Baixar:
				item.Baixar();
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(command), command.Acao, "Ação de status desconhecida.");
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		var verbo = command.Acao switch
		{
			AcaoDeStatus.Atribuir => VerbosDeAuditoria.InventarioAtribuir,
			AcaoDeStatus.Baixar => VerbosDeAuditoria.InventarioBaixar,
			_ => VerbosDeAuditoria.InventarioEditar,
		};

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					verbo,
					RecursosDeAuditoria.Inventario,
					item.Id.ToString(),
					JsonSerializer.Serialize(new { acao = command.Acao.ToString(), status = item.Status.ToString() })),
				cancellationToken)
			.ConfigureAwait(false);

		return ItemInventarioDto.FromEntity(item);
	}
}
