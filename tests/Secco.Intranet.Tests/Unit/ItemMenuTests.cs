using AwesomeAssertions;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ItemMenuTests
{
	private static readonly Guid SetorId = Guid.NewGuid();

	[Fact]
	public void Criar_ComDadosValidos_PreencheOsCampos()
	{
		var item = new ItemMenu(SetorId, null, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);

		item.SetorId.Should().Be(SetorId);
		item.ParentId.Should().BeNull();
		item.Nome.Should().Be("Documentos");
		item.Slug.Should().Be("documentos");
		item.Tipo.Should().Be(TipoDeItemMenu.Documentos);
		item.Ordem.Should().Be(0);
		item.Ativo.Should().BeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Criar_SemNome_Recusa(string? nome)
	{
		var acao = () => new ItemMenu(SetorId, null, nome!, "slug", TipoDeItemMenu.Personalizado, null, null, 0);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void Criar_SemSlug_Recusa(string? slug)
	{
		var acao = () => new ItemMenu(SetorId, null, "Nome", slug!, TipoDeItemMenu.Personalizado, null, null, 0);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_ComIconeInvalido_Recusa()
	{
		var acao = () => new ItemMenu(SetorId, null, "Nome", "slug", TipoDeItemMenu.Personalizado, null, "bi-a bi-b", 0);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_IconeCopiadoDoSite_FicaNormalizado()
	{
		var item = new ItemMenu(
			SetorId, null, "Nome", "slug", TipoDeItemMenu.Personalizado, null, "<i class=\"bi bi-person-lines-fill\"></i>", 0);

		item.Icone.Should().Be("bi-person-lines-fill");
	}

	[Fact]
	public void Criar_SemIcone_FicaNulo()
	{
		var item = new ItemMenu(SetorId, null, "Nome", "slug", TipoDeItemMenu.Personalizado, null, null, 0);

		item.Icone.Should().BeNull();
	}

	[Fact]
	public void Desativar_DepoisAtivar_AlternaOFlag()
	{
		var item = new ItemMenu(SetorId, null, "Nome", "slug", TipoDeItemMenu.Personalizado, null, null, 0);

		item.Desativar();
		item.Ativo.Should().BeFalse();

		item.Ativar();
		item.Ativo.Should().BeTrue();
	}

	[Fact]
	public void DefinirOrdem_TrocaOValor()
	{
		var item = new ItemMenu(SetorId, null, "Nome", "slug", TipoDeItemMenu.Personalizado, null, null, 0);

		item.DefinirOrdem(3);

		item.Ordem.Should().Be(3);
	}

	[Fact]
	public void Criar_SemNivelDeAcesso_FicaEmLeitura()
	{
		var item = new ItemMenu(SetorId, null, "Nome", "slug", TipoDeItemMenu.Personalizado, null, null, 0);

		item.NivelDeAcesso.Should().Be(NivelDeAcessoItemMenu.Leitura);
	}

	[Theory]
	[InlineData(TipoDeItemMenu.Documentos)]
	[InlineData(TipoDeItemMenu.Avisos)]
	[InlineData(TipoDeItemMenu.Vencimentos)]
	[InlineData(TipoDeItemMenu.Personalizado)]
	public void Criar_ComEscrita_AceitaTodoTipoQueNaoSejaARaiz(TipoDeItemMenu tipo)
	{
		var item = new ItemMenu(SetorId, Guid.NewGuid(), "Nome", "slug", tipo, null, null, 0, NivelDeAcessoItemMenu.Escrita);

		item.NivelDeAcesso.Should().Be(NivelDeAcessoItemMenu.Escrita);
	}

	[Fact]
	public void Criar_RaizComEscrita_Recusa()
	{
		var acao = () => new ItemMenu(SetorId, null, "Nome", "slug", TipoDeItemMenu.Setor, null, null, 0, NivelDeAcessoItemMenu.Escrita);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void DefinirNivelDeAcesso_NaRaiz_Recusa()
	{
		var raiz = new ItemMenu(SetorId, null, "Nome", "slug", TipoDeItemMenu.Setor, null, null, 0);

		var acao = () => raiz.DefinirNivelDeAcesso(NivelDeAcessoItemMenu.Escrita);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void DefinirNivelDeAcesso_ForaDoEnum_Recusa()
	{
		var item = new ItemMenu(SetorId, Guid.NewGuid(), "Nome", "slug", TipoDeItemMenu.Personalizado, null, null, 0);

		var acao = () => item.DefinirNivelDeAcesso((NivelDeAcessoItemMenu)99);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void DefinirNivelDeAcesso_TrocaOValor()
	{
		var item = new ItemMenu(SetorId, Guid.NewGuid(), "Nome", "slug", TipoDeItemMenu.Documentos, null, null, 0);

		item.DefinirNivelDeAcesso(NivelDeAcessoItemMenu.Escrita);

		item.NivelDeAcesso.Should().Be(NivelDeAcessoItemMenu.Escrita);
	}

	[Fact]
	public void Construtor_SemInformar_NovaAbaFalso()
	{
		var item = new ItemMenu(SetorId, Guid.NewGuid(), "Nome", "slug", TipoDeItemMenu.Personalizado, "/destino", null, 0);

		item.NovaAba.Should().BeFalse();
	}

	[Fact]
	public void Construtor_TipoLinksComRota_Lanca()
	{
		var acao = () => new ItemMenu(SetorId, Guid.NewGuid(), "Links", "links", TipoDeItemMenu.Links, "/destino", null, 0);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_SemRota_NovaAbaViraFalso()
	{
		var item = new ItemMenu(
			SetorId, Guid.NewGuid(), "Nome", "slug", TipoDeItemMenu.Personalizado, null, null, 0, novaAba: true);

		item.NovaAba.Should().BeFalse();
	}

	[Fact]
	public void Editar_TrocaNomeSlugRotaENovaAba()
	{
		var item = new ItemMenu(SetorId, Guid.NewGuid(), "Nome", "slug", TipoDeItemMenu.Personalizado, null, null, 0);

		item.Editar("Novo nome", "Novo-Slug", " https://exemplo.com ", novaAba: true);

		item.Nome.Should().Be("Novo nome");
		item.Slug.Should().Be("novo-slug");
		item.Rota.Should().Be("https://exemplo.com");
		item.NovaAba.Should().BeTrue();
	}

	[Fact]
	public void Editar_SemRota_NovaAbaViraFalso()
	{
		var item = new ItemMenu(SetorId, Guid.NewGuid(), "Nome", "slug", TipoDeItemMenu.Personalizado, "/a", null, 0, novaAba: true);

		item.Editar("Nome", "slug", null, novaAba: true);

		item.Rota.Should().BeNull();
		item.NovaAba.Should().BeFalse();
	}

	[Fact]
	public void Editar_NomeVazio_Lanca()
	{
		var item = new ItemMenu(SetorId, Guid.NewGuid(), "Nome", "slug", TipoDeItemMenu.Personalizado, null, null, 0);

		var acao = () => item.Editar(" ", "slug", null, novaAba: false);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Reposicionar_TrocaPaiEOrdem()
	{
		var item = new ItemMenu(SetorId, Guid.NewGuid(), "Nome", "slug", TipoDeItemMenu.Links, null, null, 0);
		var novoPai = Guid.NewGuid();

		item.Reposicionar(novoPai, 3);

		item.ParentId.Should().Be(novoPai);
		item.Ordem.Should().Be(3);
	}
}
