# Notificação do Mural — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Quem publica no Mural alcança as pessoas — sino, e-mail e canal corporativo conforme a urgência —, e vê na mesma tela quem ficou de fora.

**Architecture:** Duas portas novas na Application (`IDiretorioDeUsuarios`, `INotificadorDeMensagens`) com adaptadores na Infrastructure para SecureGate e NotificationHub. Toda regra — canais por prioridade, destinatários por visibilidade, partição e fatiamento — vive no `PublicarPublicacaoHandler` e é testável com fakes, sem HTTP. Nenhuma infraestrutura nova: sem Hangfire, sem fila local, sem migration.

**Tech Stack:** .NET 10, ASP.NET Core MVC, `Secco.NotificationHub.Client` 0.4.0, `Secco.SecureGate.Client` 0.3.0, `Secco.SDK.AspNetCore` 0.5.1, xUnit + AwesomeAssertions.

**Spec:** [`docs/specs/2026-09-07-notificacao-mural-design.md`](../specs/2026-09-07-notificacao-mural-design.md)

## Global Constraints

- **Commits vão direto na `main`.** Sem branch de feature neste repositório.
- **Capacidade de plataforma não se implementa aqui** (ADR-0006). Fila, retry, entrega e estado de lida são do Hub.
- **Controller nunca acessa repositório nem `DbContext`** (ADR-0002). Sempre via handler; view recebe ViewModel.
- **View de página não escreve markup estrutural próprio** (ADR-0004). Sidebar, card, badge e sino vêm do tema.
- **Notificar nunca derruba publicar.** Qualquer falha de notificação vira relatório, nunca exceção que perca a publicação.
- **Nenhuma migration.** Nada deste plano é dado nosso.
- **Canais são strings**, exatamente: `in_app`, `email`, `teams`, `slack`.
- **`MaxBatchDestinations` do Hub é 500.** Público maior é fatiado.
- **Erro de negócio é `Result`**, exceção só para falha de infraestrutura (ADR-0004).
- **Log sem dado sensível** (ADR-0020): status e contagem, nunca corpo, headers ou e-mail.
- Build precisa terminar com **0 avisos**; a suíte inteira verde antes de cada commit.

---

## Task 1: Portas de notificação e o relatório

Contratos puros na Application. Nenhuma implementação real, nenhum HTTP — é o vocabulário que as tarefas seguintes consomem.

**Files:**
- Create: `src/Secco.Intranet.Application/Publicacoes/Notificacao/IDiretorioDeUsuarios.cs`
- Create: `src/Secco.Intranet.Application/Publicacoes/Notificacao/INotificadorDeMensagens.cs`
- Create: `src/Secco.Intranet.Application/Publicacoes/Notificacao/RelatorioDeNotificacao.cs`
- Create: `src/Secco.Intranet.Application/Publicacoes/Notificacao/NotificacaoOptions.cs`

**Interfaces:**
- Produces: `UsuarioDoTenant`, `IDiretorioDeUsuarios.ListarDoTenantAtualAsync`, `DestinoDaMensagem`, `MensagemParaEnviar`, `INotificadorDeMensagens.EnviarLoteAsync`, `RelatorioDeNotificacao`, `NotificacaoOptions`.

- [ ] **Step 1: Criar a porta do diretório de usuários**

```csharp
namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>
/// Usuário do tenant atual, como o SecureGate o conhece. Não há nome: o <c>UserDto</c> expõe
/// identificador, e-mail e roles, e é por isso que o relatório de notificação conta em vez de
/// nomear.
/// </summary>
/// <param name="Id">Identificador do usuário, usado como destino in-app.</param>
/// <param name="Email">E-mail cadastrado; nulo ou vazio significa que não recebe e-mail.</param>
/// <param name="Roles">Roles do usuário no tenant, de onde sai o vínculo com o setor (ADR-0001).</param>
public sealed record UsuarioDoTenant(Guid Id, string? Email, IReadOnlyList<string> Roles);

/// <summary>Porta de leitura do cadastro de usuários do tenant atual.</summary>
public interface IDiretorioDeUsuarios
{
	/// <summary>Lista os usuários do tenant atual com seus roles, numa única chamada.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Criar a porta de envio**

```csharp
namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>Um destino do lote.</summary>
/// <param name="UsuarioId">Identificador do usuário, para o inbox in-app.</param>
/// <param name="Email">E-mail já validado, ou nulo quando o lote não pede o canal de e-mail.</param>
public sealed record DestinoDaMensagem(Guid UsuarioId, string? Email);

/// <summary>Um conteúdo endereçado a muitos destinos.</summary>
/// <param name="Titulo">Título da notificação.</param>
/// <param name="Resumo">Corpo em texto puro, já truncado.</param>
/// <param name="Link">Destino do clique.</param>
/// <param name="Canais">Canais pedidos: <c>in_app</c>, <c>email</c>, <c>teams</c>, <c>slack</c>.</param>
/// <param name="Origem">Vai no campo <c>Source</c> do Hub.</param>
/// <param name="Tipo">Vai no campo <c>Type</c> do Hub.</param>
/// <param name="Destinos">Destinos deste lote, no máximo 500.</param>
/// <param name="ProgramadaPara">
/// Instante da entrega; nulo entrega agora. O Hub segura e-mail <b>e</b> item do sino até a
/// hora — sem isso, o aviso apareceria no sino avisando sobre algo que o mural ainda não
/// mostra.
/// </param>
public sealed record MensagemParaEnviar(
	string Titulo,
	string Resumo,
	string Link,
	IReadOnlyList<string> Canais,
	string Origem,
	string Tipo,
	IReadOnlyList<DestinoDaMensagem> Destinos,
	DateTimeOffset? ProgramadaPara);

/// <summary>
/// Porta de envio. Fila, retry e entrega são do Hub (ADR-0006) — daqui sai uma chamada por
/// lote e nada mais.
/// </summary>
public interface INotificadorDeMensagens
{
	/// <summary>Envia um lote.</summary>
	/// <param name="mensagem">Conteúdo e destinos.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <exception cref="NotificacaoIndisponivelException">Quando o Hub não aceita o lote.</exception>
	Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default);
}

/// <summary>
/// O Hub não aceitou o lote. É falha de infraestrutura, não de negócio (ADR-0004) — por isso
/// exceção; quem publica a converte em relatório, porque publicar não pode falhar por causa
/// do aviso.
/// </summary>
public sealed class NotificacaoIndisponivelException : Exception
{
	/// <summary>Cria a exceção.</summary>
	/// <param name="message">Descrição sem dado sensível (ADR-0020).</param>
	/// <param name="innerException">Falha original.</param>
	public NotificacaoIndisponivelException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}

	/// <summary>Construtor sem argumentos, exigido pela convenção de exceções.</summary>
	public NotificacaoIndisponivelException()
		: this("O serviço de notificação está indisponível.")
	{
	}

	/// <summary>Construtor só com mensagem.</summary>
	/// <param name="message">Descrição.</param>
	public NotificacaoIndisponivelException(string message)
		: this(message, null)
	{
	}
}
```

- [ ] **Step 3: Criar o relatório**

```csharp
namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>
/// O que aconteceu com o aviso de uma publicação. Conta em vez de nomear: o cadastro de
/// usuários do SecureGate não expõe nome.
/// </summary>
/// <param name="Notificados">Quantas pessoas entraram nos lotes enviados.</param>
/// <param name="SemEmail">Quantas ficaram fora do canal de e-mail por não ter endereço cadastrado.</param>
/// <param name="ProgramadaPara">
/// Quando a entrega acontece, se for no futuro. Nulo significa que já saiu.
/// </param>
/// <param name="Indisponivel">O Hub não aceitou o lote; a publicação foi gravada mesmo assim.</param>
public sealed record RelatorioDeNotificacao(
	int Notificados,
	int SemEmail,
	DateTimeOffset? ProgramadaPara,
	bool Indisponivel)
{
	/// <summary>Nada foi enviado porque o Hub não respondeu.</summary>
	/// <param name="semEmail">Quantos já haviam sido descartados por falta de e-mail.</param>
	public static RelatorioDeNotificacao DeIndisponivel(int semEmail) =>
		new(0, semEmail, ProgramadaPara: null, Indisponivel: true);

	/// <summary>Nenhum aviso a dar: publicação sem destinatários e sem falha.</summary>
	public static RelatorioDeNotificacao Silencioso() => new(0, 0, null, false);
}
```

- [ ] **Step 4: Criar as options**

```csharp
namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>
/// Configuração da notificação, seção <c>Intranet:Notificacao</c>. Bind lazy feito pela
/// Infrastructure — a Application não conhece configuração.
/// </summary>
public sealed class NotificacaoOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	public const string SectionKey = "Intranet:Notificacao";

	/// <summary>
	/// URL pública da Intranet, sem barra final — por exemplo <c>https://intranet.exemplo.com</c>.
	/// O link do aviso precisa ser absoluto: quem recebe por e-mail está fora da aplicação, e
	/// um caminho relativo não abre. Vazia significa link relativo, que serve ao sino e
	/// degrada no e-mail.
	/// </summary>
	public string UrlBase { get; set; } = string.Empty;

	/// <summary>Teto do resumo enviado na notificação, em caracteres.</summary>
	public int TamanhoDoResumo { get; set; } = 300;
}
```

- [ ] **Step 5: Compilar**

Run: `dotnet build src/Secco.Intranet.Application`
Expected: sucesso, 0 avisos. Nada consome estes tipos ainda.

- [ ] **Step 6: Commit**

```bash
git add src/Secco.Intranet.Application/Publicacoes/Notificacao
git commit -m "feat(notificacao): portas de diretorio e envio, com relatorio que conta

