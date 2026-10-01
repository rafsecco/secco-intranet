using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Ações na árvore mudam o que um setor inteiro enxerga (desativar Documentos tira o recurso do
/// ar), e excluir não se desfaz — por isso cada uma deixa registro, e só quando de fato aconteceu.
/// </summary>
public class AuditoriaDeMenuTests
{
	private static (ItemMenuRepositorioFalso Repo, TrilhaDeAcessoFalsa Trilha, ItemMenu Raiz) Cenario()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "Financeiro", "financeiro", TipoDeItemMenu.Setor, null, null, 0);
		repo.Itens.Add(raiz);

		return (repo, new TrilhaDeAcessoFalsa(), raiz);
	}

	[Fact]
	public async Task Criar_RegistraItemCriar_ComSlugETipo()
	{
		var (repo, trilha, raiz) = Cenario();

		var criado = await new CriarItemMenuHandler(repo, trilha).HandleAsync(new CriarItemMenuCommand(
			raiz.SetorId, raiz.Id, "Painel BI", "painel-bi", TipoDeItemMenu.Personalizado, "/bi", null));

		criado.IsSuccess.Should().BeTrue();
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuItemCriar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Menu);
		registro.RecursoId.Should().Be(criado.Value.Id.ToString());
		using var json = JsonDocument.Parse(registro.Metadata!);
		json.RootElement.GetProperty("slug").GetString().Should().Be("painel-bi");
		json.RootElement.GetProperty("tipo").GetString().Should().Be("Personalizado");
		json.RootElement.GetProperty("rota").GetString().Should().Be("/bi");
	}

	[Fact]
	public async Task Criar_QueFalha_NaoRegistra()
	{
		var (repo, trilha, raiz) = Cenario();

		var criado = await new CriarItemMenuHandler(repo, trilha).HandleAsync(new CriarItemMenuCommand(
			raiz.SetorId, raiz.Id, "X", "Slug Inválido", TipoDeItemMenu.Personalizado, null, null));

		criado.IsFailure.Should().BeTrue();
		trilha.Registros.Should().BeEmpty();
	}

	[Theory]
	[InlineData(false, VerbosDeAuditoria.MenuItemDesativar)]
	[InlineData(true, VerbosDeAuditoria.MenuItemAtivar)]
	public async Task AtivarDesativar_RegistraOVerboDaAcao(bool ativar, string verbo)
	{
		var (repo, trilha, raiz) = Cenario();
		var documentos = new ItemMenu(raiz.SetorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		repo.Itens.Add(documentos);

		(await new AtivarDesativarItemMenuHandler(repo, trilha).HandleAsync(documentos.Id, ativar)).IsSuccess.Should().BeTrue();

		trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(verbo);
	}

	[Fact]
	public async Task Desativar_ARaiz_NaoRegistra()
	{
		var (repo, trilha, raiz) = Cenario();

		(await new AtivarDesativarItemMenuHandler(repo, trilha).HandleAsync(raiz.Id, ativar: false)).IsFailure.Should().BeTrue();

		trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Mover_ComVizinho_RegistraDeEPara()
	{
		var (repo, trilha, raiz) = Cenario();
		var avisos = new ItemMenu(raiz.SetorId, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 0);
		var documentos = new ItemMenu(raiz.SetorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 1);
		repo.Itens.AddRange([avisos, documentos]);

		(await new MoverItemMenuHandler(repo, trilha).HandleAsync(documentos.Id, paraCima: true)).IsSuccess.Should().BeTrue();

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuItemMover);
		using var json = JsonDocument.Parse(registro.Metadata!);
		json.RootElement.GetProperty("de").GetInt32().Should().Be(1);
		json.RootElement.GetProperty("para").GetInt32().Should().Be(0);
	}

	[Fact]
	public async Task Mover_PrimeiroParaCima_NaoRegistra()
	{
		var (repo, trilha, raiz) = Cenario();
		var avisos = new ItemMenu(raiz.SetorId, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 0);
		repo.Itens.Add(avisos);

		(await new MoverItemMenuHandler(repo, trilha).HandleAsync(avisos.Id, paraCima: true)).IsSuccess.Should().BeTrue();

		trilha.Registros.Should().BeEmpty("nada mudou de lugar");
	}

	[Fact]
	public async Task Excluir_RegistraComNomeESlug()
	{
		var (repo, trilha, raiz) = Cenario();
		var item = new ItemMenu(raiz.SetorId, raiz.Id, "Painel BI", "painel-bi", TipoDeItemMenu.Personalizado, "/bi", null, 0);
		repo.Itens.Add(item);

		(await new ExcluirItemMenuHandler(repo, trilha).HandleAsync(item.Id)).IsSuccess.Should().BeTrue();

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuItemExcluir);
		registro.RecursoId.Should().Be(item.Id.ToString());
		registro.Metadata.Should().Contain("painel-bi");
	}

	[Fact]
	public async Task Excluir_ComFilhos_NaoRegistra()
	{
		var (repo, trilha, raiz) = Cenario();
		var pai = new ItemMenu(raiz.SetorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		repo.Itens.AddRange([pai, new ItemMenu(raiz.SetorId, pai.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/v", null, 0)]);

		(await new ExcluirItemMenuHandler(repo, trilha).HandleAsync(pai.Id)).IsFailure.Should().BeTrue();

		trilha.Registros.Should().BeEmpty();
	}
}
