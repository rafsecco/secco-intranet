# Área de tenants — subsistema 2 (perfis e usuários do tenant administrado) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O `intranet-admin` administra perfis, permissões, pessoas e a federação Entra de um tenant cadastrado na Área de tenants, em `/tenants/{tenantId}/acesso`.

**Architecture:** Porta nova `IGestaoDeAcessoDoTenant` com `tenantId` explícito em toda assinatura, handlers próprios em `Application/Tenants/Acesso` com só as regras genéricas, e controller próprio sob a cadeia de filtros da Área de tenants mais o filtro novo `[SecureGateLigado]`. O tratamento de erro do adaptador atual do `/acesso` vira um núcleo compartilhado (`OperacoesDeAcessoNoSecureGate`), sem mudar o comportamento do `/acesso`.

**Tech Stack:** .NET 10, ASP.NET Core MVC, `Secco.SecureGate.Client` 0.15.0, xUnit + AwesomeAssertions, `IntranetWebFactory` (SQL Server de teste).

**Spec:** [`docs/specs/2026-10-11-area-administrativa-tenants-acesso-design.md`](../specs/2026-10-11-area-administrativa-tenants-acesso-design.md)

## Global Constraints

- Indentação: tabs em `.cs` e `.cshtml` (`.editorconfig`); documentação XML em todo membro público.
- Commits direto na `main`, um por tarefa, com a linha `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Build: `dotnet build Secco.Intranet.slnx -warnaserror` sem warnings.
- Testes: `dotnet test tests/Secco.Intranet.Tests` (integração exige Docker ou `SECCO_TEST_SQLSERVER`).
- O `tenantId` vem **só da rota** (`[FromRoute]`), nunca de form ou query.
- Tenant fora do cadastro e tenant com SecureGate desligado: **404**, nunca 403.
- Toda escrita na trilha da Intranet com `RecursosDeAuditoria.Tenant`; **nunca** e-mail do cadastrado nem a lista de permissões no payload.
- Permissões: uma por linha, validadas por `SeccoPermissions.IsValid`, no máximo **200**, duplicadas descartadas.
- Perfis reservados (`ClassificacaoDePerfil.EhReservado`) nunca aparecem em seletor nem são criados, excluídos ou atribuídos.
- Views só com parciais do contrato de tema (`_PageHeader`, `_Badge`, `_EmptyState`, `_Pagination`, classes `sc-*`).

## Review Focus

- Perfil com nome contendo ponto (`compras.aprovador`) na rota `perfis/{nome}`: precisa abrir e editar normalmente → teste na Tarefa 6.
- Texto de permissões só com linhas em branco ou espaços: deve gravar lista vazia (tirar todas), não dar erro → teste na Tarefa 4.
- Trocar o directory id já preenchido sem marcar a confirmação: recusa com mensagem que explica, nada gravado → teste na Tarefa 5.
- `perfis` do cadastro com um nome repetido ou com espaços nas pontas: normalizar e não duplicar → teste na Tarefa 5.
- Página de membros fora do intervalo (`page=999`) ou negativa: não pode dar 500 → teste na Tarefa 3.

---

## Mapa de arquivos

| Arquivo | Responsabilidade |
|---|---|
| `src/Secco.Intranet.Infrastructure/Access/OperacoesDeAcessoNoSecureGate.cs` (novo) | Núcleo: chamada ao client, tradução de erro, mapeamentos comuns |
| `src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcesso.cs` | Passa a delegar ao núcleo (sem mudança de comportamento) |
| `src/Secco.Intranet.Application/Tenants/Acesso/IGestaoDeAcessoDoTenant.cs` (novo) | Porta |
| `src/Secco.Intranet.Application/Tenants/Acesso/AcessoDoTenantDtos.cs` (novo) | DTOs próprios |
| `src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcessoDoTenant.cs` (novo) | Adaptador real |
| `src/Secco.Intranet.Infrastructure/Access/GestaoDeAcessoDoTenantIndisponivel.cs` (novo) | Adaptador no-op |
| `src/Secco.Intranet.Application/Tenants/Acesso/LeituraDoAcessoDoTenantHandler.cs` (novo) | Leituras e telas |
| `src/Secco.Intranet.Application/Tenants/Acesso/CabecalhoDoTenant.cs` (novo) | `ObterCabecalhoDoTenantHandler` e `VerificarSecureGateLigadoHandler` |
| `src/Secco.Intranet.Application/Tenants/Acesso/ListaDePermissoes.cs` (novo) | Parse e validação do texto de permissões |
| `src/Secco.Intranet.Application/Tenants/Acesso/PerfisDoTenantHandler.cs` (novo) | Criar, excluir, permissões |
| `src/Secco.Intranet.Application/Tenants/Acesso/UsuariosDoTenantHandler.cs` (novo) | Cadastro, perfis, conta, convite |
| `src/Secco.Intranet.Application/Tenants/Acesso/FederacaoDoTenantHandler.cs` (novo) | Definir federação |
| `src/Secco.Intranet.Application/Tenants/Acesso/AuditoriaDoAcessoDoTenant.cs` (novo) | Registros na trilha |
| `src/Secco.Intranet.Application/IntranetErrors.cs` | Erros novos em `Acesso` |
| `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs` | Verbos `tenant.acesso.*` |
| `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs` | Registro dos handlers |
| `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` | Escolha do adaptador |
| `src/Secco.Intranet.Web/Authentication/SecureGateLigadoAttribute.cs` (novo) | Filtro 404 |
| `src/Secco.Intranet.Web/Controllers/TenantsAcessoController.cs` (novo) | Rotas |
| `src/Secco.Intranet.Web/Models/Tenants/TenantsAcessoViewModels.cs` (novo) | View models |
| `src/Secco.Intranet.Web/Views/TenantsAcesso/{Index,Perfil,Usuario,Novo}.cshtml` (novos) | Telas |
| `src/Secco.Intranet.Web/Views/Tenants/Detalhe.cshtml` | Link "Perfis e usuários" |
| `tests/Secco.Intranet.Tests/Support/DublesDeAcessoDoTenant.cs` (novo) | Dublê da porta |
| `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs` | Propriedade `GestaoDeAcessoDoTenant` |

---

### Task 1: Núcleo compartilhado do adaptador

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Access/OperacoesDeAcessoNoSecureGate.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcesso.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/OperacoesDeAcessoNoSecureGateTests.cs`

**Interfaces:**
- Produces:
  - `internal static class OperacoesDeAcessoNoSecureGate`
  - `Task<Result<T>> LerAsync<T>(ILogger logger, string operacao, Func<CancellationToken, Task<T>> chamada, Error? naoEncontrado, CancellationToken cancellationToken)`
  - `Task<Result> EscreverAsync(ILogger logger, string operacao, Func<CancellationToken, Task> chamada, Error? naoEncontrado, Func<ApiException, Error?>? conflito, CancellationToken cancellationToken)`
  - `Task<Result<T>> EscreverAsync<T>(ILogger logger, string operacao, Func<CancellationToken, Task<T>> chamada, Error? naoEncontrado, Func<ApiException, Error?>? conflito, CancellationToken cancellationToken, Error? recusado = null)` — `recusado` traduz o 400 da plataforma (dado recusado, não SecureGate fora)
  - `SituacaoDoUsuario Situacao(string? status)`
  - `bool TemCodigo(ApiException excecao, string codigo)` — `true` se o corpo da resposta contém o código de erro da plataforma

- [ ] **Step 1: Escrever o teste do núcleo**

```csharp
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>O núcleo que os dois adaptadores de acesso compartilham: tradução de erro e mapeamentos.</summary>
public class OperacoesDeAcessoNoSecureGateTests
{
	private static ApiException Api(int status, string corpo = "") =>
		new("falha", status, corpo, new Dictionary<string, IEnumerable<string>>(), null);

	[Fact]
	public async Task Ler_404ComErroDeNaoEncontrado_DevolveOErro()
	{
		var lido = await OperacoesDeAcessoNoSecureGate.LerAsync<int>(
			NullLogger.Instance, "ler", _ => throw Api(404), IntranetErrors.Acesso.PerfilNaoEncontrado, CancellationToken.None);

		lido.Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado);
	}

	[Fact]
	public async Task Ler_TimeoutDoHttpClient_ViraIndisponivel_NaoExcecao()
	{
		var lido = await OperacoesDeAcessoNoSecureGate.LerAsync<int>(
			NullLogger.Instance, "ler", _ => throw new TaskCanceledException(), null, CancellationToken.None);

		lido.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task Escrever_409_UsaOMapeamentoDeConflitoPeloCodigo()
	{
		var escrito = await OperacoesDeAcessoNoSecureGate.EscreverAsync(
			NullLogger.Instance,
			"excluir",
			_ => throw Api(409, "{\"code\":\"SecureGate.Role.UsedByClients\"}"),
			null,
			excecao => OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Role.UsedByClients")
				? IntranetErrors.Acesso.PerfilUsadoPorClient
				: IntranetErrors.Acesso.PerfilComMembros,
			CancellationToken.None);

		escrito.Error.Should().Be(IntranetErrors.Acesso.PerfilUsadoPorClient);
	}

	[Fact]
	public async Task Escrever_CancelamentoDoChamador_ERelancado()
	{
		using var cancelado = new CancellationTokenSource();
		await cancelado.CancelAsync();

		var escrever = () => OperacoesDeAcessoNoSecureGate.EscreverAsync(
			NullLogger.Instance, "x", token => Task.FromCanceled(token), null, null, cancelado.Token);

		await escrever.Should().ThrowAsync<OperationCanceledException>();
	}

	[Theory]
	[InlineData("Active", SituacaoDoUsuario.Ativo)]
	[InlineData("deactivated", SituacaoDoUsuario.Desativado)]
	[InlineData("LockedOut", SituacaoDoUsuario.Bloqueado)]
	[InlineData(null, SituacaoDoUsuario.Desconhecida)]
	public void Situacao_MapeiaOStatusDaPlataforma(string? status, SituacaoDoUsuario esperada) =>
		OperacoesDeAcessoNoSecureGate.Situacao(status).Should().Be(esperada);
}
```

- [ ] **Step 2: Acrescentar o erro `PerfilUsadoPorClient` usado pelo teste**

Em `src/Secco.Intranet.Application/IntranetErrors.cs`, dentro de `public static class Acesso`, logo depois de `PerfilComMembros`:

```csharp
		/// <summary>Perfil usado por client de máquina do tenant (ADR-0037 da plataforma).</summary>
		public static readonly Error PerfilUsadoPorClient =
			Error.Conflict(
				"Intranet.Acesso.PerfilUsadoPorClient",
				"O perfil está em uso por uma credencial de sistema deste tenant. Retire-o da credencial antes de excluir.");
```

- [ ] **Step 3: Rodar o teste e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~OperacoesDeAcessoNoSecureGateTests"`
Expected: erro de compilação — `OperacoesDeAcessoNoSecureGate` não existe.

- [ ] **Step 4: Criar o núcleo**

`src/Secco.Intranet.Infrastructure/Access/OperacoesDeAcessoNoSecureGate.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Núcleo dos adaptadores de acesso (o do tenant da requisição e o do tenant administrado): a
/// chamada ao client, a tradução de falha em <see cref="Result"/> e os mapeamentos comuns. O tenant
/// é sempre decidido por quem chama. Falha de rede, de status e timeout viram
/// <see cref="IntranetErrors.Acesso.Indisponivel"/>; cancelamento pedido pelo chamador é relançado.
/// O log leva só a operação e o status, nunca corpo ou credencial (ADR-0020).
/// </summary>
internal static class OperacoesDeAcessoNoSecureGate
{
	/// <summary>Executa uma leitura.</summary>
	public static async Task<Result<T>> LerAsync<T>(
		ILogger logger,
		string operacao,
		Func<CancellationToken, Task<T>> chamada,
		Error? naoEncontrado,
		CancellationToken cancellationToken)
	{
		try
		{
			return Result.Success(await chamada(cancellationToken).ConfigureAwait(false));
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.NotFound && naoEncontrado is not null)
		{
			return Result.Failure<T>(naoEncontrado);
		}
		catch (Exception excecao) when (EhFalhaDeBorda(excecao, cancellationToken))
		{
			Registrar(logger, operacao, excecao);

			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}
	}

	/// <summary>Executa uma escrita sem retorno.</summary>
	public static async Task<Result> EscreverAsync(
		ILogger logger,
		string operacao,
		Func<CancellationToken, Task> chamada,
		Error? naoEncontrado,
		Func<ApiException, Error?>? conflito,
		CancellationToken cancellationToken)
	{
		var escrito = await EscreverAsync<bool>(
			logger,
			operacao,
			async token =>
			{
				await chamada(token).ConfigureAwait(false);

				return true;
			},
			naoEncontrado,
			conflito,
			cancellationToken).ConfigureAwait(false);

		return escrito.IsSuccess ? Result.Success() : Result.Failure(escrito.Error);
	}

	/// <summary>Executa uma escrita que devolve valor (ex.: o id criado). <paramref name="recusado"/> traduz o 400.</summary>
	public static async Task<Result<T>> EscreverAsync<T>(
		ILogger logger,
		string operacao,
		Func<CancellationToken, Task<T>> chamada,
		Error? naoEncontrado,
		Func<ApiException, Error?>? conflito,
		CancellationToken cancellationToken,
		Error? recusado = null)
	{
		try
		{
			return Result.Success(await chamada(cancellationToken).ConfigureAwait(false));
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.NotFound && naoEncontrado is not null)
		{
			return Result.Failure<T>(naoEncontrado);
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.Conflict
			&& conflito?.Invoke(apiException) is { } erro)
		{
			return Result.Failure<T>(erro);
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.BadRequest && recusado is not null)
		{
			return Result.Failure<T>(recusado);
		}
		catch (Exception excecao) when (EhFalhaDeBorda(excecao, cancellationToken))
		{
			Registrar(logger, operacao, excecao);

			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}
	}

	/// <summary>Situação da conta a partir do status textual da plataforma.</summary>
	public static SituacaoDoUsuario Situacao(string? status) => status?.ToLowerInvariant() switch
	{
		"active" => SituacaoDoUsuario.Ativo,
		"deactivated" => SituacaoDoUsuario.Desativado,
		"lockedout" => SituacaoDoUsuario.Bloqueado,
		_ => SituacaoDoUsuario.Desconhecida,
	};

	/// <summary>Se o ProblemDetails da resposta traz o código de erro informado.</summary>
	public static bool TemCodigo(ApiException excecao, string codigo) =>
		excecao.Response?.Contains(codigo, StringComparison.Ordinal) == true;

	// Timeout do HttpClient chega como OperationCanceledException sem o token do chamador cancelado:
	// é falha da borda, não cancelamento — escapar como exceção viraria um 500.
	private static bool EhFalhaDeBorda(Exception excecao, CancellationToken cancellationToken) => excecao switch
	{
		ApiException => true,
		HttpRequestException => true,
		OperationCanceledException => !cancellationToken.IsCancellationRequested,
		_ => false,
	};

	private static void Registrar(ILogger logger, string operacao, Exception excecao)
	{
		if (excecao is ApiException apiException)
		{
			logger.LogWarning("Falha ao {Operacao} no SecureGate (status {StatusCode}).", operacao, apiException.StatusCode);

			return;
		}

		logger.LogWarning(excecao, "Falha de rede ou timeout ao {Operacao} no SecureGate.", operacao);
	}
}
```

- [ ] **Step 5: Fazer o adaptador atual delegar ao núcleo**

Em `SecureGateGestaoDeAcesso.cs`, substituir o corpo de `LerAsync`, `EscreverAsync` e `Situacao` (fim do arquivo) por:

```csharp
	private static SituacaoDoUsuario Situacao(string? status) => OperacoesDeAcessoNoSecureGate.Situacao(status);

	private Task<Result<T>> LerAsync<T>(
		string operacao,
		Func<Guid, CancellationToken, Task<T>> chamada,
		Error? naoEncontrado,
		CancellationToken cancellationToken) =>
		tenantContext.IsResolved
			? OperacoesDeAcessoNoSecureGate.LerAsync(
				logger, operacao, token => chamada(tenantContext.TenantId!.Value, token), naoEncontrado, cancellationToken)
			: Task.FromResult(Result.Failure<T>(IntranetErrors.Acesso.Indisponivel));

	private Task<Result> EscreverAsync(
		string operacao,
		Func<Guid, CancellationToken, Task> chamada,
		Error? naoEncontrado,
		Error? conflito,
		CancellationToken cancellationToken) =>
		tenantContext.IsResolved
			? OperacoesDeAcessoNoSecureGate.EscreverAsync(
				logger,
				operacao,
				token => chamada(tenantContext.TenantId!.Value, token),
				naoEncontrado,
				conflito is null ? null : _ => conflito,
				cancellationToken)
			: Task.FromResult(Result.Failure(IntranetErrors.Acesso.Indisponivel));
```

Remover o `using System.Net;` se ficar sem uso.

- [ ] **Step 6: Rodar o núcleo e a suíte do `/acesso`**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~OperacoesDeAcessoNoSecureGateTests|FullyQualifiedName~SecureGateGestaoDeAcessoTests|FullyQualifiedName~Acesso"`
Expected: PASS em todos. A suíte do `/acesso` passando sem mudança é o critério de que o comportamento não mudou.

- [ ] **Step 7: Commit**

```bash
git add src/Secco.Intranet.Infrastructure/Access src/Secco.Intranet.Application/IntranetErrors.cs tests/Secco.Intranet.Tests/Unit/OperacoesDeAcessoNoSecureGateTests.cs
git commit -m "refactor(acesso): nucleo compartilhado de chamada ao SecureGate"
```

---

### Task 2: Porta, DTOs e adaptadores do acesso do tenant administrado

