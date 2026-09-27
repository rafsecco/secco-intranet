using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Razor;
using Secco.Intranet;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.Theming;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Resolução de views por tema e montagem do menu — as duas peças que o contrato de tema
/// (ADR-0004) apoia.
/// </summary>
public class NavegacaoETemaTests
{
	private static SetorDto Setor(string nome, string slug, bool ativo = true) =>
		new(Guid.NewGuid(), nome, slug, Domain.Setores.Setor.IconePadrao, Fixo: false, Ativo: ativo,
			CreatedAt: DateTimeOffset.UtcNow);

	[Fact]
	public void ExpandViewLocations_ComTemaAtivo_ColocaAsViewsDoTemaAntesDasDoCore()
	{
		var expander = new ThemeViewLocationExpander(new ThemeOptions { Nome = "Vertical" });
		var context = new ViewLocationExpanderContext(
			new Microsoft.AspNetCore.Mvc.ActionContext(), "Index", "Mural", areaName: null, pageName: null, isMainPage: true)
		{
			Values = new Dictionary<string, string?>(StringComparer.Ordinal),
		};

		expander.PopulateValues(context);

		var locais = expander.ExpandViewLocations(context, ["/Views/{1}/{0}.cshtml"]).ToList();

		locais.Should().StartWith("/Themes/Vertical/Views/{1}/{0}.cshtml");
		locais.Should().Contain("/Themes/Vertical/Views/Shared/{0}.cshtml");
		locais.Should().EndWith("/Views/{1}/{0}.cshtml", "o que o tema não define precisa cair nas views do core");
	}

	[Fact]
	public void PopulateValues_SempreGravaOTemaNaChaveDeCache()
	{
		var expander = new ThemeViewLocationExpander(new ThemeOptions { Nome = "Horizontal" });
		var context = new ViewLocationExpanderContext(
			new Microsoft.AspNetCore.Mvc.ActionContext(), "Index", "Mural", areaName: null, pageName: null, isMainPage: true)
		{
			Values = new Dictionary<string, string?>(StringComparer.Ordinal),
		};

		expander.PopulateValues(context);

		context.Values.Values.Should().Contain("Horizontal", "trocar o tema precisa invalidar o cache de views");
	}

	[Fact]
	public void Build_SemAcessoAoDiretorio_SoTemOMural()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/", MostrarAdministracao: false, MostrarDiretorio: false, MostrarInventario: false));

		menu.Grupos.SelectMany(grupo => grupo.Itens).Should().ContainSingle()
			.Which.Texto.Should().Be("Mural", "o Mural é o único item fixo do menu");
	}

	[Fact]
	public void Build_NaPaginaDeUmSetor_MarcaAqueleItemComoAtivo()
	{
		var menu = IntranetNavigation.Build(new NavigationRequest(
			[Setor("Financeiro", "financeiro"), Setor("Diretoria", "diretoria")],
			"/setor/financeiro/documentos",
			MostrarAdministracao: true,
			MostrarDiretorio: false,
			MostrarInventario: false));

		var itens = menu.Grupos.SelectMany(grupo => grupo.Itens).ToList();

		itens.Single(item => item.Ativo).SetorSlug.Should().Be("financeiro");
		itens.Should().Contain(item => item.Texto == "Setores", "o grupo de administração aparece para quem administra");
	}

	[Fact]
	public void Build_NaRaiz_MarcaOMuralComoAtivo()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/", MostrarAdministracao: false, MostrarDiretorio: false, MostrarInventario: false));

		menu.Grupos.SelectMany(grupo => grupo.Itens).Single(item => item.Ativo).Texto.Should().Be("Mural");
	}

	[Fact]
	public void Build_ComMostrarInventario_IncluiItemDeMenu()
	{
		var request = new NavigationRequest([], "/", MostrarAdministracao: false, MostrarDiretorio: false, MostrarInventario: true);

		var menu = IntranetNavigation.Build(request);

		menu.Grupos.SelectMany(g => g.Itens).Should().Contain(item => item.Texto == "Inventário");
	}

	[Fact]
	public void Build_SemMostrarInventario_NaoIncluiItemDeMenu()
	{
		var request = new NavigationRequest([], "/", MostrarAdministracao: false, MostrarDiretorio: false, MostrarInventario: false);

		var menu = IntranetNavigation.Build(request);

		menu.Grupos.SelectMany(g => g.Itens).Should().NotContain(item => item.Texto == "Inventário");
	}

	[Theory]
	[InlineData("financeiro")]
	[InlineData("recursos-humanos")]
	[InlineData("")]
	public void SetorHue_ParaOMesmoSlug_DevolveSempreOMesmoMatiz(string slug) =>
		SetorHue.From(slug).Should().Be(SetorHue.From(slug), "a cor de um setor não pode mudar a cada reinício");

	[Fact]
	public void Build_ComAdministracao_OfereceSetoresEAcesso()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/acesso/perfil", MostrarAdministracao: true, MostrarDiretorio: false, MostrarInventario: false));

		var itens = menu.Grupos.SelectMany(grupo => grupo.Itens).ToList();

		itens.Should().Contain(item => item.Texto == "Setores");
		itens.Single(item => item.Texto == "Acesso").Ativo.Should().BeTrue("/acesso/perfil está sob /acesso");
	}

	[Fact]
	public void Build_ComAcessoAoDiretorio_OfereceODiretorio()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/diretorio/organograma", MostrarAdministracao: false, MostrarDiretorio: true, MostrarInventario: false));

		menu.Grupos.SelectMany(grupo => grupo.Itens)
			.Should().Contain(item => item.Texto == "Diretório" && item.Ativo);
	}
}
