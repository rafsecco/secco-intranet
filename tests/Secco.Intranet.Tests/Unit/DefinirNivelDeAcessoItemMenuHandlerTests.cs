using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class DefinirNivelDeAcessoItemMenuHandlerTests
{
	private static (ItemMenuRepositorioFalso Repo, TrilhaDeAcessoFalsa Trilha, ItemMenu Raiz, ItemMenu Documentos) Cenario()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var documentos = new ItemMenu(raiz.SetorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(documentos);

		return (repo, new TrilhaDeAcessoFalsa(), raiz, documentos);
	}

	[Fact]
	public async Task Definir_NivelNovo_GravaEAudita()
	{
		var (repo, trilha, _, documentos) = Cenario();

		var resultado = await new DefinirNivelDeAcessoItemMenuHandler(repo, trilha)
			.HandleAsync(documentos.Id, NivelDeAcessoItemMenu.Escrita);

		resultado.IsSuccess.Should().BeTrue();
		documentos.NivelDeAcesso.Should().Be(NivelDeAcessoItemMenu.Escrita);

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuItemDefinirNivel);
		registro.RecursoId.Should().Be(documentos.Id.ToString());
		using var json = JsonDocument.Parse(registro.Metadata!);
		json.RootElement.GetProperty("nivelAnterior").GetString().Should().Be("Leitura");
		json.RootElement.GetProperty("nivel").GetString().Should().Be("Escrita");
	}

	[Fact]
	public async Task Definir_MesmoNivel_NaoAudita()
	{
		var (repo, trilha, _, documentos) = Cenario();

		var resultado = await new DefinirNivelDeAcessoItemMenuHandler(repo, trilha)
			.HandleAsync(documentos.Id, NivelDeAcessoItemMenu.Leitura);

		resultado.IsSuccess.Should().BeTrue();
		trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Definir_ForaDoEnum_Recusa()
	{
		var (repo, trilha, _, documentos) = Cenario();

		var resultado = await new DefinirNivelDeAcessoItemMenuHandler(repo, trilha)
			.HandleAsync(documentos.Id, (NivelDeAcessoItemMenu)99);

		resultado.Error.Should().Be(IntranetErrors.Menu.NivelDeAcessoInvalido);
		documentos.NivelDeAcesso.Should().Be(NivelDeAcessoItemMenu.Leitura);
		trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Definir_ItemInexistente_Recusa()
	{
		var (repo, trilha, _, _) = Cenario();

		var resultado = await new DefinirNivelDeAcessoItemMenuHandler(repo, trilha)
			.HandleAsync(Guid.NewGuid(), NivelDeAcessoItemMenu.Escrita);

		resultado.Error.Should().Be(IntranetErrors.Menu.NotFound);
	}

	[Fact]
	public async Task Definir_NaRaiz_Recusa()
	{
		var (repo, trilha, raiz, _) = Cenario();

		var resultado = await new DefinirNivelDeAcessoItemMenuHandler(repo, trilha)
			.HandleAsync(raiz.Id, NivelDeAcessoItemMenu.Escrita);

		resultado.Error.Should().Be(IntranetErrors.Menu.RaizProtegida);
		trilha.Registros.Should().BeEmpty();
	}
}