**Files:**
- Create: `src/Secco.Intranet.Application/Tenants/Acesso/IGestaoDeAcessoDoTenant.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Acesso/AcessoDoTenantDtos.cs`
- Create: `src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcessoDoTenant.cs`
- Create: `src/Secco.Intranet.Infrastructure/Access/GestaoDeAcessoDoTenantIndisponivel.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs` (classe `Acesso`)
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/SecureGateGestaoDeAcessoDoTenantTests.cs`

**Interfaces:**
- Consumes: `OperacoesDeAcessoNoSecureGate` (Task 1).
- Produces (namespace `Secco.Intranet.Application.Tenants.Acesso`):

```csharp
public sealed record PerfilDoTenantDto(string Nome, int TotalDePermissoes, bool Reservado);
public sealed record PerfilDoTenantDetalheDto(string Nome, IReadOnlyList<string> Permissoes, bool Reservado, int TotalDeMembros);
public sealed record UsuarioDoTenantDetalheDto(
	Guid Id, string Email, string? Nome, SituacaoDoUsuario Situacao, DateTimeOffset? BloqueadoAte,
	IReadOnlyList<string> Perfis, IReadOnlyList<string> LoginsExternos, bool DoisFatoresAtivo,
	bool TemSenha, bool EntraPorSenha);
public sealed record NovoUsuarioDoTenant(string Email, string? Nome, IReadOnlyList<string> Perfis, bool EntraPorSenha);

public interface IGestaoDeAcessoDoTenant
{
	Task<Result<IReadOnlyList<PerfilDoTenantDto>>> ListarPerfisAsync(Guid tenantId, CancellationToken cancellationToken = default);
	Task<Result<PerfilDoTenantDetalheDto>> ObterPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default);
	Task<Result<PaginaDeMembros>> ListarMembrosAsync(Guid tenantId, string nome, int pagina, CancellationToken cancellationToken = default);
	Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(Guid tenantId, CancellationToken cancellationToken = default);
	Task<Result<UsuarioDoTenantDetalheDto>> ObterUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default);
	Task<Result<FederacaoDto>> ObterFederacaoAsync(Guid tenantId, CancellationToken cancellationToken = default);
	Task<Result> CriarPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default);
	Task<Result> ExcluirPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default);
	Task<Result> DefinirPermissoesDoPerfilAsync(Guid tenantId, string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default);
	Task<Result> AtribuirPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default);
	Task<Result> RetirarPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default);
	Task<Result<Guid>> CadastrarUsuarioAsync(Guid tenantId, NovoUsuarioDoTenant novo, CancellationToken cancellationToken = default);
	Task<Result> ReenviarConviteAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default);
	Task<Result> DesativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default);
	Task<Result> ReativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default);
	Task<Result> EncerrarSessoesAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default);
	Task<Result> DefinirFederacaoAsync(Guid tenantId, Guid directoryId, bool habilitada, CancellationToken cancellationToken = default);
}
```

- Erros novos em `IntranetErrors.Acesso`: `EmailInvalido`, `EmailJaCadastrado`, `CadastroRecusado`, `EntraSemFederacao`, `ConviteNaoCabe`, `RetiradaRecusada`, `PermissoesDemais`, `TrocaDeDiretorioNaoConfirmada`, e o método `PermissoesInvalidas(IEnumerable<string> linhas)`.

- [ ] **Step 1: Escrever o teste do adaptador**

`tests/Secco.Intranet.Tests/Unit/SecureGateGestaoDeAcessoDoTenantTests.cs` — o ponto central é que **o tenant da chamada é sempre o recebido**, e o fake herda do client gerado como em `SecureGateGestaoDeAcessoTests`:

```csharp
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Adaptador do acesso do tenant administrado: tenant explícito, mapeamento do cadastro e dos 409.</summary>
public class SecureGateGestaoDeAcessoDoTenantTests
{
	private static readonly Guid Alvo = Guid.NewGuid();

	private sealed class ClientFalso() : SecureGateClient(new HttpClient())
	{
		public List<string> Chamadas { get; } = [];

		public CreateUserRequest? Criado { get; private set; }

		public Exception? Falha { get; set; }

		private Task Registrar(string chamada)
		{
			Chamadas.Add(chamada);

			return Falha is null ? Task.CompletedTask : Task.FromException(Falha);
		}

		public override async Task<ICollection<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken)
		{
			await Registrar($"ListRoles:{tenantId}");

			return [new RoleDto { Name = "compras-aprovador", Permissions = ["pedido:aprovar"] }];
		}

		public override async Task<UserDto> CreateUserAsync(Guid tenantId, CreateUserRequest body, CancellationToken cancellationToken)
		{
			await Registrar($"CreateUser:{tenantId}");
			Criado = body;

			return new UserDto { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Email = body.Email };
		}

		public override async Task<UserDetailDto> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
		{
			await Registrar($"GetUser:{tenantId}:{userId}");

			return new UserDetailDto
			{
				Id = userId, Email = "ana@compras.com", Status = "Active", HasPassword = false, LocalLoginEnabled = true,
				DisplayName = "Ana", Roles = ["compras-aprovador"],
			};
		}

		public override async Task DeleteRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken) =>
			await Registrar($"DeleteRole:{tenantId}:{role}");

		public override async Task ResendUserInviteAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
			await Registrar($"ResendUserInvite:{tenantId}:{userId}");
	}

	private static (SecureGateGestaoDeAcessoDoTenant Gestao, ClientFalso Client) Criar()
	{
		var client = new ClientFalso();

		return (new SecureGateGestaoDeAcessoDoTenant(client, NullLogger<SecureGateGestaoDeAcessoDoTenant>.Instance), client);
	}

	private static ApiException Api(int status, string corpo = "") =>
		new("falha", status, corpo, new Dictionary<string, IEnumerable<string>>(), null);

	[Fact]
	public async Task ListarPerfis_UsaOTenantRecebido()
	{
		var (gestao, client) = Criar();

		var perfis = await gestao.ListarPerfisAsync(Alvo);

		client.Chamadas.Should().Equal($"ListRoles:{Alvo}");
		perfis.Value.Should().ContainSingle().Which.Should().Be(new PerfilDoTenantDto("compras-aprovador", 1, false));
	}

	[Fact]
	public async Task Cadastrar_MontaOPedidoEDevolveOId()
	{
		var (gestao, client) = Criar();

		var id = await gestao.CadastrarUsuarioAsync(Alvo, new NovoUsuarioDoTenant("ana@compras.com", "Ana", ["compras-aprovador"], EntraPorSenha: false));

		id.Value.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
		client.Chamadas.Should().Equal($"CreateUser:{Alvo}");
		client.Criado!.Email.Should().Be("ana@compras.com");
		client.Criado.LocalLogin.Should().BeFalse();
		client.Criado.DisplayName.Should().Be("Ana");
		client.Criado.Roles.Should().Equal("compras-aprovador");
	}

	[Fact]
	public async Task Cadastrar_409_ViraEmailJaCadastrado()
	{
		var (gestao, client) = Criar();
		client.Falha = Api(409, "{\"code\":\"SecureGate.User.AlreadyExists\"}");

		var id = await gestao.CadastrarUsuarioAsync(Alvo, new NovoUsuarioDoTenant("ana@compras.com", null, [], true));

		id.Error.Should().Be(IntranetErrors.Acesso.EmailJaCadastrado);
	}

	[Fact]
	public async Task Cadastrar_400_ViraCadastroRecusado()
	{
		var (gestao, client) = Criar();
		client.Falha = Api(400);

		var id = await gestao.CadastrarUsuarioAsync(Alvo, new NovoUsuarioDoTenant("ana@compras.com", null, [], true));

		id.Error.Should().Be(IntranetErrors.Acesso.CadastroRecusado);
	}

	[Fact]
	public async Task ObterUsuario_TrazSenhaEModoDeEntrada()
	{
		var (gestao, _) = Criar();
		var usuario = Guid.NewGuid();

		var lido = await gestao.ObterUsuarioAsync(Alvo, usuario);

		lido.Value.TemSenha.Should().BeFalse();
		lido.Value.EntraPorSenha.Should().BeTrue();
		lido.Value.Nome.Should().Be("Ana");
	}

	[Theory]
	[InlineData("{\"code\":\"SecureGate.Role.UsedByClients\"}", "Intranet.Acesso.PerfilUsadoPorClient")]
	[InlineData("{\"code\":\"SecureGate.Role.HasMembers\"}", "Intranet.Acesso.PerfilComMembros")]
	public async Task ExcluirPerfil_409_DistingueOMotivo(string corpo, string codigoEsperado)
	{
		var (gestao, client) = Criar();
		client.Falha = Api(409, corpo);

		(await gestao.ExcluirPerfilAsync(Alvo, "x")).Error.Code.Should().Be(codigoEsperado);
	}

	[Fact]
	public async Task ReenviarConvite_409_ViraConviteNaoCabe()
	{
		var (gestao, client) = Criar();
		client.Falha = Api(409);

		(await gestao.ReenviarConviteAsync(Alvo, Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.ConviteNaoCabe);
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~SecureGateGestaoDeAcessoDoTenantTests"`
Expected: erro de compilação (tipos inexistentes).

- [ ] **Step 3: Erros novos**

Em `IntranetErrors.Acesso`, depois de `PerfilUsadoPorClient`:

```csharp
		/// <summary>E-mail ausente ou malformado no cadastro.</summary>
		public static readonly Error EmailInvalido =
			Error.Validation("Intranet.Acesso.EmailInvalido", "Informe um e-mail válido.");

		/// <summary>Já existe conta com o e-mail neste tenant.</summary>
		public static readonly Error EmailJaCadastrado =
			Error.Conflict("Intranet.Acesso.EmailJaCadastrado", "Já existe uma pessoa com este e-mail neste sistema.");

		/// <summary>A plataforma recusou o cadastro (dado inválido para ela).</summary>
		public static readonly Error CadastroRecusado =
			Error.Validation("Intranet.Acesso.CadastroRecusado", "O SecureGate recusou o cadastro. Confira o e-mail, o nome e os perfis.");

		/// <summary>"Só pelo Entra" pedido sem federação habilitada no tenant.</summary>
		public static readonly Error EntraSemFederacao =
			Error.Validation(
				"Intranet.Acesso.EntraSemFederacao",
				"Entrar só pelo Entra ID exige a federação habilitada para este sistema. Configure-a na aba Federação.");

		/// <summary>Reenvio de convite para quem já tem senha ou não entra por senha.</summary>
		public static readonly Error ConviteNaoCabe =
			Error.Conflict("Intranet.Acesso.ConviteNaoCabe", "Esta pessoa já definiu a senha ou não entra por senha; não há convite a reenviar.");

		/// <summary>A plataforma recusou retirar o perfil (ex.: atribuição vinda de grupo do diretório).</summary>
		public static readonly Error RetiradaRecusada =
			Error.Conflict("Intranet.Acesso.RetiradaRecusada", "O SecureGate recusou retirar este perfil; ele pode ter vindo de um grupo do diretório.");

		/// <summary>Mais permissões do que o limite por perfil.</summary>
		public static readonly Error PermissoesDemais =
			Error.Validation("Intranet.Acesso.PermissoesDemais", "Um perfil aceita no máximo 200 permissões.");

		/// <summary>Troca de directory id já configurado sem a confirmação explícita.</summary>
		public static readonly Error TrocaDeDiretorioNaoConfirmada =
			Error.Validation(
				"Intranet.Acesso.TrocaDeDiretorioNaoConfirmada",
				"Trocar o diretório muda de qual Entra ID as pessoas entram. Marque a confirmação para continuar.");

		/// <summary>Linhas de permissão fora do formato <c>recurso:acao</c>; cita até cinco.</summary>
		/// <param name="linhas">Linhas inválidas.</param>
		public static Error PermissoesInvalidas(IEnumerable<string> linhas)
		{
			ArgumentNullException.ThrowIfNull(linhas);

			var lista = linhas.ToList();
			var citadas = string.Join(", ", lista.Take(5));
			var resto = lista.Count > 5 ? $" e mais {lista.Count - 5}" : string.Empty;

			return Error.Validation(
				"Intranet.Acesso.PermissoesInvalidas",
				$"Fora do formato recurso:acao (minúsculas, dígitos e hífen): {citadas}{resto}. Nada foi gravado.");
		}
```

- [ ] **Step 4: DTOs e porta**

`src/Secco.Intranet.Application/Tenants/Acesso/AcessoDoTenantDtos.cs`:

```csharp
using Secco.Intranet.Application.Acesso;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>Perfil de um tenant administrado — sem a classificação setor/produto, que é convenção da Intranet.</summary>
/// <param name="Nome">Nome.</param>
/// <param name="TotalDePermissoes">Quantidade de permissões.</param>
/// <param name="Reservado">Se é reservado da plataforma.</param>
public sealed record PerfilDoTenantDto(string Nome, int TotalDePermissoes, bool Reservado);

/// <summary>Detalhe de um perfil de tenant administrado.</summary>
/// <param name="Nome">Nome.</param>
/// <param name="Permissoes">Permissões gravadas.</param>
/// <param name="Reservado">Se é reservado da plataforma.</param>
/// <param name="TotalDeMembros">Quantidade de membros.</param>
public sealed record PerfilDoTenantDetalheDto(string Nome, IReadOnlyList<string> Permissoes, bool Reservado, int TotalDeMembros);

/// <summary>Detalhe de uma pessoa do tenant administrado.</summary>
/// <param name="Id">Id no SecureGate.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Situacao">Situação da conta.</param>
/// <param name="BloqueadoAte">Fim do bloqueio, quando bloqueado.</param>
/// <param name="Perfis">Perfis atribuídos.</param>
/// <param name="LoginsExternos">Provedores de login externo vinculados.</param>
/// <param name="DoisFatoresAtivo">Se o segundo fator está ligado.</param>
/// <param name="TemSenha">Se já definiu senha.</param>
/// <param name="EntraPorSenha">Se a conta entra por senha (falso: só pelo Entra).</param>
public sealed record UsuarioDoTenantDetalheDto(
	Guid Id,
	string Email,
	string? Nome,
	SituacaoDoUsuario Situacao,
	DateTimeOffset? BloqueadoAte,
	IReadOnlyList<string> Perfis,
	IReadOnlyList<string> LoginsExternos,
	bool DoisFatoresAtivo,
	bool TemSenha,
	bool EntraPorSenha);

/// <summary>Dados de uma pessoa a cadastrar.</summary>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição (opcional).</param>
/// <param name="Perfis">Perfis atribuídos na criação.</param>
/// <param name="EntraPorSenha"><c>true</c>: convite para definir senha; <c>false</c>: só pelo Entra.</param>
public sealed record NovoUsuarioDoTenant(string Email, string? Nome, IReadOnlyList<string> Perfis, bool EntraPorSenha);
```

`src/Secco.Intranet.Application/Tenants/Acesso/IGestaoDeAcessoDoTenant.cs` — a interface exatamente como no bloco **Produces** acima, com o cabeçalho:

```csharp
using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>
/// Porta da gestão de acesso de um tenant administrado (subsistema 2 da Área de tenants). O tenant
/// é sempre explícito: quem chama já passou pelo cadastro (<c>[TenantAdministrado]</c>). Toda
/// falha volta como <see cref="Result"/>, sem falha aberta.
/// </summary>
public interface IGestaoDeAcessoDoTenant
{
	// métodos do bloco Produces, cada um com <summary> e <param>
}
```

- [ ] **Step 5: Adaptador real**

`src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcessoDoTenant.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Adapter real de <see cref="IGestaoDeAcessoDoTenant"/>, sobre o client administrativo do
/// SecureGate. Diferente de <see cref="SecureGateGestaoDeAcesso"/>, não lê o tenant da requisição:
/// usa só o recebido. A plataforma confere que o usuário pertence ao tenant da rota.
/// </summary>
/// <param name="client">Client administrativo do SecureGate.</param>
/// <param name="logger">Log de falhas — só operação e status.</param>
public sealed class SecureGateGestaoDeAcessoDoTenant(
	ISecureGateClient client,
	ILogger<SecureGateGestaoDeAcessoDoTenant> logger) : IGestaoDeAcessoDoTenant
{
	private const int TamanhoDaPaginaDeMembros = 100;
	private const string PerfilUsadoPorClients = "SecureGate.Role.UsedByClients";

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDoTenantDto>>> ListarPerfisAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync<IReadOnlyList<PerfilDoTenantDto>>(
			logger,
			"listar perfis do tenant",
			async token =>
			{
				var perfis = await client.ListRolesAsync(tenantId, token).ConfigureAwait(false);

				return [.. perfis.Select(p => new PerfilDoTenantDto(p.Name, p.Permissions?.Count ?? 0, ClassificacaoDePerfil.EhReservado(p.Name)))];
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<PerfilDoTenantDetalheDto>> ObterPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync(
			logger,
			"obter perfil do tenant",
			async token =>
			{
				var papel = await client.GetRoleAsync(tenantId, nome, token).ConfigureAwait(false);

				return new PerfilDoTenantDetalheDto(papel.Name, [.. papel.Permissions ?? []], papel.IsReserved, papel.MemberCount);
			},
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(Guid tenantId, string nome, int pagina, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync(
			logger,
			"listar membros do tenant",
			async token =>
			{
				var lida = await client.ListRoleMembersAsync(tenantId, nome, pagina, TamanhoDaPaginaDeMembros, token).ConfigureAwait(false);
				IReadOnlyList<MembroDoPerfilDto> itens =
					[.. (lida.Items ?? []).Select(m => new MembroDoPerfilDto(m.UserId, m.Email, OperacoesDeAcessoNoSecureGate.Situacao(m.Status)))];

				return new PaginaDeMembros(itens, lida.Page, lida.TotalPages, lida.TotalCount);
			},
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync<IReadOnlyList<UsuarioDto>>(
			logger,
			"listar usuarios do tenant",
			async token =>
			{
				var usuarios = await client.ListUsersAsync(tenantId, token).ConfigureAwait(false);

				return [.. usuarios.Select(u => new UsuarioDto(u.Id, u.Email, OperacoesDeAcessoNoSecureGate.Situacao(u.Status), [.. u.Roles ?? []], u.DisplayName))];
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<UsuarioDoTenantDetalheDto>> ObterUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync(
			logger,
			"obter usuario do tenant",
			async token =>
			{
				var u = await client.GetUserAsync(tenantId, usuarioId, token).ConfigureAwait(false);

				return new UsuarioDoTenantDetalheDto(
					u.Id,
					u.Email,
					u.DisplayName,
					OperacoesDeAcessoNoSecureGate.Situacao(u.Status),
					u.LockoutEnd,
					[.. u.Roles ?? []],
					[.. u.ExternalLogins ?? []],
					u.TwoFactorEnabled,
					u.HasPassword,
					u.LocalLoginEnabled);
			},
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<FederacaoDto>> ObterFederacaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync(
			logger,
			"obter federacao do tenant",
			async token =>
			{
				var federacao = (await client.GetTenantAsync(tenantId, token).ConfigureAwait(false)).Federation;

				return federacao is null
					? new FederacaoDto(null, false, null)
					: new FederacaoDto(federacao.DirectoryId, federacao.IsEnabled, federacao.UpdatedAt);
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "criar perfil do tenant",
			token => client.CreateRoleAsync(tenantId, new CreateRoleRequest { Name = nome }, token),
			null, _ => IntranetErrors.Acesso.PerfilJaExiste, cancellationToken);

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "excluir perfil do tenant",
			token => client.DeleteRoleAsync(tenantId, nome, token),
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			excecao => OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, PerfilUsadoPorClients)
				? IntranetErrors.Acesso.PerfilUsadoPorClient
				: IntranetErrors.Acesso.PerfilComMembros,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> DefinirPermissoesDoPerfilAsync(
		Guid tenantId, string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "definir permissoes do tenant",
			token => client.SetRolePermissionsAsync(tenantId, nome, new SetRolePermissionsRequest { Permissions = [.. permissoes] }, token),
			IntranetErrors.Acesso.PerfilNaoEncontrado, null, cancellationToken);

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "atribuir perfil no tenant",
			token => client.AddUserRoleAsync(tenantId, usuarioId, perfil, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, null, cancellationToken);

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "retirar perfil no tenant",
			token => client.RemoveUserRoleAsync(tenantId, usuarioId, perfil, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, _ => IntranetErrors.Acesso.RetiradaRecusada, cancellationToken);

	/// <inheritdoc />
	public Task<Result<Guid>> CadastrarUsuarioAsync(Guid tenantId, NovoUsuarioDoTenant novo, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(novo);

		return OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger,
			"cadastrar usuario no tenant",
			async token => (await client.CreateUserAsync(
				tenantId,
				new CreateUserRequest { Email = novo.Email, DisplayName = novo.Nome, LocalLogin = novo.EntraPorSenha, Roles = [.. novo.Perfis] },
				token).ConfigureAwait(false)).Id,
			null,
			_ => IntranetErrors.Acesso.EmailJaCadastrado,
			cancellationToken,
			recusado: IntranetErrors.Acesso.CadastroRecusado);
	}

	/// <inheritdoc />
	public Task<Result> ReenviarConviteAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "reenviar convite no tenant",
			token => client.ResendUserInviteAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, _ => IntranetErrors.Acesso.ConviteNaoCabe, cancellationToken);

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "desativar usuario no tenant",
			token => client.DeactivateUserAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, _ => IntranetErrors.Acesso.DesativacaoRecusada, cancellationToken);

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "reativar usuario no tenant",
			token => client.ActivateUserAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, null, cancellationToken);

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "encerrar sessoes no tenant",
			token => client.RevokeUserSessionsAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, null, cancellationToken);

	/// <inheritdoc />
	public Task<Result> DefinirFederacaoAsync(Guid tenantId, Guid directoryId, bool habilitada, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "definir federacao do tenant",
			token => client.UpsertTenantFederationAsync(
				tenantId, new UpsertTenantFederationRequest { DirectoryId = directoryId, Enabled = habilitada }, token),
			null, null, cancellationToken);
}
```

- [ ] **Step 6: Adaptador no-op**

`src/Secco.Intranet.Infrastructure/Access/GestaoDeAcessoDoTenantIndisponivel.cs`: classe `public sealed class GestaoDeAcessoDoTenantIndisponivel : IGestaoDeAcessoDoTenant` em que **todo** método devolve `Result.Failure(...)` (ou `Result.Failure<T>(...)`) com `IntranetErrors.Acesso.NaoConfigurado`, no mesmo molde de `GestaoDeAcessoIndisponivel` (campo `private static readonly Error Erro = IntranetErrors.Acesso.NaoConfigurado;` e expressões `Task.FromResult(...)`). Summary: "Adapter no-op para DEV e Testing sem a seção `Secco:SecureGate`".

- [ ] **Step 7: Registro e fábrica de testes**

Em `IntranetInfrastructureExtensions.cs`, ao lado de `services.AddScoped(CriarGestaoDeAcesso);`:

```csharp
		services.AddScoped(CriarGestaoDeAcessoDoTenant);
```

e o método, ao lado de `CriarGestaoDeAcesso`:

```csharp
	private static IGestaoDeAcessoDoTenant CriarGestaoDeAcessoDoTenant(IServiceProvider serviceProvider)
	{
		var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

		return credenciais.IsConfigured
			? ActivatorUtilities.CreateInstance<SecureGateGestaoDeAcessoDoTenant>(serviceProvider)
			: new GestaoDeAcessoDoTenantIndisponivel();
	}
```

(com `using Secco.Intranet.Application.Tenants.Acesso;`).

Em `IntranetWebFactory.cs`, ao lado de `GestaoDeTenants`:

```csharp
	/// <summary>Dublê da gestão de acesso de tenant administrado; nulo = "não configurado".</summary>
	public IGestaoDeAcessoDoTenant? GestaoDeAcessoDoTenant { get; set; }
```

e em `ConfigureTestServices`:

```csharp
		services.AddScoped<IGestaoDeAcessoDoTenant>(_ => GestaoDeAcessoDoTenant ?? new GestaoDeAcessoDoTenantIndisponivel());
```

- [ ] **Step 8: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~SecureGateGestaoDeAcessoDoTenantTests|FullyQualifiedName~OperacoesDeAcessoNoSecureGateTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src tests
git commit -m "feat(tenants): porta e adaptador do acesso do tenant administrado"
```

---

### Task 3: Leituras, cabeçalho do tenant e dublê da porta

**Files:**
- Create: `tests/Secco.Intranet.Tests/Support/DublesDeAcessoDoTenant.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Acesso/CabecalhoDoTenant.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Acesso/LeituraDoAcessoDoTenantHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/LeituraDoAcessoDoTenantTests.cs`

**Interfaces:**
- Consumes: `IGestaoDeAcessoDoTenant` e DTOs (Task 2); `ITenantsAdministrados`, `IGestaoDeTenants`, `TenantDaPlataformaDetalheDto` (existentes).
- Produces:

```csharp
public sealed record CabecalhoDoTenantDto(Guid TenantId, string Sistema, string Nome, bool Ativo);
public sealed class VerificarSecureGateLigadoHandler(ITenantsAdministrados cadastro)
{ public Task<bool> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default); }
public sealed class ObterCabecalhoDoTenantHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao)
{ public Task<Result<CabecalhoDoTenantDto>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default); }