O relatorio conta em vez de nomear porque o UserDto do SecureGate expoe id,
e-mail e roles - nao ha nome a mostrar."
```

---

## Task 2: A regra de notificação no publicar

O coração. Tudo aqui é testável com fakes, sem HTTP e sem banco.

**Files:**
- Modify: `src/Secco.Intranet.Application/Publicacoes/PublicarPublicacaoHandler.cs`
- Create: `src/Secco.Intranet.Application/Publicacoes/Notificacao/CanaisDaPrioridade.cs`
- Create: `src/Secco.Intranet.Application/Publicacoes/Notificacao/AvisoDePublicacao.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/AvisoDePublicacaoTests.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/SetorController.cs`

**Interfaces:**
- Consumes: `IDiretorioDeUsuarios`, `INotificadorDeMensagens`, `RelatorioDeNotificacao`, `NotificacaoOptions` (Task 1).
- Produces: `PublicacaoPublicadaDto(PublicacaoDto Publicacao, RelatorioDeNotificacao Notificacao)`; `PublicarPublicacaoCommand` ganha `Guid? CriadoPorId` como último parâmetro; `PublicarPublicacaoHandler.HandleAsync` passa a devolver `Task<Result<PublicacaoPublicadaDto>>`.

- [ ] **Step 1: Escrever os testes**

```csharp
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
		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(new Setor("Financeiro", "financeiro", false));

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(null);

		public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(true);

		public Task AddAsync(Setor setor, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<PagedResult<SetorDto>> SearchAsync(
			SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Create<SetorDto>([], criteria.Page, 0));
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
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --filter FullyQualifiedName~AvisoDePublicacaoTests`
Expected: falha de compilação — `PublicacaoPublicadaDto` não existe, `PublicarPublicacaoCommand` não tem `CriadoPorId`, e o construtor do handler tem três parâmetros, não seis.

- [ ] **Step 3: Criar a tabela de canais**

```csharp
using Secco.Intranet.Domain.Publicacoes;

namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>
/// Prioridade decide canal. Quem publica escolhe a urgência, não as caixinhas: escolher canal
/// é decisão de produto, e ninguém erra menos marcando opções uma a uma.
/// </summary>
internal static class CanaisDaPrioridade
{
	/// <summary>Item no inbox in-app.</summary>
	internal const string InApp = "in_app";

	/// <summary>Envio por e-mail.</summary>
	internal const string Email = "email";

	/// <summary>Canal do Microsoft Teams.</summary>
	internal const string Teams = "teams";

	/// <summary>Canal do Slack.</summary>
	internal const string Slack = "slack";

	/// <summary>Canais de uma urgência.</summary>
	/// <param name="prioridade">Urgência escolhida por quem publica.</param>
	internal static IReadOnlyList<string> De(PrioridadePublicacao prioridade) => prioridade switch
	{
		PrioridadePublicacao.Urgente => [InApp, Email, Teams, Slack],
		PrioridadePublicacao.Importante => [InApp, Email],
		_ => [InApp],
	};
}
```

- [ ] **Step 4: Criar o montador do aviso**

```csharp
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;

namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>
/// Transforma uma publicação recém-criada nos lotes que o Hub recebe. Fica separado do
/// handler porque é a parte que mais tem regra e menos tem dependência: nada aqui toca banco,
/// HTTP ou relógio.
/// </summary>
internal static class AvisoDePublicacao
{
	/// <summary>Teto de destinos por lote, imposto pelo <c>MaxBatchDestinations</c> do Hub.</summary>
	internal const int MaxDestinosPorLote = 500;

	/// <summary>Valor de <c>Source</c> em toda notificação criada pelo Mural.</summary>
	internal const string Origem = "mural";

	/// <summary>Quem deve receber o aviso desta publicação.</summary>
	/// <param name="usuarios">Usuários do tenant.</param>
	/// <param name="visibilidade">Visibilidade da publicação.</param>
	/// <param name="setorSlug">Slug do setor dono.</param>
	/// <param name="autorId">Quem publicou; sai da lista.</param>
	internal static IReadOnlyList<UsuarioDoTenant> Destinatarios(
		IReadOnlyList<UsuarioDoTenant> usuarios,
		Visibilidade visibilidade,
		string setorSlug,
		Guid? autorId)
	{
		var roles = new[] { $"{setorSlug}-admin", $"{setorSlug}-user" };

		return usuarios
			.Where(usuario => autorId is null || usuario.Id != autorId)
			.Where(usuario => visibilidade == Visibilidade.Empresa
				|| usuario.Roles.Any(role => roles.Contains(role, StringComparer.OrdinalIgnoreCase)))
			.ToList();
	}

	/// <summary>
	/// Separa quem o lote pode levar de quem ficaria de fora. O lote do Hub é tudo-ou-nada e
	/// reprova inteiro por um destino inválido, então a partição acontece aqui — e sai de
	/// graça, porque a lista já está sendo percorrida.
	/// </summary>
	/// <param name="destinatarios">Quem deve receber.</param>
	/// <param name="canais">Canais pedidos.</param>
	internal static (IReadOnlyList<DestinoDaMensagem> Validos, int SemEmail) Particionar(
		IReadOnlyList<UsuarioDoTenant> destinatarios,
		IReadOnlyList<string> canais)
	{
		var exigeEmail = canais.Contains(CanaisDaPrioridade.Email, StringComparer.Ordinal);
		var validos = new List<DestinoDaMensagem>();
		var semEmail = 0;

		foreach (var usuario in destinatarios)
		{
			var temEmail = !string.IsNullOrWhiteSpace(usuario.Email);

			if (exigeEmail && !temEmail)
			{
				semEmail++;

				continue;
			}

			validos.Add(new DestinoDaMensagem(usuario.Id, exigeEmail ? usuario.Email : null));
		}

		return (validos, semEmail);
	}

	/// <summary>Fatia os destinos em lotes do tamanho que o Hub aceita.</summary>
	/// <param name="destinos">Destinos válidos.</param>
	internal static IEnumerable<IReadOnlyList<DestinoDaMensagem>> Fatiar(
		IReadOnlyList<DestinoDaMensagem> destinos) =>
		destinos.Chunk(MaxDestinosPorLote).Select(bloco => (IReadOnlyList<DestinoDaMensagem>)bloco);
}
```

- [ ] **Step 5: Ligar no handler**

Substitua `src/Secco.Intranet.Application/Publicacoes/PublicarPublicacaoHandler.cs` inteiro:

```csharp
using Markdig;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Comando de publicação.</summary>
/// <param name="SetorSlug">Slug do setor dono.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Corpo">Corpo em Markdown.</param>
/// <param name="Tipo">Natureza.</param>
/// <param name="Visibilidade">Quem enxerga.</param>
/// <param name="Prioridade">Urgência.</param>
/// <param name="PublicadoEm">Entrada no ar.</param>
/// <param name="ExpiraEm">Expiração.</param>
/// <param name="CriadoPor">Quem publica, como aparece na tela.</param>
/// <param name="CriadoPorId">
/// Identificador de quem publica, para excluí-lo do próprio aviso. Não é persistido: casar
/// nome de exibição com usuário seria frágil, e guardar o id exigiria migration sem uso —
/// edição não re-notifica.
/// </param>
public sealed record PublicarPublicacaoCommand(
	string? SetorSlug,
	string? Titulo,
	string? Corpo,
	TipoPublicacao Tipo,
	Visibilidade Visibilidade,
	PrioridadePublicacao Prioridade,
	DateTimeOffset PublicadoEm,
	DateTimeOffset? ExpiraEm,
	string CriadoPor,
	Guid? CriadoPorId = null);

/// <summary>A publicação criada e o que aconteceu com o aviso dela.</summary>
/// <param name="Publicacao">A publicação persistida.</param>
/// <param name="Notificacao">Relatório do aviso.</param>
public sealed record PublicacaoPublicadaDto(PublicacaoDto Publicacao, RelatorioDeNotificacao Notificacao);

/// <summary>
/// Cria uma publicação no setor informado e avisa quem pode vê-la. Notificar nunca derruba
/// publicar: a publicação é o que o usuário pediu, o aviso é consequência.
/// </summary>
/// <param name="repository">Persistência de publicações.</param>
/// <param name="setorRepository">Consulta de setores.</param>
/// <param name="limites">Limites de entrada do produto.</param>
/// <param name="diretorio">Cadastro de usuários do tenant.</param>
/// <param name="notificador">Envio de avisos.</param>
/// <param name="notificacaoOptions">Configuração do aviso.</param>
public sealed class PublicarPublicacaoHandler(
	IPublicacaoRepository repository,
	ISetorRepository setorRepository,
	IntranetOptions limites,
	IDiretorioDeUsuarios diretorio,
	INotificadorDeMensagens notificador,
	NotificacaoOptions notificacaoOptions)
{
	private static readonly MarkdownPipeline PipelineDeTexto = new MarkdownPipelineBuilder()
		.DisableHtml()
		.Build();

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PublicacaoPublicadaDto>> HandleAsync(
		PublicarPublicacaoCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var erro = ValidacaoDePublicacao.Validar(
			command.Titulo, command.Corpo, command.PublicadoEm, command.ExpiraEm, limites.MaxNameLength);

		if (erro is not null)
		{
			return erro;
		}

		if (string.IsNullOrWhiteSpace(command.SetorSlug))
		{
			return IntranetErrors.Setores.NotFound;
		}

		var setor = await setorRepository.GetBySlugAsync(command.SetorSlug, cancellationToken).ConfigureAwait(false);

		if (setor is null || !setor.Ativo)
		{
			return IntranetErrors.Setores.NotFound;
		}

		var publicacao = new Publicacao(
			setor.Id, command.Titulo!, command.Corpo!, command.Tipo, command.Visibilidade,
			command.Prioridade, command.PublicadoEm, command.ExpiraEm, command.CriadoPor);

		await repository.AddAsync(publicacao, cancellationToken).ConfigureAwait(false);

		var dto = Projecao.De(new PublicacaoComSetor(publicacao, setor.Nome, setor.Slug));
		var relatorio = await AvisarAsync(dto, command.CriadoPorId, cancellationToken).ConfigureAwait(false);

		return new PublicacaoPublicadaDto(dto, relatorio);
	}

	private async Task<RelatorioDeNotificacao> AvisarAsync(
		PublicacaoDto publicacao,
		Guid? autorId,
		CancellationToken cancellationToken)
	{
		// Publicação agendada não sai sem aviso, e o aviso não sai antes da hora: o Hub
		// entrega no instante pedido, segurando e-mail E item do sino (secco-platform#24).
		var programadaPara = publicacao.PublicadoEm > DateTimeOffset.UtcNow
			? publicacao.PublicadoEm
			: (DateTimeOffset?)null;

		var canais = CanaisDaPrioridade.De(publicacao.Prioridade);
		var usuarios = await diretorio.ListarDoTenantAtualAsync(cancellationToken).ConfigureAwait(false);

		var destinatarios = AvisoDePublicacao.Destinatarios(
			usuarios, publicacao.Visibilidade, publicacao.SetorSlug, autorId);

		var (validos, semEmail) = AvisoDePublicacao.Particionar(destinatarios, canais);

		if (validos.Count == 0)
		{
			return new RelatorioDeNotificacao(0, semEmail, programadaPara, false);
		}

		var resumo = Resumir(publicacao.Corpo);
		var link = MontarLink(publicacao.Id);

		try
		{
			foreach (var bloco in AvisoDePublicacao.Fatiar(validos))
			{
				await notificador
					.EnviarLoteAsync(
						new MensagemParaEnviar(
							publicacao.Titulo, resumo, link, canais,
							AvisoDePublicacao.Origem, publicacao.Id.ToString(), bloco, programadaPara),
						cancellationToken)
					.ConfigureAwait(false);
			}
		}
		catch (NotificacaoIndisponivelException)
		{
			return RelatorioDeNotificacao.DeIndisponivel(semEmail);
		}

		return new RelatorioDeNotificacao(validos.Count, semEmail, programadaPara, false);
	}

	private string MontarLink(Guid publicacaoId)
	{
		var caminho = $"/publicacoes/{publicacaoId}";

		return string.IsNullOrWhiteSpace(notificacaoOptions.UrlBase)
			? caminho
			: $"{notificacaoOptions.UrlBase.TrimEnd('/')}{caminho}";
	}

	private string Resumir(string corpo)
	{
		// Texto puro: o Hub tem um Message só, servindo sino e e-mail, e markdown cru
		// apareceria literalmente no e-mail.
		var texto = Markdown.ToPlainText(corpo, PipelineDeTexto).Replace('\n', ' ').Trim();

		return texto.Length <= notificacaoOptions.TamanhoDoResumo
			? texto
			: texto[..notificacaoOptions.TamanhoDoResumo].TrimEnd() + "…";
	}
}
```

- [ ] **Step 6: Acrescentar o Markdig à Application**

Em `src/Secco.Intranet.Application/Secco.Intranet.Application.csproj`, no `ItemGroup` de pacotes:

```xml
    <PackageReference Include="Markdig" />
```

O pacote já está no `Directory.Packages.props` desde a Task 6 do plano do Mural.

- [ ] **Step 7: Ajustar o controller**

Em `src/Secco.Intranet.Web/Controllers/SetorController.cs`, ação `SalvarAviso`, a chamada de publicar passa a informar o id do autor e a ler a publicação de dentro do resultado.

Troque a construção do comando:

```csharp
			? await publicarPublicacaoHandler.HandleAsync(
				new PublicarPublicacaoCommand(slug, form.Titulo, form.Corpo, form.Tipo, form.Visibilidade,
					form.Prioridade, publicadoEm, expiraEm, User.Identity?.Name ?? "desconhecido",
					IdDoUsuarioAtual()),
				cancellationToken).ConfigureAwait(false)
```

Como publicar e editar passam a devolver tipos diferentes, separe os dois caminhos. Substitua o bloco que hoje faz `var resultado = form.Id == Guid.Empty ? ... : ...;` e o tratamento seguinte por:

```csharp
		string titulo;
		RelatorioDeNotificacao? relatorio = null;

		if (form.Id == Guid.Empty)
		{
			var publicado = await publicarPublicacaoHandler.HandleAsync(
				new PublicarPublicacaoCommand(slug, form.Titulo, form.Corpo, form.Tipo, form.Visibilidade,
					form.Prioridade, publicadoEm, expiraEm, User.Identity?.Name ?? "desconhecido",
					IdDoUsuarioAtual()),
				cancellationToken).ConfigureAwait(false);

			if (publicado.IsFailure)
			{
				return await ComErroAsync(slug, form, publicado.Error.Description, cancellationToken)
					.ConfigureAwait(false);
			}

			titulo = publicado.Value.Publicacao.Titulo;
			relatorio = publicado.Value.Notificacao;
		}
		else
		{
			var editado = await editarPublicacaoHandler.HandleAsync(
				new EditarPublicacaoCommand(form.Id, SetorAcesso.SlugsAdministrados(User), exigirVinculo,
					form.Titulo, form.Corpo, form.Tipo, form.Visibilidade, form.Prioridade,
					publicadoEm, expiraEm),
				cancellationToken).ConfigureAwait(false);

			if (editado.IsFailure)
			{
				return await ComErroAsync(slug, form, editado.Error.Description, cancellationToken)
					.ConfigureAwait(false);
			}

			titulo = editado.Value.Titulo;
		}

		TempData["Mensagem"] = MensagemDeSalvamento.Montar(titulo, relatorio);

		return RedirectToAction(nameof(Avisos), new { slug });
	}

	/// <summary>
	/// Identificador do usuário atual, quando a autenticação está ativa. Sem ele — o modo
	/// aberto de DEV — ninguém é excluído do próprio aviso, o que é inofensivo.
	/// </summary>
	private Guid? IdDoUsuarioAtual() =>
		Guid.TryParse(User.FindFirst(SeccoClaims.Subject)?.Value, out var id) ? id : null;

	private async Task<IActionResult> ComErroAsync(
		string slug,
		PublicacaoFormViewModel form,
		string mensagem,
		CancellationToken cancellationToken)
	{
		ModelState.AddModelError(string.Empty, mensagem);

		var model = await MontarAvisosAsync(slug, form, cancellationToken).ConfigureAwait(false);

		return model is null ? NotFound() : View(nameof(Avisos), model);
	}
