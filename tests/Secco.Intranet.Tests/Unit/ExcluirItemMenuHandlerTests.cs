using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ExcluirItemMenuHandlerTests
{
	[Fact]
	public async Task Personalizado_Exclui()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var item = new ItemMenu(raiz.SetorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(item);
		var handler = new ExcluirItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(item.Id);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Should().NotContain(i => i.Id == item.Id);
	}

	// Com filhos, a FK de ParentId (Restrict) recusaria a exclusão no banco — virava 500. O
	// handler precisa recusar antes, com mensagem.
	[Fact]
	public async Task PersonalizadoComFilhos_Recusa_SemExcluirNada()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var relatorios = new ItemMenu(raiz.SetorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		var vendas = new ItemMenu(raiz.SetorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/x", null, 0);
		repo.Itens.AddRange([raiz, relatorios, vendas]);
		var handler = new ExcluirItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(relatorios.Id);

		resultado.Error.Should().Be(IntranetErrors.Menu.ItemComFilhos);
		repo.Itens.Should().HaveCount(3);
	}

	[Fact]
	public async Task Documentos_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var doc = new ItemMenu(raiz.SetorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(doc);
		var handler = new ExcluirItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(doc.Id);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.TipoEmbutidoNaoExclui);
		repo.Itens.Should().ContainSingle(i => i.Id == doc.Id);
	}

	[Fact]
	public async Task Raiz_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		repo.Itens.Add(raiz);
		var handler = new ExcluirItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(raiz.Id);

		resultado.Error.Should().Be(IntranetErrors.Menu.RaizProtegida);
	}
}
