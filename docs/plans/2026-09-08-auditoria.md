# Auditoria transversal — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Toda escrita em Mural, Documentos e Setor deixa rastro no `Secco.LogStream`, dizendo quem fez o quê e quando.

**Architecture:** Duas portas na Application — `ITrilhaDeAuditoria` e `IAtorAtual` — com adaptadores nas bordas. O handler diz apenas *qual verbo, qual recurso, qual id*; quem resolve ator, correlação e instante é o adaptador. **A porta nunca lança:** falha de auditoria vira aviso no log num único lugar, e não em oito `try/catch` espalhados.

**Tech Stack:** .NET 10, ASP.NET Core MVC, `Secco.LogStream.Client` 0.3.1, `Secco.SDK.AspNetCore` 0.5.1, xUnit + AwesomeAssertions.

**Spec:** [`docs/specs/2026-09-08-auditoria-design.md`](../specs/2026-09-08-auditoria-design.md)

## Global Constraints

- **Commits vão direto na `main`**, por caminho explícito — nunca `git add -A`.
- **Auditar nunca derruba a ação.** `ITrilhaDeAuditoria.RegistrarAsync` não lança, por contrato.
- **Capacidade de plataforma não se implementa aqui** (ADR-0006): a trilha é do LogStream, e este repositório não guarda cópia dela.
- **Controller nunca acessa repositório nem `DbContext`** (ADR-0002).
- **Erro de negócio é `Result`**, exceção só para falha de infraestrutura (ADR-0004).
- **Log sem dado sensível** (ADR-0020): status e contagem, nunca corpo, bytes ou e-mail.
- **Metadata nunca carrega conteúdo** — nem corpo de publicação, nem bytes de documento.
- **Leitura não é auditada.** `documento.baixar` não existe como verbo; a ausência é decisão registrada no spec.
- **Sem ator resolvido, não registra** — modo aberto de DEV não suja a trilha.
- **Nenhuma migration.** Nada deste plano é dado nosso.
- Build precisa terminar com **0 avisos**; a suíte inteira verde antes de cada commit.

---

## Task 1: Portas, verbos e o no-op

Contrato puro mais o adaptador que não faz nada. Ao fim desta tarefa nada é auditado ainda, mas o vocabulário existe e o contêiner resolve tudo.

**Files:**
- Create: `src/Secco.Intranet.Application/Auditoria/ITrilhaDeAuditoria.cs`
- Create: `src/Secco.Intranet.Application/Auditoria/IAtorAtual.cs`
- Create: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`
- Create: `src/Secco.Intranet.Application/Auditoria/AuditoriaOptions.cs`
- Create: `src/Secco.Intranet.Infrastructure/Auditoria/TrilhaSilenciosa.cs`
- Create: `src/Secco.Intranet.Web/Auditoria/AtorDoHttpContext.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`
- Modify: `src/Secco.Intranet.Web/Program.cs`

**Interfaces:**
- Produces: `RegistroDeAuditoria(string Verbo, string Recurso, string RecursoId, string? Metadata)`; `ITrilhaDeAuditoria.RegistrarAsync(RegistroDeAuditoria, CancellationToken)`; `AtorDaAcao(string Id, string Nome)`; `IAtorAtual.Atual()`; `VerbosDeAuditoria` e `RecursosDeAuditoria` (constantes); `AuditoriaOptions` com `SectionKey` e `LogStreamUrl`.

- [ ] **Step 1: Criar a porta da trilha**

```csharp
namespace Secco.Intranet.Application.Auditoria;

/// <summary>Uma ação a registrar na trilha.</summary>
/// <param name="Verbo">Verbo canônico, de <see cref="VerbosDeAuditoria"/>.</param>
/// <param name="Recurso">Tipo do recurso, de <see cref="RecursosDeAuditoria"/>.</param>
/// <param name="RecursoId">Identificador do recurso afetado.</param>
/// <param name="Metadata">
/// JSON curto com o que identifica a ação. Nunca conteúdo: nem corpo de publicação, nem bytes
/// de documento — a trilha diz o que aconteceu, e duplicar dado sensível num serviço de
/// observabilidade cria um segundo lugar de onde ele pode vazar (ADR-0020).
/// </param>
public sealed record RegistroDeAuditoria(string Verbo, string Recurso, string RecursoId, string? Metadata);

