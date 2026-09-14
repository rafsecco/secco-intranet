using AwesomeAssertions;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class SearchItensInventarioHandlerTests
{
	private sealed class RepositorioFalso(PagedResult<ItemInventario> pagina) : IItemInventarioRepository
	{
		public Task AddAsync(ItemInventario item, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<ItemInventario?>(null);

		public Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<ItemInventario?>(null);

		public Task<PagedResult<ItemInventario>> SearchAsync(
			ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(pagina);

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	[Fact]
	public async Task DevolveAPaginaProjetadaComoDto()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var pagina = PagedResult.Create<ItemInventario>([item], new PageRequest(1), 1);
		var handler = new SearchItensInventarioHandler(new RepositorioFalso(pagina));

		var resultado = await handler.HandleAsync(new ItemInventarioSearchCriteria());

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Items.Should().ContainSingle(dto => dto.Nome == "Notebook");
	}
}