```

Acrescente os `using` que faltam ao topo do arquivo:

```csharp
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.SharedKernel.Constants;
```

- [ ] **Step 8: Criar o texto do relatório**

Create: `src/Secco.Intranet.Web/Models/Publicacoes/MensagemDeSalvamento.cs`

```csharp
using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Web.Models.Publicacoes;

/// <summary>
/// Traduz o relatório de notificação para a frase que quem publicou lê. Contar a verdade na
/// mesma tela é o que dispensa qualquer consulta posterior de status.
/// </summary>
public static class MensagemDeSalvamento
{
	/// <summary>Monta a mensagem.</summary>
	/// <param name="titulo">Título da publicação salva.</param>
	/// <param name="relatorio">Relatório do aviso; nulo quando a operação foi uma edição.</param>
	public static string Montar(string titulo, RelatorioDeNotificacao? relatorio)
	{
		var mensagem = $"Publicação \"{titulo}\" salva.";

		if (relatorio is null)
		{
			return mensagem;
		}

		if (relatorio.ProgramadaPara is not null)
		{
			return mensagem
				+ $" O aviso será enviado em {relatorio.ProgramadaPara.Value.ToLocalTime():dd/MM/yyyy 'às' HH:mm}, quando a publicação entrar no ar.";
		}

		if (relatorio.Indisponivel)
		{
			return mensagem + " O aviso não pôde ser enviado agora — a publicação está no ar mesmo assim.";
		}

		if (relatorio.Notificados > 0)
		{
			mensagem += $" {relatorio.Notificados} {(relatorio.Notificados == 1 ? "pessoa avisada" : "pessoas avisadas")}.";
		}

		if (relatorio.SemEmail > 0)
		{
			mensagem += $" {relatorio.SemEmail} sem e-mail cadastrado não {(relatorio.SemEmail == 1 ? "recebeu" : "receberam")} por e-mail.";
		}

		return mensagem;
	}
}
```

- [ ] **Step 9: Os adaptadores no-op**

Sem eles a Application não resolve as portas e a suíte de integração quebra. Não dependem de
pacote nenhum, então entram aqui e não na tarefa dos adaptadores reais.

```csharp
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter no-op de <see cref="INotificadorDeMensagens"/> — modo DEV/Testing, quando a seção
/// <c>Intranet:Notificacao:Hub</c> não está configurada. Publicar continua funcionando e o
/// relatório sai zerado, que é a verdade: não há para onde enviar.
/// </summary>
/// <param name="logger">Log em nível Debug.</param>
public sealed class NotificadorSilencioso(ILogger<NotificadorSilencioso> logger) : INotificadorDeMensagens
{
	/// <inheritdoc />
	public Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default)
	{
		logger.LogDebug("notificação desativada — seção Intranet:Notificacao:Hub ausente");

		return Task.CompletedTask;
	}
}
```

```csharp
using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter no-op de <see cref="IDiretorioDeUsuarios"/> — sem SecureGate configurado não há
/// cadastro a consultar, e ninguém é destinatário.
/// </summary>
public sealed class DiretorioVazio : IDiretorioDeUsuarios
{
	/// <inheritdoc />
	public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(
		CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<UsuarioDoTenant>>([]);
}
```

- [ ] **Step 10: Registrar as options e as portas**

A Application agora exige `IDiretorioDeUsuarios`, `INotificadorDeMensagens` e `NotificacaoOptions` no contêiner. Registre no `IntranetInfrastructureExtensions.cs`, junto das outras options:

```csharp
		services.AddSingleton(sp => BindSection(sp, NotificacaoOptions.SectionKey, new NotificacaoOptions()));