/// <summary>
/// Porta de escrita da trilha de auditoria. A trilha vive no <c>Secco.LogStream</c> (ADR-0006);
/// este produto não guarda cópia.
/// </summary>
public interface ITrilhaDeAuditoria
{
	/// <summary>
	/// Registra uma ação. <b>Nunca lança</b>: auditar não pode derrubar o que o usuário pediu,
	/// e concentrar essa garantia aqui evita oito <c>try/catch</c> espalhados pelos handlers.
	/// Falha vira aviso no log e a trilha fica com um buraco — decisão registrada no spec.
	/// </summary>
	/// <param name="registro">Ação a registrar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Criar a porta do ator**

```csharp
namespace Secco.Intranet.Application.Auditoria;

/// <summary>Quem está executando a ação.</summary>
/// <param name="Id">Identificador do usuário (claim <c>sub</c>).</param>
/// <param name="Nome">Nome de exibição.</param>
public sealed record AtorDaAcao(string Id, string Nome);

/// <summary>
/// Porta de resolução do usuário atual. Existe porque o <c>SeccoAmbientContext</c> carrega
/// tenant e correlação, mas não usuário — e passar o ator em cada comando repetiria oito vezes
/// o que a notificação precisou fazer uma vez.
/// </summary>
public interface IAtorAtual
{
	/// <summary>
	/// Ator da requisição atual, ou <c>null</c> quando não há usuário identificado — o modo
	/// aberto de DEV. Sem ator, nada é registrado: entrada com ator inventado não prova nada.
	/// </summary>
	AtorDaAcao? Atual();
}
```

- [ ] **Step 3: Criar os verbos e recursos**

```csharp
namespace Secco.Intranet.Application.Auditoria;

/// <summary>
/// Verbos canônicos da trilha. Constantes, e não texto solto no handler: o verbo é o que
/// alguém vai filtrar daqui a um ano, e um erro de digitação some da busca sem avisar.
/// </summary>
public static class VerbosDeAuditoria
{
	/// <summary>Publicação criada.</summary>
	public const string MuralPublicar = "mural.publicar";

	/// <summary>Conteúdo, agendamento, visibilidade ou prioridade alterados.</summary>
	public const string MuralEditar = "mural.editar";

	/// <summary>Publicação tirada de circulação.</summary>
	public const string MuralArquivar = "mural.arquivar";

	/// <summary>Arquivo enviado.</summary>
	public const string DocumentoPublicar = "documento.publicar";

	/// <summary>Documento tirado de circulação.</summary>
	public const string DocumentoArquivar = "documento.arquivar";

	/// <summary>Setor criado — provisiona as Roles no SecureGate (ADR-0001).</summary>
	public const string SetorCriar = "setor.criar";

	/// <summary>Nome ou ícone do setor alterados.</summary>
	public const string SetorEditar = "setor.editar";

	/// <summary>Setor passou a inativo — muda quem enxerga o quê.</summary>
	public const string SetorDesativar = "setor.desativar";

	/// <summary>Setor voltou a ativo.</summary>
	public const string SetorReativar = "setor.reativar";
}

/// <summary>Tipos de recurso da trilha.</summary>
public static class RecursosDeAuditoria
{
	/// <summary>Publicação do mural.</summary>
	public const string Publicacao = "publicacao";

	/// <summary>Documento de setor.</summary>
	public const string Documento = "documento";

	/// <summary>Setor.</summary>
	public const string Setor = "setor";
}
```

- [ ] **Step 4: Criar as options**

```csharp
namespace Secco.Intranet.Application.Auditoria;

/// <summary>
/// Configuração da auditoria, seção <c>Intranet:Auditoria</c>. Bind lazy feito pela
/// Infrastructure — a Application não conhece configuração.
/// </summary>
public sealed class AuditoriaOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	public const string SectionKey = "Intranet:Auditoria";

	/// <summary>
	/// URL base do <c>Secco.LogStream</c>. Vazia desliga o registro — é o modo DEV/Testing, em
	/// que a operação acontece e nada é auditado, que é a verdade: não há para onde escrever.
	/// </summary>
	public string LogStreamUrl { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Criar o no-op**

```csharp
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Auditoria;

namespace Secco.Intranet.Infrastructure.Auditoria;

/// <summary>
/// Adapter no-op de <see cref="ITrilhaDeAuditoria"/> — modo DEV/Testing, quando o
/// <c>Secco.LogStream</c> não está configurado.
/// </summary>
/// <param name="logger">Log em nível Debug.</param>
public sealed class TrilhaSilenciosa(ILogger<TrilhaSilenciosa> logger) : ITrilhaDeAuditoria
{
	/// <inheritdoc />
	public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(registro);

		logger.LogDebug(
			"auditoria desativada — LogStream não configurado; verbo {Verbo} não registrado",
			registro.Verbo);

		return Task.CompletedTask;
	}
}
```

- [ ] **Step 6: Criar o ator do HttpContext**

```csharp
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.Auditoria;

/// <summary>
/// Resolve o ator a partir do usuário da requisição. Fica no Web porque é o único lugar que
/// conhece <c>HttpContext</c> — a Application só enxerga a porta.
/// </summary>
/// <param name="httpContextAccessor">Acesso à requisição atual.</param>
public sealed class AtorDoHttpContext(IHttpContextAccessor httpContextAccessor) : IAtorAtual
{
	/// <inheritdoc />
	public AtorDaAcao? Atual()
	{
		var usuario = httpContextAccessor.HttpContext?.User;

		if (usuario?.Identity?.IsAuthenticated != true)
		{
			return null;
		}

		var id = usuario.FindFirst(SeccoClaims.Subject)?.Value;

		return string.IsNullOrWhiteSpace(id)
			? null
			: new AtorDaAcao(id, usuario.Identity.Name ?? id);
	}
}
```

- [ ] **Step 7: Registrar no DI**

Em `IntranetInfrastructureExtensions.cs`, junto das outras options e adaptadores:

```csharp
		services.AddSingleton(sp => BindSection(sp, AuditoriaOptions.SectionKey, new AuditoriaOptions()));

		// O adapter real chega na Task 2, com o pacote do LogStream. Até lá o produto não
		// audita — e é a verdade: sem LogStream configurado não há para onde escrever.
		services.AddScoped<ITrilhaDeAuditoria, TrilhaSilenciosa>();
```

Acrescente ao topo do arquivo: `using Secco.Intranet.Application.Auditoria;` e
`using Secco.Intranet.Infrastructure.Auditoria;`.

Em `Program.cs`, ao lado de `AddSingleton<IRenderizadorMarkdown, RenderizadorMarkdown>()`:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAtorAtual, AtorDoHttpContext>();
```

Acrescente `using Secco.Intranet.Application.Auditoria;` e `using Secco.Intranet.Web.Auditoria;`.

- [ ] **Step 8: Rodar a suíte**

Run: `dotnet build && dotnet test`
Expected: build com 0 avisos e a suíte inteira verde. Nada consome as portas ainda.

- [ ] **Step 9: Commit**

```bash
git add src/Secco.Intranet.Application/Auditoria src/Secco.Intranet.Infrastructure/Auditoria src/Secco.Intranet.Web/Auditoria src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs src/Secco.Intranet.Web/Program.cs
git commit -m "feat(auditoria): portas da trilha e do ator, com verbos canonicos

A porta nunca lanca: auditar nao pode derrubar o que o usuario pediu, e essa
garantia fica num lugar so em vez de oito try/catch pelos handlers.

O ator vem de porta propria lendo HttpContext, e nao de campo em comando: o
SeccoAmbientContext carrega tenant e correlacao, mas nao usuario."
```

---

