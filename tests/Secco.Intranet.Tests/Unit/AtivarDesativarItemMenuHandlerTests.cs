using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class AtivarDesativarItemMenuHandlerTests
{
	[Fact]
	public async Task Desativar_ItemComum_Desativa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var doc = new ItemMenu(raiz.SetorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(doc);
		var handler = new AtivarDesativarItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(doc.Id, ativar: false);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Single(i => i.Id == doc.Id).Ativo.Should().BeFalse();
	}

	[Fact]
	public async Task Desativar_ARaiz_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		repo.Itens.Add(raiz);
		var handler = new AtivarDesativarItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(raiz.Id, ativar: false);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.RaizProtegida);
	}

	[Fact]
	public async Task ItemInexistente_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var handler = new AtivarDesativarItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(Guid.NewGuid(), ativar: true);

		resultado.Error.Should().Be(IntranetErrors.Menu.NotFound);
	}
}