		// Os adaptadores reais chegam na Task 3, com o pacote do Hub. Até lá o produto usa os
		// no-op: publicar funciona e o relatório sai zerado, que é a verdade — não há para
		// onde enviar.
		services.AddScoped<IDiretorioDeUsuarios, DiretorioVazio>();
		services.AddScoped<INotificadorDeMensagens, NotificadorSilencioso>();
```

Esta tarefa fecha verde por conta própria: nada aqui depende do `Secco.NotificationHub.Client`.

- [ ] **Step 11: Rodar a suíte**

Run: `dotnet build && dotnet test`
Expected: build com 0 avisos e a suíte inteira verde, incluindo os 15 testes novos de
`AvisoDePublicacaoTests`.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "feat(notificacao): regra do aviso dentro do publicar

Canais por prioridade, destinatarios por visibilidade, autor fora, particao de
quem nao tem e-mail e fatiamento em 500 - tudo testavel com fakes, sem HTTP.

Publicacao agendada nao notifica: o Hub ainda nao entrega em data futura
(secco-platform#24), e avisar hoje apontaria para algo que o mural nao mostra."
```

---

## Task 3: Adaptadores de SecureGate e NotificationHub

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Notificacao/SecureGateDiretorioDeUsuarios.cs`
- Create: `src/Secco.Intranet.Infrastructure/Notificacao/NotificationHubNotificador.cs`
- Create: `src/Secco.Intranet.Infrastructure/Notificacao/NotificadorSilencioso.cs`
- Create: `src/Secco.Intranet.Infrastructure/Notificacao/DiretorioVazio.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Secco.Intranet.Infrastructure.csproj`
- Modify: `Directory.Packages.props`

**Interfaces:**
- Consumes: `IDiretorioDeUsuarios`, `INotificadorDeMensagens`, `NotificacaoIndisponivelException` (Task 1).
- Produces: registro de DI que resolve o adaptador real quando configurado e o no-op quando não.

- [ ] **Step 1: Apurar a versão publicada e adicionar o pacote**

O `ScheduledFor` entrou no monorepo **depois** da tag `notificationhub-client/v0.4.0`, então a
0.4.0 não serve. Descubra a versão que a release publicou antes de fixar — issue fechada não é
pacote publicado:

```bash
MSYS_NO_PATHCONV=1 gh api "user/packages/nuget/Secco.NotificationHub.Client/versions" --jq '.[].name'
```

Use a maior versão listada. Se ainda for `0.4.0`, **pare**: a release não saiu, e esta tarefa
não pode ser feita.

Em `Directory.Packages.props`, no `ItemGroup Label="Secco Platform"`:

```xml
    <!-- SearchNotifications (relatorio de entrega numa chamada) e ScheduledFor (entrega na
         entrada no ar). -->
    <PackageVersion Include="Secco.NotificationHub.Client" Version="<versão apurada abaixo>" />
```

Em `src/Secco.Intranet.Infrastructure/Secco.Intranet.Infrastructure.csproj`:

```xml
    <PackageReference Include="Secco.NotificationHub.Client" />
```

- [ ] **Step 2: Adaptador do diretório**

```csharp
using System.Net;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter real de <see cref="IDiretorioDeUsuarios"/>: uma chamada a <c>ListUsers</c> devolve
/// os usuários do tenant já com os roles, então o filtro por setor acontece em memória, sem
/// N consultas.
/// </summary>
/// <param name="client">Client administrativo do SecureGate.</param>
/// <param name="tenantContext">Tenant da requisição atual (ADR-0005).</param>
/// <param name="logger">Log de falhas, sem dado sensível (ADR-0020).</param>
public sealed class SecureGateDiretorioDeUsuarios(
	ISecureGateClient client,
	ITenantContext tenantContext,
	ILogger<SecureGateDiretorioDeUsuarios> logger) : IDiretorioDeUsuarios
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(
		CancellationToken cancellationToken = default)
	{
		if (!tenantContext.IsResolved)
		{
			return [];
		}

		try
		{
			var usuarios = await client
				.ListUsersAsync(tenantContext.TenantId!.Value, cancellationToken)
				.ConfigureAwait(false);

			return [.. usuarios.Select(usuario => new UsuarioDoTenant(
				usuario.Id,
				usuario.Email,
				usuario.Roles is null ? [] : [.. usuario.Roles]))];
		}
		catch (ApiException apiException)
		{
			logger.LogWarning(
				"Falha ao listar usuários do tenant no SecureGate (status {StatusCode}).",
				apiException.StatusCode);

			return [];
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao listar usuários do tenant no SecureGate.");

			return [];
		}
	}
}
```

> Lista vazia significa "ninguém a avisar", e o relatório sai zerado. Não derruba a publicação, que é a regra deste recurso inteiro.

- [ ] **Step 3: Adaptador do Hub**

```csharp
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.NotificationHub.Client;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter real de <see cref="INotificadorDeMensagens"/>: um lote vira uma chamada a
/// <c>DispatchNotificationBatch</c>. Fila, retry e entrega são do Hub (ADR-0006).
/// </summary>
/// <param name="client">Client do NotificationHub.</param>
/// <param name="logger">Log de falhas, sem dado sensível (ADR-0020).</param>
public sealed class NotificationHubNotificador(
	INotificationHubClient client,
	ILogger<NotificationHubNotificador> logger) : INotificadorDeMensagens
{
	/// <inheritdoc />
	public async Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(mensagem);

		var request = new DispatchNotificationBatchRequest
		{
			Title = mensagem.Titulo,
			Message = mensagem.Resumo,
			Link = mensagem.Link,
			Source = mensagem.Origem,
			Type = mensagem.Tipo,
			Channels = [.. mensagem.Canais],
			ScheduledFor = mensagem.ProgramadaPara,
			Destinations =
			[
				.. mensagem.Destinos.Select(destino => new NotificationDestination
				{
					UserId = destino.UsuarioId,
					Recipient = destino.Email,
				}),
			],
		};

		try
		{
			await client.DispatchNotificationBatchAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (ApiException apiException)
		{
			logger.LogWarning(
				"O NotificationHub recusou um lote de {Quantidade} destino(s) (status {StatusCode}).",
				mensagem.Destinos.Count,
				apiException.StatusCode);

			throw new NotificacaoIndisponivelException(
				"O serviço de notificação recusou o envio.", apiException);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(
				httpRequestException,
				"Falha de rede ao enviar um lote de {Quantidade} destino(s) ao NotificationHub.",
				mensagem.Destinos.Count);

			throw new NotificacaoIndisponivelException(
				"O serviço de notificação está indisponível.", httpRequestException);
		}
	}
}
```

- [ ] **Step 4: Compor no DI**

Em `NotificacaoOptions`, acrescente a URL do Hub:

```csharp
	/// <summary>URL base do NotificationHub. Vazia desliga o envio (modo DEV/Testing).</summary>
	public string HubUrl { get; set; } = string.Empty;