## Task 2: Adaptador do LogStream

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Auditoria/LogStreamTrilhaDeAuditoria.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/TrilhaDeAuditoriaTests.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Secco.Intranet.Infrastructure.csproj`
- Modify: `Directory.Packages.props`

**Interfaces:**
- Consumes: `ITrilhaDeAuditoria`, `IAtorAtual`, `RegistroDeAuditoria`, `AuditoriaOptions` (Task 1).
- Produces: `LogStreamTrilhaDeAuditoria`, escolhido no DI quando `AuditoriaOptions.LogStreamUrl` está preenchida.

- [ ] **Step 1: Adicionar o pacote**

Em `Directory.Packages.props`, no `ItemGroup Label="Secco Platform (feed privado — versões conforme tags MinVer do monorepo)"`:

```xml
    <!-- audit-entries: a trilha de acao de usuario (ADR-0006). -->
    <PackageVersion Include="Secco.LogStream.Client" Version="0.3.1" />
```

Em `src/Secco.Intranet.Infrastructure/Secco.Intranet.Infrastructure.csproj`, no `ItemGroup` de pacotes:

```xml
    <PackageReference Include="Secco.LogStream.Client" />
```

Confirme que a versão fixada é a maior publicada antes de seguir — tag no monorepo não é pacote no feed:

```bash
MSYS_NO_PATHCONV=1 gh api "user/packages/nuget/Secco.LogStream.Client/versions" --jq '.[].name'
```

- [ ] **Step 2: Escrever o teste do adaptador**

```csharp
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Infrastructure.Auditoria;
using Secco.LogStream.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// O adaptador da trilha. O que importa aqui não é o formato do registro: é que ele
/// <b>nunca lance</b>. Auditar é consequência da ação do usuário, e uma falha de rede no
/// serviço de observabilidade não pode desfazer o que a pessoa acabou de fazer.
/// </summary>
public class TrilhaDeAuditoriaTests
{
	private sealed class ClientFalso : ILogStreamClient
	{
		public List<CreateAuditEntryRequest> Registros { get; } = [];

		public Task<AuditEntryDto> CreateAuditEntryAsync(
			CreateAuditEntryRequest body, CancellationToken cancellationToken)
		{
			Registros.Add(body);

			return Task.FromResult(new AuditEntryDto());
		}

		public Task<AuditEntryDto> CreateAuditEntryAsync(CreateAuditEntryRequest body) =>
			CreateAuditEntryAsync(body, CancellationToken.None);
	}

	private sealed class ClientQueFalha : ILogStreamClient
	{
		public Task<AuditEntryDto> CreateAuditEntryAsync(
			CreateAuditEntryRequest body, CancellationToken cancellationToken) =>
			throw new HttpRequestException("LogStream fora do ar.");

		public Task<AuditEntryDto> CreateAuditEntryAsync(CreateAuditEntryRequest body) =>
			CreateAuditEntryAsync(body, CancellationToken.None);
	}

	private sealed class AtorFalso(AtorDaAcao? ator) : IAtorAtual
	{
		public AtorDaAcao? Atual() => ator;
	}

	private static readonly AtorDaAcao Alguem = new("018f0000-0000-7000-8000-000000000009", "Ana");

	private static RegistroDeAuditoria Registro() =>
		new(VerbosDeAuditoria.MuralPublicar, RecursosDeAuditoria.Publicacao, "abc-123", """{"titulo":"x"}""");

	[Fact]
	public async Task ComAtor_EnviaVerboRecursoEId()
	{
		var client = new ClientFalso();
		var trilha = new LogStreamTrilhaDeAuditoria(
			client, new AtorFalso(Alguem), NullLogger<LogStreamTrilhaDeAuditoria>.Instance);

		await trilha.RegistrarAsync(Registro());

		var enviado = client.Registros.Should().ContainSingle().Subject;
		enviado.Action.Should().Be("mural.publicar");
		enviado.ResourceType.Should().Be("publicacao");
		enviado.ResourceId.Should().Be("abc-123");
		enviado.ActorId.Should().Be(Alguem.Id);
		enviado.ActorName.Should().Be("Ana");
		enviado.ActorType.Should().Be(ActorType.User);
		enviado.OccurredAt.Should().NotBeNull("a trilha registra quando aconteceu, não quando chegou");
	}

	[Fact]
	public async Task SemAtor_NaoRegistra()
	{
		var client = new ClientFalso();
		var trilha = new LogStreamTrilhaDeAuditoria(
			client, new AtorFalso(null), NullLogger<LogStreamTrilhaDeAuditoria>.Instance);

		await trilha.RegistrarAsync(Registro());

		client.Registros.Should().BeEmpty(
			"entrada com ator inventado não prova nada e só sujaria a trilha");
	}

	[Fact]
	public async Task LogStreamForaDoAr_NaoLanca()
	{
		var trilha = new LogStreamTrilhaDeAuditoria(
			new ClientQueFalha(), new AtorFalso(Alguem), NullLogger<LogStreamTrilhaDeAuditoria>.Instance);

		var registrar = async () => await trilha.RegistrarAsync(Registro());

		await registrar.Should().NotThrowAsync(
			"a garantia de não derrubar a ação mora aqui, e não em cada handler");
	}
}
```

> `ILogStreamClient` tem muitos membros além dos de auditoria. O fake acima implementa só o
> que o teste usa; declare os demais lançando `NotImplementedException` ou, se o compilador
> reclamar de membros faltantes, gere os stubs a partir dos erros — cada um é uma linha.

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test --filter FullyQualifiedName~TrilhaDeAuditoriaTests`
Expected: falha de compilação — `LogStreamTrilhaDeAuditoria` não existe.

- [ ] **Step 4: Implementar o adaptador**

