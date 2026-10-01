using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ResolverCaminhoDeMenuHandlerTests
{
	private static (ItemMenuRepositorioFalso Repo, Guid SetorId, ItemMenu Raiz, ItemMenu Avisos, ItemMenu Documentos) Cenario()
	{
		var repo = new ItemMenuRepositorioFalso();
		var setorId = Guid.NewGuid();
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var avisos = new ItemMenu(setorId, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 0);
		var documentos = new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 1);
		repo.Itens.Add(raiz);
		repo.Itens.Add(avisos);
		repo.Itens.Add(documentos);

		return (repo, setorId, raiz, avisos, documentos);
	}

	[Fact]
	public async Task CaminhoVazio_ResolveARaiz()
	{
		var (repo, setorId, raiz, _, _) = Cenario();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, []);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.No.Id.Should().Be(raiz.Id);
	}

	[Fact]
	public async Task CaminhoDeUmSegmento_ResolveOFilho()
	{
		var (repo, setorId, _, _, documentos) = Cenario();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, ["documentos"]);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.No.Id.Should().Be(documentos.Id);
		resultado.Value.CaminhoCompleto.Should().Equal("documentos");
	}

	[Fact]
	public async Task SegmentoSemMatch_Falha()
	{
		var (repo, setorId, _, _, _) = Cenario();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, ["nao-existe"]);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.NotFound);
	}

	[Fact]
	public async Task ItemDesativado_Falha()
	{
		var (repo, setorId, _, avisos, _) = Cenario();
		repo.Itens.Single(i => i.Id == avisos.Id).Desativar();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, ["avisos"]);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.NotFound);
	}

	[Fact]
	public async Task CaminhoDeDoisNiveis_ResolveONeto()
	{
		var repo = new ItemMenuRepositorioFalso();
		var setorId = Guid.NewGuid();
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var relatorios = new ItemMenu(setorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		var vendas = new ItemMenu(setorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/x", null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(relatorios);
		repo.Itens.Add(vendas);
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, ["relatorios", "vendas"]);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.No.Id.Should().Be(vendas.Id);
		resultado.Value.CaminhoCompleto.Should().Equal("relatorios", "vendas");
	}

	[Fact]
	public async Task CaminhoDoTipo_DocumentosNoNivel1_DevolveUmSegmento()
	{
		var (repo, setorId, _, _, _) = Cenario();

		var caminho = await new ResolverCaminhoDeMenuHandler(repo).CaminhoDoTipoAsync(setorId, TipoDeItemMenu.Documentos);

		caminho.Should().Equal("documentos");
	}

	[Fact]
	public async Task CaminhoDoTipo_ItemDesativado_DevolveNulo()
	{
		var (repo, setorId, _, avisos, _) = Cenario();
		repo.Itens.Single(i => i.Id == avisos.Id).Desativar();

		var caminho = await new ResolverCaminhoDeMenuHandler(repo).CaminhoDoTipoAsync(setorId, TipoDeItemMenu.Avisos);

		caminho.Should().BeNull();
	}

	[Fact]
	public async Task CaminhoDoTipo_AninhadoDebaixoDeAncestralDesativado_DevolveNulo()
	{
		var repo = new ItemMenuRepositorioFalso();
		var setorId = Guid.NewGuid();
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var grupo = new ItemMenu(setorId, raiz.Id, "Recursos", "recursos", TipoDeItemMenu.Personalizado, null, null, 0);
		var documentos = new ItemMenu(setorId, grupo.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		grupo.Desativar();
		repo.Itens.AddRange([raiz, grupo, documentos]);

		var caminho = await new ResolverCaminhoDeMenuHandler(repo).CaminhoDoTipoAsync(setorId, TipoDeItemMenu.Documentos);

		caminho.Should().BeNull("o pai está desativado — o recurso ficou inalcançável, logo desligado");
	}

	[Fact]
	public async Task CaminhoDoTipo_SemOItem_DevolveNulo()
	{
		var repo = new ItemMenuRepositorioFalso();
		var setorId = Guid.NewGuid();
		repo.Itens.Add(new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0));

		var caminho = await new ResolverCaminhoDeMenuHandler(repo).CaminhoDoTipoAsync(setorId, TipoDeItemMenu.Documentos);

		caminho.Should().BeNull();
	}

	[Fact]
	public async Task Neto_DevolveOsAncestraisSemARaiz()
	{
		var (repo, setorId, raiz, _, _) = Cenario();
		var relatorios = new ItemMenu(setorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 2);
		var vendas = new ItemMenu(setorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/v", null, 0);
		repo.Itens.AddRange([relatorios, vendas]);

		var resultado = await new ResolverCaminhoDeMenuHandler(repo).HandleAsync(setorId, ["relatorios", "vendas"]);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.No.Id.Should().Be(vendas.Id);
		resultado.Value.Ancestrais.Select(a => a.Slug).Should().Equal("relatorios");
		resultado.Value.CaminhoCompleto.Should().Equal("relatorios", "vendas");
	}

	[Fact]
	public async Task FilhoDaRaiz_NaoTemAncestrais()
	{
		var (repo, setorId, _, avisos, _) = Cenario();

		var resultado = await new ResolverCaminhoDeMenuHandler(repo).HandleAsync(setorId, ["avisos"]);

		resultado.Value.No.Id.Should().Be(avisos.Id);
		resultado.Value.Ancestrais.Should().BeEmpty();
	}

	[Fact]
	public async Task AncestralDesativado_Falha()
	{
		var (repo, setorId, raiz, _, _) = Cenario();
		var relatorios = new ItemMenu(setorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 2);
		relatorios.Desativar();
		repo.Itens.AddRange([relatorios, new ItemMenu(setorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/v", null, 0)]);

		var resultado = await new ResolverCaminhoDeMenuHandler(repo).HandleAsync(setorId, ["relatorios", "vendas"]);

		resultado.IsFailure.Should().BeTrue();
	}
}