```

`AddIntranetInfrastructure` recebe **apenas** `IServiceCollection` — não há `IConfiguration`
aqui, e ler configuração direto quebraria o bind lazy que o arquivo inteiro usa. Então o
`HttpClient` é registrado sempre, lendo a URL na resolução, e quem decide entre adapter real e
no-op são as options resolvidas do contêiner. É exatamente o padrão do
`AddSecureGateAdminClient()`.

Em `IntranetInfrastructureExtensions.cs`, junto das outras options:

```csharp
		services.AddSingleton(sp => BindSection(sp, NotificacaoOptions.SectionKey, new NotificacaoOptions()));
```

E, logo após o registro do `ISetorAccessProvisioner`:

```csharp
		// Store próprio para o Hub: um por recurso/scope (least privilege), como o SecureGate faz.
		var tokenStoreDoHub = new SeccoAccessTokenStore();

		services.AddHttpClient<INotificationHubClient, NotificationHubClient>()
			.ConfigureHttpClient((serviceProvider, client) =>
			{
				var notificacao = serviceProvider.GetRequiredService<NotificacaoOptions>();

				if (!string.IsNullOrWhiteSpace(notificacao.HubUrl))
				{
					client.BaseAddress = new Uri(notificacao.HubUrl, UriKind.Absolute);
				}
			})
			.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
			{
				// AddNotificationHubClient() do pacote só aceita BaseUrl, mas todo endpoint do
				// Hub exige permissão — então o handler de credenciais entra à mão aqui.
				var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

				if (credenciais.IsConfigured)
				{
					credenciais.Validate(requireProduct: false);

					handlers.Add(new SeccoClientCredentialsHandler(
						credenciais.BaseUrl!,
						credenciais.ClientId!,
						credenciais.ClientSecret!,
						"notifications:read notifications:write",
						tokenStoreDoHub));
				}
			});

		services.AddScoped<IDiretorioDeUsuarios>(serviceProvider =>
		{
			var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

			return credenciais.IsConfigured
				? ActivatorUtilities.CreateInstance<SecureGateDiretorioDeUsuarios>(serviceProvider)
				: ActivatorUtilities.CreateInstance<DiretorioVazio>(serviceProvider);
		});

		services.AddScoped<INotificadorDeMensagens>(serviceProvider =>
			string.IsNullOrWhiteSpace(serviceProvider.GetRequiredService<NotificacaoOptions>().HubUrl)
				? ActivatorUtilities.CreateInstance<NotificadorSilencioso>(serviceProvider)
				: ActivatorUtilities.CreateInstance<NotificationHubNotificador>(serviceProvider));
```

Acrescente os `using` necessários no topo: `Secco.Intranet.Application.Publicacoes.Notificacao`,
`Secco.Intranet.Infrastructure.Notificacao`, `Secco.NotificationHub.Client`,
`Secco.SDK.AspNetCore.Authentication`, `Secco.SecureGate.Client`.

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet build && dotnet test`
Expected: build com 0 avisos, suíte verde. Os testes de integração voltam a passar — o host resolve os no-op, porque nem `Secco:SecureGate` nem `Intranet:Notificacao:Hub` estão configurados em Testing.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(notificacao): adaptadores de SecureGate e NotificationHub