```csharp
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Auditoria;
using Secco.LogStream.Client;
using Secco.SDK.AspNetCore.Ambient;

namespace Secco.Intranet.Infrastructure.Auditoria;

/// <summary>
/// Adapter real de <see cref="ITrilhaDeAuditoria"/>: uma chamada a <c>CreateAuditEntry</c> por
/// ação. A escrita do LogStream é síncrona — não há fila do lado de lá —, então a garantia de
/// não derrubar a operação é inteiramente daqui.
/// </summary>
/// <param name="client">Client do LogStream.</param>
/// <param name="ator">Resolução do usuário atual.</param>
/// <param name="logger">Log de falhas, sem dado sensível (ADR-0020).</param>
public sealed class LogStreamTrilhaDeAuditoria(
	ILogStreamClient client,
	IAtorAtual ator,
	ILogger<LogStreamTrilhaDeAuditoria> logger) : ITrilhaDeAuditoria
{
	/// <inheritdoc />
	public async Task RegistrarAsync(
		RegistroDeAuditoria registro,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(registro);

		var quem = ator.Atual();

		if (quem is null)
		{
			// Modo aberto de DEV: registrar com ator inventado não prova nada.
			logger.LogDebug("sem ator identificado — verbo {Verbo} não registrado", registro.Verbo);

			return;
		}

		var request = new CreateAuditEntryRequest
		{
			ActorId = quem.Id,
			ActorName = quem.Nome,
			ActorType = ActorType.User,
			Action = registro.Verbo,
			ResourceType = registro.Recurso,
			ResourceId = registro.RecursoId,
			Metadata = registro.Metadata,
			CorrelationId = SeccoAmbientContext.CorrelationId,
			OccurredAt = DateTimeOffset.UtcNow,
		};

		try
		{
			await client.CreateAuditEntryAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (ApiException apiException)
		{
			// Falha aberta: a acao do usuario ja aconteceu, e desfaze-la por causa do registro
			// seria pior que o buraco na trilha. O buraco fica visivel aqui.
			logger.LogWarning(
				"O LogStream recusou o registro de {Verbo} (status {StatusCode}).",
				registro.Verbo,
				apiException.StatusCode);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(
				httpRequestException,
				"Falha de rede ao registrar {Verbo} no LogStream.",
				registro.Verbo);
		}
	}
}
```

> `SeccoAmbientContext.CorrelationId` é `Guid?` e o `CreateAuditEntryRequest.CorrelationId`
> também — atribuição direta. Se o tipo divergir na versão instalada, converta explicitamente
> em vez de mudar o contrato.

- [ ] **Step 5: Compor no DI**

Em `IntranetInfrastructureExtensions.cs`, substitua o registro fixo da Task 1
(`services.AddScoped<ITrilhaDeAuditoria, TrilhaSilenciosa>();`) por:

```csharp
		// Store próprio para o LogStream: um por recurso/scope (least privilege), como o
		// SecureGate e o NotificationHub. O client é registrado sempre e lê a URL na
		// resolução; quem decide entre adapter real e no-op são as options.
		var tokenStoreDoLogStream = new SeccoAccessTokenStore();

		services.AddHttpClient<ILogStreamClient, LogStreamClient>()
			.ConfigureHttpClient((serviceProvider, client) =>
			{
				var auditoria = serviceProvider.GetRequiredService<AuditoriaOptions>();

				if (!string.IsNullOrWhiteSpace(auditoria.LogStreamUrl))
				{
					client.BaseAddress = new Uri(auditoria.LogStreamUrl, UriKind.Absolute);
				}
			})
			.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
			{
				// AddLogStreamClient() do pacote só aceita BaseUrl, mas os endpoints de
				// auditoria exigem AuditEntries.Write — o handler de credenciais entra à mão.
				var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

				if (credenciais.IsConfigured)
				{
					handlers.Add(new SeccoClientCredentialsHandler(
						credenciais.BaseUrl!,
						credenciais.ClientId!,
						credenciais.ClientSecret!,
						"audit-entries:read audit-entries:write",
						tokenStoreDoLogStream));
				}
			});

		services.AddScoped<ITrilhaDeAuditoria>(serviceProvider =>
			string.IsNullOrWhiteSpace(serviceProvider.GetRequiredService<AuditoriaOptions>().LogStreamUrl)
				? ActivatorUtilities.CreateInstance<TrilhaSilenciosa>(serviceProvider)
				: ActivatorUtilities.CreateInstance<LogStreamTrilhaDeAuditoria>(serviceProvider));
```

Acrescente `using Secco.LogStream.Client;` ao topo do arquivo.

> Scope verificado no monorepo: `LogStreamPermissions.AuditEntries.Read` é
> `audit-entries:read` e `.Write` é `audit-entries:write`. O nome importa porque errar ali vira
> 403, e a falha aberta engole o 403 sem reclamar — a trilha simplesmente ficaria vazia.

- [ ] **Step 6: Rodar a suíte**

Run: `dotnet build && dotnet test`
Expected: build com 0 avisos, suíte verde, 3 testes novos. Em Testing a URL está vazia e o
no-op é resolvido.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props src/Secco.Intranet.Infrastructure src/Secco.Intranet.Application tests/Secco.Intranet.Tests/Unit/TrilhaDeAuditoriaTests.cs
git commit -m "feat(auditoria): adaptador do LogStream

Segundo adotante a registrar o HttpClient a mao com SeccoClientCredentialsHandler:
LogStreamClientOptions so aceita BaseUrl, e os endpoints de auditoria exigem
AuditEntries.Write.