public sealed record PerfilDoTenantTelaDto(PerfilDoTenantDetalheDto Perfil, PaginaDeMembros Membros, IReadOnlyList<UsuarioDto> Candidatos);
public sealed record UsuarioDoTenantTelaDto(UsuarioDoTenantDetalheDto Usuario, IReadOnlyList<string> PerfisAtribuiveis);
public sealed class LeituraDoAcessoDoTenantHandler(IGestaoDeAcessoDoTenant gestao)
{
	Task<Result<IReadOnlyList<PerfilDoTenantDto>>> ListarPerfisAsync(Guid tenantId, CancellationToken ct = default);
	Task<Result<IReadOnlyList<string>>> PerfisAtribuiveisAsync(Guid tenantId, CancellationToken ct = default);
	Task<Result<PerfilDoTenantTelaDto>> ObterPerfilAsync(Guid tenantId, string nome, int pagina, CancellationToken ct = default);
	Task<Result<PagedResult<UsuarioDto>>> ListarUsuariosAsync(Guid tenantId, string? busca, int pagina, CancellationToken ct = default);
	Task<Result<UsuarioDoTenantTelaDto>> ObterUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default);
	Task<Result<FederacaoDto>> ObterFederacaoAsync(Guid tenantId, CancellationToken ct = default);
}
```

- `GestaoDeAcessoDoTenantFalsa` (tests/Support): `ComPerfil(string nome, bool reservado = false, params string[] permissoes)`, `ComUsuario(Guid id, string email, SituacaoDoUsuario situacao, bool temSenha = false, bool entraPorSenha = true, params string[] perfis)`, `Federacao` (`FederacaoDto`), `FalharCom` (`Error?`), `Chamadas` (`List<string>`, formato `{metodo}:{tenantId}[:args]`), `Cadastrados` (`List<NovoUsuarioDoTenant>`), `Permissoes` (`Dictionary<string, IReadOnlyCollection<string>>`).

- [ ] **Step 1: Dublê da porta**

`tests/Secco.Intranet.Tests/Support/DublesDeAcessoDoTenant.cs`:

```csharp
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// Acesso de um tenant administrado em memória. Toda chamada fica em <see cref="Chamadas"/> com o
/// tenant recebido — é assim que os testes provam que o tenant da rota chegou à porta.
/// </summary>
public sealed class GestaoDeAcessoDoTenantFalsa : IGestaoDeAcessoDoTenant
{
	private readonly Dictionary<string, (bool Reservado, List<string> Permissoes)> _perfis = new(StringComparer.Ordinal);
	private readonly Dictionary<Guid, UsuarioDoTenantDetalheDto> _usuarios = [];

	/// <summary>Chamadas, na ordem.</summary>
	public List<string> Chamadas { get; } = [];

	/// <summary>Pessoas cadastradas por <see cref="CadastrarUsuarioAsync"/>.</summary>
	public List<NovoUsuarioDoTenant> Cadastrados { get; } = [];

	/// <summary>Federação devolvida pela leitura.</summary>
	public FederacaoDto Federacao { get; set; } = new(null, false, null);

	/// <summary>Quando definido, toda chamada falha com este erro.</summary>
	public Error? FalharCom { get; set; }

	/// <summary>Acrescenta um perfil.</summary>
	public GestaoDeAcessoDoTenantFalsa ComPerfil(string nome, bool reservado = false, params string[] permissoes)
	{
		_perfis[nome] = (reservado, [.. permissoes]);

		return this;
	}

	/// <summary>Acrescenta uma pessoa.</summary>
	public GestaoDeAcessoDoTenantFalsa ComUsuario(
		Guid id, string email, SituacaoDoUsuario situacao, bool temSenha = false, bool entraPorSenha = true, params string[] perfis)
	{
		_usuarios[id] = new UsuarioDoTenantDetalheDto(id, email, null, situacao, null, [.. perfis], [], false, temSenha, entraPorSenha);

		return this;
	}

	/// <summary>Permissões gravadas de um perfil.</summary>
	public IReadOnlyList<string> PermissoesDe(string perfil) => _perfis[perfil].Permissoes;

	private Result Registrar(string chamada)
	{
		Chamadas.Add(chamada);

		return FalharCom is { } erro ? Result.Failure(erro) : Result.Success();
	}

	private Result<T> Registrar<T>(string chamada, Func<Result<T>> valor)
	{
		Chamadas.Add(chamada);

		return FalharCom is { } erro ? Result.Failure<T>(erro) : valor();
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDoTenantDto>>> ListarPerfisAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar<IReadOnlyList<PerfilDoTenantDto>>($"listarPerfis:{tenantId}", () =>
			Result.Success<IReadOnlyList<PerfilDoTenantDto>>(
				[.. _perfis.Select(p => new PerfilDoTenantDto(p.Key, p.Value.Permissoes.Count, p.Value.Reservado))])));

	/// <inheritdoc />
	public Task<Result<PerfilDoTenantDetalheDto>> ObterPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"obterPerfil:{tenantId}:{nome}", () =>
			_perfis.TryGetValue(nome, out var perfil)
				? Result.Success(new PerfilDoTenantDetalheDto(nome, perfil.Permissoes, perfil.Reservado, Membros(nome).Count))
				: Result.Failure<PerfilDoTenantDetalheDto>(IntranetErrors.Acesso.PerfilNaoEncontrado)));

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(Guid tenantId, string nome, int pagina, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"listarMembros:{tenantId}:{nome}:{pagina}", () =>
		{
			var membros = Membros(nome);

			return Result.Success(new PaginaDeMembros(
				[.. membros.Select(u => new MembroDoPerfilDto(u.Id, u.Email, u.Situacao))], pagina, 1, membros.Count));
		}));

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar<IReadOnlyList<UsuarioDto>>($"listarUsuarios:{tenantId}", () =>
			Result.Success<IReadOnlyList<UsuarioDto>>(
				[.. _usuarios.Values.Select(u => new UsuarioDto(u.Id, u.Email, u.Situacao, u.Perfis, u.Nome))])));

	/// <inheritdoc />
	public Task<Result<UsuarioDoTenantDetalheDto>> ObterUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"obterUsuario:{tenantId}:{usuarioId}", () =>
			_usuarios.TryGetValue(usuarioId, out var usuario)
				? Result.Success(usuario)
				: Result.Failure<UsuarioDoTenantDetalheDto>(IntranetErrors.Acesso.UsuarioNaoEncontrado)));

	/// <inheritdoc />
	public Task<Result<FederacaoDto>> ObterFederacaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"obterFederacao:{tenantId}", () => Result.Success(Federacao)));

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default)
	{
		var registrado = Registrar($"criarPerfil:{tenantId}:{nome}");

		if (registrado.IsSuccess)
		{
			_perfis.TryAdd(nome, (false, []));
		}

		return Task.FromResult(registrado);
	}

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default)
	{
		var registrado = Registrar($"excluirPerfil:{tenantId}:{nome}");

		if (registrado.IsSuccess)
		{
			_perfis.Remove(nome);
		}

		return Task.FromResult(registrado);
	}

	/// <inheritdoc />
	public Task<Result> DefinirPermissoesDoPerfilAsync(
		Guid tenantId, string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default)
	{
		var registrado = Registrar($"definirPermissoes:{tenantId}:{nome}");

		if (registrado.IsSuccess)
		{
			_perfis[nome] = (_perfis[nome].Reservado, [.. permissoes]);
		}

		return Task.FromResult(registrado);
	}

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Task.FromResult(AlterarPerfis($"atribuir:{tenantId}:{usuarioId}:{perfil}", usuarioId, perfis => perfis.Append(perfil).Distinct()));

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Task.FromResult(AlterarPerfis($"retirar:{tenantId}:{usuarioId}:{perfil}", usuarioId, perfis => perfis.Where(p => p != perfil)));

	/// <inheritdoc />
	public Task<Result<Guid>> CadastrarUsuarioAsync(Guid tenantId, NovoUsuarioDoTenant novo, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"cadastrar:{tenantId}", () =>
		{
			Cadastrados.Add(novo);
			var id = Guid.NewGuid();
			_usuarios[id] = new UsuarioDoTenantDetalheDto(
				id, novo.Email, novo.Nome, SituacaoDoUsuario.Ativo, null, novo.Perfis, [], false, false, novo.EntraPorSenha);

			return Result.Success(id);
		}));

	/// <inheritdoc />
	public Task<Result> ReenviarConviteAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"reenviarConvite:{tenantId}:{usuarioId}"));

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"desativar:{tenantId}:{usuarioId}"));

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"reativar:{tenantId}:{usuarioId}"));

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"encerrarSessoes:{tenantId}:{usuarioId}"));

	/// <inheritdoc />
	public Task<Result> DefinirFederacaoAsync(Guid tenantId, Guid directoryId, bool habilitada, CancellationToken cancellationToken = default)
	{
		var registrado = Registrar($"definirFederacao:{tenantId}:{directoryId}:{habilitada}");

		if (registrado.IsSuccess)
		{
			Federacao = new FederacaoDto(directoryId, habilitada, DateTimeOffset.UtcNow);
		}

		return Task.FromResult(registrado);
	}

	private List<UsuarioDoTenantDetalheDto> Membros(string perfil) =>
		[.. _usuarios.Values.Where(u => u.Perfis.Contains(perfil, StringComparer.Ordinal))];

	private Result AlterarPerfis(string chamada, Guid usuarioId, Func<IEnumerable<string>, IEnumerable<string>> alterar)
	{
		var registrado = Registrar(chamada);

		if (registrado.IsFailure)
		{
			return registrado;
		}

		if (!_usuarios.TryGetValue(usuarioId, out var usuario))
		{
			return Result.Failure(IntranetErrors.Acesso.UsuarioNaoEncontrado);
		}

		_usuarios[usuarioId] = usuario with { Perfis = [.. alterar(usuario.Perfis)] };

		return Result.Success();
	}
}
```

- [ ] **Step 2: Escrever os testes das leituras**

`tests/Secco.Intranet.Tests/Unit/LeituraDoAcessoDoTenantTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Leituras do acesso do tenant administrado: tenant explícito, reservados fora dos seletores, paginação segura.</summary>
public class LeituraDoAcessoDoTenantTests
{
	private static readonly Guid Tenant = Guid.NewGuid();
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bia = Guid.NewGuid();

	private static GestaoDeAcessoDoTenantFalsa Gestao() => new GestaoDeAcessoDoTenantFalsa()
		.ComPerfil("compras-aprovador", false, "pedido:aprovar")
		.ComPerfil("installation-operator", reservado: true)
		.ComUsuario(Ana, "ana@compras.com", SituacaoDoUsuario.Ativo, perfis: "compras-aprovador")
		.ComUsuario(Bia, "bia@compras.com", SituacaoDoUsuario.Ativo);

