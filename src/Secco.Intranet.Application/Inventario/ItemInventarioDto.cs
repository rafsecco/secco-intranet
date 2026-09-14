using Secco.Intranet.Domain.Inventario;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Representação de leitura de um item de inventário — a entidade nunca cruza a borda HTTP.</summary>
public sealed record ItemInventarioDto(
	Guid Id,
	string Nome,
	string? Descricao,
	string? Categoria,
	string? CodigoPatrimonio,
	Guid? SetorId,
	StatusDoItem Status,
	Guid? AtribuidoAUsuarioId,
	string? AtribuidoANome,
	DateTimeOffset CreatedAt)
{
	/// <summary>Projeta a entidade para o DTO.</summary>
	public static ItemInventarioDto FromEntity(ItemInventario entity) => new(
		entity.Id,
		entity.Nome,
		entity.Descricao,
		entity.Categoria,
		entity.CodigoPatrimonio,
		entity.SetorId,
		entity.Status,
		entity.AtribuidoAUsuarioId,
		entity.AtribuidoANome,
		entity.CreatedAt);
}