A escrita do LogStream e sincrona - nao ha fila do lado de la -, entao a garantia
de nao derrubar a operacao e inteiramente do adaptador."
```

---

## Task 3: Verbos do Mural

**Files:**
- Modify: `src/Secco.Intranet.Application/Publicacoes/PublicarPublicacaoHandler.cs`
- Modify: `src/Secco.Intranet.Application/Publicacoes/EditarPublicacaoHandler.cs`
- Modify: `src/Secco.Intranet.Application/Publicacoes/ArquivarPublicacaoHandler.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/AuditoriaDoMuralTests.cs`

**Interfaces:**
- Consumes: `ITrilhaDeAuditoria`, `RegistroDeAuditoria`, `VerbosDeAuditoria`, `RecursosDeAuditoria` (Task 1).
- Produces: os três handlers passam a receber `ITrilhaDeAuditoria` como último parâmetro do construtor.

- [ ] **Step 1: Escrever o teste**

```csharp
using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
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
/// Os verbos do Mural. Um verbo errado não quebra nada hoje — some da busca daqui a um ano,
/// quando alguém precisar saber quem publicou o quê.
/// </summary>
public class AuditoriaDoMuralTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public List<RegistroDeAuditoria> Registros { get; } = [];

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Registros.Add(registro);

			return Task.CompletedTask;
		}
	}

	private sealed class DiretorioVazioFalso : IDiretorioDeUsuarios
	{
		public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(
			CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<UsuarioDoTenant>>([]);
	}

	private sealed class NotificadorMudo : INotificadorDeMensagens
	{
		public Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class PublicacaoRepositorioFalso(PublicacaoComSetor? encontrada) : IPublicacaoRepository
	{
		public Task AddAsync(Publicacao publicacao, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<PublicacaoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(encontrada);

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

	private static PublicacaoComSetor Existente() =>
		new(new Publicacao(Guid.NewGuid(), "Comunicado", "Corpo secreto", TipoPublicacao.Aviso,
				Visibilidade.Empresa, PrioridadePublicacao.Normal,
				DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem.publicou"),
			"Financeiro", "financeiro");

	[Fact]
	public async Task Publicar_RegistraMuralPublicar()
	{
		var trilha = new TrilhaFalsa();
		var handler = new PublicarPublicacaoHandler(
			new PublicacaoRepositorioFalso(null), new SetorRepositorioFalso(), new IntranetOptions(),
			new DiretorioVazioFalso(), new NotificadorMudo(), new NotificacaoOptions(), trilha);

		var resultado = await handler.HandleAsync(new PublicarPublicacaoCommand(
			"financeiro", "Comunicado", "Corpo em **markdown**.", TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal,
			DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem.publicou", Guid.NewGuid()));

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MuralPublicar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Publicacao);
		registro.RecursoId.Should().Be(resultado.Value.Publicacao.Id.ToString());
	}

	[Fact]
	public async Task Publicar_NaoLevaOCorpoNoMetadata()
	{
		var trilha = new TrilhaFalsa();
		var handler = new PublicarPublicacaoHandler(
			new PublicacaoRepositorioFalso(null), new SetorRepositorioFalso(), new IntranetOptions(),
			new DiretorioVazioFalso(), new NotificadorMudo(), new NotificacaoOptions(), trilha);

		await handler.HandleAsync(new PublicarPublicacaoCommand(
			"financeiro", "Comunicado", "Segredo industrial em texto longo.", TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal,
			DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem.publicou", Guid.NewGuid()));

		var metadata = trilha.Registros.Should().ContainSingle().Subject.Metadata;
		metadata.Should().NotBeNull();
		metadata.Should().NotContain("Segredo industrial",
			"a trilha diz o que aconteceu; duplicar conteudo cria um segundo lugar de onde vazar");
		JsonDocument.Parse(metadata!).RootElement.GetProperty("titulo").GetString()
			.Should().Be("Comunicado");
	}

	[Fact]
	public async Task Editar_RegistraMuralEditar()
	{
		var trilha = new TrilhaFalsa();
		var encontrada = Existente();
		var handler = new EditarPublicacaoHandler(
			new PublicacaoRepositorioFalso(encontrada), new IntranetOptions(), trilha);

		await handler.HandleAsync(new EditarPublicacaoCommand(
			encontrada.Publicacao.Id, new HashSet<string>(["financeiro"], StringComparer.OrdinalIgnoreCase),
			ExigirVinculo: true, "Novo", "Corpo", TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, DateTimeOffset.UtcNow, null));

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MuralEditar);
		registro.RecursoId.Should().Be(encontrada.Publicacao.Id.ToString());
	}

	[Fact]
	public async Task Arquivar_RegistraMuralArquivar()
	{
		var trilha = new TrilhaFalsa();
		var encontrada = Existente();
		var handler = new ArquivarPublicacaoHandler(new PublicacaoRepositorioFalso(encontrada), trilha);

		await handler.HandleAsync(new ArquivarPublicacaoCommand(
			encontrada.Publicacao.Id,
			new HashSet<string>(["financeiro"], StringComparer.OrdinalIgnoreCase),
			ExigirVinculo: true));

		trilha.Registros.Should().ContainSingle()
			.Which.Verbo.Should().Be(VerbosDeAuditoria.MuralArquivar);
	}

	[Fact]
	public async Task Negado_NaoRegistra()
	{
		var trilha = new TrilhaFalsa();
		var handler = new ArquivarPublicacaoHandler(new PublicacaoRepositorioFalso(Existente()), trilha);

		var resultado = await handler.HandleAsync(new ArquivarPublicacaoCommand(
			Guid.NewGuid(),
			new HashSet<string>(["diretoria"], StringComparer.OrdinalIgnoreCase),
			ExigirVinculo: true));

		resultado.IsFailure.Should().BeTrue();
		trilha.Registros.Should().BeEmpty("o que nao aconteceu nao entra na trilha");
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --filter FullyQualifiedName~AuditoriaDoMuralTests`
Expected: falha de compilação — os construtores dos três handlers não aceitam o parâmetro novo.

- [ ] **Step 3: Ligar no `PublicarPublicacaoHandler`**

Acrescente `using System.Text.Json;` e `using Secco.Intranet.Application.Auditoria;` ao topo.

No construtor primário, depois de `NotificacaoOptions notificacaoOptions`, acrescente:

```csharp
	ITrilhaDeAuditoria trilha)
```

E, no `HandleAsync`, logo depois de `var dto = Projecao.De(...)` e **antes** de `AvisarAsync`:

```csharp
		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.MuralPublicar,
					RecursosDeAuditoria.Publicacao,
					dto.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						titulo = dto.Titulo,
						tipo = dto.Tipo.ToString(),
						visibilidade = dto.Visibilidade.ToString(),
						prioridade = dto.Prioridade.ToString(),
						setor = dto.SetorSlug,
					})),
				cancellationToken)
			.ConfigureAwait(false);
```

- [ ] **Step 4: Ligar no `EditarPublicacaoHandler`**

Acrescente os mesmos dois `using`. O construtor passa a ser:

```csharp
public sealed class EditarPublicacaoHandler(
	IPublicacaoRepository repository,
	IntranetOptions limites,
	ITrilhaDeAuditoria trilha)
```

Logo antes do `return Projecao.De(encontrada);`:

```csharp
		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.MuralEditar,
					RecursosDeAuditoria.Publicacao,
					encontrada.Publicacao.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						titulo = encontrada.Publicacao.Titulo,
						tipo = encontrada.Publicacao.Tipo.ToString(),
						visibilidade = encontrada.Publicacao.Visibilidade.ToString(),
						prioridade = encontrada.Publicacao.Prioridade.ToString(),
						setor = encontrada.SetorSlug,
					})),
				cancellationToken)
			.ConfigureAwait(false);