	[Fact]
	public async Task PerfisAtribuiveis_SemReservados()
	{
		var atribuiveis = await new LeituraDoAcessoDoTenantHandler(Gestao()).PerfisAtribuiveisAsync(Tenant);

		atribuiveis.Value.Should().Equal("compras-aprovador");
	}

	[Fact]
	public async Task ObterPerfil_CandidatosSaoAtivosSemOPerfil()
	{
		var tela = await new LeituraDoAcessoDoTenantHandler(Gestao()).ObterPerfilAsync(Tenant, "compras-aprovador", 1);

		tela.Value.Candidatos.Select(u => u.Id).Should().Equal(Bia);
		tela.Value.Membros.Itens.Select(m => m.UsuarioId).Should().Equal(Ana);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public async Task ObterPerfil_PaginaNaoPositiva_ViraPrimeira(int pagina)
	{
		var gestao = Gestao();

		await new LeituraDoAcessoDoTenantHandler(gestao).ObterPerfilAsync(Tenant, "compras-aprovador", pagina);

		gestao.Chamadas.Should().Contain($"listarMembros:{Tenant}:compras-aprovador:1");
	}

	[Fact]
	public async Task ListarUsuarios_BuscaPorEmailEPagina999SemErro()
	{
		var leitura = new LeituraDoAcessoDoTenantHandler(Gestao());

		(await leitura.ListarUsuariosAsync(Tenant, "BIA", 1)).Value.Items.Select(u => u.Id).Should().Equal(Bia);
		(await leitura.ListarUsuariosAsync(Tenant, null, 999)).Value.Items.Should().BeEmpty();
	}

	[Fact]
	public async Task ObterUsuario_AtribuiveisExcluemOsQueJaTemEReservados()
	{
		var tela = await new LeituraDoAcessoDoTenantHandler(Gestao()).ObterUsuarioAsync(Tenant, Bia);

		tela.Value.PerfisAtribuiveis.Should().Equal("compras-aprovador");
	}

	[Fact]
	public async Task TodaLeitura_UsaOTenantRecebido()
	{
		var gestao = Gestao();
		var leitura = new LeituraDoAcessoDoTenantHandler(gestao);

		await leitura.ListarPerfisAsync(Tenant);
		await leitura.ObterUsuarioAsync(Tenant, Ana);
		await leitura.ObterFederacaoAsync(Tenant);

		gestao.Chamadas.Should().OnlyContain(c => c.Contains(Tenant.ToString(), StringComparison.Ordinal));
	}
}
```

E em `tests/Secco.Intranet.Tests/Unit/LeituraDeTenantsHandlersTests.cs` (existente), acrescentar — o dublê de cadastro é `TenantsAdministradosFalso`, de `Support/DublesDeTenants.cs`:

```csharp
	[Fact]
	public async Task VerificarSecureGateLigado_SoComAMarcaDoCadastro()
	{
		var ligado = Guid.NewGuid();
		var desligado = Guid.NewGuid();
		var cadastro = new TenantsAdministradosFalso();
		var comSecureGate = new TenantAdministrado(ligado, "Compras", "Ana", OrigemDoTenant.Criado, "admin");
		comSecureGate.HabilitarSecureGate();
		await cadastro.TentarAdicionarAsync(comSecureGate);
		await cadastro.TentarAdicionarAsync(new TenantAdministrado(desligado, "RH", "Bia", OrigemDoTenant.Criado, "admin"));

		var verificar = new VerificarSecureGateLigadoHandler(cadastro);

		(await verificar.HandleAsync(ligado)).Should().BeTrue();
		(await verificar.HandleAsync(desligado)).Should().BeFalse();
		(await verificar.HandleAsync(Guid.NewGuid())).Should().BeFalse("fora do cadastro");
	}

	[Fact]
	public async Task ObterCabecalho_JuntaCadastroEPlataforma()
	{
		var tenant = Guid.NewGuid();
		var cadastro = new TenantsAdministradosFalso();
		await cadastro.TentarAdicionarAsync(new TenantAdministrado(tenant, "Compras", "Ana", OrigemDoTenant.Criado, "admin"));
		var gestao = new GestaoDeTenantsFalsa().ComTenant(tenant, "compras", ativo: false);

		var cabecalho = await new ObterCabecalhoDoTenantHandler(cadastro, gestao).HandleAsync(tenant);

		cabecalho.Value.Should().Be(new CabecalhoDoTenantDto(tenant, "Compras", "Tenant compras", false));
	}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~LeituraDoAcessoDoTenantTests|FullyQualifiedName~LeituraDeTenantsHandlersTests"`
Expected: erro de compilação.

- [ ] **Step 4: Implementar cabeçalho e verificação**

`src/Secco.Intranet.Application/Tenants/Acesso/CabecalhoDoTenant.cs`:

```csharp
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>O que toda tela de acesso do tenant mostra no topo — em qual sistema se está mexendo.</summary>
/// <param name="TenantId">Id.</param>
/// <param name="Sistema">Sistema que o tenant representa.</param>
/// <param name="Nome">Nome na plataforma.</param>
/// <param name="Ativo">Se o tenant está ativo.</param>
public sealed record CabecalhoDoTenantDto(Guid TenantId, string Sistema, string Nome, bool Ativo);

/// <summary>Se o tenant está no cadastro com o SecureGate ligado — a checagem do filtro <c>[SecureGateLigado]</c>.</summary>
/// <param name="cadastro">Cadastro local.</param>
public sealed class VerificarSecureGateLigadoHandler(ITenantsAdministrados cadastro)
{
	/// <summary>Executa a checagem.</summary>
	/// <param name="tenantId">Id recebido na rota.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<bool> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false) is { SecureGateHabilitado: true };
}

/// <summary>Monta o cabeçalho: sistema do cadastro, nome e situação da plataforma.</summary>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="gestao">Porta de tenants.</param>
public sealed class ObterCabecalhoDoTenantHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao)
{
	/// <summary>Executa a consulta.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<CabecalhoDoTenantDto>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var registro = await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (registro is null)
		{
			return Result.Failure<CabecalhoDoTenantDto>(IntranetErrors.Tenants.NaoEncontrado);
		}

		var tenant = await gestao.ObterTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return tenant.IsFailure
			? Result.Failure<CabecalhoDoTenantDto>(tenant.Error)
			: Result.Success(new CabecalhoDoTenantDto(tenantId, registro.Sistema, tenant.Value.Nome, tenant.Value.Ativo));
	}
}
```

- [ ] **Step 5: Implementar as leituras**

`src/Secco.Intranet.Application/Tenants/Acesso/LeituraDoAcessoDoTenantHandler.cs`:

```csharp
using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>Tela de um perfil do tenant administrado.</summary>
/// <param name="Perfil">Detalhe.</param>
/// <param name="Membros">Página de membros.</param>
/// <param name="Candidatos">Pessoas ativas que ainda não têm o perfil.</param>
public sealed record PerfilDoTenantTelaDto(PerfilDoTenantDetalheDto Perfil, PaginaDeMembros Membros, IReadOnlyList<UsuarioDto> Candidatos);

/// <summary>Tela de uma pessoa do tenant administrado.</summary>
/// <param name="Usuario">Detalhe.</param>
/// <param name="PerfisAtribuiveis">Perfis que ela ainda não tem, sem reservados.</param>
public sealed record UsuarioDoTenantTelaDto(UsuarioDoTenantDetalheDto Usuario, IReadOnlyList<string> PerfisAtribuiveis);

/// <summary>Leituras do acesso de um tenant administrado. Só as regras genéricas — nenhuma convenção da Intranet.</summary>
/// <param name="gestao">Porta.</param>
public sealed class LeituraDoAcessoDoTenantHandler(IGestaoDeAcessoDoTenant gestao)
{
	private const int TamanhoDaPagina = 20;

	/// <summary>Todos os perfis, reservados inclusive (a lista os sinaliza).</summary>
	public Task<Result<IReadOnlyList<PerfilDoTenantDto>>> ListarPerfisAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		gestao.ListarPerfisAsync(tenantId, cancellationToken);

	/// <summary>Nomes dos perfis oferecíveis em seletor — sem reservados, em ordem alfabética.</summary>
	public async Task<Result<IReadOnlyList<string>>> PerfisAtribuiveisAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var perfis = await gestao.ListarPerfisAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return perfis.IsFailure
			? Result.Failure<IReadOnlyList<string>>(perfis.Error)
			: Result.Success<IReadOnlyList<string>>(
				[.. perfis.Value.Where(p => !p.Reservado).Select(p => p.Nome).Order(StringComparer.OrdinalIgnoreCase)]);
	}

	/// <summary>Detalhe de um perfil com membros e candidatos.</summary>
	public async Task<Result<PerfilDoTenantTelaDto>> ObterPerfilAsync(
		Guid tenantId, string nome, int pagina, CancellationToken cancellationToken = default)
	{
		var perfil = await gestao.ObterPerfilAsync(tenantId, nome, cancellationToken).ConfigureAwait(false);

		if (perfil.IsFailure)
		{
			return Result.Failure<PerfilDoTenantTelaDto>(perfil.Error);
		}

		var membros = await gestao.ListarMembrosAsync(tenantId, perfil.Value.Nome, Math.Max(1, pagina), cancellationToken).ConfigureAwait(false);

		if (membros.IsFailure)
		{
			return Result.Failure<PerfilDoTenantTelaDto>(membros.Error);
		}

		var usuarios = await gestao.ListarUsuariosAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (usuarios.IsFailure)
		{
			return Result.Failure<PerfilDoTenantTelaDto>(usuarios.Error);
		}

		IReadOnlyList<UsuarioDto> candidatos =
		[
			.. usuarios.Value
				.Where(u => u.Situacao == SituacaoDoUsuario.Ativo && !u.Perfis.Contains(perfil.Value.Nome, StringComparer.Ordinal))
				.OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase),
		];

		return Result.Success(new PerfilDoTenantTelaDto(perfil.Value, membros.Value, candidatos));
	}

	/// <summary>Pessoas com busca por e-mail e paginação em memória (a plataforma devolve o tenant inteiro).</summary>
	public async Task<Result<PagedResult<UsuarioDto>>> ListarUsuariosAsync(
		Guid tenantId, string? busca, int pagina, CancellationToken cancellationToken = default)
	{
		var lidos = await gestao.ListarUsuariosAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (lidos.IsFailure)
		{
			return Result.Failure<PagedResult<UsuarioDto>>(lidos.Error);
		}

		var termo = busca?.Trim();
		var filtrados = lidos.Value
			.Where(u => string.IsNullOrEmpty(termo) || (u.Email ?? string.Empty).Contains(termo, StringComparison.OrdinalIgnoreCase))
			.OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var pedido = new PageRequest(Math.Max(1, pagina), TamanhoDaPagina);
		IReadOnlyList<UsuarioDto> itens = [.. filtrados.Skip(pedido.Skip).Take(pedido.Size)];

		return PagedResult.Create(itens, pedido, filtrados.Count);
	}

	/// <summary>Detalhe de uma pessoa com os perfis que ainda pode receber.</summary>
	public async Task<Result<UsuarioDoTenantTelaDto>> ObterUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = await gestao.ObterUsuarioAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure<UsuarioDoTenantTelaDto>(usuario.Error);
		}

		var atribuiveis = await PerfisAtribuiveisAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (atribuiveis.IsFailure)
		{
			return Result.Failure<UsuarioDoTenantTelaDto>(atribuiveis.Error);
		}

		IReadOnlyList<string> restantes = [.. atribuiveis.Value.Where(p => !usuario.Value.Perfis.Contains(p, StringComparer.Ordinal))];

		return Result.Success(new UsuarioDoTenantTelaDto(usuario.Value, restantes));
	}

	/// <summary>Federação do tenant.</summary>
	public Task<Result<FederacaoDto>> ObterFederacaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		gestao.ObterFederacaoAsync(tenantId, cancellationToken);
}
```

Confirmar os `using` de `PageRequest`/`PagedResult` olhando `ListarUsuariosHandler.cs` (mesmos tipos); usar o mesmo namespace que ele usa.

- [ ] **Step 6: Registrar**

Em `IntranetApplicationExtensions.cs`, depois de `services.AddScoped<AlterarSituacaoDoTenantHandler>();`:

```csharp
		services.AddScoped<VerificarSecureGateLigadoHandler>();
		services.AddScoped<ObterCabecalhoDoTenantHandler>();
		services.AddScoped<LeituraDoAcessoDoTenantHandler>();
```

com `using Secco.Intranet.Application.Tenants.Acesso;`.

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~LeituraDoAcessoDoTenantTests|FullyQualifiedName~LeituraDeTenantsHandlersTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(tenants): leituras do acesso do tenant administrado"
```

---

### Task 4: Escrita de perfis e permissões

**Files:**
- Create: `src/Secco.Intranet.Application/Tenants/Acesso/ListaDePermissoes.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Acesso/AuditoriaDoAcessoDoTenant.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Acesso/PerfisDoTenantHandler.cs`
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/PerfisDoTenantHandlerTests.cs`

**Interfaces:**
- Consumes: porta, dublê (Tasks 2–3); `TrilhaDeAcessoFalsa` (existente em `Support/DublesDeAcesso.cs`).
- Produces:

```csharp
public static class ListaDePermissoes
{
	public const int Maximo = 200;
	public static Result<IReadOnlyList<string>> Ler(string? texto);
}
public sealed class PerfisDoTenantHandler(IGestaoDeAcessoDoTenant gestao, ITrilhaDeAuditoria trilha)
{
	Task<Result> CriarAsync(Guid tenantId, string? nome, CancellationToken ct = default);
	Task<Result> ExcluirAsync(Guid tenantId, string? nome, CancellationToken ct = default);
	Task<Result> DefinirPermissoesAsync(Guid tenantId, string? nome, string? texto, CancellationToken ct = default);
}
internal static class AuditoriaDoAcessoDoTenant
{
	Task PerfilAsync(ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, string perfil, CancellationToken ct);
	Task PermissoesAsync(ITrilhaDeAuditoria trilha, Guid tenantId, string perfil, int antes, int depois, CancellationToken ct);
	Task UsuarioAsync(ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, Guid usuarioId, string? perfil, CancellationToken ct);
	Task CadastroAsync(ITrilhaDeAuditoria trilha, Guid tenantId, Guid usuarioId, bool entraPorSenha, IReadOnlyList<string> perfis, CancellationToken ct);
	Task FederacaoAsync(ITrilhaDeAuditoria trilha, Guid tenantId, Guid directoryId, bool habilitada, CancellationToken ct);
}
```

- Verbos (`VerbosDeAuditoria`): `TenantAcessoPerfilCriar = "tenant.acesso.perfil-criar"`, `TenantAcessoPerfilExcluir = "tenant.acesso.perfil-excluir"`, `TenantAcessoPermissoesEditar = "tenant.acesso.perfil-permissoes-editar"`, `TenantAcessoPerfilAtribuir = "tenant.acesso.perfil-atribuir"`, `TenantAcessoPerfilRetirar = "tenant.acesso.perfil-retirar"`, `TenantAcessoUsuarioCadastrar = "tenant.acesso.usuario-cadastrar"`, `TenantAcessoConviteReenviar = "tenant.acesso.convite-reenviar"`, `TenantAcessoUsuarioDesativar = "tenant.acesso.usuario-desativar"`, `TenantAcessoUsuarioReativar = "tenant.acesso.usuario-reativar"`, `TenantAcessoSessoesEncerrar = "tenant.acesso.sessoes-encerrar"`, `TenantAcessoFederacaoDefinir = "tenant.acesso.federacao-definir"`.

- [ ] **Step 1: Escrever os testes**

`tests/Secco.Intranet.Tests/Unit/PerfisDoTenantHandlerTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Regras de perfis do tenant administrado: nome, reservado, membros, permissões em texto e trilha.</summary>
public class PerfisDoTenantHandlerTests
{
	private static readonly Guid Tenant = Guid.NewGuid();

	private static (PerfisDoTenantHandler Handler, GestaoDeAcessoDoTenantFalsa Gestao, TrilhaDeAcessoFalsa Trilha) Criar()
	{
		var gestao = new GestaoDeAcessoDoTenantFalsa()
			.ComPerfil("compras-aprovador", false, "pedido:aprovar")
			.ComPerfil("installation-operator", reservado: true)
			.ComUsuario(Guid.NewGuid(), "ana@compras.com", SituacaoDoUsuario.Ativo, perfis: "compras-aprovador")
			.ComPerfil("vazio");
		var trilha = new TrilhaDeAcessoFalsa();

		return (new PerfisDoTenantHandler(gestao, trilha), gestao, trilha);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("tem espaço")]
	[InlineData("-comeca-com-hifen")]
	public async Task Criar_NomeInvalido_NaoChamaAPorta(string? nome)
	{
		var (handler, gestao, _) = Criar();

		(await handler.CriarAsync(Tenant, nome)).IsFailure.Should().BeTrue();
		gestao.Chamadas.Should().NotContain(c => c.StartsWith("criarPerfil", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Criar_Reservado_Recusado()
	{
		var (handler, _, _) = Criar();

		(await handler.CriarAsync(Tenant, "installation-operator")).Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
	}

	[Fact]
	public async Task Criar_SemSufixoDeSetor_Aceito_ERegistraNaTrilha()
	{
		var (handler, gestao, trilha) = Criar();

		(await handler.CriarAsync(Tenant, " compras.aprovador ")).IsSuccess.Should().BeTrue();

		gestao.Chamadas.Should().Contain($"criarPerfil:{Tenant}:compras.aprovador");
		trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantAcessoPerfilCriar);
		trilha.Registros[0].Metadata.Should().Contain(Tenant.ToString());
	}

	[Fact]
	public async Task Excluir_ComMembros_RecusadoSemChamarExclusao()
	{
		var (handler, gestao, _) = Criar();

		(await handler.ExcluirAsync(Tenant, "compras-aprovador")).Error.Should().Be(IntranetErrors.Acesso.PerfilComMembros);
		gestao.Chamadas.Should().NotContain(c => c.StartsWith("excluirPerfil", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Excluir_SemMembros_Exclui()
	{
		var (handler, gestao, _) = Criar();

		(await handler.ExcluirAsync(Tenant, "vazio")).IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Contain($"excluirPerfil:{Tenant}:vazio");
	}

	[Fact]
	public async Task Permissoes_LinhaInvalida_ApontaENaoGrava()
	{
		var (handler, gestao, _) = Criar();

		var definido = await handler.DefinirPermissoesAsync(Tenant, "compras-aprovador", "pedido:aprovar\npedido:aprovra mais\nPEDIDO:LER");

		definido.Error.Code.Should().Be("Intranet.Acesso.PermissoesInvalidas");
		definido.Error.Description.Should().Contain("pedido:aprovra mais").And.Contain("PEDIDO:LER");
		gestao.PermissoesDe("compras-aprovador").Should().Equal("pedido:aprovar");
	}

	[Fact]
	public async Task Permissoes_DuplicadasEVaziasDescartadas_TrilhaSoComContagem()
	{
		var (handler, gestao, trilha) = Criar();

		var definido = await handler.DefinirPermissoesAsync(Tenant, "compras-aprovador", "pedido:aprovar\r\n\r\n  pedido:ler  \npedido:aprovar\n");

		definido.IsSuccess.Should().BeTrue();
		gestao.PermissoesDe("compras-aprovador").Should().Equal("pedido:aprovar", "pedido:ler");
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.TenantAcessoPermissoesEditar);
		registro.Metadata.Should().NotContain("pedido:ler", "a lista não vai para a trilha, só a contagem");
	}

	[Fact]
	public async Task Permissoes_SoLinhasEmBranco_GravaListaVazia()
	{
		var (handler, gestao, _) = Criar();

		(await handler.DefinirPermissoesAsync(Tenant, "compras-aprovador", "  \n\t\n")).IsSuccess.Should().BeTrue();
		gestao.PermissoesDe("compras-aprovador").Should().BeEmpty();
	}

	[Fact]
	public void Permissoes_AcimaDoLimite_Recusa()
	{
		var texto = string.Join('\n', Enumerable.Range(1, ListaDePermissoes.Maximo + 1).Select(i => $"recurso-{i}:ler"));

		ListaDePermissoes.Ler(texto).Error.Should().Be(IntranetErrors.Acesso.PermissoesDemais);
	}
}
```

`RegistroDeAuditoria` é `record (string Verbo, string Recurso, string RecursoId, string? Metadata)`; o JSON do alvo fica em `Metadata`.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~PerfisDoTenantHandlerTests"`
Expected: erro de compilação.

- [ ] **Step 3: Verbos**

Em `VerbosDeAuditoria.cs`, depois de `TenantDesativar`, as 11 constantes da seção **Produces**, cada uma com `/// <summary>` em português (ex.: `/// <summary>Perfil criado em tenant administrado.</summary>`).

- [ ] **Step 4: Lista de permissões**

`src/Secco.Intranet.Application/Tenants/Acesso/ListaDePermissoes.cs`:

```csharp
using Secco.SharedKernel.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>
/// Permissões em texto livre, uma por linha — o paliativo da secco-platform#33 até o catálogo da
/// ADR-0039 ser publicado. Valida o formato <c>recurso:acao</c>; não sabe se a permissão existe.
/// </summary>
public static class ListaDePermissoes
{
	/// <summary>Máximo de permissões por perfil.</summary>
	public const int Maximo = 200;

	/// <summary>Lê o texto: apara, ignora linhas vazias, descarta duplicadas mantendo a ordem.</summary>
	/// <param name="texto">Texto do formulário.</param>
	public static Result<IReadOnlyList<string>> Ler(string? texto)
	{
		var linhas = (texto ?? string.Empty)
			.Split('\n')
			.Select(linha => linha.Trim())
			.Where(linha => linha.Length > 0)
			.Distinct(StringComparer.Ordinal)
			.ToList();

		var invalidas = linhas.Where(linha => !SeccoPermissions.IsValid(linha)).ToList();

		if (invalidas.Count > 0)
		{
			return Result.Failure<IReadOnlyList<string>>(IntranetErrors.Acesso.PermissoesInvalidas(invalidas));
		}

		return linhas.Count > Maximo
			? Result.Failure<IReadOnlyList<string>>(IntranetErrors.Acesso.PermissoesDemais)
			: Result.Success<IReadOnlyList<string>>(linhas);
	}
}
```

- [ ] **Step 5: Auditoria**

`src/Secco.Intranet.Application/Tenants/Acesso/AuditoriaDoAcessoDoTenant.cs`:

```csharp
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>
/// Registro na trilha da Intranet das ações sobre o acesso de um tenant administrado — só depois que
/// a plataforma aceitou. Nunca recebe e-mail de terceiro nem a lista de permissões.
/// </summary>
internal static class AuditoriaDoAcessoDoTenant
{
	/// <summary>Ação sobre um perfil.</summary>
	public static Task PerfilAsync(ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, string perfil, CancellationToken cancellationToken) =>
		Registrar(trilha, verbo, tenantId, new { tenantId, perfil }, cancellationToken);

	/// <summary>Edição de permissões: só as contagens.</summary>
	public static Task PermissoesAsync(
		ITrilhaDeAuditoria trilha, Guid tenantId, string perfil, int antes, int depois, CancellationToken cancellationToken) =>
		Registrar(trilha, VerbosDeAuditoria.TenantAcessoPermissoesEditar, tenantId, new { tenantId, perfil, antes, depois }, cancellationToken);

	/// <summary>Ação sobre uma pessoa, opcionalmente com o perfil envolvido.</summary>
	public static Task UsuarioAsync(
		ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, Guid usuarioId, string? perfil, CancellationToken cancellationToken) =>
		Registrar(trilha, verbo, tenantId, new { tenantId, usuarioId, perfil }, cancellationToken);

	/// <summary>Cadastro: id devolvido pela plataforma, modo de entrada e perfis iniciais.</summary>
	public static Task CadastroAsync(
		ITrilhaDeAuditoria trilha, Guid tenantId, Guid usuarioId, bool entraPorSenha, IReadOnlyList<string> perfis,
		CancellationToken cancellationToken) =>
		Registrar(
			trilha, VerbosDeAuditoria.TenantAcessoUsuarioCadastrar, tenantId,
			new { tenantId, usuarioId, modo = entraPorSenha ? "convite" : "entra", perfis }, cancellationToken);

	/// <summary>Federação definida; o directory id não é segredo (ADR-0026 da plataforma).</summary>
	public static Task FederacaoAsync(
		ITrilhaDeAuditoria trilha, Guid tenantId, Guid directoryId, bool habilitada, CancellationToken cancellationToken) =>
		Registrar(trilha, VerbosDeAuditoria.TenantAcessoFederacaoDefinir, tenantId, new { tenantId, directoryId, habilitada }, cancellationToken);

	private static Task Registrar(ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, object dados, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Tenant, tenantId.ToString(), JsonSerializer.Serialize(dados)),
			cancellationToken);
}
```

- [ ] **Step 6: Handler de perfis**

`src/Secco.Intranet.Application/Tenants/Acesso/PerfisDoTenantHandler.cs`:

```csharp
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>
/// Perfis de um tenant administrado: criar, excluir e definir permissões. Sem a convenção de
/// setor nem os perfis do produto — são da Intranet, não do sistema administrado.
/// </summary>
/// <param name="gestao">Porta.</param>
/// <param name="trilha">Trilha da Intranet.</param>
public sealed class PerfisDoTenantHandler(IGestaoDeAcessoDoTenant gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Cria um perfil.</summary>
	public async Task<Result> CriarAsync(Guid tenantId, string? nome, CancellationToken cancellationToken = default)
	{
		var valido = Validar(nome);

		if (valido.IsFailure)
		{
			return valido;
		}

		var aparado = nome!.Trim();
		var criado = await gestao.CriarPerfilAsync(tenantId, aparado, cancellationToken).ConfigureAwait(false);

		if (criado.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant.PerfilAsync(trilha, VerbosDeAuditoria.TenantAcessoPerfilCriar, tenantId, aparado, cancellationToken)
				.ConfigureAwait(false);
		}

		return criado;
	}

	/// <summary>Exclui um perfil sem membros.</summary>
	public async Task<Result> ExcluirAsync(Guid tenantId, string? nome, CancellationToken cancellationToken = default)
	{
		var valido = Validar(nome);

		if (valido.IsFailure)
		{
			return valido;
		}

		var perfil = await gestao.ObterPerfilAsync(tenantId, nome!.Trim(), cancellationToken).ConfigureAwait(false);

		if (perfil.IsFailure)
		{
			return Result.Failure(perfil.Error);
		}

		if (perfil.Value.Reservado)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		if (perfil.Value.TotalDeMembros > 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilComMembros);
		}

		var excluido = await gestao.ExcluirPerfilAsync(tenantId, perfil.Value.Nome, cancellationToken).ConfigureAwait(false);

		if (excluido.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant.PerfilAsync(trilha, VerbosDeAuditoria.TenantAcessoPerfilExcluir, tenantId, perfil.Value.Nome, cancellationToken)
				.ConfigureAwait(false);
		}

		return excluido;
	}

	/// <summary>Substitui a lista de permissões pelo texto (uma por linha).</summary>
	public async Task<Result> DefinirPermissoesAsync(Guid tenantId, string? nome, string? texto, CancellationToken cancellationToken = default)
	{
		var lista = ListaDePermissoes.Ler(texto);

		if (lista.IsFailure)
		{
			return Result.Failure(lista.Error);
		}

		var perfil = await gestao.ObterPerfilAsync(tenantId, nome?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);

		if (perfil.IsFailure)
		{
			return Result.Failure(perfil.Error);
		}

		if (perfil.Value.Reservado)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		var definido = await gestao.DefinirPermissoesDoPerfilAsync(tenantId, perfil.Value.Nome, lista.Value, cancellationToken).ConfigureAwait(false);

		if (definido.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant
				.PermissoesAsync(trilha, tenantId, perfil.Value.Nome, perfil.Value.Permissoes.Count, lista.Value.Count, cancellationToken)
				.ConfigureAwait(false);
		}

		return definido;
	}

	private static Result Validar(string? nome)
	{
		var aparado = nome?.Trim() ?? string.Empty;

		if (aparado.Length == 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (ClassificacaoDePerfil.EhReservado(aparado))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		return ClassificacaoDePerfil.NomeValido(aparado) ? Result.Success() : Result.Failure(IntranetErrors.Acesso.PerfilNomeInvalido);
	}
}
```

Registrar em `IntranetApplicationExtensions.cs`: `services.AddScoped<PerfisDoTenantHandler>();`.

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~PerfisDoTenantHandlerTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(tenants): perfis e permissoes do tenant administrado"
```

---

### Task 5: Pessoas e federação do tenant administrado

**Files:**
- Create: `src/Secco.Intranet.Application/Tenants/Acesso/UsuariosDoTenantHandler.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Acesso/FederacaoDoTenantHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/UsuariosEFederacaoDoTenantTests.cs`

**Interfaces:**
- Consumes: porta, dublê, `AuditoriaDoAcessoDoTenant`, verbos (Tasks 2–4); `NomeDeExibicao` (`Normalizar`, `MaxLength = 160`).
- Produces:

```csharp
public sealed record CadastrarUsuarioDoTenantCommand(string? Email, string? Nome, IReadOnlyList<string>? Perfis, bool EntraPorSenha);
public sealed class UsuariosDoTenantHandler(IGestaoDeAcessoDoTenant gestao, ITrilhaDeAuditoria trilha)
{
	Task<Result<Guid>> CadastrarAsync(Guid tenantId, CadastrarUsuarioDoTenantCommand command, CancellationToken ct = default);
	Task<Result> AtribuirPerfilAsync(Guid tenantId, Guid usuarioId, string? perfil, CancellationToken ct = default);
	Task<Result> RetirarPerfilAsync(Guid tenantId, Guid usuarioId, string? perfil, CancellationToken ct = default);
	Task<Result> ReenviarConviteAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default);
	Task<Result> DesativarAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default);
	Task<Result> ReativarAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default);
	Task<Result> EncerrarSessoesAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default);
}
public sealed record DefinirFederacaoDoTenantCommand(string? DirectoryId, bool Habilitada, bool ConfirmarTroca);
public sealed class FederacaoDoTenantHandler(IGestaoDeAcessoDoTenant gestao, ITrilhaDeAuditoria trilha)
{
	Task<Result> DefinirAsync(Guid tenantId, DefinirFederacaoDoTenantCommand command, CancellationToken ct = default);
}
```

- [ ] **Step 1: Escrever os testes**

`tests/Secco.Intranet.Tests/Unit/UsuariosEFederacaoDoTenantTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Cadastro, perfis, convite, conta e federação de pessoas do tenant administrado.</summary>
public class UsuariosEFederacaoDoTenantTests
{
	private static readonly Guid Tenant = Guid.NewGuid();
	private static readonly Guid ComSenha = Guid.NewGuid();
	private static readonly Guid SemSenha = Guid.NewGuid();
	private static readonly Guid SoEntra = Guid.NewGuid();
	private static readonly Guid Diretorio = Guid.NewGuid();

