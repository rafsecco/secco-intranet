using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LinksDoMenuPrincipalHandlersTests
{
	private sealed class RepositorioFalso : ILinkDoMenuPrincipalRepository
	{
		public List<LinkDoMenuPrincipal> Links { get; } = [];

		public int Salvamentos { get; private set; }

		public Task<IReadOnlyList<LinkDoMenuPrincipal>> ListarAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<LinkDoMenuPrincipal>>([.. Links]);

		public Task<LinkDoMenuPrincipal?> ObterAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(Links.FirstOrDefault(l => l.Id == id));

		public Task AddAsync(LinkDoMenuPrincipal link, CancellationToken cancellationToken = default)
		{
			Links.Add(link);
			Salvamentos++;

			return Task.CompletedTask;
		}

		public Task SalvarAsync(CancellationToken cancellationToken = default)
		{
			Salvamentos++;

			return Task.CompletedTask;
		}

		public Task RemoverAsync(LinkDoMenuPrincipal link, CancellationToken cancellationToken = default)
		{
			Links.Remove(link);
			Salvamentos++;

			return Task.CompletedTask;
		}
	}

	private readonly RepositorioFalso _repo = new();
	private readonly TrilhaDeAcessoFalsa _trilha = new();

	private Task<Result<Guid>> CriarAsync(string? rotulo, string? url, string? icone = null, bool novaAba = false) =>
		new CriarLinkDoMenuPrincipalHandler(_repo, _trilha).HandleAsync(new CriarLinkDoMenuPrincipalCommand(rotulo, url, icone, novaAba));

	[Fact]
	public async Task Criar_UrlExterna_GravaETrilhaMenuLinkCriar()
	{
		var resultado = await CriarAsync("Abrir um chamado", "https://suporte.exemplo.com.br", null, true);

		resultado.IsSuccess.Should().BeTrue();
		var link = _repo.Links.Single();
		link.Id.Should().Be(resultado.Value);
		link.Rotulo.Should().Be("Abrir um chamado");
		link.Url.Should().Be("https://suporte.exemplo.com.br");
		link.NovaAba.Should().BeTrue();
		var registro = _trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuLinkCriar);
		registro.Verbo.Should().Be("menu.link-criar");
		registro.Recurso.Should().Be(RecursosDeAuditoria.Menu);
		registro.RecursoId.Should().Be(link.Id.ToString());
		registro.Metadata.Should().Contain("Abrir um chamado").And.Contain("suporte.exemplo.com.br");
	}

	[Fact]
	public async Task Criar_CaminhoLocal_Aceita()
	{
		var resultado = await CriarAsync("Diretório", "/diretorio");

		resultado.IsSuccess.Should().BeTrue();
		_repo.Links.Single().Url.Should().Be("/diretorio");
	}

	[Theory]
	[InlineData("javascript:alert(1)")]
	[InlineData("JavaScript:alert(1)")]
	[InlineData("//outro.host")]
	[InlineData("/\\outro.host")]
	[InlineData("/\t/outro.host")]
	[InlineData("/\n/outro.host")]
	[InlineData("ftp://x")]
	[InlineData("data:text/html,x")]
	[InlineData("suporte.exemplo.com.br")]
	public async Task Criar_UrlRecusada_RotaInvalida_NadaGravado(string url)
	{
		var resultado = await CriarAsync("Chamado", url);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.RotaInvalida);
		_repo.Links.Should().BeEmpty();
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_UrlComEspacosEEsquemaMaiusculo_GravaAparada()
	{
		var resultado = await CriarAsync("Chamado", "  HTTPS://Suporte.exemplo.com.br  ");

		resultado.IsSuccess.Should().BeTrue();
		_repo.Links.Single().Url.Should().Be("HTTPS://Suporte.exemplo.com.br");
	}

	[Fact]
	public async Task Criar_RotuloVazio_RotuloRequired()
	{
		var resultado = await CriarAsync("  ", "/diretorio");

		resultado.Error.Should().Be(IntranetErrors.LinksDoMenu.RotuloRequired);
		_repo.Links.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_UrlVazia_UrlRequired()
	{
		var resultado = await CriarAsync("Chamado", " ");

		resultado.Error.Should().Be(IntranetErrors.LinksDoMenu.UrlRequired);
	}

	[Fact]
	public async Task Criar_RotuloDe257_RotuloMuitoLongo()
	{
		var resultado = await CriarAsync(new string('a', 257), "/diretorio");

		resultado.Error.Should().Be(IntranetErrors.LinksDoMenu.RotuloMuitoLongo);
	}

	[Fact]
	public async Task Criar_UrlDe513_UrlMuitoLonga()
	{
		var resultado = await CriarAsync("Chamado", "https://exemplo.com.br/" + new string('a', 513 - 23));

		resultado.Error.Should().Be(IntranetErrors.LinksDoMenu.UrlMuitoLonga);
	}

	[Theory]
	[InlineData("headset")]
	[InlineData("bi bi-headset")]
	public async Task Criar_IconeNormalizado(string icone)
	{
		var resultado = await CriarAsync("Chamado", "/diretorio", icone);

		resultado.IsSuccess.Should().BeTrue();
		_repo.Links.Single().Icone.Should().Be("bi-headset");
	}

	[Fact]
	public async Task Criar_IconeInvalido_IconeInvalido()
	{
		var resultado = await CriarAsync("Chamado", "/diretorio", "<b>x</b>");

		resultado.Error.Should().Be(IntranetErrors.Menu.IconeInvalido);
		_repo.Links.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_TrocaCamposETrilhaAntesEDepois()
	{
		var link = new LinkDoMenuPrincipal("Chamado", "/antigo", null, false);
		_repo.Links.Add(link);

		var resultado = await new EditarLinkDoMenuPrincipalHandler(_repo, _trilha)
			.HandleAsync(new EditarLinkDoMenuPrincipalCommand(link.Id, "Novo", "https://suporte.exemplo.com.br", "headset", true));

		resultado.IsSuccess.Should().BeTrue();
		link.Rotulo.Should().Be("Novo");
		link.Url.Should().Be("https://suporte.exemplo.com.br");
		link.Icone.Should().Be("bi-headset");
		link.NovaAba.Should().BeTrue();
		_repo.Salvamentos.Should().Be(1);
		var registro = _trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuLinkEditar);
		registro.RecursoId.Should().Be(link.Id.ToString());
		registro.Metadata.Should().Contain("antes").And.Contain("depois").And.Contain("/antigo").And.Contain("suporte.exemplo.com.br");
	}

	[Fact]
	public async Task Editar_IdInexistente_NotFound()
	{
		var resultado = await new EditarLinkDoMenuPrincipalHandler(_repo, _trilha)
			.HandleAsync(new EditarLinkDoMenuPrincipalCommand(Guid.NewGuid(), "Novo", "/x", null, false));

		resultado.Error.Should().Be(IntranetErrors.LinksDoMenu.NotFound);
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Excluir_RemoveETrilhaMenuLinkExcluir()
	{
		var link = new LinkDoMenuPrincipal("Chamado", "/x", "bi-headset", true);
		_repo.Links.Add(link);

		var resultado = await new ExcluirLinkDoMenuPrincipalHandler(_repo, _trilha).HandleAsync(link.Id);

		resultado.IsSuccess.Should().BeTrue();
		_repo.Links.Should().BeEmpty();
		var registro = _trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuLinkExcluir);
		registro.RecursoId.Should().Be(link.Id.ToString());
		registro.Metadata.Should().Contain("Chamado").And.Contain("bi-headset");
	}

	[Fact]
	public async Task Excluir_IdInexistente_NotFound()
	{
		var resultado = await new ExcluirLinkDoMenuPrincipalHandler(_repo, _trilha).HandleAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.LinksDoMenu.NotFound);
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Listar_OrdenaPorRotuloSemDiferenciarCaixa()
	{
		_repo.Links.Add(new LinkDoMenuPrincipal("webmail", "/w", null, false));
		_repo.Links.Add(new LinkDoMenuPrincipal("Abrir um chamado", "/a", null, false));

		var resultado = await new ListarLinksDoMenuPrincipalHandler(_repo).HandleAsync();

		resultado.Value.Select(l => l.Rotulo).Should().Equal("Abrir um chamado", "webmail");
		resultado.Value[0].Chave.Should().StartWith("link-");
	}
}