```

- [ ] **Step 5: Ligar no `ArquivarPublicacaoHandler`**

Acrescente os mesmos dois `using`. O construtor passa a ser:

```csharp
public sealed class ArquivarPublicacaoHandler(IPublicacaoRepository repository, ITrilhaDeAuditoria trilha)
```

Logo antes do `return encontrada.Publicacao.Id;` **final** (o que vem depois de
`SaveChangesAsync`, não o do atalho de publicação já arquivada):

```csharp
		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.MuralArquivar,
					RecursosDeAuditoria.Publicacao,
					encontrada.Publicacao.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						titulo = encontrada.Publicacao.Titulo,
						setor = encontrada.SetorSlug,
					})),
				cancellationToken)
			.ConfigureAwait(false);
```

- [ ] **Step 6: Rodar a suíte**

Run: `dotnet build && dotnet test`
Expected: build com 0 avisos e suíte verde. Os testes de `AvisoDePublicacaoTests` e
`PublicacaoAutorizacaoTests` vão exigir o parâmetro novo nos construtores — acrescente a eles
uma `TrilhaFalsa` ou uma instância de `TrilhaSilenciosa` com `NullLogger`.

- [ ] **Step 7: Commit**

```bash
git add src/Secco.Intranet.Application/Publicacoes tests/Secco.Intranet.Tests/Unit
git commit -m "feat(auditoria): verbos do Mural

O metadata leva titulo, tipo, visibilidade, prioridade e setor - nunca o corpo.
A trilha diz o que aconteceu; duplicar conteudo sensivel num servico de
observabilidade cria um segundo lugar de onde ele pode vazar."
```

---

## Task 4: Verbos de Documentos

**Files:**
- Modify: `src/Secco.Intranet.Application/Documentos/PublicarDocumentoHandler.cs`
- Modify: `src/Secco.Intranet.Application/Documentos/ArquivarDocumentoHandler.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/AuditoriaDeDocumentosTests.cs`

**Interfaces:**
- Consumes: `ITrilhaDeAuditoria`, `RegistroDeAuditoria`, `VerbosDeAuditoria`, `RecursosDeAuditoria` (Task 1).
- Produces: os dois handlers passam a receber `ITrilhaDeAuditoria` como último parâmetro.

- [ ] **Step 1: Escrever o teste**

```csharp
using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Os verbos de Documentos. Escrita entra na trilha; leitura não — decisão registrada no
/// spec de 2026-09-08, com a consequência assumida de não haver como saber quem baixou.
/// </summary>
public class AuditoriaDeDocumentosTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public List<RegistroDeAuditoria> Registros { get; } = [];

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Registros.Add(registro);

			return Task.CompletedTask;
		}
	}

	private sealed class RepositorioFalso(DocumentoComSetor? resultado) : IDocumentoRepository
	{
		public Task AddAsync(Documento documento, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<DocumentoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(resultado);

		public Task<IReadOnlyList<DocumentoDto>> ListarPorSetorAsync(
			string setorSlug,
			CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<DocumentoDto>>([]);
	}

	private static DocumentoComSetor Existente()
	{
		var documento = new Documento(
			Guid.NewGuid(),
			"Política interna",
			descricao: null,
			"politica.pdf",
			"application/pdf",
			tamanho: 128,
			Visibilidade.Setor,
			"tenant/ab/arquivo",
			"secco-enc:v1:x",
			"quem.publicou");

		return new DocumentoComSetor(documento, "Financeiro", "financeiro");
	}

	private static HashSet<string> Setores(params string[] slugs) =>
		new(slugs, StringComparer.OrdinalIgnoreCase);

	[Fact]
	public async Task Arquivar_RegistraDocumentoArquivar()
	{
		var trilha = new TrilhaFalsa();
		var encontrado = Existente();
		var handler = new ArquivarDocumentoHandler(new RepositorioFalso(encontrado), trilha);

		await handler.HandleAsync(new ArquivarDocumentoCommand(
			encontrado.Documento.Id, Setores("financeiro"), ExigirVinculo: true));

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.DocumentoArquivar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Documento);
		registro.RecursoId.Should().Be(encontrado.Documento.Id.ToString());
		JsonDocument.Parse(registro.Metadata!).RootElement.GetProperty("setor").GetString()
			.Should().Be("financeiro");
	}

	[Fact]
	public async Task Arquivar_Negado_NaoRegistra()
	{
		var trilha = new TrilhaFalsa();
		var handler = new ArquivarDocumentoHandler(new RepositorioFalso(Existente()), trilha);

		var resultado = await handler.HandleAsync(new ArquivarDocumentoCommand(
			Guid.NewGuid(), Setores("diretoria"), ExigirVinculo: true));

		resultado.IsFailure.Should().BeTrue();
		trilha.Registros.Should().BeEmpty("o que não aconteceu não entra na trilha");
	}

	[Fact]
	public void Publicar_DependeDaTrilha()
	{
		var dependencias = typeof(PublicarDocumentoHandler)
			.GetConstructors()
			.Single()
			.GetParameters()
			.Select(parametro => parametro.ParameterType);

		dependencias.Should().Contain(
			typeof(ITrilhaDeAuditoria),
			"montar este handler num teste exigiria IArquivoStore, Stream e validação de magic "
			+ "bytes; a asserção de dependência prova a fiação sem duplicar esse setup, e o "
			+ "conteúdo do registro é conferido na verificação de navegador da Task 6");
	}

	[Fact]
	public void Baixar_NaoDependeDaTrilha()
	{
		var dependencias = typeof(BaixarDocumentoHandler)
			.GetConstructors()
			.Single()
			.GetParameters()
			.Select(parametro => parametro.ParameterType);

		dependencias.Should().NotContain(
			typeof(ITrilhaDeAuditoria),
			"leitura fica fora da trilha por decisão registrada no spec de 2026-09-08; "
			+ "este teste existe para que acrescentar auditoria de leitura seja deliberado — "
			+ "quem o fizer precisa apagar este teste, e portanto rever a decisão");
	}
}
```

> Os dois testes de reflexão são deliberados e simétricos: um fixa que publicar **tem** a
> trilha, o outro que baixar **não tem**. Nenhum dos dois prova o conteúdo do registro — provam
> a fiação, que é o que quebra silenciosamente. O conteúdo de `documento.publicar` é conferido
> na verificação de navegador da Task 6.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --filter FullyQualifiedName~AuditoriaDeDocumentosTests`
Expected: falha de compilação — os construtores não aceitam o parâmetro novo.

