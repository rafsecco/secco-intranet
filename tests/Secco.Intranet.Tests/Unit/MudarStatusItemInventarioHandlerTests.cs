using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class MudarStatusItemInventarioHandlerTests
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

	private static (MudarStatusItemInventarioHandler Handler, RepositorioFalso Repositorio, TrilhaFalsa Trilha) Montar(
		ItemInventario? item)
	{
		var repositorio = new RepositorioFalso(item);
		var trilha = new TrilhaFalsa();

		return (new MudarStatusItemInventarioHandler(repositorio, trilha), repositorio, trilha);
	}

	[Fact]
	public async Task ItemInexistente_DevolveNotFound()
	{
		var (handler, _, _) = Montar(null);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(Guid.NewGuid(), AcaoDeStatus.Baixar));

		resultado.Error.Should().Be(IntranetErrors.Inventario.NotFound);
	}

	[Fact]
	public async Task ItemBaixado_QualquerAcao_DevolveErro()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		item.Baixar();
		var (handler, _, _) = Montar(item);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.EnviarParaManutencao));

		resultado.Error.Should().Be(IntranetErrors.Inventario.ItemBaixado);
	}

	[Fact]
	public async Task Atribuir_SemUsuario_DevolveErro()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var (handler, _, _) = Montar(item);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.Atribuir));

		resultado.Error.Should().Be(IntranetErrors.Inventario.UsuarioRequired);
	}

	[Fact]
	public async Task Atribuir_ComUsuario_MudaStatusEGravaEAudita()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var (handler, repositorio, trilha) = Montar(item);
		var usuarioId = Guid.NewGuid();

		var resultado = await handler.HandleAsync(
			new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.Atribuir, usuarioId, "ana@exemplo.local"));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Status.Should().Be(StatusDoItem.EmUso);
		repositorio.Gravou.Should().BeTrue();
		trilha.Ultimo!.Verbo.Should().Be(VerbosDeAuditoria.InventarioAtribuir);
	}

	[Fact]
	public async Task Desatribuir_APartirDeDisponivel_DevolveTransicaoInvalida()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var (handler, _, _) = Montar(item);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.Desatribuir));

		resultado.Error.Should().Be(IntranetErrors.Inventario.TransicaoInvalida);
	}

	[Fact]
	public async Task Baixar_GravaEAuditaComVerboProprio()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var (handler, repositorio, trilha) = Montar(item);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.Baixar));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Status.Should().Be(StatusDoItem.Baixado);
		repositorio.Gravou.Should().BeTrue();
		trilha.Ultimo!.Verbo.Should().Be(VerbosDeAuditoria.InventarioBaixar);
	}

	[Fact]
	public async Task VoltarDaManutencao_SemEstarEmManutencao_DevolveTransicaoInvalida()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var (handler, _, _) = Montar(item);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.VoltarDaManutencao));

		resultado.Error.Should().Be(IntranetErrors.Inventario.TransicaoInvalida);
	}
}