O client do Hub e registrado a mao com SeccoClientCredentialsHandler:
AddNotificationHubClient() so aceita BaseUrl, mas todos os endpoints do Hub
exigem permissao."
```

---

## Task 4: Permalink `/publicacoes/{id}`

O destino que o link da notificação precisa, e que não existia.

**Files:**
- Create: `src/Secco.Intranet.Application/Publicacoes/ObterPublicacaoHandler.cs`
- Create: `src/Secco.Intranet.Web/Views/Mural/Detalhe.cshtml`
- Modify: `src/Secco.Intranet.Web/Controllers/MuralController.cs`
- Modify: `src/Secco.Intranet.Web/Models/Mural/MuralViewModel.cs`
- Create: `tests/Secco.Intranet.Tests/Integration/PermalinkPublicacaoTests.cs`

**Interfaces:**
- Consumes: `IPublicacaoRepository.GetByIdAsync`, `PublicacaoComSetor` (já existentes).
- Produces: `ObterPublicacaoQuery(Guid Id, IReadOnlyList<string> SetoresDoLeitor, bool ExigirVisibilidade)`; `ObterPublicacaoHandler.HandleAsync(...) → Task<Result<PublicacaoDto>>`; `PublicacaoDetalheViewModel(PublicacaoViewModel Publicacao)`.

- [ ] **Step 1: Escrever o teste**

```csharp
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O permalink é o destino do aviso. Inexistente e fora do alcance devolvem a mesma resposta,
/// pelo motivo de sempre: distinguir revelaria a existência.
/// </summary>
public class PermalinkPublicacaoTests(IntranetWebFactory factory)
	: IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		return client;
	}

	private async Task<Guid> SemearAsync(DateTimeOffset publicadoEm)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var slug = $"perma-{Guid.NewGuid():N}"[..20];
		var criarSetor = escopo.ServiceProvider.GetRequiredService<CreateSetorHandler>();
		var setor = await criarSetor.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));
		setor.IsSuccess.Should().BeTrue();

		var repositorio = escopo.ServiceProvider.GetRequiredService<IPublicacaoRepository>();
		var publicacao = new Publicacao(
			setor.Value.Id, "Comunicado do permalink", "Corpo em **markdown**.", TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal, publicadoEm, null, "teste");

		await repositorio.AddAsync(publicacao);

		return publicacao.Id;
	}

	[Fact]
	public async Task NoAr_AbreComOCorpoRenderizado()
	{
		var id = await SemearAsync(DateTimeOffset.UtcNow.AddMinutes(-5));

		var resposta = await CriarCliente().GetAsync($"/publicacoes/{id}");
		var html = await resposta.Content.ReadAsStringAsync();

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		html.Should().Contain("Comunicado do permalink");
		html.Should().Contain("<strong>markdown</strong>");
	}

	[Fact]
	public async Task Agendada_NaoAbre()
	{
		var id = await SemearAsync(DateTimeOffset.UtcNow.AddDays(1));

		var resposta = await CriarCliente().GetAsync($"/publicacoes/{id}");

		resposta.StatusCode.Should().Be(
			HttpStatusCode.NotFound, "o permalink aplica a mesma regra de relógio da listagem");
	}

	[Fact]
	public async Task Inexistente_RespondeIgualAoQueNaoPodeSerVisto()
	{
		var agendada = await SemearAsync(DateTimeOffset.UtcNow.AddDays(1));
		var client = CriarCliente();

		var semAcesso = await client.GetAsync($"/publicacoes/{agendada}");
		var inexistente = await client.GetAsync($"/publicacoes/{Guid.NewGuid()}");

		semAcesso.StatusCode.Should().Be(inexistente.StatusCode);
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --filter FullyQualifiedName~PermalinkPublicacaoTests`
Expected: os três testes falham com 404 na primeira asserção de OK — a rota não existe.

- [ ] **Step 3: Criar o handler**

```csharp
using Secco.Intranet.Domain;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Consulta de uma publicação pelo identificador, para exibição.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="SetoresDoLeitor">Slugs dos setores aos quais o leitor pertence.</param>
/// <param name="ExigirVisibilidade">Quando <c>false</c>, dispensa o filtro — modo aberto de DEV.</param>
public sealed record ObterPublicacaoQuery(
	Guid Id,
	IReadOnlyList<string> SetoresDoLeitor,
	bool ExigirVisibilidade);

/// <summary>
/// Devolve uma publicação se — e só se — ela está no ar e o leitor pode vê-la. Fora do ar e
/// fora do alcance devolvem o mesmo erro de inexistente: distinguir revelaria a existência.
/// </summary>
/// <param name="repository">Persistência de publicações.</param>
public sealed class ObterPublicacaoHandler(IPublicacaoRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Consulta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PublicacaoDto>> HandleAsync(
		ObterPublicacaoQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var encontrada = await repository.GetByIdAsync(query.Id, cancellationToken).ConfigureAwait(false);

		if (encontrada is null || !encontrada.Publicacao.EstaNoAr(DateTimeOffset.UtcNow))
		{
			return IntranetErrors.Publicacoes.NotFound;
		}

		var visivel = !query.ExigirVisibilidade
			|| encontrada.Publicacao.Visibilidade == Visibilidade.Empresa
			|| query.SetoresDoLeitor.Contains(encontrada.SetorSlug, StringComparer.OrdinalIgnoreCase);

		return visivel
			? Projecao.De(encontrada)
			: IntranetErrors.Publicacoes.NotFound;
	}
}
```

Registre em `IntranetApplicationExtensions.cs`, ao lado de `ListarMuralHandler`:

```csharp
		services.AddScoped<ObterPublicacaoHandler>();
```

- [ ] **Step 4: Acrescentar o ViewModel**

Ao fim de `src/Secco.Intranet.Web/Models/Mural/MuralViewModel.cs`:

```csharp
/// <summary>Modelo da página de uma publicação.</summary>
/// <param name="Publicacao">A publicação, com o corpo já renderizado.</param>
public sealed record PublicacaoDetalheViewModel(PublicacaoViewModel Publicacao);
```

- [ ] **Step 5: Acrescentar a ação**

Em `MuralController`, depois de `Index`:

```csharp
	/// <summary>Página de uma publicação — o destino do aviso e o endereço de compartilhamento.</summary>
	/// <param name="id">Identificador da publicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("/publicacoes/{id:guid}")]
	public async Task<IActionResult> Detalhe(Guid id, CancellationToken cancellationToken = default)
	{
		if (!tenantContext.IsResolved)
		{
			return NotFound();
		}

		var resultado = await serviceProvider
			.GetRequiredService<ObterPublicacaoHandler>()
			.HandleAsync(
				new ObterPublicacaoQuery(
					id,
					[.. SetorAcesso.SlugsDoUsuario(User)],
					ExigirVisibilidade: IntranetAuthenticationExtensions.IsConfigured(configuration)),
				cancellationToken)
			.ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		var publicacao = resultado.Value;

		return View(new PublicacaoDetalheViewModel(new PublicacaoViewModel(
			publicacao.Id,
			publicacao.Titulo,
			renderizador.Renderizar(publicacao.Corpo),
			publicacao.Tipo,
			publicacao.Prioridade,
			publicacao.PublicadoEm,
			publicacao.SetorNome,
			publicacao.SetorSlug)));
	}
```

- [ ] **Step 6: Criar a view**

Create: `src/Secco.Intranet.Web/Views/Mural/Detalhe.cshtml`

```razor
@using Secco.Intranet.Domain.Publicacoes
@using Secco.Intranet.Web.Models.Mural
@model PublicacaoDetalheViewModel
@{
    var publicacao = Model.Publicacao;

    ViewData["Title"] = publicacao.Titulo;

    static string Rotulo(TipoPublicacao tipo) => tipo switch
    {
        TipoPublicacao.Aviso => "Aviso",
        TipoPublicacao.Evento => "Evento",
        _ => "Notícia",
    };

    var cabecalho = new PageHeaderModel(
        publicacao.Titulo,
        $"{Rotulo(publicacao.Tipo)} · {publicacao.SetorNome} · {publicacao.PublicadoEm.ToLocalTime():dd/MM/yyyy}",
        publicacao.SetorSlug,
        new[] { new PageActionModel("Voltar ao mural", Url.Action("Index", "Mural")!, "bi-arrow-left") });

    var card = new CardModel(
        Titulo: publicacao.Titulo,
        Meta: $"{Rotulo(publicacao.Tipo)} · {publicacao.PublicadoEm.ToLocalTime():dd/MM/yyyy}",
        SetorSlug: publicacao.SetorSlug,
        SetorNome: publicacao.SetorNome,
        Corpo: publicacao.Corpo);

    var urgente = publicacao.Prioridade == PrioridadePublicacao.Urgente;
}

<partial name="_PageHeader" model="cabecalho" />

@if (urgente)
{
    <div class="sc-urgente">
        <partial name="_Card" model="card" />
    </div>
}
else
{
    <partial name="_Card" model="card" />
}
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test --filter FullyQualifiedName~PermalinkPublicacaoTests`
Expected: PASS, 3 testes.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(mural): permalink de publicacao

O aviso precisava de destino, mas o buraco ja existia: nao havia como mandar um
comunicado para alguem por nenhum meio."
```

---

## Task 5: O sino

Cresce o contrato de tema. É o primeiro item novo desde que o contrato nasceu.

**Files:**
- Create: `src/Secco.Intranet.Application/Publicacoes/Notificacao/ICaixaDeNotificacoes.cs`
- Create: `src/Secco.Intranet.Infrastructure/Notificacao/CaixaVazia.cs`
- Create: `src/Secco.Intranet.Web.Theming/Contracts/NotificacoesModel.cs`
- Create: `src/Secco.Intranet.Web/ViewComponents/NotificacoesViewComponent.cs`
- Create: `src/Secco.Intranet.Themes.Vertical/Themes/Vertical/Views/Shared/Components/Notificacoes/Default.cshtml`
- Create: `src/Secco.Intranet.Themes.Horizontal/Themes/Horizontal/Views/Shared/Components/Notificacoes/Default.cshtml`
- Modify: os dois `_Layout.cshtml` e os dois `wwwroot/scss/_components.scss`
- Modify: `docs/temas.md`

> **Existem dois temas.** `Vertical` e `Horizontal` implementam o contrato inteiro, e o core
> não tem view de fallback: um tema sem `Components/Notificacoes/Default.cshtml` quebra em
> toda página no instante em que o layout invocar o componente. O sino entra nos **dois**.
>
> O adaptador real do Hub (`NotificationHubCaixaDeNotificacoes`) fica na Task 3, junto dos
> outros que dependem do pacote. Aqui vai só o no-op, então esta tarefa fecha verde sem ele.

**Interfaces:**
- Consumes: `INotificationHubClient` (Task 3).
- Produces: `NotificacaoDaCaixa(Guid Id, string Titulo, string Mensagem, string? Link, DateTimeOffset CriadaEm)`; `ICaixaDeNotificacoes.NaoLidasAsync(Guid, CancellationToken)`; `NotificacoesModel(int NaoLidas, IReadOnlyList<NotificacaoNoSinoModel> Itens, bool Habilitado)`.

- [ ] **Step 1: Criar a porta**

```csharp
namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>Uma notificação não lida do inbox in-app.</summary>
/// <param name="Id">Identificador, usado para marcar como lida.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Mensagem">Resumo.</param>
/// <param name="Link">Destino do clique, quando houver.</param>
/// <param name="CriadaEm">Quando foi criada.</param>
public sealed record NotificacaoDaCaixa(
	Guid Id,
	string Titulo,
	string Mensagem,
	string? Link,
	DateTimeOffset CriadaEm);

/// <summary>
/// Porta de leitura do inbox in-app. O Hub é dono do estado de lida — a Intranet não guarda
/// nada disso (ADR-0006).
/// </summary>
public interface ICaixaDeNotificacoes
{
	/// <summary>Notificações ainda não lidas do usuário.</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<NotificacaoDaCaixa>> NaoLidasAsync(
		Guid usuarioId,
		CancellationToken cancellationToken = default);

	/// <summary>Marca uma notificação como lida.</summary>
	/// <param name="notificacaoId">Identificador da notificação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task MarcarComoLidaAsync(Guid notificacaoId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: O no-op da caixa**

```csharp
using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>Adapter no-op de <see cref="ICaixaDeNotificacoes"/> — sem Hub configurado, o sino não aparece.</summary>
public sealed class CaixaVazia : ICaixaDeNotificacoes
{
	/// <inheritdoc />
	public Task<IReadOnlyList<NotificacaoDaCaixa>> NaoLidasAsync(
		Guid usuarioId,
		CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<NotificacaoDaCaixa>>([]);

	/// <inheritdoc />
	public Task MarcarComoLidaAsync(Guid notificacaoId, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;
}
```

Registre no `IntranetInfrastructureExtensions.cs`, ao lado do `INotificadorDeMensagens`:

```csharp
		services.AddScoped<ICaixaDeNotificacoes>(serviceProvider =>
			string.IsNullOrWhiteSpace(serviceProvider.GetRequiredService<NotificacaoOptions>().HubUrl)
				? ActivatorUtilities.CreateInstance<CaixaVazia>(serviceProvider)
				: ActivatorUtilities.CreateInstance<NotificationHubCaixaDeNotificacoes>(serviceProvider));
```

- [ ] **Step 3: Model do contrato de tema**

```csharp
namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Uma linha do sino.</summary>
/// <param name="Id">Identificador da notificação.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Mensagem">Resumo.</param>
/// <param name="Link">Destino do clique; nulo desabilita a navegação.</param>
/// <param name="Quando">Texto relativo já formatado, como "há 2 horas".</param>
public sealed record NotificacaoNoSinoModel(
	Guid Id,
	string Titulo,
	string Mensagem,
	string? Link,
	string Quando);

/// <summary>
/// Sino de notificações. O Hub só expõe não lidas, então a lista é exatamente isso — não há
/// histórico de lidas a mostrar, nem "marcar todas", que seriam N chamadas.
/// </summary>
/// <param name="NaoLidas">Quantidade não lida; zero esconde o contador.</param>
/// <param name="Itens">Notificações não lidas, da mais recente para a mais antiga.</param>
/// <param name="Habilitado">
/// Quando <c>false</c>, o tema não renderiza o sino — é o caso do modo aberto de DEV, sem
/// usuário identificado.
/// </param>
public sealed record NotificacoesModel(
	int NaoLidas,
	IReadOnlyList<NotificacaoNoSinoModel> Itens,
	bool Habilitado);
```

- [ ] **Step 4: View component**

```csharp
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Web.Theming.Contracts;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.ViewComponents;

/// <summary>
/// Monta o sino e entrega ao markup do tema (ADR-0004). Está no layout de todas as páginas,
/// então nenhuma falha aqui pode derrubar a página: sem usuário identificado ou sem Hub
/// configurado, o sino simplesmente não aparece.
/// </summary>
/// <param name="caixa">Inbox in-app.</param>
public sealed class NotificacoesViewComponent(ICaixaDeNotificacoes caixa) : ViewComponent
{
	private const int LimiteNoSino = 10;

	/// <summary>Renderiza o sino.</summary>
	public async Task<IViewComponentResult> InvokeAsync()
	{
		if (!Guid.TryParse(HttpContext.User.FindFirst(SeccoClaims.Subject)?.Value, out var usuarioId))
		{
			return View(new NotificacoesModel(0, [], Habilitado: false));
		}

		var naoLidas = await caixa.NaoLidasAsync(usuarioId, HttpContext.RequestAborted).ConfigureAwait(false);

		var itens = naoLidas
			.OrderByDescending(notificacao => notificacao.CriadaEm)
			.Take(LimiteNoSino)
			.Select(notificacao => new NotificacaoNoSinoModel(
				notificacao.Id,
				notificacao.Titulo,
				notificacao.Mensagem,
				notificacao.Link,
				Quando(notificacao.CriadaEm)))
			.ToList();

		return View(new NotificacoesModel(naoLidas.Count, itens, Habilitado: true));
	}

	private static string Quando(DateTimeOffset criadaEm)
	{
		var decorrido = DateTimeOffset.UtcNow - criadaEm;

		return decorrido switch
		{
			{ TotalMinutes: < 1 } => "agora",
			{ TotalHours: < 1 } => $"há {(int)decorrido.TotalMinutes} min",
			{ TotalDays: < 1 } => $"há {(int)decorrido.TotalHours} h",
			_ => $"há {(int)decorrido.TotalDays} d",
		};
	}
}
```

- [ ] **Step 5: View do tema**

Create: `src/Secco.Intranet.Themes.Vertical/Themes/Vertical/Views/Shared/Components/Notificacoes/Default.cshtml`

```razor
@model NotificacoesModel
@if (Model.Habilitado)
{
    <div class="sc-sino dropdown">
        <button class="sc-sino__botao" type="button" data-bs-toggle="dropdown" aria-expanded="false"
                aria-label="@(Model.NaoLidas == 0 ? "Notificações" : $"Notificações, {Model.NaoLidas} não lidas")">
            <i class="bi bi-bell" aria-hidden="true"></i>
            @if (Model.NaoLidas > 0)
            {
                <span class="sc-sino__contador">@(Model.NaoLidas > 99 ? "99+" : Model.NaoLidas.ToString())</span>
            }
        </button>

        <div class="dropdown-menu dropdown-menu-end sc-sino__lista">
            @if (Model.Itens.Count == 0)
            {
                <p class="sc-sino__vazio">Nenhuma notificação nova.</p>
            }
            else
            {
                @foreach (var item in Model.Itens)
                {
                    <a class="sc-sino__item" href="@(item.Link ?? "#")" data-sc-notificacao="@item.Id">
                        <span class="sc-sino__titulo">@item.Titulo</span>
                        <span class="sc-sino__mensagem">@item.Mensagem</span>
                        <span class="sc-meta">@item.Quando</span>
                    </a>
                }
            }
        </div>
    </div>
}
```

- [ ] **Step 6: Chamar no layout**

Em `_Layout.cshtml`, imediatamente antes de `@await Component.InvokeAsync("UserMenu")`:

```razor
            @await Component.InvokeAsync("Notificacoes")
```

- [ ] **Step 7: Estilo**

Ao fim de `_components.scss`:

```scss
// Sino de notificacoes na barra superior.
.sc-sino {
  &__botao {
    position: relative;
    padding: .375rem;
    border: 0;
    border-radius: var(--bs-border-radius);
    background: transparent;
    color: var(--sc-text-muted);
    line-height: 1;
    cursor: pointer;
  }

  &__botao:hover {
    color: var(--sc-text);
  }

  &__contador {
    position: absolute;
    top: 0;
    right: 0;
    min-width: 1rem;
    padding: 0 .25rem;
    border-radius: 1rem;
    background-color: var(--bs-danger);
    color: #fff;
    font-size: .625rem;
    font-weight: 600;
    line-height: 1rem;
  }

  &__lista {
    width: 22rem;
    max-height: 24rem;
    overflow-y: auto;
    padding: .25rem;
  }

  &__vazio {
    margin: 0;
    padding: 1rem;
    color: var(--sc-text-muted);
    font-size: .8125rem;
    text-align: center;
  }

  &__item {
    display: block;
    padding: .625rem .75rem;
    border-radius: var(--bs-border-radius);
    color: inherit;
    text-decoration: none;
  }

  &__item:hover {
    background-color: var(--sc-surface-muted, rgba(127, 127, 127, .08));
  }

  &__titulo {
    display: block;
    font-weight: 600;
    font-size: .8125rem;
  }

  &__mensagem {
    display: block;
    margin: .125rem 0;
    color: var(--sc-text-muted);
    font-size: .8125rem;
  }
}
```

- [ ] **Step 8: Documentar o contrato**

Em `docs/temas.md`, na tabela de parciais e componentes, acrescente a linha:

```markdown
| `Components/Notificacoes/Default.cshtml` | `NotificacoesModel` | Sino de notificações da barra superior |
```

E, abaixo da tabela, o parágrafo:

```markdown
O sino mostra **apenas notificações não lidas**, e não oferece "marcar todas como lidas": o
`Secco.NotificationHub` expõe contar não lidas, listar não lidas e marcar uma como lida, e um
botão de marcar todas viraria uma chamada por item. Um tema pode mudar a aparência do sino à
vontade, mas não deve prometer o que a capacidade não entrega.
```

- [ ] **Step 9: Compilar assets e rodar**

Run: `npm --prefix src/Secco.Intranet.Themes.Vertical run build:css && dotnet build && dotnet test`
Expected: build com 0 avisos, suíte verde. O sino não aparece em Testing, porque não há claim `sub`.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat(tema): sino de notificacoes no contrato de tema

Primeiro item novo do contrato desde que ele nasceu. Mostra so nao lidas e nao
oferece marcar todas: e exatamente o que o Hub entrega."
```

---

## Task 6: Relatório de entrega no permalink

A pergunta *"o que não chegou?"* — feita só por quem quer a resposta.

**Files:**
- Create: `src/Secco.Intranet.Application/Publicacoes/Notificacao/IConsultaDeEntregas.cs`
- Create: `src/Secco.Intranet.Infrastructure/Notificacao/NotificationHubConsultaDeEntregas.cs`
- Create: `src/Secco.Intranet.Infrastructure/Notificacao/ConsultaDeEntregasVazia.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/MuralController.cs`
- Modify: `src/Secco.Intranet.Web/Models/Mural/MuralViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Views/Mural/Detalhe.cshtml`

**Interfaces:**
- Consumes: `INotificationHubClient` (Task 3), `AvisoDePublicacao.Origem` (Task 2).
- Produces: `ResumoDeEntrega(int Enviadas, int Falharam)`; `IConsultaDeEntregas.DaPublicacaoAsync(Guid, CancellationToken)`.

- [ ] **Step 1: Criar a porta**

```csharp
namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>Como foi a entrega dos avisos de uma publicação.</summary>
/// <param name="Enviadas">Notificações entregues.</param>
/// <param name="Falharam">Notificações que o Hub registrou como falha.</param>
public sealed record ResumoDeEntrega(int Enviadas, int Falharam);

/// <summary>
/// Porta de consulta de entrega. É <b>preguiçosa por desenho</b>: a pergunta só é feita
/// quando alguém abre a publicação, e nunca no instante de publicar.
/// </summary>
public interface IConsultaDeEntregas
{
	/// <summary>Resumo da entrega dos avisos de uma publicação.</summary>
	/// <param name="publicacaoId">Identificador da publicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<ResumoDeEntrega?> DaPublicacaoAsync(Guid publicacaoId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Adaptadores**

```csharp
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.NotificationHub.Client;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter real de <see cref="IConsultaDeEntregas"/>: uma busca filtrada por <c>Source</c> e
/// <c>Type</c> — campos que o Hub declaradamente nunca interpreta e que a publicação grava
/// justamente para isto — responde "como foi o envio da publicação X" numa chamada.
/// </summary>
/// <param name="client">Client do NotificationHub.</param>
/// <param name="logger">Log de falhas, sem dado sensível (ADR-0020).</param>
public sealed class NotificationHubConsultaDeEntregas(
	INotificationHubClient client,
	ILogger<NotificationHubConsultaDeEntregas> logger) : IConsultaDeEntregas
{
	private const int TamanhoDaPagina = 200;

	/// <inheritdoc />
	public async Task<ResumoDeEntrega?> DaPublicacaoAsync(
		Guid publicacaoId,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var enviadas = await ContarAsync(publicacaoId, NotificationStatus.Sent, cancellationToken)
				.ConfigureAwait(false);
			var falharam = await ContarAsync(publicacaoId, NotificationStatus.Failed, cancellationToken)
				.ConfigureAwait(false);

			return new ResumoDeEntrega(enviadas, falharam);
		}
		catch (ApiException apiException)
		{
			logger.LogWarning(
				"Falha ao consultar entregas no NotificationHub (status {StatusCode}).",
				apiException.StatusCode);

			return null;
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao consultar entregas no NotificationHub.");

			return null;
		}
	}

	private async Task<int> ContarAsync(
		Guid publicacaoId,
		NotificationStatus status,
		CancellationToken cancellationToken)
	{
		var pagina = await client
			.SearchNotificationsAsync(
				from: null, to: null, status: status, channel: null,
				source: "mural", type: publicacaoId.ToString(),
				page: 1, size: TamanhoDaPagina, cancellationToken)
			.ConfigureAwait(false);

		return (int)pagina.TotalCount;
	}
}
```

> Verificado no client gerado: `PagedResultOfNotificationDto.TotalCount` é `long`, daí o cast, e `NotificationStatus` tem `Pending`, `Sent` e `Failed`.

```csharp
using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>Adapter no-op de <see cref="IConsultaDeEntregas"/> — sem Hub configurado não há entrega a consultar.</summary>
public sealed class ConsultaDeEntregasVazia : IConsultaDeEntregas
{
	/// <inheritdoc />
	public Task<ResumoDeEntrega?> DaPublicacaoAsync(
		Guid publicacaoId,
		CancellationToken cancellationToken = default) =>
		Task.FromResult<ResumoDeEntrega?>(null);
}
```

Registre no `IntranetInfrastructureExtensions.cs`, no mesmo bloco dos outros:

```csharp
		services.AddScoped<IConsultaDeEntregas>(serviceProvider =>
			string.IsNullOrWhiteSpace(serviceProvider.GetRequiredService<NotificacaoOptions>().HubUrl)
				? ActivatorUtilities.CreateInstance<ConsultaDeEntregasVazia>(serviceProvider)
				: ActivatorUtilities.CreateInstance<NotificationHubConsultaDeEntregas>(serviceProvider));
```

- [ ] **Step 3: Levar ao ViewModel**

Substitua o `PublicacaoDetalheViewModel` criado na Task 4 por:

```csharp
/// <summary>Modelo da página de uma publicação.</summary>
/// <param name="Publicacao">A publicação, com o corpo já renderizado.</param>
/// <param name="Entrega">
/// Resumo da entrega dos avisos, só para quem administra o setor. Nulo quando o usuário não
/// administra, quando não há Hub configurado, ou quando a consulta falhou.
/// </param>
public sealed record PublicacaoDetalheViewModel(
	PublicacaoViewModel Publicacao,
	ResumoDeEntrega? Entrega);
```

Acrescente `using Secco.Intranet.Application.Publicacoes.Notificacao;` ao topo do arquivo.

- [ ] **Step 4: Consultar na ação**

Em `MuralController.Detalhe`, troque o `return View(...)` final por:

```csharp
		// Preguiçoso por desenho: a pergunta só é feita por quem pode agir sobre a resposta.
		var entrega = SetorAcesso.AdministraSetor(User, publicacao.SetorSlug)
			? await serviceProvider
				.GetRequiredService<IConsultaDeEntregas>()
				.DaPublicacaoAsync(publicacao.Id, cancellationToken)
				.ConfigureAwait(false)
			: null;

		return View(new PublicacaoDetalheViewModel(
			new PublicacaoViewModel(
				publicacao.Id,
				publicacao.Titulo,
				renderizador.Renderizar(publicacao.Corpo),
				publicacao.Tipo,
				publicacao.Prioridade,
				publicacao.PublicadoEm,
				publicacao.SetorNome,
				publicacao.SetorSlug),
			entrega));
```

- [ ] **Step 5: Mostrar na view**

Ao fim de `Views/Mural/Detalhe.cshtml`:

```razor
@if (Model.Entrega is not null)
{
    <div class="sc-panel mt-3">
        <h2 class="h6 mb-2">Entrega do aviso</h2>
        <p class="mb-0 text-body-secondary small">
            @Model.Entrega.Enviadas entregue(s).
            @if (Model.Entrega.Falharam > 0)
            {
                <span class="text-danger">@Model.Entrega.Falharam não chegaram.</span>
            }
        </p>
    </div>
}
```

- [ ] **Step 6: Rodar**

Run: `dotnet build && dotnet test`
Expected: build com 0 avisos, suíte verde. Em Testing a consulta devolve `null` e o painel não aparece.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(notificacao): relatorio de entrega no permalink

Uma chamada, filtrando por Source e Type, e so quando quem administra o setor
abre a publicacao. A aba do setor segue com zero chamadas extras."
```

---

## Task 7: Aviso de agendamento no formulário e documentação

Fecha o comportamento interino e deixa o rastro para quando a plataforma entregar.

**Files:**
- Modify: `src/Secco.Intranet.Web/Views/Setor/Avisos.cshtml`
- Modify: `docs/roadmap.md`
- Modify: `README.md`

- [ ] **Step 1: Avisar antes de salvar**

Em `Views/Setor/Avisos.cshtml`, substitua o parágrafo de legenda das datas por:

```razor
                <div class="col-12">
                    <p class="form-text mb-0">
                        A publicação aparece no mural a partir da data de entrada no ar e deixa de
                        aparecer na data de saída. Deixe a saída em branco para não expirar.
                    </p>
                    <p class="form-text mb-0">
                        <i class="bi bi-info-circle" aria-hidden="true"></i>
                        O aviso é enviado <strong>na data de entrada no ar</strong>, não no momento
                        de salvar — publicação agendada avisa na hora certa.
                    </p>
                </div>
```

- [ ] **Step 2: Registrar no roadmap**

Em `docs/roadmap.md`, logo abaixo da linha do Mural, acrescente:

```markdown
- [x] Notificação do Mural: sino, e-mail e canal corporativo conforme a urgência, com entrega
      na data de entrada no ar — [spec](specs/2026-09-07-notificacao-mural-design.md)
```

- [ ] **Step 3: Atualizar o README**

Na seção de tecnologias, acrescente `Secco.NotificationHub.Client` à lista de pacotes da plataforma consumidos.

- [ ] **Step 4: Verificação final**

```bash
npm --prefix src/Secco.Intranet.Themes.Vertical run build
dotnet build
dotnet test
```

Expected: build com 0 avisos e a suíte verde.

- [ ] **Step 5: Verificar no navegador**

Suba a aplicação e confira:

1. `/setor/<slug>/avisos` diz que o aviso sai na data de entrada no ar.
2. Publicar com data atual redireciona com a mensagem de quantas pessoas foram avisadas — em DEV, sem Hub configurado, a contagem é zero e nenhuma exceção aparece.
3. `/publicacoes/<id>` abre com o corpo renderizado, e o painel de entrega **não** aparece sem Hub.
4. O sino **não** aparece em DEV, porque não há claim `sub`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(mural): avisa que publicacao agendada nao notifica

Dizer isso ANTES de salvar e o que impede a pessoa descobrir depois que o
comunicado dela nao alcancou ninguem."
```

---

## Depois deste plano

1. **Auditoria transversal** — cliente do LogStream, token de máquina, política de indisponibilidade, cobrindo Mural **e** Documentos. Os verbos do Mural já estão definidos no spec do Mural; os de Documentos ainda não existem, e a decisão de auditar ou não `documento.baixar` continua aberta.
2. **Composição do client do Hub** — vale abrir demanda para `AddNotificationHubClient` aceitar client credentials, como as três extensões do SecureGate aceitam. Hoje cada adotante registra o `HttpClient` à mão para anexar o `SeccoClientCredentialsHandler`.
