using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class EditarItemInventarioHandlerTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class RepositorioFalso(ItemInventario? item) : IItemInventarioRepository
	{
		public bool Gravou { get; private set; }

		public Task AddAsync(ItemInventario novo, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(item);

		public Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(item);

		public Task<PagedResult<ItemInventario>> SearchAsync(
			ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<ItemInventario>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			Gravou = true;

			return Task.CompletedTask;
		}
	}

	private static EditarItemInventarioHandler Handler(RepositorioFalso repositorio) =>
		new(repositorio, new IntranetOptions(), new TrilhaFalsa());

	[Fact]
	public async Task ItemInexistente_DevolveNotFound()
	{
		var handler = Handler(new RepositorioFalso(null));

		var resultado = await handler.HandleAsync(new EditarItemInventarioCommand(Guid.NewGuid(), "X", null, null, null, null));

		resultado.Error.Should().Be(IntranetErrors.Inventario.NotFound);
	}

	[Fact]
	public async Task ItemBaixado_DevolveErro()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		item.Baixar();
		var handler = Handler(new RepositorioFalso(item));

		var resultado = await handler.HandleAsync(
			new EditarItemInventarioCommand(item.Id, "Novo nome", null, null, null, null));

		resultado.Error.Should().Be(IntranetErrors.Inventario.ItemBaixado);
	}

	[Fact]
	public async Task Valido_AlteraEGrava()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var repositorio = new RepositorioFalso(item);
		var handler = Handler(repositorio);

		var resultado = await handler.HandleAsync(
			new EditarItemInventarioCommand(item.Id, "Notebook Dell", "Descrição nova", "TI", "PAT-002", null));

		resultado.IsSuccess.Should().BeTrue();
		repositorio.Gravou.Should().BeTrue();
		item.Nome.Should().Be("Notebook Dell");
		item.CodigoPatrimonio.Should().Be("PAT-002");
	}
}
