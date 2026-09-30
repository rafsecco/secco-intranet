using AwesomeAssertions;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class MoverItemMenuHandlerTests
{
	private static (ItemMenuRepositorioFalso Repo, ItemMenu A, ItemMenu B, ItemMenu C) CenarioComTresIrmaos()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var a = new ItemMenu(raiz.SetorId, raiz.Id, "A", "a", TipoDeItemMenu.Personalizado, null, null, 0);
		var b = new ItemMenu(raiz.SetorId, raiz.Id, "B", "b", TipoDeItemMenu.Personalizado, null, null, 1);
		var c = new ItemMenu(raiz.SetorId, raiz.Id, "C", "c", TipoDeItemMenu.Personalizado, null, null, 2);
		repo.Itens.Add(raiz);
		repo.Itens.Add(a);
		repo.Itens.Add(b);
		repo.Itens.Add(c);

		return (repo, a, b, c);
	}

	[Fact]
	public async Task MoverParaCima_TrocaComOAnterior()
	{
		var (repo, a, b, _) = CenarioComTresIrmaos();
		var handler = new MoverItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(b.Id, paraCima: true);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Single(i => i.Id == b.Id).Ordem.Should().Be(0);
		repo.Itens.Single(i => i.Id == a.Id).Ordem.Should().Be(1);
	}

	[Fact]
	public async Task MoverParaBaixo_TrocaComOProximo()
	{
		var (repo, _, b, c) = CenarioComTresIrmaos();
		var handler = new MoverItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(b.Id, paraCima: false);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Single(i => i.Id == b.Id).Ordem.Should().Be(2);
		repo.Itens.Single(i => i.Id == c.Id).Ordem.Should().Be(1);
	}

	[Fact]
	public async Task MoverOPrimeiroParaCima_NaoFazNada()
	{
		var (repo, a, _, _) = CenarioComTresIrmaos();
		var handler = new MoverItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(a.Id, paraCima: true);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Single(i => i.Id == a.Id).Ordem.Should().Be(0);
	}

	[Fact]
	public async Task MoverOUltimoParaBaixo_NaoFazNada()
	{
		var (repo, _, _, c) = CenarioComTresIrmaos();
		var handler = new MoverItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(c.Id, paraCima: false);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Single(i => i.Id == c.Id).Ordem.Should().Be(2);
	}

	[Fact]
	public async Task OrdensComBuraco_MoverOUltimoParaCima_MantemAOrdemDosOutros()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var a = new ItemMenu(raiz.SetorId, raiz.Id, "A", "a", TipoDeItemMenu.Personalizado, null, null, 0);
		var b = new ItemMenu(raiz.SetorId, raiz.Id, "B", "b", TipoDeItemMenu.Personalizado, null, null, 5);
		var c = new ItemMenu(raiz.SetorId, raiz.Id, "C", "c", TipoDeItemMenu.Personalizado, null, null, 6);
		var d = new ItemMenu(raiz.SetorId, raiz.Id, "D", "d", TipoDeItemMenu.Personalizado, null, null, 7);
		repo.Itens.AddRange([raiz, a, b, c, d]);

		await new MoverItemMenuHandler(repo).HandleAsync(d.Id, paraCima: true);

		repo.Itens.Where(i => i.ParentId == raiz.Id).OrderBy(i => i.Ordem).Select(i => i.Nome)
			.Should().Equal("A", "B", "D", "C");
	}
}
