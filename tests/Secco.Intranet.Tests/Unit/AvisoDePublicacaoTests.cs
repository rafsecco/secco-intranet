using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// A regra do aviso. Errar aqui não aparece como exceção: aparece como comunicado que não
/// chegou, ou como e-mail para quem não podia sequer ver a publicação.
/// </summary>
public class AvisoDePublicacaoTests
{
	private static readonly Guid Autor = Guid.NewGuid();

	private sealed class DiretorioFalso(params UsuarioDoTenant[] usuarios) : IDiretorioDeUsuarios
	{
		public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(
			CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<UsuarioDoTenant>>(usuarios);
	}

	private sealed class NotificadorFalso : INotificadorDeMensagens
	{
		public List<MensagemParaEnviar> Lotes { get; } = [];

		public Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default)
		{
			Lotes.Add(mensagem);

			return Task.CompletedTask;
		}
	}

	private sealed class NotificadorQueFalha : INotificadorDeMensagens
	{
		public Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default) =>
			throw new NotificacaoIndisponivelException("Hub fora do ar.");
	}

	private sealed class RepositorioFalso : IPublicacaoRepository
	{
		public Task AddAsync(Publicacao publicacao, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<PublicacaoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<PublicacaoComSetor?>(null);

		public Task<PagedResult<PublicacaoDto>> ListarNoMuralAsync(
			MuralCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Create<PublicacaoDto>([], criteria.Page, 0));

		public Task<IReadOnlyList<PublicacaoDto>> ListarDoSetorAsync(
			string setorSlug, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<PublicacaoDto>>([]);
	}

	private sealed class SetorRepositorioFalso : ISetorRepository
	{
		public Task AddAsync(Setor setor, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(null);

		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(null);

		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(new Setor("Financeiro", "financeiro"));

		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(true);

		public Task<PagedResult<Setor>> SearchAsync(
			SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<Setor>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private static PublicarPublicacaoCommand Comando(
		Visibilidade visibilidade = Visibilidade.Empresa,
		PrioridadePublicacao prioridade = PrioridadePublicacao.Normal,
		DateTimeOffset? publicadoEm = null) =>
		new("financeiro", "Comunicado", "Corpo em **markdown**.", TipoPublicacao.Aviso, visibilidade,
			prioridade, publicadoEm ?? DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem.publicou", Autor);

	private static UsuarioDoTenant Usuario(string? email, params string[] roles) =>
		new(Guid.NewGuid(), email, roles);

	private static (PublicarPublicacaoHandler Handler, NotificadorFalso Notificador) Montar(
		params UsuarioDoTenant[] usuarios)
	{
		var notificador = new NotificadorFalso();
		var handler = new PublicarPublicacaoHandler(
			new RepositorioFalso(), new SetorRepositorioFalso(), new IntranetOptions(),
			new DiretorioFalso(usuarios), notificador, new NotificacaoOptions());

		return (handler, notificador);
	}

	[Fact]
	public async Task Normal_UsaSomenteOCanalInApp()
	{
		var (handler, notificador) = Montar(Usuario("a@x.com", "financeiro-user"));

		await handler.HandleAsync(Comando(prioridade: PrioridadePublicacao.Normal));

		notificador.Lotes.Should().ContainSingle()
			.Which.Canais.Should().BeEquivalentTo(["in_app"]);
	}

	[Fact]
	public async Task Importante_AcrescentaEmail()
	{
		var (handler, notificador) = Montar(Usuario("a@x.com", "financeiro-user"));

		await handler.HandleAsync(Comando(prioridade: PrioridadePublicacao.Importante));

		notificador.Lotes.Should().ContainSingle()
			.Which.Canais.Should().BeEquivalentTo(["in_app", "email"]);
	}

	[Fact]
	public async Task Urgente_AcrescentaCanalCorporativo()
	{
		var (handler, notificador) = Montar(Usuario("a@x.com", "financeiro-user"));

		await handler.HandleAsync(Comando(prioridade: PrioridadePublicacao.Urgente));

		notificador.Lotes.Should().ContainSingle()
			.Which.Canais.Should().BeEquivalentTo(["in_app", "email", "teams", "slack"]);
	}

	[Fact]
	public async Task VisibilidadeDeSetor_AlcancaSomenteQuemTemARole()
	{
		var doSetor = Usuario("dentro@x.com", "financeiro-user");
		var deFora = Usuario("fora@x.com", "diretoria-user");
		var (handler, notificador) = Montar(doSetor, deFora);

		await handler.HandleAsync(Comando(visibilidade: Visibilidade.Setor));

		notificador.Lotes.Should().ContainSingle()
			.Which.Destinos.Select(destino => destino.UsuarioId)
			.Should().BeEquivalentTo([doSetor.Id]);
	}

	[Fact]
	public async Task VisibilidadeDeEmpresa_AlcancaTodos()
	{
		var primeiro = Usuario("a@x.com", "financeiro-user");
		var segundo = Usuario("b@x.com", "diretoria-user");
		var (handler, notificador) = Montar(primeiro, segundo);

		await handler.HandleAsync(Comando(visibilidade: Visibilidade.Empresa));

		notificador.Lotes.Should().ContainSingle().Which.Destinos.Should().HaveCount(2);
	}

	[Fact]
	public async Task QuemPublicou_NaoRecebeOProprioAviso()
	{
		var autor = new UsuarioDoTenant(Autor, "autor@x.com", ["financeiro-admin"]);
		var outro = Usuario("outro@x.com", "financeiro-user");
		var (handler, notificador) = Montar(autor, outro);

		await handler.HandleAsync(Comando());

		notificador.Lotes.Should().ContainSingle()
			.Which.Destinos.Select(destino => destino.UsuarioId)
			.Should().BeEquivalentTo([outro.Id], "ninguém precisa ser avisado do que acabou de escrever");
	}

	[Fact]
	public async Task SemEmail_SaiDoLoteEEntraNaContagem()
	{
		var comEmail = Usuario("tem@x.com", "financeiro-user");
		var semEmail = Usuario(null, "financeiro-user");
		var (handler, notificador) = Montar(comEmail, semEmail);

		var resultado = await handler.HandleAsync(Comando(prioridade: PrioridadePublicacao.Importante));

		resultado.Value.Notificacao.SemEmail.Should().Be(1);
		notificador.Lotes.Should().ContainSingle()
			.Which.Destinos.Should().HaveCount(1, "o lote do Hub é tudo-ou-nada, então a partição é nossa");
	}

	[Fact]
	public async Task SemEmail_ContinuaRecebendoQuandoOLoteNaoPedeEmail()
	{
		var semEmail = Usuario(null, "financeiro-user");
		var (handler, notificador) = Montar(semEmail);

		var resultado = await handler.HandleAsync(Comando(prioridade: PrioridadePublicacao.Normal));

		resultado.Value.Notificacao.SemEmail.Should().Be(0, "in_app não precisa de e-mail");
		notificador.Lotes.Should().ContainSingle().Which.Destinos.Should().HaveCount(1);
	}

	[Fact]
	public async Task AcimaDeQuinhentos_Fatia()
	{
		var usuarios = Enumerable.Range(0, 501)
			.Select(indice => Usuario($"pessoa{indice}@x.com", "financeiro-user"))
			.ToArray();
		var (handler, notificador) = Montar(usuarios);

		var resultado = await handler.HandleAsync(Comando());

		notificador.Lotes.Should().HaveCount(2, "mil pessoas viram dois lotes, não mil chamadas");
		notificador.Lotes[0].Destinos.Should().HaveCount(500);
		notificador.Lotes[1].Destinos.Should().HaveCount(1);
		resultado.Value.Notificacao.Notificados.Should().Be(501);
	}

	[Fact]
	public async Task Agendada_EntregaNaEntradaNoAr()
	{
		var entradaNoAr = DateTimeOffset.UtcNow.AddDays(1);
		var (handler, notificador) = Montar(Usuario("a@x.com", "financeiro-user"));

		var resultado = await handler.HandleAsync(Comando(publicadoEm: entradaNoAr));

		notificador.Lotes.Should().ContainSingle()
			.Which.ProgramadaPara.Should().Be(entradaNoAr,
				"o Hub segura e-mail e sino até a hora, então o aviso nunca aponta para algo invisível");
		resultado.Value.Notificacao.ProgramadaPara.Should().Be(entradaNoAr);
	}

	[Fact]
	public async Task PublicadaAgora_NaoProgramaEntrega()
	{
		var (handler, notificador) = Montar(Usuario("a@x.com", "financeiro-user"));

		var resultado = await handler.HandleAsync(Comando());

		notificador.Lotes.Should().ContainSingle().Which.ProgramadaPara.Should().BeNull();
		resultado.Value.Notificacao.ProgramadaPara.Should().BeNull();
	}

	[Fact]
	public async Task HubForaDoAr_PublicaAssimMesmo()
	{
		var handler = new PublicarPublicacaoHandler(
			new RepositorioFalso(), new SetorRepositorioFalso(), new IntranetOptions(),
			new DiretorioFalso(Usuario("a@x.com", "financeiro-user")),
			new NotificadorQueFalha(), new NotificacaoOptions());

		var resultado = await handler.HandleAsync(Comando());

		resultado.IsSuccess.Should().BeTrue("a publicação é o que o usuário pediu; o aviso é consequência");
		resultado.Value.Notificacao.Indisponivel.Should().BeTrue();
	}

	[Fact]
	public async Task Resumo_VaiEmTextoPuroETruncado()
	{
		var (handler, notificador) = Montar(Usuario("a@x.com", "financeiro-user"));

		await handler.HandleAsync(Comando() with { Corpo = "Aviso **importante** para todos." });

		var resumo = notificador.Lotes.Should().ContainSingle().Subject.Resumo;
		resumo.Should().Contain("importante");
		resumo.Should().NotContain("**", "markdown cru apareceria literalmente no e-mail");
	}

	[Fact]
	public async Task Link_ApontaParaOPermalinkDaPublicacao()
	{
		var notificador = new NotificadorFalso();
		var handler = new PublicarPublicacaoHandler(
			new RepositorioFalso(), new SetorRepositorioFalso(), new IntranetOptions(),
			new DiretorioFalso(Usuario("a@x.com", "financeiro-user")), notificador,
			new NotificacaoOptions { UrlBase = "https://intranet.exemplo.com" });

		var resultado = await handler.HandleAsync(Comando());

		notificador.Lotes.Should().ContainSingle()
			.Which.Link.Should().Be($"https://intranet.exemplo.com/publicacoes/{resultado.Value.Publicacao.Id}");
	}

	[Fact]
	public async Task Origem_ETipo_CarregamORastroDaPublicacao()
	{
		var (handler, notificador) = Montar(Usuario("a@x.com", "financeiro-user"));

		var resultado = await handler.HandleAsync(Comando());

		var lote = notificador.Lotes.Should().ContainSingle().Subject;
		lote.Origem.Should().Be("mural");
		lote.Tipo.Should().Be(resultado.Value.Publicacao.Id.ToString(),
			"é o que torna o relatório de entrega uma consulta só");
	}
}