	private static (UsuariosDoTenantHandler Usuarios, FederacaoDoTenantHandler Federacao, GestaoDeAcessoDoTenantFalsa Gestao, TrilhaDeAcessoFalsa Trilha) Criar(
		bool federacaoHabilitada = false)
	{
		var gestao = new GestaoDeAcessoDoTenantFalsa()
			.ComPerfil("compras-aprovador")
			.ComPerfil("compras-leitor")
			.ComPerfil("installation-operator", reservado: true)
			.ComUsuario(ComSenha, "ana@compras.com", SituacaoDoUsuario.Ativo, temSenha: true)
			.ComUsuario(SemSenha, "bia@compras.com", SituacaoDoUsuario.Ativo, temSenha: false, entraPorSenha: true)
			.ComUsuario(SoEntra, "caio@compras.com", SituacaoDoUsuario.Ativo, temSenha: false, entraPorSenha: false);
		gestao.Federacao = new FederacaoDto(federacaoHabilitada ? Diretorio : null, federacaoHabilitada, null);
		var trilha = new TrilhaDeAcessoFalsa();

		return (new UsuariosDoTenantHandler(gestao, trilha), new FederacaoDoTenantHandler(gestao, trilha), gestao, trilha);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("sem-arroba")]
	[InlineData("ana @compras.com")]
	public async Task Cadastrar_EmailInvalido(string? email)
	{
		var (usuarios, _, gestao, _) = Criar();

		(await usuarios.CadastrarAsync(Tenant, new(email, null, [], true))).Error.Should().Be(IntranetErrors.Acesso.EmailInvalido);
		gestao.Cadastrados.Should().BeEmpty();
	}

	[Fact]
	public async Task Cadastrar_SoPeloEntraSemFederacao_Recusado()
	{
		var (usuarios, _, gestao, _) = Criar(federacaoHabilitada: false);

		(await usuarios.CadastrarAsync(Tenant, new("dani@compras.com", null, [], EntraPorSenha: false)))
			.Error.Should().Be(IntranetErrors.Acesso.EntraSemFederacao);
		gestao.Cadastrados.Should().BeEmpty();
	}

	[Fact]
	public async Task Cadastrar_SoPeloEntraComFederacao_Aceito()
	{
		var (usuarios, _, gestao, _) = Criar(federacaoHabilitada: true);

		(await usuarios.CadastrarAsync(Tenant, new("dani@compras.com", null, [], EntraPorSenha: false))).IsSuccess.Should().BeTrue();
		gestao.Cadastrados.Should().ContainSingle().Which.EntraPorSenha.Should().BeFalse();
	}

