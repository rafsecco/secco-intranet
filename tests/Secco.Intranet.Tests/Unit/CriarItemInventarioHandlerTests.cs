using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class CriarItemInventarioHandlerTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public RegistroDeAuditoria? Ultimo { get; private set; }

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Ultimo = registro;

			return Task.CompletedTask;
		}
	}

	private sealed class RepositorioFalso : IItemInventarioRepository
	{
		public ItemInventario? Adicionado { get; private set; }

		public Task AddAsync(ItemInventario item, CancellationToken cancellationToken = default)
		{
			Adicionado = item;

			return Task.CompletedTask;
		}

		public Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<ItemInventario?>(null);

		public Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<ItemInventario?>(null);

		public Task<PagedResult<ItemInventario>> SearchAsync(
			ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<ItemInventario>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private static (CriarItemInventarioHandler Handler, RepositorioFalso Repositorio, TrilhaFalsa Trilha) Montar()
	{
		var repositorio = new RepositorioFalso();
		var trilha = new TrilhaFalsa();

		return (new CriarItemInventarioHandler(repositorio, new IntranetOptions(), trilha), repositorio, trilha);
	}

	[Fact]
	public async Task NomeVazio_DevolveErro()
	{
		var (handler, _, _) = Montar();

		var resultado = await handler.HandleAsync(new CriarItemInventarioCommand(" ", null, null, null, null));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Inventario.NomeRequired);
	}

	[Fact]
	public async Task NomeAcimaDoLimite_DevolveErro()
	{
		var (handler, _, _) = Montar();
		var nome = new string('a', 300);

		var resultado = await handler.HandleAsync(new CriarItemInventarioCommand(nome, null, null, null, null));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Code.Should().Be(IntranetErrors.Inventario.NomeTooLong(256).Code);
	}

	[Fact]
	public async Task Valido_GravaERegistraAuditoria()
	{
		var (handler, repositorio, trilha) = Montar();

		var resultado = await handler.HandleAsync(
			new CriarItemInventarioCommand("Notebook Dell", "Descrição", "Equipamento", "PAT-001", null));

		resultado.IsSuccess.Should().BeTrue();
		repositorio.Adicionado.Should().NotBeNull();
		repositorio.Adicionado!.Nome.Should().Be("Notebook Dell");
		trilha.Ultimo!.Verbo.Should().Be(VerbosDeAuditoria.InventarioCriar);
		trilha.Ultimo.Recurso.Should().Be(RecursosDeAuditoria.Inventario);
	}
}
