using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Comando de criação de um item de inventário.</summary>
public sealed record CriarItemInventarioCommand(
	string? Nome, string? Descricao, string? Categoria, string? CodigoPatrimonio, Guid? SetorId);

/// <summary>
/// Caso de uso: cria um item de inventário. Sem provisionamento de Role — diferente de
/// Setor, este recurso não pertence a ninguém em específico (ADR-0001 revisada).
/// </summary>
public sealed class CriarItemInventarioHandler(
	IItemInventarioRepository repository, IntranetOptions options, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ItemInventarioDto>> HandleAsync(
		CriarItemInventarioCommand command, CancellationToken cancellationToken = default)
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

		var item = new ItemInventario(command.Nome, command.Descricao, command.Categoria, command.CodigoPatrimonio, command.SetorId);

		await repository.AddAsync(item, cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.InventarioCriar,
					RecursosDeAuditoria.Inventario,
					item.Id.ToString(),
					JsonSerializer.Serialize(new { nome = item.Nome, categoria = item.Categoria, status = item.Status.ToString() })),
				cancellationToken)
			.ConfigureAwait(false);

		return ItemInventarioDto.FromEntity(item);
	}
}