	[Fact]
	public async Task Cadastrar_PerfilReservadoOuInexistente_Recusado()
	{
		var (usuarios, _, _, _) = Criar();

		(await usuarios.CadastrarAsync(Tenant, new("d@compras.com", null, ["installation-operator"], true)))
			.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		(await usuarios.CadastrarAsync(Tenant, new("d@compras.com", null, ["nao-existe"], true)))
			.Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado);
	}

	[Fact]
	public async Task Cadastrar_NormalizaPerfisENome_TrilhaSemEmail()
	{
		var (usuarios, _, gestao, trilha) = Criar();

		var id = await usuarios.CadastrarAsync(
			Tenant, new(" Dani@Compras.com ", "  Dani  ", [" compras-leitor", "compras-leitor", "compras-aprovador "], true));

		id.IsSuccess.Should().BeTrue();
		var cadastrado = gestao.Cadastrados.Should().ContainSingle().Subject;
		cadastrado.Email.Should().Be("Dani@Compras.com");
		cadastrado.Nome.Should().Be("Dani");
		cadastrado.Perfis.Should().Equal("compras-leitor", "compras-aprovador");
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.TenantAcessoUsuarioCadastrar);
		registro.Metadata.Should().NotContain("Dani@Compras.com").And.Contain(id.Value.ToString());
	}

	[Fact]
	public async Task Cadastrar_NomeLongoDemais_Recusado()
	{
		var (usuarios, _, _, _) = Criar();

		(await usuarios.CadastrarAsync(Tenant, new("d@compras.com", new string('x', NomeDeExibicao.MaxLength + 1), [], true)))
			.IsFailure.Should().BeTrue();
	}

	[Fact]
	public async Task ReenviarConvite_SoParaQuemEntraPorSenhaESemSenha()
	{
		var (usuarios, _, gestao, _) = Criar();

		(await usuarios.ReenviarConviteAsync(Tenant, ComSenha)).Error.Should().Be(IntranetErrors.Acesso.ConviteNaoCabe);
		(await usuarios.ReenviarConviteAsync(Tenant, SoEntra)).Error.Should().Be(IntranetErrors.Acesso.ConviteNaoCabe);
		(await usuarios.ReenviarConviteAsync(Tenant, SemSenha)).IsSuccess.Should().BeTrue();

		gestao.Chamadas.Should().ContainSingle(c => c.StartsWith("reenviarConvite", StringComparison.Ordinal))
			.Which.Should().Be($"reenviarConvite:{Tenant}:{SemSenha}");
	}

	[Fact]
	public async Task Atribuir_Reservado_Recusado_EAtribuivelVaiParaATrilha()
	{
		var (usuarios, _, _, trilha) = Criar();

		(await usuarios.AtribuirPerfilAsync(Tenant, ComSenha, "installation-operator")).Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		(await usuarios.AtribuirPerfilAsync(Tenant, ComSenha, "compras-leitor")).IsSuccess.Should().BeTrue();

		trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantAcessoPerfilAtribuir);
	}

	[Fact]
	public async Task Federacao_TrocaSemConfirmacao_Recusada()
	{
		var (_, federacao, gestao, _) = Criar(federacaoHabilitada: true);

		(await federacao.DefinirAsync(Tenant, new(Guid.NewGuid().ToString(), true, ConfirmarTroca: false)))
			.Error.Should().Be(IntranetErrors.Acesso.TrocaDeDiretorioNaoConfirmada);
		gestao.Chamadas.Should().NotContain(c => c.StartsWith("definirFederacao", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Federacao_MesmoDiretorioOuPrimeiraVez_NaoPedeConfirmacao()
	{
		var (_, comDiretorio, _, _) = Criar(federacaoHabilitada: true);
		var (_, semDiretorio, _, _) = Criar(federacaoHabilitada: false);

		(await comDiretorio.DefinirAsync(Tenant, new(Diretorio.ToString(), false, false))).IsSuccess.Should().BeTrue();
		(await semDiretorio.DefinirAsync(Tenant, new(Guid.NewGuid().ToString(), true, false))).IsSuccess.Should().BeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("nao-e-guid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public async Task Federacao_DirectoryIdInvalido(string? directoryId)
	{
		var (_, federacao, _, _) = Criar();

		(await federacao.DefinirAsync(Tenant, new(directoryId, true, true))).Error.Should().Be(IntranetErrors.Acesso.DirectoryIdInvalido);
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~UsuariosEFederacaoDoTenantTests"`
Expected: erro de compilação.

- [ ] **Step 3: Handler de pessoas**

`src/Secco.Intranet.Application/Tenants/Acesso/UsuariosDoTenantHandler.cs`:

```csharp
using System.Net.Mail;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>Pedido de cadastro de uma pessoa no tenant administrado.</summary>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição (opcional).</param>
/// <param name="Perfis">Perfis iniciais.</param>
/// <param name="EntraPorSenha"><c>true</c>: convite para senha; <c>false</c>: só pelo Entra.</param>
public sealed record CadastrarUsuarioDoTenantCommand(string? Email, string? Nome, IReadOnlyList<string>? Perfis, bool EntraPorSenha);

/// <summary>
/// Pessoas de um tenant administrado. O cadastro é a única porta de entrada: o login federado da
/// plataforma é fail-closed e só casa quem já existe (ADR-0026). Sem as regras de "último
/// intranet-admin" e "a própria conta" — o intranet-admin não é usuário daquele tenant.
/// </summary>
/// <param name="gestao">Porta.</param>
/// <param name="trilha">Trilha da Intranet.</param>
public sealed class UsuariosDoTenantHandler(IGestaoDeAcessoDoTenant gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Cadastra a pessoa; devolve o id criado.</summary>
	public async Task<Result<Guid>> CadastrarAsync(Guid tenantId, CadastrarUsuarioDoTenantCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var email = command.Email?.Trim() ?? string.Empty;

		if (!EmailValido(email))
		{
			return Result.Failure<Guid>(IntranetErrors.Acesso.EmailInvalido);
		}

		var nome = NomeDeExibicao.Normalizar(command.Nome);

		if (nome is { Length: > NomeDeExibicao.MaxLength })
		{
			return Result.Failure<Guid>(IntranetErrors.Acesso.CadastroRecusado);
		}

		IReadOnlyList<string> perfis = [.. (command.Perfis ?? []).Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal)];

		var perfisValidos = await ConferirPerfisAsync(tenantId, perfis, cancellationToken).ConfigureAwait(false);

		if (perfisValidos.IsFailure)
		{
			return Result.Failure<Guid>(perfisValidos.Error);
		}

		if (!command.EntraPorSenha)
		{
			var federacao = await gestao.ObterFederacaoAsync(tenantId, cancellationToken).ConfigureAwait(false);

			if (federacao.IsFailure)
			{
				return Result.Failure<Guid>(federacao.Error);
			}

			if (!federacao.Value.Habilitada)
			{
				return Result.Failure<Guid>(IntranetErrors.Acesso.EntraSemFederacao);
			}
		}

		var criado = await gestao
			.CadastrarUsuarioAsync(tenantId, new NovoUsuarioDoTenant(email, nome, perfis, command.EntraPorSenha), cancellationToken)
			.ConfigureAwait(false);

		if (criado.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant.CadastroAsync(trilha, tenantId, criado.Value, command.EntraPorSenha, perfis, cancellationToken)
				.ConfigureAwait(false);
		}

		return criado;
	}

	/// <summary>Atribui um perfil não reservado.</summary>
	public async Task<Result> AtribuirPerfilAsync(Guid tenantId, Guid usuarioId, string? perfil, CancellationToken cancellationToken = default)
	{
		var nome = perfil?.Trim() ?? string.Empty;
		var conferido = await ConferirPerfisAsync(tenantId, [nome], cancellationToken).ConfigureAwait(false);

		if (conferido.IsFailure)
		{
			return conferido;
		}

		var atribuido = await gestao.AtribuirPerfilAsync(tenantId, usuarioId, nome, cancellationToken).ConfigureAwait(false);

		return await RegistrarSeOkAsync(atribuido, VerbosDeAuditoria.TenantAcessoPerfilAtribuir, tenantId, usuarioId, nome, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>Retira um perfil não reservado.</summary>
	public async Task<Result> RetirarPerfilAsync(Guid tenantId, Guid usuarioId, string? perfil, CancellationToken cancellationToken = default)
	{
		var nome = perfil?.Trim() ?? string.Empty;

		if (nome.Length == 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (ClassificacaoDePerfil.EhReservado(nome))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		var retirado = await gestao.RetirarPerfilAsync(tenantId, usuarioId, nome, cancellationToken).ConfigureAwait(false);

		return await RegistrarSeOkAsync(retirado, VerbosDeAuditoria.TenantAcessoPerfilRetirar, tenantId, usuarioId, nome, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>Reenvia o convite — só a quem entra por senha e ainda não a definiu.</summary>
	public async Task<Result> ReenviarConviteAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = await gestao.ObterUsuarioAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		if (usuario.Value.TemSenha || !usuario.Value.EntraPorSenha)
		{
			return Result.Failure(IntranetErrors.Acesso.ConviteNaoCabe);
		}

		var reenviado = await gestao.ReenviarConviteAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false);

		return await RegistrarSeOkAsync(reenviado, VerbosDeAuditoria.TenantAcessoConviteReenviar, tenantId, usuarioId, null, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>Desativa a conta.</summary>
	public async Task<Result> DesativarAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		await RegistrarSeOkAsync(
			await gestao.DesativarUsuarioAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false),
			VerbosDeAuditoria.TenantAcessoUsuarioDesativar, tenantId, usuarioId, null, cancellationToken).ConfigureAwait(false);

	/// <summary>Reativa a conta.</summary>
	public async Task<Result> ReativarAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		await RegistrarSeOkAsync(
			await gestao.ReativarUsuarioAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false),
			VerbosDeAuditoria.TenantAcessoUsuarioReativar, tenantId, usuarioId, null, cancellationToken).ConfigureAwait(false);

	/// <summary>Encerra as sessões.</summary>
	public async Task<Result> EncerrarSessoesAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		await RegistrarSeOkAsync(
			await gestao.EncerrarSessoesAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false),
			VerbosDeAuditoria.TenantAcessoSessoesEncerrar, tenantId, usuarioId, null, cancellationToken).ConfigureAwait(false);

	private static bool EmailValido(string email) =>
		email.Length is > 0 and <= 256
		&& !email.Any(char.IsWhiteSpace)
		&& MailAddress.TryCreate(email, out var endereco)
		&& string.Equals(endereco.Address, email, StringComparison.Ordinal);

	private async Task<Result> ConferirPerfisAsync(Guid tenantId, IReadOnlyList<string> perfis, CancellationToken cancellationToken)
	{
		if (perfis.Any(p => p.Length == 0))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (perfis.Any(ClassificacaoDePerfil.EhReservado))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		if (perfis.Count == 0)
		{
			return Result.Success();
		}

		var existentes = await gestao.ListarPerfisAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (existentes.IsFailure)
		{
			return Result.Failure(existentes.Error);
		}

		var porNome = existentes.Value.ToDictionary(p => p.Nome, StringComparer.Ordinal);

		foreach (var perfil in perfis)
		{
			if (!porNome.TryGetValue(perfil, out var existente))
			{
				return Result.Failure(IntranetErrors.Acesso.PerfilNaoEncontrado);
			}

			if (existente.Reservado)
			{
				return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
			}
		}

		return Result.Success();
	}

	private async Task<Result> RegistrarSeOkAsync(
		Result resultado, string verbo, Guid tenantId, Guid usuarioId, string? perfil, CancellationToken cancellationToken)
	{
		if (resultado.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant.UsuarioAsync(trilha, verbo, tenantId, usuarioId, perfil, cancellationToken).ConfigureAwait(false);
		}

		return resultado;
	}
}
```

- [ ] **Step 4: Handler de federação**

`src/Secco.Intranet.Application/Tenants/Acesso/FederacaoDoTenantHandler.cs`:

```csharp
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>Pedido de definição da federação do tenant administrado.</summary>
/// <param name="DirectoryId">Tenant GUID do Entra, como digitado.</param>
/// <param name="Habilitada">Se o login por Entra fica ativo.</param>
/// <param name="ConfirmarTroca">Confirmação explícita para trocar um diretório já configurado.</param>
public sealed record DefinirFederacaoDoTenantCommand(string? DirectoryId, bool Habilitada, bool ConfirmarTroca);

/// <summary>
/// Define a federação Entra do tenant administrado. Trocar um directory id já configurado muda de
/// qual Entra as pessoas entram, e exige confirmação; configurar a primeira vez ou manter o mesmo, não.
/// </summary>
/// <param name="gestao">Porta.</param>
/// <param name="trilha">Trilha da Intranet.</param>
public sealed class FederacaoDoTenantHandler(IGestaoDeAcessoDoTenant gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result> DefinirAsync(Guid tenantId, DefinirFederacaoDoTenantCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (!Guid.TryParse(command.DirectoryId, out var directoryId) || directoryId == Guid.Empty)
		{
			return Result.Failure(IntranetErrors.Acesso.DirectoryIdInvalido);
		}

		var atual = await gestao.ObterFederacaoAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (atual.IsFailure)
		{
			return Result.Failure(atual.Error);
		}

		if (atual.Value.DirectoryId is { } anterior && anterior != directoryId && !command.ConfirmarTroca)
		{
			return Result.Failure(IntranetErrors.Acesso.TrocaDeDiretorioNaoConfirmada);
		}

		var definida = await gestao.DefinirFederacaoAsync(tenantId, directoryId, command.Habilitada, cancellationToken).ConfigureAwait(false);

		if (definida.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant.FederacaoAsync(trilha, tenantId, directoryId, command.Habilitada, cancellationToken).ConfigureAwait(false);
		}

		return definida;
	}
}
```

Registrar em `IntranetApplicationExtensions.cs`: `services.AddScoped<UsuariosDoTenantHandler>();` e `services.AddScoped<FederacaoDoTenantHandler>();`.

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~UsuariosEFederacaoDoTenantTests|FullyQualifiedName~PerfisDoTenantHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(tenants): pessoas e federacao do tenant administrado"
```

---

### Task 6: Filtro `[SecureGateLigado]`, controller e autorização

**Files:**
- Create: `src/Secco.Intranet.Web/Authentication/SecureGateLigadoAttribute.cs`
- Create: `src/Secco.Intranet.Web/Controllers/TenantsAcessoController.cs`
- Create: `src/Secco.Intranet.Web/Models/Tenants/TenantsAcessoViewModels.cs`
- Create: `src/Secco.Intranet.Web/Views/TenantsAcesso/Index.cshtml` (mínima nesta tarefa; completa na Task 7)
- Test: `tests/Secco.Intranet.Tests/Integration/TenantsAcessoAutorizacaoTests.cs`

**Interfaces:**
- Consumes: todos os handlers das Tasks 3–5; `[SomenteIntranetAdmin]`, `[ExigeSegundoFator]`, `[TenantAdministrado]`.
- Produces: rotas

| Verbo | Rota | Action |
|---|---|---|
| GET | `/tenants/{tenantId}/acesso?aba=&busca=&page=` | `Index` |
| POST | `/tenants/{tenantId}/acesso/perfis` | `CriarPerfil(nome)` |
| GET | `/tenants/{tenantId}/acesso/perfis/{nome}?page=` | `Perfil` |
| POST | `/tenants/{tenantId}/acesso/perfis/{nome}/excluir` | `ExcluirPerfil` |
| POST | `/tenants/{tenantId}/acesso/perfis/{nome}/permissoes` | `EditarPermissoes(permissoes)` |
| POST | `/tenants/{tenantId}/acesso/perfis/{nome}/membros` | `AdicionarMembro(usuarioId)` |
| GET/POST | `/tenants/{tenantId}/acesso/usuarios/novo` | `Novo` |
| GET | `/tenants/{tenantId}/acesso/usuarios/{usuarioId}` | `Usuario` |
| POST | `/tenants/{tenantId}/acesso/usuarios/{usuarioId}/perfis` | `AtribuirPerfil(perfil, origem)` |
| POST | `/tenants/{tenantId}/acesso/usuarios/{usuarioId}/perfis/retirar` | `RetirarPerfil(perfil, origem)` |
| POST | `/tenants/{tenantId}/acesso/usuarios/{usuarioId}/desativar` | `Desativar` |
| POST | `/tenants/{tenantId}/acesso/usuarios/{usuarioId}/reativar` | `Reativar` |
| POST | `/tenants/{tenantId}/acesso/usuarios/{usuarioId}/sessoes/encerrar` | `EncerrarSessoes` |
| POST | `/tenants/{tenantId}/acesso/usuarios/{usuarioId}/convite` | `ReenviarConvite` |
| POST | `/tenants/{tenantId}/acesso/federacao` | `SalvarFederacao(directoryId, habilitada, confirmarTroca)` |

- View models (namespace `Secco.Intranet.Web.Models.Tenants`):

```csharp
public enum AbaDoAcessoDoTenant { Perfis, Usuarios, Federacao }
public sealed record TenantsAcessoIndexViewModel(
	CabecalhoDoTenantDto Tenant, AbaDoAcessoDoTenant Aba,
	IReadOnlyList<PerfilDoTenantDto>? Perfis, PagedResult<UsuarioDto>? Usuarios, string? Busca, FederacaoDto? Federacao);
public sealed record TenantsAcessoPerfilViewModel(CabecalhoDoTenantDto Tenant, PerfilDoTenantTelaDto Tela);
public sealed record TenantsAcessoUsuarioViewModel(CabecalhoDoTenantDto Tenant, UsuarioDoTenantTelaDto Tela);
public sealed class TenantsAcessoNovoViewModel
{
	public CabecalhoDoTenantDto? Tenant { get; set; }
	public IReadOnlyList<string> PerfisDisponiveis { get; set; } = [];
	public bool FederacaoHabilitada { get; set; }
	public string? Email { get; set; }
	public string? Nome { get; set; }
	public List<string> Perfis { get; set; } = [];
	public bool EntraPorSenha { get; set; } = true;
	public string? Erro { get; set; }
}
```

- [ ] **Step 1: Escrever os testes de autorização e IDOR**

`tests/Secco.Intranet.Tests/Integration/TenantsAcessoAutorizacaoTests.cs`:

```csharp
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Acesso do tenant administrado pelo host real: o trio da ADR-0008 e o 2FA em toda rota, 404 para
/// tenant fora do cadastro ou com SecureGate desligado, e o tenant da rota vencendo o do corpo.
/// </summary>
public class TenantsAcessoAutorizacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Admin = Guid.NewGuid();
	private static readonly Guid SemFator = Guid.NewGuid();
	private static readonly Guid Pessoa = Guid.NewGuid();
	private readonly Guid _ligado = Guid.NewGuid();
	private readonly Guid _desligado = Guid.NewGuid();
	private readonly Guid _foraDoCadastro = Guid.NewGuid();
	private GestaoDeAcessoDoTenantFalsa _acesso = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa()
			.ComUsuario(Admin, "admin@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin")
			.ComDoisFatores(Admin)
			.ComUsuario(SemFator, "sem@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		factory.GestaoDeTenants = new GestaoDeTenantsFalsa()
			.ComTenant(_ligado, $"lig-{_ligado:N}"[..20])
			.ComTenant(_desligado, $"des-{_desligado:N}"[..20])
			.ComTenant(_foraDoCadastro, $"for-{_foraDoCadastro:N}"[..20]);
		_acesso = new GestaoDeAcessoDoTenantFalsa()
			.ComPerfil("compras.aprovador", false, "pedido:aprovar")
			.ComUsuario(Pessoa, "ana@compras.com", SituacaoDoUsuario.Ativo);
		factory.GestaoDeAcessoDoTenant = _acesso;

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var cadastro = escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>();
		var comSecureGate = new TenantAdministrado(_ligado, "Compras", "Ana", OrigemDoTenant.Criado, "admin");
		comSecureGate.HabilitarSecureGate();
		await cadastro.TentarAdicionarAsync(comSecureGate);
		await cadastro.TentarAdicionarAsync(new TenantAdministrado(_desligado, "RH", "Bia", OrigemDoTenant.Criado, "admin"));
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Cliente(Guid? usuario, params string[] roles)
	{
		var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		if (usuario is not null)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, usuario.Value.ToString());
		}

		return client;
	}

	private static IEnumerable<string> RotasGet(Guid tenant) =>
	[
		$"/tenants/{tenant}/acesso",
		$"/tenants/{tenant}/acesso?aba=usuarios",
		$"/tenants/{tenant}/acesso?aba=federacao",
		$"/tenants/{tenant}/acesso/perfis/compras.aprovador",
		$"/tenants/{tenant}/acesso/usuarios/novo",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}",
	];

	private static IEnumerable<string> RotasPost(Guid tenant) =>
	[
		$"/tenants/{tenant}/acesso/perfis",
		$"/tenants/{tenant}/acesso/perfis/compras.aprovador/excluir",
		$"/tenants/{tenant}/acesso/perfis/compras.aprovador/permissoes",
		$"/tenants/{tenant}/acesso/perfis/compras.aprovador/membros",
		$"/tenants/{tenant}/acesso/usuarios/novo",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/perfis",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/perfis/retirar",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/desativar",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/reativar",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/sessoes/encerrar",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/convite",
		$"/tenants/{tenant}/acesso/federacao",
	];

	public static TheoryData<string[]> UsuariosSemAcesso => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin" },
		new[] { "inventario-admin" },
	};

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task SemIntranetAdmin_403_EmTodaRota(string[] roles)
	{
		var client = Cliente(Admin, roles);

		foreach (var rota in RotasGet(_ligado))
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota}");
		}

		foreach (var rota in RotasPost(_ligado))
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"POST {rota}");
		}
	}

	[Fact]
	public async Task IntranetAdminSemSegundoFator_403_EmTodaRota()
	{
		var client = Cliente(SemFator, "intranet-admin");

		foreach (var rota in RotasGet(_ligado))
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota}");
		}

		foreach (var rota in RotasPost(_ligado))
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"POST {rota}");
		}
	}

	[Fact]
	public async Task ForaDoCadastroOuSecureGateDesligado_404_EmTodaRota_SemTocarAPorta()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/acesso");
		_acesso.Chamadas.Clear();

		foreach (var tenant in new[] { _foraDoCadastro, _desligado })
		{
			foreach (var rota in RotasGet(tenant))
			{
				(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.NotFound, $"GET {rota}");
			}

			foreach (var rota in RotasPost(tenant))
			{
				(await client.PostAsync(rota, AuxiliaresDeHttp.Form(("__RequestVerificationToken", token))))
					.StatusCode.Should().Be(HttpStatusCode.NotFound, $"POST {rota}");
			}
		}

		_acesso.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task TenantNoCorpo_EIgnorado_ValeODaRota()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/acesso");

		await client.PostAsync($"/tenants/{_ligado}/acesso/perfis",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("nome", "compras-leitor"), ("tenantId", _foraDoCadastro.ToString())));

		_acesso.Chamadas.Should().Contain($"criarPerfil:{_ligado}:compras-leitor")
			.And.NotContain(c => c.Contains(_foraDoCadastro.ToString(), StringComparison.Ordinal));
	}

	[Fact]
	public async Task PerfilComPontoNoNome_AbreNaRota()
	{
		var resposta = await Cliente(Admin, "intranet-admin").GetAsync($"/tenants/{_ligado}/acesso/perfis/compras.aprovador");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TenantsAcessoAutorizacaoTests"`
Expected: falha de compilação (`GestaoDeAcessoDoTenantFalsa` existe; o que falta são rotas) e, depois de compilar, 404 nas rotas que deveriam dar 403/200.

- [ ] **Step 3: Filtro**

`src/Secco.Intranet.Web/Authentication/SecureGateLigadoAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Tenants.Acesso;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Barra o acesso de tenant administrado cujo SecureGate não foi ligado no cadastro. Responde 404,
/// como <see cref="TenantAdministradoAttribute"/>, e roda logo depois dele.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SecureGateLigadoAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
	/// <inheritdoc />
	public int Order => int.MinValue + 3;

	/// <inheritdoc />
	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!Guid.TryParse(context.RouteData.Values[TenantAdministradoAttribute.ValorDeRota]?.ToString(), out var tenantId))
		{
			context.Result = new NotFoundResult();

			return;
		}

		var verificar = context.HttpContext.RequestServices.GetRequiredService<VerificarSecureGateLigadoHandler>();

		if (!await verificar.HandleAsync(tenantId, context.HttpContext.RequestAborted).ConfigureAwait(false))
		{
			context.Result = new NotFoundResult();
		}
	}
}
```

A ordem fica `[SomenteIntranetAdmin]` (`int.MinValue`), `[ExigeSegundoFator]` (`+1`), `[TenantAdministrado]` (`+2`) e este (`+3`).

- [ ] **Step 4: View models**

`src/Secco.Intranet.Web/Models/Tenants/TenantsAcessoViewModels.cs` com os tipos do bloco **Produces**, cada um com `/// <summary>`, e os `using` de `Secco.Intranet.Application.Acesso`, `Secco.Intranet.Application.Tenants.Acesso` e do namespace de `PagedResult`.

- [ ] **Step 5: Controller**

`src/Secco.Intranet.Web/Controllers/TenantsAcessoController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Tenants;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Perfis, pessoas e federação de um tenant administrado (subsistema 2 da Área de tenants). Mesma
/// cadeia de filtros da Área de tenants, mais o SecureGate ligado no cadastro. O tenant vem só da
/// rota: nenhum parâmetro de tenant é lido de form ou query.
/// </summary>
[Route("tenants/{tenantId:guid}/acesso")]
[SomenteIntranetAdmin]
[ExigeSegundoFator]
[TenantAdministrado]
[SecureGateLigado]
public sealed class TenantsAcessoController(
	ObterCabecalhoDoTenantHandler cabecalho,
	LeituraDoAcessoDoTenantHandler leitura,
	PerfisDoTenantHandler perfis,
	UsuariosDoTenantHandler usuarios,
	FederacaoDoTenantHandler federacao) : Controller
{
	/// <summary>Abas Perfis, Usuários e Federação.</summary>
	[HttpGet("")]
	public async Task<IActionResult> Index(
		[FromRoute] Guid tenantId, string? aba, string? busca, int page = 1, CancellationToken cancellationToken = default)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		if (string.Equals(aba, "usuarios", StringComparison.OrdinalIgnoreCase))
		{
			var lista = await leitura.ListarUsuariosAsync(tenantId, busca, page, cancellationToken);

			return lista.IsFailure
				? Falha(lista.Error)
				: View(new TenantsAcessoIndexViewModel(topo.Value, AbaDoAcessoDoTenant.Usuarios, null, lista.Value, busca, null));
		}

		if (string.Equals(aba, "federacao", StringComparison.OrdinalIgnoreCase))
		{
			var lida = await leitura.ObterFederacaoAsync(tenantId, cancellationToken);

			return lida.IsFailure
				? Falha(lida.Error)
				: View(new TenantsAcessoIndexViewModel(topo.Value, AbaDoAcessoDoTenant.Federacao, null, null, null, lida.Value));
		}

		var lidos = await leitura.ListarPerfisAsync(tenantId, cancellationToken);

		return lidos.IsFailure
			? Falha(lidos.Error)
			: View(new TenantsAcessoIndexViewModel(topo.Value, AbaDoAcessoDoTenant.Perfis, lidos.Value, null, null, null));
	}

	/// <summary>Cria um perfil.</summary>
	[HttpPost("perfis")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> CriarPerfil([FromRoute] Guid tenantId, string? nome, CancellationToken cancellationToken = default) =>
		Concluir(await perfis.CriarAsync(tenantId, nome, cancellationToken), "Perfil criado.", Url.Action(nameof(Index), new { tenantId })!);

	/// <summary>Detalhe de um perfil.</summary>
	[HttpGet("perfis/{nome}")]
	public async Task<IActionResult> Perfil([FromRoute] Guid tenantId, [FromRoute] string nome, int page = 1, CancellationToken cancellationToken = default)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var tela = await leitura.ObterPerfilAsync(tenantId, nome, page, cancellationToken);

		return tela.IsFailure ? Falha(tela.Error) : View(new TenantsAcessoPerfilViewModel(topo.Value, tela.Value));
	}

	/// <summary>Exclui um perfil sem membros.</summary>
	[HttpPost("perfis/{nome}/excluir")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ExcluirPerfil([FromRoute] Guid tenantId, [FromRoute] string nome, CancellationToken cancellationToken = default)
	{
		var excluido = await perfis.ExcluirAsync(tenantId, nome, cancellationToken);

		return Concluir(
			excluido,
			"Perfil excluído.",
			excluido.IsSuccess ? Url.Action(nameof(Index), new { tenantId })! : Url.Action(nameof(Perfil), new { tenantId, nome })!);
	}

	/// <summary>Substitui as permissões do perfil.</summary>
	[HttpPost("perfis/{nome}/permissoes")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EditarPermissoes(
		[FromRoute] Guid tenantId, [FromRoute] string nome, string? permissoes, CancellationToken cancellationToken = default) =>
		Concluir(await perfis.DefinirPermissoesAsync(tenantId, nome, permissoes, cancellationToken),
			"Permissões salvas.", Url.Action(nameof(Perfil), new { tenantId, nome })!);

	/// <summary>Adiciona um membro a partir da tela do perfil.</summary>
	[HttpPost("perfis/{nome}/membros")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AdicionarMembro(
		[FromRoute] Guid tenantId, [FromRoute] string nome, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.AtribuirPerfilAsync(tenantId, usuarioId, nome, cancellationToken),
			"Membro adicionado.", Url.Action(nameof(Perfil), new { tenantId, nome })!);

	/// <summary>Formulário de cadastro.</summary>
	[HttpGet("usuarios/novo")]
	public async Task<IActionResult> Novo([FromRoute] Guid tenantId, CancellationToken cancellationToken = default)
	{
		var modelo = new TenantsAcessoNovoViewModel();

		return await PreencherNovo(tenantId, modelo, cancellationToken) ?? View(modelo);
	}

	/// <summary>Cadastra a pessoa.</summary>
	[HttpPost("usuarios/novo")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Novo([FromRoute] Guid tenantId, TenantsAcessoNovoViewModel modelo, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(modelo);

		var criado = await usuarios.CadastrarAsync(
			tenantId, new CadastrarUsuarioDoTenantCommand(modelo.Email, modelo.Nome, modelo.Perfis, modelo.EntraPorSenha), cancellationToken);

		if (criado.IsSuccess)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = modelo.EntraPorSenha
				? "Pessoa cadastrada. O SecureGate enviou o convite para ela definir a senha."
				: "Pessoa cadastrada. Ela entra pelo Entra ID da empresa.";

			return Redirect(Url.Action(nameof(Usuario), new { tenantId, usuarioId = criado.Value })!);
		}

		modelo.Erro = criado.Error.Description;

		return await PreencherNovo(tenantId, modelo, cancellationToken) ?? View(modelo);
	}

	/// <summary>Detalhe de uma pessoa.</summary>
	[HttpGet("usuarios/{usuarioId:guid}")]
	public async Task<IActionResult> Usuario([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var tela = await leitura.ObterUsuarioAsync(tenantId, usuarioId, cancellationToken);

		return tela.IsFailure ? Falha(tela.Error) : View(new TenantsAcessoUsuarioViewModel(topo.Value, tela.Value));
	}

	/// <summary>Atribui um perfil.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/perfis")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AtribuirPerfil(
		[FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, string? perfil, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.AtribuirPerfilAsync(tenantId, usuarioId, perfil, cancellationToken),
			"Perfil atribuído.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Retira um perfil; <paramref name="origem"/> = <c>perfil</c> volta à tela do perfil.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/perfis/retirar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> RetirarPerfil(
		[FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, string? perfil, string? origem, CancellationToken cancellationToken = default) =>
		Concluir(
			await usuarios.RetirarPerfilAsync(tenantId, usuarioId, perfil, cancellationToken),
			"Perfil retirado.",
			string.Equals(origem, "perfil", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(perfil)
				? Url.Action(nameof(Perfil), new { tenantId, nome = perfil })!
				: Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Desativa a conta.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/desativar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Desativar([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.DesativarAsync(tenantId, usuarioId, cancellationToken), "Conta desativada.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Reativa a conta.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/reativar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Reativar([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.ReativarAsync(tenantId, usuarioId, cancellationToken), "Conta reativada.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Encerra as sessões.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/sessoes/encerrar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EncerrarSessoes([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.EncerrarSessoesAsync(tenantId, usuarioId, cancellationToken),
			"Sessões encerradas. Vale nos sistemas em até alguns minutos.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Reenvia o convite.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/convite")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ReenviarConvite([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.ReenviarConviteAsync(tenantId, usuarioId, cancellationToken), "Convite reenviado.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Define a federação.</summary>
	[HttpPost("federacao")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SalvarFederacao(
		[FromRoute] Guid tenantId, string? directoryId, bool habilitada, bool confirmarTroca, CancellationToken cancellationToken = default) =>
		Concluir(
			await federacao.DefinirAsync(tenantId, new DefinirFederacaoDoTenantCommand(directoryId, habilitada, confirmarTroca), cancellationToken),
			habilitada ? "Federação com o Entra ID habilitada." : "Federação com o Entra ID desabilitada.",
			Url.Action(nameof(Index), new { tenantId, aba = "federacao" })!);

	private async Task<IActionResult?> PreencherNovo(Guid tenantId, TenantsAcessoNovoViewModel modelo, CancellationToken cancellationToken)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var atribuiveis = await leitura.PerfisAtribuiveisAsync(tenantId, cancellationToken);

		if (atribuiveis.IsFailure)
		{
			return Falha(atribuiveis.Error);
		}

		var lida = await leitura.ObterFederacaoAsync(tenantId, cancellationToken);

		if (lida.IsFailure)
		{
			return Falha(lida.Error);
		}

		modelo.Tenant = topo.Value;
		modelo.PerfisDisponiveis = atribuiveis.Value;
		modelo.FederacaoHabilitada = lida.Value.Habilitada;

		return null;
	}

	private RedirectResult Concluir(Result resultado, string sucesso, string destino)
	{
		TempData[resultado.IsSuccess ? FeedbackViewComponent.ChaveDaMensagem : FeedbackViewComponent.ChaveDaMensagemDeErro] =
			resultado.IsSuccess ? sucesso : resultado.Error.Description;

		return Redirect(destino);
	}

	private IActionResult Falha(Error erro) =>
		erro.Type == ErrorType.NotFound
			? NotFound()
			: View("~/Views/Tenants/Indisponivel.cshtml", new TenantsIndisponivelViewModel(erro.Description));
}
```

- [ ] **Step 6: View mínima para o teste**

`src/Secco.Intranet.Web/Views/TenantsAcesso/Index.cshtml` provisória (a Task 7 a completa):

```cshtml
@model TenantsAcessoIndexViewModel
@{
    ViewData["Title"] = $"Acesso — {Model.Tenant.Sistema}";
}
<partial name="_PageHeader" model="@(new PageHeaderModel(Model.Tenant.Sistema, "Perfis e pessoas deste sistema."))" />
```

Acrescentar `@using Secco.Intranet.Application.Tenants.Acesso` e `@using Secco.Intranet.Web.Models.Tenants` em `Views/_ViewImports.cshtml` se ainda não estiverem. Criar também `Perfil.cshtml`, `Usuario.cshtml` e `Novo.cshtml` provisórias no mesmo molde (só o `_PageHeader` com o sistema), para os GETs do teste responderem 200.

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TenantsAcessoAutorizacaoTests|FullyQualifiedName~TenantsAutorizacaoTests|FullyQualifiedName~TenantsSegurancaTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(tenants): rotas do acesso do tenant administrado sob a cadeia de filtros"
```

---

### Task 7: Telas e link na página do tenant

**Files:**
- Modify: `src/Secco.Intranet.Web/Views/TenantsAcesso/{Index,Perfil,Usuario,Novo}.cshtml`
- Modify: `src/Secco.Intranet.Web/Views/Tenants/Detalhe.cshtml`
- Test: `tests/Secco.Intranet.Tests/Integration/TenantsAcessoTelasTests.cs`

**Interfaces:**
- Consumes: view models e rotas (Task 6).

- [ ] **Step 1: Escrever os testes de tela**

`tests/Secco.Intranet.Tests/Integration/TenantsAcessoTelasTests.cs` — mesma montagem de `TenantsAcessoAutorizacaoTests` (copiar `InitializeAsync` e `Cliente`, com tenant `_ligado` e `Pessoa` em senha pendente), e os testes:

```csharp
	[Fact]
	public async Task Index_MostraOSistemaEmQualSeEstaMexendo()
	{
		var html = AuxiliaresDeHttp.Decodificar(await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso"));

		html.Should().Contain("Compras").And.Contain("compras.aprovador").And.Contain("Cadastrar pessoa");
	}

	[Fact]
	public async Task Perfil_PermissoesEmTextoUmaPorLinha()
	{
		var html = AuxiliaresDeHttp.Decodificar(
			await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso/perfis/compras.aprovador"));

		html.Should().Contain("<textarea").And.Contain("pedido:aprovar");
	}

	[Fact]
	public async Task Novo_SemFederacao_NaoOfereceSoPeloEntra()
	{
		var html = await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso/usuarios/novo");

		AuxiliaresDeHttp.Decodificar(html).Should().NotContain("Só pelo Entra");
	}

	[Fact]
	public async Task Novo_ComFederacao_OfereceSoPeloEntra()
	{
		_acesso.Federacao = new(Guid.NewGuid(), true, null);

		var html = AuxiliaresDeHttp.Decodificar(await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso/usuarios/novo"));

		html.Should().Contain("Só pelo Entra");
	}

	[Fact]
	public async Task Usuario_SemSenhaEPorSenha_OfereceReenviarConvite()
	{
		var html = AuxiliaresDeHttp.Decodificar(
			await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso/usuarios/{Pessoa}"));

		html.Should().Contain("Reenviar convite");
	}

	[Fact]
	public async Task Federacao_ComDiretorio_PedeConfirmacaoDeTroca()
	{
		_acesso.Federacao = new(Guid.NewGuid(), true, null);

		var html = AuxiliaresDeHttp.Decodificar(
			await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso?aba=federacao"));

		html.Should().Contain("name=\"confirmarTroca\"");
	}

	[Fact]
	public async Task DetalheDoTenant_ComSecureGateLigado_LinkaOAcesso()
	{
		var html = await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}");

		html.Should().Contain($"href=\"/tenants/{_ligado}/acesso\"");
	}

	[Fact]
	public async Task Cadastrar_PeloFormulario_RedirecionaParaAPessoa()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/acesso/usuarios/novo");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/acesso/usuarios/novo",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("Email", "dani@compras.com"), ("EntraPorSenha", "true"),
				("Perfis", "compras.aprovador")));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.ToString().Should().StartWith($"/tenants/{_ligado}/acesso/usuarios/");
		_acesso.Cadastrados.Should().ContainSingle().Which.Perfis.Should().Equal("compras.aprovador");
	}
```

O `DetalheDoTenant` usa `IGestaoDeTenants.ObterStatusDosBancosAsync`; o `GestaoDeTenantsFalsa` já responde a ela. A página de detalhe mostra o SecureGate como `Ligado` porque o cadastro tem a marca.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TenantsAcessoTelasTests"`
Expected: FAIL (views provisórias).

- [ ] **Step 3: `Index.cshtml`**

Mesmo desenho de `Views/Acesso/Index.cshtml`, com estas diferenças:
- Cabeçalho: `new PageHeaderModel(Model.Tenant.Sistema, $"Perfis e pessoas do tenant {Model.Tenant.Nome}.", Acoes: new[] { new PageActionModel("Voltar ao tenant", $"/tenants/{Model.Tenant.TenantId}", "bi-arrow-left"), new PageActionModel("Cadastrar pessoa", $"/tenants/{Model.Tenant.TenantId}/acesso/usuarios/novo", "bi-person-plus") })`.
- Se `!Model.Tenant.Ativo`, logo abaixo do cabeçalho: `<p><partial name="_Badge" model="@(new BadgeModel("Tenant desativado", BadgeVariante.Aviso))" /> <span class="text-body-secondary">O sistema não está no ar; os acessos ficam preparados.</span></p>`.
- Abas com `href` explícito: `/tenants/{id}/acesso?aba=perfis|usuarios|federacao`.
- Perfis: lista de `PerfilDoTenantDto` com badge `Reservado` (perigo) ou `Perfil` (neutro) e `@perfil.TotalDePermissoes permissões`; o link do perfil é `/tenants/{id}/acesso/perfis/@Uri.EscapeDataString(perfil.Nome)`. Sem bloco de perfis do produto e sem botão de reconciliar. Formulário "Novo perfil" com `action="/tenants/{id}/acesso/perfis"`, `@Html.AntiForgeryToken()`, campo `nome` com `maxlength="100"`.
- Usuários: busca `GET` para `/tenants/{id}/acesso` com `aba=usuarios`; lista como no `/acesso`, com o link `/tenants/{id}/acesso/usuarios/@usuario.Id`, mostrando `usuario.Nome` quando houver, antes do e-mail; paginação apontando para a mesma rota.
- Federação: o mesmo painel do `/acesso`, com `action="/tenants/{id}/acesso/federacao"`. Quando `federacao.DirectoryId is not null`, mostrar o atual (`<p class="sc-meta">Diretório atual: <code>@federacao.DirectoryId</code></p>`) e, abaixo do campo, a caixa:

```cshtml
<div class="form-check mt-2">
    <input class="form-check-input" type="checkbox" id="confirmarTroca" name="confirmarTroca" value="true" />
    <label class="form-check-label" for="confirmarTroca">Confirmo a troca do diretório: as pessoas passam a entrar pelo Entra ID informado.</label>
</div>
```

Todos os formulários com `@Html.AntiForgeryToken()` (o `action` é string, o tag helper não injeta o token sozinho).

- [ ] **Step 4: `Perfil.cshtml`**

Mesmo desenho de `Views/Acesso/Perfil.cshtml`, com:
- Cabeçalho com o nome do perfil e subtítulo `Perfil de {Model.Tenant.Sistema}`, ação Voltar para `/tenants/{id}/acesso`.
- Permissões: quando `!perfil.Reservado`, formulário para `/tenants/{id}/acesso/perfis/{nome escapado}/permissoes`:

```cshtml
<form method="post" action="/tenants/@Model.Tenant.TenantId/acesso/perfis/@Uri.EscapeDataString(perfil.Nome)/permissoes">
    @Html.AntiForgeryToken()
    <label class="form-label" for="permissoes">Permissões, uma por linha</label>
    <textarea class="form-control font-monospace" id="permissoes" name="permissoes" rows="10" spellcheck="false">@string.Join("\n", perfil.Permissoes)</textarea>
    <div class="form-text">Formato <code>recurso:acao</code>, em minúsculas. O SecureGate ainda não publica o catálogo de cada sistema; confira os nomes com quem mantém o sistema.</div>
    <button class="btn btn-outline-primary mt-2" type="submit">Salvar permissões</button>
</form>
```

- Membros: lista paginada (`?page=`), cada um com o link da pessoa e o botão Retirar (form para `/tenants/{id}/acesso/usuarios/{membro}/perfis/retirar` com `perfil` e `origem=perfil`).
- Adicionar membro: `select` de `Tela.Candidatos` em form para `/tenants/{id}/acesso/perfis/{nome}/membros`.
- Excluir: quando `!perfil.Reservado`, form para `…/excluir` com `data-confirmar`.

- [ ] **Step 5: `Usuario.cshtml`**

Mesmo desenho de `Views/Acesso/Usuario.cshtml`, **sem** o bloco de setores:
- `dl` com Situação, Nome (quando houver), Modo de entrada (`EntraPorSenha ? (TemSenha ? "Senha definida" : "Senha por convite, ainda não definida") : "Só pelo Entra ID"`), Segundo fator, Logins externos.
- Perfis: lista de `Tela.Usuario.Perfis` com Retirar (`origem=usuario`) e `select` de `Tela.PerfisAtribuiveis` para `/tenants/{id}/acesso/usuarios/{id}/perfis`.
- Ações: Desativar ou Reativar, Encerrar sessões e, quando `Tela.Usuario.EntraPorSenha && !Tela.Usuario.TemSenha`, `Reenviar convite` (form para `…/convite`).

- [ ] **Step 6: `Novo.cshtml`**

```cshtml
@model TenantsAcessoNovoViewModel
@{
    var tenant = Model.Tenant!;
    ViewData["Title"] = $"Cadastrar pessoa — {tenant.Sistema}";
    var cabecalho = new PageHeaderModel(
        "Cadastrar pessoa",
        $"Em {tenant.Sistema}. A pessoa precisa existir aqui antes do primeiro login, inclusive pelo Entra ID.",
        Acoes: new[] { new PageActionModel("Voltar", $"/tenants/{tenant.TenantId}/acesso?aba=usuarios", "bi-arrow-left") });
}

<partial name="_PageHeader" model="cabecalho" />

@if (Model.Erro is { } erro)
{
    <div class="alert alert-danger" role="alert">@erro</div>
}

<form method="post" action="/tenants/@tenant.TenantId/acesso/usuarios/novo" class="sc-panel sc-form" style="max-width: 40rem">
    @Html.AntiForgeryToken()
    <div class="sc-form__field">
        <label class="form-label" for="Email">E-mail</label>
        <input class="form-control" id="Email" name="Email" type="email" value="@Model.Email" maxlength="256" required />
    </div>
    <div class="sc-form__field">
        <label class="form-label" for="Nome">Nome de exibição (opcional)</label>
        <input class="form-control" id="Nome" name="Nome" value="@Model.Nome" maxlength="160" />
    </div>

    <fieldset class="sc-form__field">
        <legend class="form-label">Perfis iniciais</legend>
        @if (Model.PerfisDisponiveis.Count == 0)
        {
            <p class="text-muted mb-0">Nenhum perfil criado ainda; dá para atribuir depois.</p>
        }
        @foreach (var perfil in Model.PerfisDisponiveis)
        {
            <div class="form-check">
                <input class="form-check-input" type="checkbox" name="Perfis" value="@perfil" id="perfil-@perfil" checked="@Model.Perfis.Contains(perfil)" />
                <label class="form-check-label" for="perfil-@perfil">@perfil</label>
            </div>
        }
    </fieldset>

    <fieldset class="sc-form__field">
        <legend class="form-label">Como a pessoa entra</legend>
        <div class="form-check">
            <input class="form-check-input" type="radio" name="EntraPorSenha" id="porSenha" value="true" checked="@Model.EntraPorSenha" />
            <label class="form-check-label" for="porSenha">Senha própria — o SecureGate envia um convite válido por 72 horas</label>
        </div>
        @if (Model.FederacaoHabilitada)
        {
            <div class="form-check">
                <input class="form-check-input" type="radio" name="EntraPorSenha" id="soEntra" value="false" checked="@(!Model.EntraPorSenha)" />
                <label class="form-check-label" for="soEntra">Só pelo Entra ID da empresa — sem senha e sem convite</label>
            </div>
        }
    </fieldset>

    <button class="btn btn-primary" type="submit">Cadastrar</button>
</form>
```

- [ ] **Step 7: Link na página do tenant**

Em `Views/Tenants/Detalhe.cshtml`, na célula de ação da linha do recurso (`<td class="text-end">`), acrescentar antes do `if (r.Situacao == SituacaoDoRecurso.NaoLigado)`:

```cshtml
@if (r.Recurso == RecursoDaPlataforma.SecureGate && r.Situacao == SituacaoDoRecurso.Ligado)
{
    <a class="btn btn-outline-primary btn-sm" href="/tenants/@Model.TenantId/acesso">Perfis e usuários</a>
}
```

(`RecursoDaPlataforma.SecureGate` é o membro do enum em `RecursosDaPlataforma.cs`.)

- [ ] **Step 8: Rodar telas e a suíte inteira**

Run: `dotnet build Secco.Intranet.slnx -warnaserror` e `dotnet test tests/Secco.Intranet.Tests`
Expected: build limpo; todos os testes PASS (as fumaças opt-in aparecem como ignoradas).

- [ ] **Step 9: Conferir no navegador, nos dois temas**

Rodar a aplicação em Development (F5 ou `dotnet run --project src/Secco.Intranet.Web`) e abrir `/tenants/{id}/acesso`, a página de um perfil, a de uma pessoa e o cadastro, nos temas Vertical e Horizontal, em claro e escuro. Sem SecureGate configurado, a área mostra a tela de "não configurado". Para ver as telas preenchidas, usar a fumaça manual da Task 8.

- [ ] **Step 10: Commit**

```bash
git add src tests
git commit -m "feat(tenants): telas de perfis e pessoas do tenant administrado"
```

---

### Task 8: Fumaça contra o SecureGate real e documentação

**Files:**
- Create: `tests/Secco.Intranet.Tests/Smoke/SecureGateGestaoDeAcessoDoTenantFumacaTests.cs`
- Modify: `docs/roadmap.md`
- Modify: `docs/roteiro-fumaca-securegate.md`

**Interfaces:**
- Consumes: `SecureGateGestaoDeAcessoDoTenant` (Task 2), `SecureGateGestaoDeTenants` (existente), `FumacaFactAttribute`.

- [ ] **Step 1: Escrever a fumaça**

```csharp
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.Intranet.Infrastructure.Tenants;
using Secco.SecureGate.Client;
using Secco.SecureGate.Client.Administration;
using Xunit;

namespace Secco.Intranet.Tests.Smoke;

/// <summary>
/// Acesso de tenant administrado contra um SecureGate <b>de verdade</b>. Prova, além do contrato, que
/// a plataforma recusa atribuir perfil a um usuário de outro tenant — a Intranet confia nisso.
/// Deixa um tenant desativado por execução.
/// </summary>
public class SecureGateGestaoDeAcessoDoTenantFumacaTests
{
	private static ServiceProvider Provider()
	{
		var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Secco:SecureGate:BaseUrl"] = Environment.GetEnvironmentVariable(FumacaFactAttribute.VariavelDaUrl),
			["Secco:SecureGate:ClientId"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_ID") ?? "secco-dev-console",
			["Secco:SecureGate:ClientSecret"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_SECRET")
				?? "secco-dev-console-secret-32-chars-min!",
		}).Build();

		return new ServiceCollection()
			.AddSingleton<IConfiguration>(configuracao)
			.AddLogging()
			.AddSecureGateAdminClient()
			.BuildServiceProvider();
	}

	[FumacaFact]
	public async Task PerfisPessoasEIsolamento_ContraOContratoReal()
	{
		using var provider = Provider();
		var client = provider.GetRequiredService<ISecureGateClient>();
		var tenants = new SecureGateGestaoDeTenants(client, NullLogger<SecureGateGestaoDeTenants>.Instance);
		var acesso = new SecureGateGestaoDeAcessoDoTenant(client, NullLogger<SecureGateGestaoDeAcessoDoTenant>.Instance);

		var sufixo = Guid.NewGuid().ToString("N")[..10];
		var alvo = (await tenants.CriarTenantAsync($"Fumaça acesso {sufixo}", $"fa-{sufixo}")).Value.Id;
		var outro = (await tenants.CriarTenantAsync($"Fumaça outro {sufixo}", $"fo-{sufixo}")).Value.Id;

		try
		{
			(await acesso.CriarPerfilAsync(alvo, "compras.aprovador")).IsSuccess.Should().BeTrue();
			(await acesso.DefinirPermissoesDoPerfilAsync(alvo, "compras.aprovador", ["pedido:aprovar"])).IsSuccess.Should().BeTrue();
			(await acesso.ObterPerfilAsync(alvo, "compras.aprovador")).Value.Permissoes.Should().Equal("pedido:aprovar");

			var porSenha = await acesso.CadastrarUsuarioAsync(alvo, new NovoUsuarioDoTenant($"senha-{sufixo}@fumaca.invalid", "Senha", ["compras.aprovador"], true));
			porSenha.IsSuccess.Should().BeTrue(porSenha.IsFailure ? porSenha.Error.Description : null);
			(await acesso.ObterUsuarioAsync(alvo, porSenha.Value)).Value.Should().Match<UsuarioDoTenantDetalheDto>(u => !u.TemSenha && u.EntraPorSenha);

			var repetido = await acesso.CadastrarUsuarioAsync(alvo, new NovoUsuarioDoTenant($"senha-{sufixo}@fumaca.invalid", null, [], true));
			repetido.Error.Code.Should().Be("Intranet.Acesso.EmailJaCadastrado");

			var deOutroTenant = await acesso.CadastrarUsuarioAsync(outro, new NovoUsuarioDoTenant($"outro-{sufixo}@fumaca.invalid", null, [], true));
			(await acesso.AtribuirPerfilAsync(alvo, deOutroTenant.Value, "compras.aprovador"))
				.Error.Code.Should().Be("Intranet.Acesso.UsuarioNaoEncontrado", "a plataforma recusa usuário de outro tenant");

			(await acesso.RetirarPerfilAsync(alvo, porSenha.Value, "compras.aprovador")).IsSuccess.Should().BeTrue();
			(await acesso.DesativarUsuarioAsync(alvo, porSenha.Value)).IsSuccess.Should().BeTrue();
		}
		finally
		{
			await tenants.DesativarAsync(alvo);
			await tenants.DesativarAsync(outro);
		}
	}
}
```

O cadastro "só pelo Entra" exige federação habilitada **e** a app registration configurada no SecureGate; não entra na fumaça automática. Fica no roteiro manual do Step 3.

- [ ] **Step 2: Rodar a fumaça (opt-in)**

Run (com um SecureGate local de pé, ver `docs/roteiro-fumaca-securegate.md`): `SECCO_SMOKE_SECUREGATE_URL=http://localhost:4001 dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~SecureGateGestaoDeAcessoDoTenantFumacaTests"`
Expected: PASS. Sem a variável, o teste aparece como ignorado. Se não houver SecureGate disponível, registrar no relatório da tarefa que a fumaça não foi rodada.

- [ ] **Step 3: Documentação**

- `docs/roadmap.md`, no item `2. Perfis e usuários do tenant administrado, reaproveitando /acesso`: trocar por `[x] 2. Perfis, permissões (texto livre até a ADR-0039 ser publicada), pessoas por convite ou só pelo Entra, e federação do tenant administrado — [spec](specs/2026-10-11-area-administrativa-tenants-acesso-design.md), [plano](plans/2026-10-11-area-administrativa-tenants-acesso.md)`, e acrescentar abaixo do item 4 um item novo `- [ ] Permissões dos perfis administrados por catálogo (caixas por produto) e publicação do catálogo da própria Intranet no migrate, com o escopo permissions:intranet no client de máquina — quando a ADR-0039 da plataforma for publicada ([secco-platform#33](https://github.com/rafsecco/secco-platform/issues/33))`.
- `docs/roteiro-fumaca-securegate.md`: seção "Acesso de tenant administrado" com o comando do Step 2 e o roteiro manual: criar um tenant pela tela, ligar o SecureGate, abrir "Perfis e usuários", criar um perfil, cadastrar uma pessoa por convite (conferir o e-mail no SMTP de DEV), habilitar a federação e cadastrar uma pessoa "só pelo Entra".

- [ ] **Step 4: Commit**

```bash
git add tests docs
git commit -m "test(tenants): fumaca do acesso do tenant administrado; roadmap e roteiro"
```
