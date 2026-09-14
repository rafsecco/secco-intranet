using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Pedido de edição dos campos descritivos de um item — status não entra aqui.</summary>
public sealed record EditarItemInventarioCommand(
	Guid Id, string? Nome, string? Descricao, string? Categoria, string? CodigoPatrimonio, Guid? SetorId);

/// <summary>
/// Altera nome, descrição, categoria, código de patrimônio e setor informativo. A invariante
/// "item Baixado não edita" é checada aqui, antes de chamar o domínio (ADR-0004) — a exceção
/// do domínio é rede de segurança, não o caminho normal.
/// </summary>
public sealed class EditarItemInventarioHandler(
	IItemInventarioRepository repository, IntranetOptions options, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ItemInventarioDto>> HandleAsync(
		EditarItemInventarioCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Nome))
		{
			return IntranetErrors.Inventario.NomeRequired;
		}

		if (command.Nome.Length > options.MaxNameLength)
		{
			return IntranetErrors.Inventario.NomeTooLong(options.MaxNameLength);
		}

		var item = await repository.GetParaEdicaoAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return IntranetErrors.Inventario.NotFound;
		}

		if (item.Status == StatusDoItem.Baixado)
		{
			return IntranetErrors.Inventario.ItemBaixado;
		}

		item.Editar(command.Nome, command.Descricao, command.Categoria, command.CodigoPatrimonio, command.SetorId);

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.InventarioEditar,
					RecursosDeAuditoria.Inventario,
					item.Id.ToString(),
					JsonSerializer.Serialize(new { nome = item.Nome, categoria = item.Categoria })),
				cancellationToken)
			.ConfigureAwait(false);

		return ItemInventarioDto.FromEntity(item);
	}
}