- [ ] **Step 3: Ligar no `PublicarDocumentoHandler`**

Acrescente `using System.Text.Json;` e `using Secco.Intranet.Application.Auditoria;`. O
construtor ganha `ITrilhaDeAuditoria trilha` como último parâmetro. Logo antes do `return` de
sucesso:

```csharp
		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DocumentoPublicar,
					RecursosDeAuditoria.Documento,
					documento.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						titulo = documento.Titulo,
						arquivo = documento.NomeArquivo,
						visibilidade = documento.Visibilidade.ToString(),
						tamanho = documento.Tamanho,
						setor = setor.Slug,
					})),
				cancellationToken)
			.ConfigureAwait(false);
```

> Use os nomes de variável que já existem no método — se a entidade criada tiver outro nome,
> ajuste; o que não muda é o conjunto de campos.

- [ ] **Step 4: Ligar no `ArquivarDocumentoHandler`**

Mesmos `using`, mesmo parâmetro novo. Logo antes do `return` de sucesso:

```csharp
		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DocumentoArquivar,
					RecursosDeAuditoria.Documento,
					encontrado.Documento.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						titulo = encontrado.Documento.Titulo,
						setor = encontrado.SetorSlug,
					})),
				cancellationToken)
			.ConfigureAwait(false);
```

- [ ] **Step 5: Rodar a suíte**

Run: `dotnet build && dotnet test`
Expected: build com 0 avisos e suíte verde.

- [ ] **Step 6: Commit**

```bash
git add src/Secco.Intranet.Application/Documentos tests/Secco.Intranet.Tests/Unit
git commit -m "feat(auditoria): verbos de Documentos

Baixar continua fora da trilha, por decisao registrada no spec. Entra um teste
que fixa a ausencia: acrescentar auditoria de leitura passa a exigir mexer no
teste, e portanto revisar a decisao."
```

---

## Task 5: Verbos de Setor

O único ponto com escolha de verbo: a mesma operação registra `editar`, `desativar` ou `reativar`.

**Files:**
- Modify: `src/Secco.Intranet.Application/Setores/CreateSetorHandler.cs`
- Modify: `src/Secco.Intranet.Application/Setores/EditarSetorHandler.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/AuditoriaDeSetorTests.cs`

**Interfaces:**
- Consumes: `ITrilhaDeAuditoria`, `RegistroDeAuditoria`, `VerbosDeAuditoria`, `RecursosDeAuditoria` (Task 1).
- Produces: os dois handlers passam a receber `ITrilhaDeAuditoria` como último parâmetro.

- [ ] **Step 1: Escrever o teste**

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Os verbos de Setor. Desativar ganha verbo próprio porque muda quem enxerga o quê — e um
/// "setor.editar" genérico esconderia exatamente a mudança que alguém vai procurar.
/// </summary>
public class AuditoriaDeSetorTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public List<RegistroDeAuditoria> Registros { get; } = [];

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Registros.Add(registro);

			return Task.CompletedTask;
		}
	}

	private sealed class RepositorioFalso(Setor? setor) : ISetorRepository
	{
		public Task AddAsync(Setor novo, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(setor);

		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(setor);

		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(null);

		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(false);

		public Task<PagedResult<Setor>> SearchAsync(
			SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<Setor>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private static EditarSetorHandler Montar(Setor setor, TrilhaFalsa trilha) =>
		new(new RepositorioFalso(setor), new IntranetOptions(), trilha);

	[Fact]
	public async Task Editar_SoNomeEIcone_RegistraSetorEditar()
	{
		var trilha = new TrilhaFalsa();
		var setor = new Setor("Financeiro", "financeiro");

		await Montar(setor, trilha).HandleAsync(
			new EditarSetorCommand(Guid.NewGuid(), "Financeiro e Controladoria", "bi-cash-coin", Ativo: true));

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.SetorEditar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Setor);
	}

	[Fact]
	public async Task Editar_Desativando_RegistraSetorDesativar()
	{
		var trilha = new TrilhaFalsa();
		var setor = new Setor("Financeiro", "financeiro");

		await Montar(setor, trilha).HandleAsync(
			new EditarSetorCommand(Guid.NewGuid(), "Financeiro", "bi-cash-coin", Ativo: false));

		trilha.Registros.Should().ContainSingle()
			.Which.Verbo.Should().Be(VerbosDeAuditoria.SetorDesativar,
				"desativar muda quem enxerga o quê, e um 'editar' genérico esconderia isso");
	}

	[Fact]
	public async Task Editar_Reativando_RegistraSetorReativar()
	{
		var trilha = new TrilhaFalsa();
		var setor = new Setor("Financeiro", "financeiro");
		setor.Desativar();

		await Montar(setor, trilha).HandleAsync(
			new EditarSetorCommand(Guid.NewGuid(), "Financeiro", "bi-cash-coin", Ativo: true));

		trilha.Registros.Should().ContainSingle()
			.Which.Verbo.Should().Be(VerbosDeAuditoria.SetorReativar);
	}

	[Fact]
	public async Task Editar_Recusado_NaoRegistra()
	{
		var trilha = new TrilhaFalsa();
		var fixo = new Setor("Infraestrutura", "infraestrutura", fixo: true);

		var resultado = await Montar(fixo, trilha).HandleAsync(
			new EditarSetorCommand(Guid.NewGuid(), "Infra", "bi-hdd-rack", Ativo: false));

		resultado.IsFailure.Should().BeTrue();
		trilha.Registros.Should().BeEmpty();
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --filter FullyQualifiedName~AuditoriaDeSetorTests`
Expected: falha de compilação — os construtores não aceitam o parâmetro novo.

- [ ] **Step 3: Ligar no `CreateSetorHandler`**

Acrescente `using System.Text.Json;` e `using Secco.Intranet.Application.Auditoria;`. O
construtor ganha `ITrilhaDeAuditoria trilha` como último parâmetro. Logo antes do
`return SetorDto.FromEntity(setor);`:

```csharp
		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.SetorCriar,
					RecursosDeAuditoria.Setor,
					setor.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						nome = setor.Nome,
						slug = setor.Slug,
						icone = setor.Icone,
						fixo = setor.Fixo,
					})),
				cancellationToken)
			.ConfigureAwait(false);
