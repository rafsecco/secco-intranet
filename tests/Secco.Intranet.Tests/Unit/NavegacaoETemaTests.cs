using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Razor;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.Theming;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Resolução de views por tema e montagem do menu — as duas peças que o contrato de tema
/// (ADR-0004) apoia.
/// </summary>
public class NavegacaoETemaTests
{
	private static ClaimsPrincipal Usuario(params string[] roles) =>
		new(new ClaimsIdentity(
			roles.Select(role => new Claim(SeccoClaims.Role, role)),
			authenticationType: "Teste"));

	private static SetorDto Setor(string nome, string slug, bool ativo = true) =>
		new(Guid.NewGuid(), nome, slug, Fixo: false, Ativo: ativo, DateTimeOffset.UtcNow);

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

	[Theory]
	[InlineData("financeiro-admin", "financeiro")]
	[InlineData("recursos-humanos-user", "recursos-humanos")]
	public void SlugsDoUsuario_ExtraiOSlugDeCadaRoleDeSetor(string role, string slugEsperado) =>
		SetorAcesso.SlugsDoUsuario(Usuario(role)).Should().Contain(slugEsperado);

	[Fact]
	public void SlugsDoUsuario_IgnoraRolesQueNaoSaoDeSetor() =>
		SetorAcesso.SlugsDoUsuario(Usuario("plataforma:leitura", "-admin", "user"))
			.Should().BeEmpty("apenas roles no formato {slug}-admin/{slug}-user representam setor");

	[Fact]
	public void AdministraSetor_SoAceitaARoleDeAdministracaoDaqueleSetor()
	{
		var usuario = Usuario("financeiro-admin", "diretoria-user");

		SetorAcesso.AdministraSetor(usuario, "financeiro").Should().BeTrue();
		SetorAcesso.AdministraSetor(usuario, "diretoria").Should().BeFalse("estar no setor não é administrá-lo");
	}

	[Fact]
	public void Visiveis_ComVinculoExigido_DevolveSomenteOsSetoresDoUsuario()
	{
		IReadOnlyList<SetorDto> setores = [Setor("Financeiro", "financeiro"), Setor("Diretoria", "diretoria")];

		var visiveis = SetorAcesso.Visiveis(setores, Usuario("financeiro-user"), exigirVinculo: true);

		visiveis.Should().ContainSingle().Which.Slug.Should().Be("financeiro");
	}

	[Fact]
	public void Visiveis_SemVinculoExigido_DevolveTodosOsAtivos()
	{
		IReadOnlyList<SetorDto> setores =
		[
			Setor("Financeiro", "financeiro"),
			Setor("Extinto", "extinto", ativo: false),
		];

		var visiveis = SetorAcesso.Visiveis(setores, Usuario(), exigirVinculo: false);

		visiveis.Should().ContainSingle("o modo aberto de DEV mostra todos os setores, mas nunca um inativo");
	}

	[Fact]
	public void Build_ForaDaDemonstracao_NaoOfereceOsItensDeDemonstracao()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/", MostrarAdministracao: false, DemoHabilitado: false));

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
			DemoHabilitado: false));

		var itens = menu.Grupos.SelectMany(grupo => grupo.Itens).ToList();

		itens.Single(item => item.Ativo).SetorSlug.Should().Be("financeiro");
		itens.Should().Contain(item => item.Texto == "Setores", "o grupo de administração aparece para quem administra");
	}

	[Fact]
	public void Build_NaRaiz_MarcaOMuralComoAtivo()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/", MostrarAdministracao: false, DemoHabilitado: false));

		menu.Grupos.SelectMany(grupo => grupo.Itens).Single(item => item.Ativo).Texto.Should().Be("Mural");
	}

	[Theory]
	[InlineData("financeiro")]
	[InlineData("recursos-humanos")]
	[InlineData("")]
	public void SetorHue_ParaOMesmoSlug_DevolveSempreOMesmoMatiz(string slug) =>
		SetorHue.From(slug).Should().Be(SetorHue.From(slug), "a cor de um setor não pode mudar a cada reinício");
}
