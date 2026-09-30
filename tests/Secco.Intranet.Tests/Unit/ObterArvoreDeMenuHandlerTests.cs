using AwesomeAssertions;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ObterArvoreDeMenuHandlerTests
{
	[Fact]
	public async Task MontaAArvoreAninhada_ComOsFilhosDosFilhos()
	{
		var repo = new ItemMenuRepositorioFalso();
		var setorId = Guid.NewGuid();
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var relatorios = new ItemMenu(setorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		var vendas = new ItemMenu(setorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/relatorios/vendas", null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(relatorios);
		repo.Itens.Add(vendas);
		var handler = new ObterArvoreDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Item.Tipo.Should().Be(TipoDeItemMenu.Setor);
		resultado.Value.Filhos.Should().ContainSingle(f => f.Item.Slug == "relatorios");
		resultado.Value.Filhos.Single().Filhos.Should().ContainSingle(f => f.Item.Slug == "vendas");
	}

	[Fact]
	public async Task SetorSemArvore_Falha()
	{
		var repo = new ItemMenuRepositorioFalso();
		var handler = new ObterArvoreDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(Guid.NewGuid());

		resultado.IsFailure.Should().BeTrue();
	}
}