```

- [ ] **Step 4: Ligar no `EditarSetorHandler`**

Acrescente os mesmos dois `using` e o parâmetro `ITrilhaDeAuditoria trilha`.

A situação anterior precisa ser lida **antes** das alterações, senão o verbo sai errado.
Imediatamente depois da guarda de setor fixo e **antes** de `setor.Renomear(...)`:

```csharp
		var estavaAtivo = setor.Ativo;
```

E logo antes do `return SetorDto.FromEntity(setor);`:

```csharp
		// Desativar e reativar ganham verbo proprio porque mudam quem enxerga o que; um
		// "setor.editar" generico esconderia exatamente a mudanca que alguem vai procurar.
		var verbo = (estavaAtivo, setor.Ativo) switch
		{
			(true, false) => VerbosDeAuditoria.SetorDesativar,
			(false, true) => VerbosDeAuditoria.SetorReativar,
			_ => VerbosDeAuditoria.SetorEditar,
		};

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					verbo,
					RecursosDeAuditoria.Setor,
					setor.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						nome = setor.Nome,
						slug = setor.Slug,
						icone = setor.Icone,
						ativo = setor.Ativo,
					})),
				cancellationToken)
			.ConfigureAwait(false);
```

- [ ] **Step 5: Rodar a suíte**

Run: `dotnet build && dotnet test`
Expected: build com 0 avisos e suíte verde. `CreateSetorHandlerTests` e
`EditarSetorHandlerTests` vão exigir o parâmetro novo — acrescente a `TrilhaFalsa` a eles.

- [ ] **Step 6: Commit**

```bash
git add src/Secco.Intranet.Application/Setores tests/Secco.Intranet.Tests/Unit
git commit -m "feat(auditoria): verbos de Setor

Desativar e reativar ganham verbo proprio: mudam quem enxerga o que, e um
setor.editar generico esconderia a mudanca que alguem vai procurar.

A situacao anterior e lida antes das alteracoes - depois de Renomear e
DefinirIcone o setor ja mudou, e o verbo sairia errado."
```

---

## Task 6: Fechamento — documentação e verificação

**Files:**
- Modify: `docs/roadmap.md`
- Modify: `docs/plataforma.md`
- Modify: `README.md`

- [ ] **Step 1: Marcar no roadmap**

Em `docs/roadmap.md`, abaixo da linha da notificação do Mural:

```markdown
- [x] Auditoria transversal: verbos de Mural, Documentos e Setor no `Secco.LogStream` —
      [spec](specs/2026-09-08-auditoria-design.md). Leitura de documento fica fora, por
      decisão registrada
```

- [ ] **Step 2: Registrar a lacuna recorrente**

Em `docs/plataforma.md`, na tabela **Demandas abertas**:

```markdown
| Extensões de client aceitarem client credentials | `AddNotificationHubClient()` e `AddLogStreamClient()` só aceitam `BaseUrl`, mas todos os endpoints dos dois exigem permissão — cada adotante reescreve a composição do `HttpClient` à mão para anexar o `SeccoClientCredentialsHandler` | (a abrir) |
```

> Abrir a issue no `secco-platform` é passo para fora deste repositório. **Pergunte antes**;
> se a resposta for não, deixe a linha com "(a abrir)" e siga.

- [ ] **Step 3: Atualizar o README**

Na tabela de tecnologias, abaixo da linha de Notificação:

```markdown
| Auditoria           | `Secco.LogStream.Client`                                       | Trilha de ação de usuário; a trilha vive no LogStream (ADR-0006)      |
```

- [ ] **Step 4: Verificação final**

```bash
dotnet build
dotnet test
```

Expected: build com 0 avisos e a suíte verde.

- [ ] **Step 5: Verificar no navegador**

Suba a aplicação e confira que **nada quebrou** — em DEV não há LogStream configurado nem
usuário autenticado, então o caminho exercitado é o no-op:

1. `/` abre e lista o mural.
2. `/setor/<slug>/avisos` publica um aviso sem erro.
3. `/Setores/Create` cria um setor e `/Setores/Edit/<id>` salva.
4. Nenhuma exceção no console da aplicação.

- [ ] **Step 6: Commit**

```bash
git add docs/roadmap.md docs/plataforma.md README.md
git commit -m "docs(auditoria): fecha o item no roadmap e registra a lacuna recorrente de client"
```

---

## Depois deste plano

1. **Tela para ler a trilha** — `SearchAuditEntries` existe. Quem consulta e por qual tela encosta na demanda [#4](https://github.com/rafsecco/secco-platform/issues/4) da plataforma, ainda aberta.
2. **`AddLogStream()` do `Secco.SDK.Logging`** — o sink `ILogger` → LogStream, assunto separado. A **0.1.0 nunca deve ser usada**: foi empacotada contra o `LogStream.Client` 0.2.0 e falha com `MissingMethodException` que o dispatcher engole, então o sintoma é log que simplesmente não chega.
3. **Auditoria de leitura de documento** — decidida como fora de escopo em 2026-09-08. Reverter custa um verbo e uma chamada no `BaixarDocumentoHandler`, sem migration.
