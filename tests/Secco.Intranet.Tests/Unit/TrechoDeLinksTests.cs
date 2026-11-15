using AwesomeAssertions;
using Secco.Intranet.Application.Links;
using Secco.Intranet.Domain.Menu;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class TrechoDeLinksTests
{
	private static readonly Guid Setor = Guid.NewGuid();

	private static ItemMenu Raiz() => new(Setor, null, "Financeiro", "financeiro", TipoDeItemMenu.Setor, null, null, 0);

	private static ItemMenu No(ItemMenu raiz) => new(Setor, raiz.Id, "Links", "links", TipoDeItemMenu.Links, null, null, 0);

	private static ItemMenu Grupo(ItemMenu pai, string nome, int ordem) =>
		new(Setor, pai.Id, nome, nome.ToLowerInvariant(), TipoDeItemMenu.Personalizado, null, null, ordem);

	private static ItemMenu Link(ItemMenu pai, string nome, int ordem) =>
		new(Setor, pai.Id, nome, nome.ToLowerInvariant(), TipoDeItemMenu.Personalizado, "https://exemplo.test/" + nome, null, ordem);

	[Fact]
	public void De_SemNoLinks_Nulo()
	{
		var raiz = Raiz();

		TrechoDeLinks.De([raiz]).Should().BeNull();
	}

	[Fact]
	public void De_NoDesativado_Nulo()
	{
		var raiz = Raiz();
		var no = No(raiz);
		no.Desativar();

		TrechoDeLinks.De([raiz, no]).Should().BeNull();
	}

	[Fact]
	public void Item_ForaDoTrecho_Nulo()
	{
		var raiz = Raiz();
		var no = No(raiz);
		var documentos = new ItemMenu(Setor, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 1);
		var doAdmin = Link(raiz, "admin", 2);
		var trecho = TrechoDeLinks.De([raiz, no, documentos, doAdmin])!;

		trecho.Item(documentos.Id).Should().BeNull();
		trecho.Item(doAdmin.Id).Should().BeNull();
		trecho.Item(raiz.Id).Should().BeNull();
		trecho.Item(Guid.NewGuid()).Should().BeNull();
	}

	[Fact]
	public void Item_OProprioNo_Nulo()
	{
		var raiz = Raiz();
		var no = No(raiz);
		var trecho = TrechoDeLinks.De([raiz, no])!;

		trecho.No.Should().BeSameAs(no);
		trecho.Item(no.Id).Should().BeNull();
	}

	[Fact]
	public void Item_LinkDentroDeGrupo_Acha()
	{
		var raiz = Raiz();
		var no = No(raiz);
		var grupo = Grupo(no, "bancos", 0);
		var link = Link(grupo, "itau", 0);
		var fundo = Grupo(grupo, "fundo", 1);
		var profundo = Link(fundo, "profundo", 0);
		var trecho = TrechoDeLinks.De([raiz, no, grupo, link, fundo, profundo])!;

		trecho.Item(link.Id).Should().BeSameAs(link);
		trecho.Item(grupo.Id).Should().BeSameAs(grupo);
		trecho.Item(profundo.Id).Should().BeSameAs(profundo);
	}

	[Fact]
	public void Grupo_IdDeLink_Nulo()
	{
		var raiz = Raiz();
		var no = No(raiz);
		var grupo = Grupo(no, "bancos", 0);
		var link = Link(no, "solto", 1);
		var linkNoGrupo = Link(grupo, "itau", 0);
		var trecho = TrechoDeLinks.De([raiz, no, grupo, link, linkNoGrupo])!;

		trecho.Grupo(link.Id).Should().BeNull();
		trecho.Grupo(linkNoGrupo.Id).Should().BeNull();
		trecho.Grupo(no.Id).Should().BeNull();
		trecho.Grupo(grupo.Id).Should().BeSameAs(grupo);
		trecho.Grupos.Should().ContainSingle().Which.Should().BeSameAs(grupo);
	}
}
