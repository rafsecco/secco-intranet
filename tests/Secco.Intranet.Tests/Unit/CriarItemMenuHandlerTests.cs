using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class CriarItemMenuHandlerTests
{
	private static readonly Guid SetorId = Guid.NewGuid();

	private static ItemMenu CriarRaiz(ItemMenuRepositorioFalso repo)
	{
		var raiz = new ItemMenu(SetorId, null, "Setor", "setor", TipoDeItemMenu.Setor, null, null, 0);
		repo.Itens.Add(raiz);

		return raiz;
	}

	[Fact]
	public async Task ComDadosValidos_Cria()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Nome.Should().Be("Relatórios");
		repo.Itens.Should().ContainSingle(i => i.Slug == "relatorios");
	}

	[Fact]
	public async Task SemNome_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "", "slug", TipoDeItemMenu.Personalizado, null, null));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.NomeRequired);
	}

	[Fact]
	public async Task SegundoDocumentosNoMesmoSetor_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		repo.Itens.Add(new ItemMenu(SetorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0));
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Documentos 2", "documentos-2", TipoDeItemMenu.Documentos, null, null));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.TipoJaExiste);
	}

	[Fact]
	public async Task SlugDuplicadoEntreIrmaos_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		repo.Itens.Add(new ItemMenu(SetorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0));
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Outro", "relatorios", TipoDeItemMenu.Personalizado, null, null));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.SlugJaExisteEntreIrmaos);
	}

	[Fact]
	public async Task SlugIgualEmOutroPai_NaoRecusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var relatorios = new ItemMenu(SetorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		repo.Itens.Add(relatorios);
		repo.Itens.Add(new ItemMenu(SetorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, null, null, 0));
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		// "vendas" já existe debaixo de Relatórios, mas nasce solto (debaixo da raiz) sem problema.
		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, null, null));

		resultado.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task PaiDeOutroSetor_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raizDeOutroSetor = new ItemMenu(Guid.NewGuid(), null, "Y", "y", TipoDeItemMenu.Setor, null, null, 0);
		repo.Itens.Add(raizDeOutroSetor);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raizDeOutroSetor.Id, "Nome", "slug", TipoDeItemMenu.Personalizado, null, null));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.PaiInvalido);
	}

	// Documentos e Avisos são folhas: com um filho, a página deles redirecionaria para o filho
	// e o recurso ficaria inalcançável.
	[Theory]
	[InlineData(TipoDeItemMenu.Documentos)]
	[InlineData(TipoDeItemMenu.Avisos)]
	public async Task PaiDocumentosOuAvisos_Recusa(TipoDeItemMenu tipoDoPai)
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var pai = new ItemMenu(SetorId, raiz.Id, "Recurso", "recurso", tipoDoPai, null, null, 0);
		repo.Itens.Add(pai);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, pai.Id, "Filho", "filho", TipoDeItemMenu.Personalizado, null, null));

		resultado.Error.Should().Be(IntranetErrors.Menu.PaiInvalido);
	}

	[Fact]
	public async Task PaiPersonalizado_Aceita()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var relatorios = new ItemMenu(SetorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		repo.Itens.Add(relatorios);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, null, null));

		resultado.IsSuccess.Should().BeTrue();
	}

	// A tela nunca oferece Tipo = Setor, mas o handler é a fronteira: um POST forjado (ou um
	// número de enum fora da faixa) chega aqui do mesmo jeito.
	[Theory]
	[InlineData(TipoDeItemMenu.Setor)]
	[InlineData((TipoDeItemMenu)99)]
	public async Task TipoSetorOuForaDoEnum_Recusa(TipoDeItemMenu tipo)
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Nome", "slug", tipo, null, null));

		resultado.Error.Should().Be(IntranetErrors.Menu.TipoInvalido);
		repo.Itens.Should().ContainSingle("só a raiz — nada foi criado");
	}

	// O slug vira segmento de URL (/setor/x/{slug}); com barra, espaço ou maiúscula o item
	// ficaria inalcançável pela resolução de caminho.
	[Theory]
	[InlineData("com/barra")]
	[InlineData("com espaco")]
	[InlineData("-comeca-com-hifen")]
	[InlineData("acentuação")]
	public async Task SlugForaDoFormato_Recusa(string slug)
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Nome", slug, TipoDeItemMenu.Personalizado, null, null));

		resultado.Error.Should().Be(IntranetErrors.Menu.SlugInvalido);
	}

	[Fact]
	public async Task SlugEmMaiusculas_ENormalizado_NaoRecusado()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Nome", "Relatorios-2026", TipoDeItemMenu.Personalizado, null, null));

		resultado.Value.Slug.Should().Be("relatorios-2026");
	}

	[Fact]
	public async Task NomeAcimaDoLimite_Recusa_EmVezDeEstourarNoBanco()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, new string('x', 257), "slug", TipoDeItemMenu.Personalizado, null, null));

		resultado.Error.Type.Should().Be(Secco.SharedKernel.Results.ErrorType.Validation);
	}

	// Rota é destino de redirect (Task 9). Aceita caminho local ou http(s) absoluto; recusa
	// "//host" (vira redirect para outro domínio sem esquema), javascript: e afins (ADR-0020).
	[Theory]
	[InlineData("/relatorios/vendas", true)]
	[InlineData("https://bi.exemplo.com/painel", true)]
	[InlineData("http://intranet-legado/x", true)]
	[InlineData("//outro-dominio.com", false)]
	[InlineData("javascript:alert(1)", false)]
	[InlineData("relativo-sem-barra", false)]
	[InlineData("ftp://x/y", false)]
	public async Task Rota_SoCaminhoLocalOuHttp(string rota, bool aceita)
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Nome", "slug", TipoDeItemMenu.Personalizado, rota, null));

		resultado.IsSuccess.Should().Be(aceita);

		if (!aceita)
		{
			resultado.Error.Should().Be(IntranetErrors.Menu.RotaInvalida);
		}
	}

	[Fact]
	public async Task RotaEmTipoQueNaoEPersonalizado_EIgnorada()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var handler = new CriarItemMenuHandler(repo, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, "/qualquer", null));

		resultado.Value.Rota.Should().BeNull("Rota só tem papel em Personalizado — não guardar lixo nos outros tipos");
	}
}
