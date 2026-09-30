using AwesomeAssertions;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ReconciliarItensDeMenuHandlerTests
{
	private sealed class FakeSetorRepository : ISetorRepository
	{
		private readonly List<Setor> _setores = [];

		public FakeSetorRepository Com(string slug)
		{
			_setores.Add(new Setor(slug, slug));

			return this;
		}

		public Task<PagedResult<Setor>> SearchAsync(SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(new PagedResult<Setor>(_setores, PageRequest.FirstPage, Math.Max(_setores.Count, 1), _setores.Count));

		public Task AddAsync(Setor setor, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

		public IReadOnlyList<Setor> Todos => _setores;
	}

	[Fact]
	public async Task SetorSemNenhumItem_GanhaRaizEOsDoisFilhos()
	{
		var setores = new FakeSetorRepository().Com("financeiro");
		var itens = new ItemMenuRepositorioFalso();
		var handler = new ReconciliarItensDeMenuHandler(new SearchSetoresHandler(setores), itens);

		var quantos = await handler.HandleAsync();

		quantos.Should().Be(1);
		var setorId = setores.Todos.Single().Id;
		itens.Itens.Should().ContainSingle(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Setor);
		itens.Itens.Should().ContainSingle(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Documentos);
		itens.Itens.Should().ContainSingle(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Avisos);
	}

	[Fact]
	public async Task SetorComDocumentosMasSemAvisos_SoCompletaOQueFalta()
	{
		var setores = new FakeSetorRepository().Com("ti");
		var setorId = setores.Todos.Single().Id;
		var itens = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(setorId, null, "ti", "ti", TipoDeItemMenu.Setor, null, null, 0);
		itens.Itens.Add(raiz);
		itens.Itens.Add(new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0));
		var handler = new ReconciliarItensDeMenuHandler(new SearchSetoresHandler(setores), itens);

		await handler.HandleAsync();

		itens.Itens.Where(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Setor).Should().ContainSingle();
		itens.Itens.Where(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Documentos).Should().ContainSingle();
		itens.Itens.Where(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Avisos).Should().ContainSingle();
	}

	[Fact]
	public async Task RodarDuasVezes_NaoDuplica()
	{
		var setores = new FakeSetorRepository().Com("rh");
		var itens = new ItemMenuRepositorioFalso();
		var handler = new ReconciliarItensDeMenuHandler(new SearchSetoresHandler(setores), itens);

		await handler.HandleAsync();
		await handler.HandleAsync();

		var setorId = setores.Todos.Single().Id;
		itens.Itens.Count(i => i.SetorId == setorId).Should().Be(3);
	}

	[Fact]
	public async Task PersonalizadoJaUsandoOSlugDocumentos_ReconciliaComSufixo()
	{
		var setores = new FakeSetorRepository().Com("rh-colisao");
		var setorId = setores.Todos.Single().Id;
		var itens = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(setorId, null, "rh", "rh", TipoDeItemMenu.Setor, null, null, 0);
		itens.Itens.Add(raiz);
		itens.Itens.Add(new ItemMenu(setorId, raiz.Id, "Documentos antigos", "documentos", TipoDeItemMenu.Personalizado, "/x", null, 0));
		var handler = new ReconciliarItensDeMenuHandler(new SearchSetoresHandler(setores), itens);

		await handler.HandleAsync();

		itens.Itens.Single(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Documentos).Slug.Should().Be("documentos-2");
	}
}
