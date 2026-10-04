using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Razor;
using Secco.Intranet;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.Theming;
using Secco.Intranet.Web.Theming.Contracts;
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

	private static ItemMenuDto No(Guid? pai, string nome, string slug, TipoDeItemMenu tipo, int ordem = 0, string? rota = null) =>
		new(Guid.NewGuid(), pai, nome, slug, tipo, rota, null, ordem, Ativo: true);

	private static (SetorDto Setor, Dictionary<Guid, IReadOnlyList<ItemMenuDto>> Arvores) Financeiro(params Func<ItemMenuDto, ItemMenuDto[]>[] filhosDaRaiz)
	{
		var setor = Setor("Financeiro", "financeiro");
		var raiz = No(null, "Financeiro", "financeiro", TipoDeItemMenu.Setor);
		var itens = new List<ItemMenuDto> { raiz };

		foreach (var filhos in filhosDaRaiz)
		{
			itens.AddRange(filhos(raiz));
		}

		return (setor, new Dictionary<Guid, IReadOnlyList<ItemMenuDto>> { [setor.Id] = itens });
	}

	private static NavigationItemModel SetorNoMenu(NavigationModel menu) =>
		menu.Grupos.Single(grupo => grupo.Titulo == "Setores").Itens.Single();

	[Fact]
	public void Build_SetorViraAgrupadorSemUrl_ComOsFilhosEmOrdem()
	{
		var (setor, arvores) = Financeiro(raiz =>
		[
			No(raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, ordem: 1),
			No(raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, ordem: 0),
		]);

		var item = SetorNoMenu(IntranetNavigation.Build(new NavigationRequest([setor], "/", false, false, false, arvores)));

		item.Url.Should().BeNull("o setor só agrupa");
		item.SetorSlug.Should().Be("financeiro");
		item.Filhos!.Select(filho => filho.Url).Should().Equal("/financeiro/avisos", "/financeiro/documentos");
	}

	[Fact]
	public void Build_AtivoMarcaOCaminhoInteiro()
	{
		ItemMenuDto relatorios = null!;
		var (setor, arvores) = Financeiro(raiz =>
		{
			relatorios = No(raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado);
			return [relatorios, No(relatorios.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos)];
		});

		var item = SetorNoMenu(IntranetNavigation.Build(
			new NavigationRequest([setor], "/financeiro/relatorios/documentos", false, false, false, arvores)));

		item.Ativo.Should().BeTrue();
		item.Filhos!.Single().Ativo.Should().BeTrue();
		item.Filhos!.Single().Filhos!.Single().Ativo.Should().BeTrue();
	}

	[Fact]
	public void Build_PersonalizadoComRota_ApontaParaARota()
	{
		var (setor, arvores) = Financeiro(raiz => [No(raiz.Id, "Painel BI", "painel-bi", TipoDeItemMenu.Personalizado, rota: "https://bi.exemplo/x")]);

		var item = SetorNoMenu(IntranetNavigation.Build(new NavigationRequest([setor], "/", false, false, false, arvores)));

		item.Filhos!.Single().Url.Should().Be("https://bi.exemplo/x");
	}

	[Fact]
	public void Build_CadeiaDeAgrupadoresVazios_SomeJuntoComOSetor()
	{
		var (setor, arvores) = Financeiro(raiz =>
		{
			var relatorios = No(raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado);
			return [relatorios, No(relatorios.Id, "Mensais", "mensais", TipoDeItemMenu.Personalizado)];
		});

		var menu = IntranetNavigation.Build(new NavigationRequest([setor], "/", false, false, false, arvores));

		menu.Grupos.Single(grupo => grupo.Titulo == "Setores").Itens.Should().BeEmpty();
	}

	[Fact]
	public void Build_FilhoCujoPaiNaoVeio_FicaDeFora()
	{
		// O handler já tira inativos; um filho ativo de pai inativo chega órfão e não pode subir de nível.
		var (setor, arvores) = Financeiro(raiz =>
			[No(raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos), No(Guid.NewGuid(), "Órfão", "orfao", TipoDeItemMenu.Documentos)]);

		var item = SetorNoMenu(IntranetNavigation.Build(new NavigationRequest([setor], "/", false, false, false, arvores)));

		item.Filhos!.Select(filho => filho.Texto).Should().Equal("Avisos");
	}

	[Fact]
	public void Build_SetorSemArvore_NaoAparece()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([Setor("Financeiro", "financeiro")], "/", false, false, false));

		menu.Grupos.Single(grupo => grupo.Titulo == "Setores").Itens.Should().BeEmpty();
	}

	[Fact]
	public void Build_Inventario_FicaNoGrupoAdministracao()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/inventario", MostrarAdministracao: true, MostrarDiretorio: false, MostrarInventario: true));

		menu.Grupos.Single(grupo => grupo.Titulo is null).Itens.Should().NotContain(item => item.Texto == "Inventário");
		menu.Grupos.Single(grupo => grupo.Titulo == "Administração").Itens.Select(item => item.Texto)
			.Should().Equal("Setores", "Acesso", "Inventário");
	}

	[Fact]
	public void Build_SoComInventario_MostraAdministracaoSoComEle()
	{
		// inventario-admin sem intranet-admin: vê o Inventário, mas não Setores nem Acesso.
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/", MostrarAdministracao: false, MostrarDiretorio: false, MostrarInventario: true));

		menu.Grupos.Single(grupo => grupo.Titulo == "Administração").Itens
			.Should().ContainSingle().Which.Texto.Should().Be("Inventário");
	}

	[Fact]
	public void Build_SemAdministracaoNemInventario_NaoMostraOGrupo()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/", MostrarAdministracao: false, MostrarDiretorio: false, MostrarInventario: false));

		menu.Grupos.Should().NotContain(grupo => grupo.Titulo == "Administração");
	}

	[Fact]
	public void Build_SetorComSlugReservado_NaoApareceNoMenu()
	{
		// Criado antes da lista de reservados existir: a rota fixa vence e o link daria 404.
		var setor = Setor("Documentos", "documentos");
		var raiz = No(null, "Documentos", "documentos", TipoDeItemMenu.Setor);
		var arvores = new Dictionary<Guid, IReadOnlyList<ItemMenuDto>>
		{
			[setor.Id] = [raiz, No(raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos)],
		};

		var menu = IntranetNavigation.Build(new NavigationRequest([setor], "/", false, false, false, arvores));

		menu.Grupos.Single(grupo => grupo.Titulo == "Setores").Itens.Should().BeEmpty();
	}
}
