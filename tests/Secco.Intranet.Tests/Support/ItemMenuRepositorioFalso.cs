using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Tests.Support;

/// <summary>Repositório de <see cref="ItemMenu"/> em memória, para os testes de handler.</summary>
public sealed class ItemMenuRepositorioFalso : IItemMenuRepository
{
	/// <summary>Itens existentes — os testes montam o cenário mexendo aqui direto.</summary>
	public List<ItemMenu> Itens { get; } = [];

	public Task AddAsync(ItemMenu item, CancellationToken cancellationToken = default)
	{
		Itens.Add(item);

		return Task.CompletedTask;
	}

	public Task<ItemMenu?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		Task.FromResult(Itens.FirstOrDefault(item => item.Id == id));

	public Task<ItemMenu?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
		Task.FromResult(Itens.FirstOrDefault(item => item.Id == id));

	public Task<IReadOnlyList<ItemMenu>> ListarPorSetorAsync(Guid setorId, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<ItemMenu>>([.. Itens.Where(item => item.SetorId == setorId)]);

	public Task<bool> ExisteTipoAsync(Guid setorId, TipoDeItemMenu tipo, CancellationToken cancellationToken = default) =>
		Task.FromResult(Itens.Any(item => item.SetorId == setorId && item.Tipo == tipo));

	public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task ExcluirAsync(Guid id, CancellationToken cancellationToken = default)
	{
		Itens.RemoveAll(item => item.Id == id);

		return Task.CompletedTask;
	}
}
