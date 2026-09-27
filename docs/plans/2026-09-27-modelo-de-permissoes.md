# Modelo de permissões — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trocar autorização por nome de Role por autorização por permissão (`recurso:acao`,
ADR-0021 da plataforma) nos pontos que precisam disso hoje: Mural, Documentos, a página do
próprio setor, o menu, o Diretório e uma nova entrada de leitura no Inventário — sem depender de
nenhum desenvolvimento novo da secco-platform.

**Architecture:** Reaproveita o mecanismo já publicado em `Secco.SDK.AspNetCore` 0.8.3
(`AddSeccoAuthorization()`, `IPermissionResolver`, policies dinâmicas por `recurso:acao`) e em
`Secco.SecureGate.Client` 0.11.0 (`GetRolePermissionsAsync`/`SetRolePermissionsAsync`,
`AddSecureGatePermissionResolver()`). Um atributo próprio (`ExigePermissaoAttribute`) cobre gates de
rota inteira; um serviço novo (`IPermissoesDeSetor`) cobre filtragem por setor. `intranet-admin`
continua bypass por identidade, fora do mecanismo genérico.

**Tech Stack:** .NET 10, ASP.NET Core MVC, `Secco.SDK.AspNetCore`, `Secco.SecureGate.Client`,
`Secco.SharedKernel.Authorization` (todos já referenciados pelo projeto).

**Spec:** [docs/specs/2026-09-27-modelo-de-permissoes-design.md](../specs/2026-09-27-modelo-de-permissoes-design.md)

## Global Constraints

- Formato de permissão: **um único `:`**, kebab-case minúsculo dos dois lados
  (`Secco.SharedKernel.Authorization.SeccoPermissions`). Nunca inventar um formato próprio —
  compor sempre com `SeccoPermissions.Create(recurso, acao)`.
- Ações em **inglês** (`read`, `write`, `manage`) — decisão confirmada pelo dono do produto
  (2026-09-27), para bater com os nomes já prometidos nas specs do Diretório (`diretorio:read`,
  `diretorio:manage`) e da Área administrativa (`inventario:read`). Nomes de recurso continuam em
  português (`setor-{slug}`, `diretorio`, `inventario`).
- `intranet-admin` **nunca** é resolvido por permissão — é bypass por identidade
  (`AcessoAdministrativo.SomenteIntranetAdmin`), em todo novo gate. Não ganha as permissões gravadas.
- Concessão automática de permissão (Setor, Diretório) é sempre **mesclagem**: ler o que a Role já
  tem, unir com o mínimo exigido, gravar a união. Nunca substitui a lista inteira.
- `ModoAbertoDeDev` continua sendo checado **antes** de qualquer chamada de autorização — sem isso,
  DEV local sem SecureGate quebra.
- Tabelas de indentação: `\t` em `.cs`, 4 espaços em `.cshtml` (`.editorconfig` do repo).
- Build com **0 avisos**; suíte completa verde antes de cada commit que a spec não isente
  explicitamente.

## Review Focus

- **Setor criado antes desta mudança, nunca editado depois:** sem rodar "Reconciliar permissões",
  `{slug}-admin`/`{slug}-user` continuam sem nenhuma permissão gravada — a pessoa que já enxergava o
  setor pela Role passa a ficar bloqueada. A Task 8 precisa de um teste que prove isso (setor
  "antigo" sem permissão, reconciliação resolve, sem reconciliação continua bloqueado) — não basta
  testar setor criado depois da mudança.
- **`intranet-admin` sem nenhuma permissão gravada em lugar nenhum:** todo gate novo (Task 3, 9, 11,
  12) precisa continuar liberando esse usuário mesmo que o resolvedor de permissão devolva conjunto
  vazio — é fácil escrever um teste que só cobre "tem a permissão" e esquecer do bypass.
- **Perfil comum (ex.: `todos`) com a permissão global `setores:read` mas nenhuma `setor-{slug}:read`
  específica:** precisa enxergar **todo** setor do tenant, inclusive um criado depois da atribuição
  — é o cenário de aceite da spec, e é fácil implementar só o caminho "tem a permissão específica" e
  esquecer do OR com a global.
- **Resolução de permissão indisponível (SecureGate fora do ar) durante uma requisição real:** o
  mecanismo da plataforma é fail-closed por padrão, mas a Task 3 precisa de um teste que prove que
  isso realmente nega (403), e não que uma exceção não tratada vire 500 — 500 não é a mesma coisa
  que "acesso negado" para quem está de fora tentando entrar sem permissão.
- **Editar permissões de um perfil com uma string fora do catálogo** (a tela deveria oferecer só
  lista, mas um `POST` forjado pode mandar qualquer texto): a Task 5 precisa rejeitar, não gravar
  uma permissão desconhecida no SecureGate — senão o catálogo "fixo" da spec vira decoração.

---

## Task 1: Catálogo de permissões

**Files:**
- Create: `src/Secco.Intranet.Application/Acesso/IntranetPermissoes.cs`
- Test: `tests/Secco.Intranet.Tests/Acesso/IntranetPermissoesTests.cs`

**Interfaces:**
- Produces: `IntranetPermissoes.Setor.Read(string slug)`, `IntranetPermissoes.Setor.Write(string slug)`,
  `IntranetPermissoes.Setor.ReadGlobal` (const), `IntranetPermissoes.Diretorio.Read` (const),
  `IntranetPermissoes.Diretorio.Manage` (const), `IntranetPermissoes.Inventario.Read` (const),
  `IntranetPermissoes.Catalogo` (`IReadOnlyList<string>` — todas as permissões fixas que a tela de
  edição de perfil pode oferecer, sem as por-setor dinâmicas).

- [ ] **Step 1: Escrever o teste que falha**

```csharp
using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Authorization;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Acesso;

public class IntranetPermissoesTests
{
	[Fact]
	public void Setor_Read_ComponhaNoFormatoCanonico()
	{
		var permissao = IntranetPermissoes.Setor.Read("recursos-humanos");

		permissao.Should().Be("setor-recursos-humanos:read");
		SeccoPermissions.IsValid(permissao).Should().BeTrue();
	}

	[Fact]
	public void Setor_Write_ComponhaNoFormatoCanonico()
	{
		var permissao = IntranetPermissoes.Setor.Write("ti");

		permissao.Should().Be("setor-ti:write");
		SeccoPermissions.IsValid(permissao).Should().BeTrue();
	}

	[Theory]
	[InlineData(IntranetPermissoes.Diretorio.Read)]
	[InlineData(IntranetPermissoes.Diretorio.Manage)]
	[InlineData(IntranetPermissoes.Inventario.Read)]
	[InlineData(IntranetPermissoes.Setor.ReadGlobal)]
	public void PermissoesFixas_EstaoNoFormatoCanonico(string permissao)
	{
		SeccoPermissions.IsValid(permissao).Should().BeTrue();
	}

	[Fact]
	public void Diretorio_NomesBatemComOPrometidoNaSpec()
	{
		IntranetPermissoes.Diretorio.Read.Should().Be("diretorio:read");
		IntranetPermissoes.Diretorio.Manage.Should().Be("diretorio:manage");
	}

	[Fact]
	public void Catalogo_NaoInclueAsPorSetor_SaoDinamicasPorSlug()
	{
		IntranetPermissoes.Catalogo.Should().NotContain(p => p.StartsWith("setor-", StringComparison.Ordinal));
		IntranetPermissoes.Catalogo.Should().Contain(IntranetPermissoes.Diretorio.Read);
		IntranetPermissoes.Catalogo.Should().Contain(IntranetPermissoes.Setor.ReadGlobal);
	}
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter IntranetPermissoesTests`
Expected: FAIL (compilação — `IntranetPermissoes` não existe)

- [ ] **Step 3: Implementar**

```csharp
using Secco.SharedKernel.Authorization;

namespace Secco.Intranet.Application.Acesso;

/// <summary>
/// Catálogo de permissões do produto, no formato canônico da plataforma (`recurso:acao`,
/// ADR-0021). Ações em inglês (`read`/`write`/`manage`) — decisão de 2026-09-27, para bater com os
/// nomes já prometidos nas specs do Diretório e da Área administrativa. Fixo: a tela de edição de
/// perfil oferece lista, nunca texto livre (ver <see cref="Catalogo"/>).
/// </summary>
public static class IntranetPermissoes
{
	/// <summary>Permissões de setor — a por-slug é dinâmica, a global cobre "todos, inclusive futuros".</summary>
	public static class Setor
	{
		/// <summary>Permissão de leitura de um setor específico.</summary>
		/// <param name="slug">Slug do setor, já normalizado (minúsculo).</param>
		public static string Read(string slug) => SeccoPermissions.Create($"setor-{slug}", "read");

		/// <summary>Permissão de escrita de um setor específico.</summary>
		/// <param name="slug">Slug do setor, já normalizado (minúsculo).</param>
		public static string Write(string slug) => SeccoPermissions.Create($"setor-{slug}", "write");

		/// <summary>Leitura de todo setor do tenant, inclusive os criados depois da atribuição.</summary>
		public const string ReadGlobal = "setores:read";
	}

	/// <summary>Permissões do Diretório organizacional — nomes já fixados na spec de 2026-09-24.</summary>
	public static class Diretorio
	{
		/// <summary>Ver o diretório e editar o próprio contato.</summary>
		public const string Read = "diretorio:read";

		/// <summary>Tudo, inclusive dados funcionais de terceiros e importação.</summary>
		public const string Manage = "diretorio:manage";
	}

	/// <summary>Permissões do Inventário — administrar continua por nome de Role (`inventario-admin`).</summary>
	public static class Inventario
	{
		/// <summary>Nível de só-consulta, além do administrativo.</summary>
		public const string Read = "inventario:read";
	}

	/// <summary>
	/// Permissões fixas oferecidas pela tela de edição de perfil (Task 5). Não inclui
	/// <see cref="Setor.Read(string)"/>/<see cref="Setor.Write(string)"/> — essas são geradas por
	/// setor existente, não uma lista fixa.
	/// </summary>
	public static readonly IReadOnlyList<string> Catalogo =
	[
		Setor.ReadGlobal,
		Diretorio.Read,
		Diretorio.Manage,
		Inventario.Read,
	];
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter IntranetPermissoesTests`
Expected: PASS (6 testes)

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Application/Acesso/IntranetPermissoes.cs tests/Secco.Intranet.Tests/Acesso/IntranetPermissoesTests.cs
git commit -m "feat(acesso): catalogo de permissoes recurso:acao do produto"
```

---

## Task 2: Composição da autorização por permissão

**Files:**
- Modify: `src/Secco.Intranet.Web/Program.cs`
- Modify: `src/Secco.Intranet.Web/Authentication/IntranetAuthenticationExtensions.cs`
- Create: `tests/Secco.Intranet.Tests/Support/PermissionResolverDeTeste.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/AutorizacaoPorPermissaoTests.cs`

**Interfaces:**
- Consumes: `Secco.SDK.AspNetCore.Extensions.SeccoAuthorizationServiceCollectionExtensions.AddSeccoAuthorization()`,
  `Secco.SDK.AspNetCore.Authorization.IPermissionResolver` (`ValueTask<IReadOnlySet<string>> ResolveAsync(Guid tenantId, string role, CancellationToken ct)`),
  `Secco.SecureGate.Client.Authorization.SecureGatePermissionResolverExtensions.AddSecureGatePermissionResolver()`.
- Produces: `IntranetWebFactory.ResolvedorDePermissoes` (propriedade `IPermissionResolver?`, mesmo
  molde de `GestaoDeAcesso`/`UsuariosDoDiretorio`) — as próximas tasks configuram permissão por Role
  através dela.

`AddSeccoAuthorization()` precisa ser registrado **incondicionalmente** (não só quando o SecureGate
está configurado): o ambiente `Testing` não configura `Secco:SecureGate:Authority` mas precisa da
autorização por permissão rodando de verdade — é assim que a `RolesDeTesteMiddleware` (claims falsas,
autorização real) já funciona para os gates por nome de Role hoje. Sem SecureGate real,
`AddSeccoAuthorization()` já registra `ConfigurationPermissionResolver` sozinho (`TryAddSingleton`);
o teste substitui isso pelo dublê.

- [ ] **Step 1: Escrever o teste que falha**

Um controller mínimo só para este teste não é necessário — a Task 3 (`ExigePermissaoAttribute`) é
quem primeiro consome isso de verdade. Este teste prova a composição sozinha, direto no
`IAuthorizationService`, através de um endpoint que já existe e não depende de mais nada: usamos
o próprio host para resolver o serviço em um teste de unidade fininho, sem subir requisição HTTP.

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Tests.Support;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

[Collection(nameof(IntranetWebCollection))]
public class AutorizacaoPorPermissaoTests(IntranetWebFactory factory)
{
	[Fact]
	public async Task PolicyDinamica_ConcedeQuandoOResolvedorDevolveAPermissao()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao("diretorio-user", "diretorio:read");

		using var scope = factory.Services.CreateScope();
		var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
		var usuario = PrincipalDeTeste.ComRoles("diretorio-user");

		var resultado = await authorizationService.AuthorizeAsync(usuario, "diretorio:read");

		resultado.Succeeded.Should().BeTrue();

		factory.ResolvedorDePermissoes = null;
	}

	[Fact]
	public async Task PolicyDinamica_NegaQuandoNenhumaRoleTemAPermissao()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao("diretorio-user", "diretorio:read");

		using var scope = factory.Services.CreateScope();
		var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
		var usuario = PrincipalDeTeste.ComRoles("diretorio-user");

		var resultado = await authorizationService.AuthorizeAsync(usuario, "diretorio:manage");

		resultado.Succeeded.Should().BeFalse();

		factory.ResolvedorDePermissoes = null;
	}
}
```

`PrincipalDeTeste.ComRoles(...)` provavelmente já existe (é o mesmo tipo de claim que
`RolesDeTesteMiddleware` monta); se o teste apontar um nome diferente do que já existe no projeto de
testes, ajuste para o helper real de montar `ClaimsPrincipal` com role e `tenant_id` — confira em
`tests/Secco.Intranet.Tests/Support/` antes de criar um novo. O tenant usado pelo principal precisa
ser um dos tenants da fábrica (`factory.TenantAlfa`), porque `PermissionResolverDeTeste` (Step 3)
ignora tenant e só chaveia por Role — qualquer tenant serve, mas o principal precisa ter a claim
`tenant_id` para o `ITenantContext` resolver (sem isso o handler nega por "sem tenant", fail-closed).

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter AutorizacaoPorPermissaoTests`
Expected: FAIL (compilação — `ResolvedorDePermissoes` e `PermissionResolverDeTeste` não existem)

- [ ] **Step 3: Implementar o dublê de resolvedor**

```csharp
using Secco.SDK.AspNetCore.Authorization;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// Dublê de <see cref="IPermissionResolver"/>: ignora tenant (os testes deste produto não
/// precisam distinguir tenant para permissão) e resolve só pelo nome da Role, configurado à mão
/// pelo teste. Sem nenhuma permissão configurada para a Role, devolve vazio — nunca lança.
/// </summary>
public sealed class PermissionResolverDeTeste : IPermissionResolver
{
	private readonly Dictionary<string, HashSet<string>> _permissoesPorRole = new(StringComparer.Ordinal);

	/// <summary>Registra uma permissão para uma Role. Encadeável.</summary>
	public PermissionResolverDeTeste ComPermissao(string role, string permissao)
	{
		if (!_permissoesPorRole.TryGetValue(role, out var permissoes))
		{
			permissoes = new HashSet<string>(StringComparer.Ordinal);
			_permissoesPorRole[role] = permissoes;
		}

		permissoes.Add(permissao);

		return this;
	}

	/// <inheritdoc />
	public ValueTask<IReadOnlySet<string>> ResolveAsync(Guid tenantId, string role, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlySet<string>>(
			_permissoesPorRole.TryGetValue(role, out var permissoes) ? permissoes : new HashSet<string>());
}
```

- [ ] **Step 4: Ligar o hook na fábrica de testes**

Em `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs`, ao lado de `GestaoDeAcesso`:

```csharp
/// <summary>
/// Resolvedor de permissão que o host devolve. Nulo, vale o <c>ConfigurationPermissionResolver</c>
/// padrão do SDK (sem nenhuma permissão configurada em <c>Secco:Authorization</c> — nega tudo,
/// menos <c>intranet-admin</c>, que é bypass por identidade). Os testes de permissão atribuem um
/// dublê aqui.
/// </summary>
public IPermissionResolver? ResolvedorDePermissoes { get; set; }
```

E em `ConfigureTestServices`:

```csharp
services.AddSingleton<IPermissionResolver>(_ => ResolvedorDePermissoes ?? new PermissionResolverDeTeste());
```

(`using Secco.SDK.AspNetCore.Authorization;` e `using Secco.Intranet.Tests.Support;` no topo do arquivo.)

- [ ] **Step 5: Registrar `AddSeccoAuthorization()` e `AddSecureGatePermissionResolver()`**

Em `src/Secco.Intranet.Web/Program.cs`, logo abaixo de `builder.Services.AddIntranetAuthentication(...)`:

```csharp
// ADR-0021: autorização por permissão, incondicional — o ambiente Testing não configura
// SecureGate, mas precisa da policy dinâmica rodando de verdade (mesmo padrão dos gates por nome
// de Role de hoje, com claims falsas e autorização real). Sem SecureGate real, o próprio
// AddSeccoAuthorization() já registra um resolvedor por configuração (nega tudo por padrão).
builder.Services.AddSeccoAuthorization();
```

Em `src/Secco.Intranet.Web/Authentication/IntranetAuthenticationExtensions.cs`, dentro de
`AddIntranetAuthentication`, depois de `services.AddSecureGateSessionVersionResolver();`:

```csharp
// Em produção (SecureGate configurado), o resolvedor real substitui o de configuração que
// AddSeccoAuthorization() já registrou por padrão (TryAddSingleton — este Replace vence).
services.AddSecureGatePermissionResolver();
```

(`using Secco.SecureGate.Client.Authorization;` já está no topo do arquivo — é o mesmo namespace de
`SecureGateSessionVersionResolver`.)

- [ ] **Step 6: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter AutorizacaoPorPermissaoTests`
Expected: PASS (2 testes)

- [ ] **Step 7: Build completo com 0 avisos e suíte inteira**

Run: `dotnet build` (0 avisos) e `dotnet test tests/Secco.Intranet.Tests` (verde, fumaças puladas)

- [ ] **Step 8: Commit**

```bash
git add src/Secco.Intranet.Web/Program.cs src/Secco.Intranet.Web/Authentication/IntranetAuthenticationExtensions.cs tests/Secco.Intranet.Tests/Support/PermissionResolverDeTeste.cs tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs tests/Secco.Intranet.Tests/Integration/AutorizacaoPorPermissaoTests.cs
git commit -m "feat(acesso): liga AddSeccoAuthorization/AddSecureGatePermissionResolver e o dublê de teste"
```

---

## Task 3: `ExigePermissaoAttribute` — gate de rota inteira

**Files:**
- Create: `src/Secco.Intranet.Web/Authentication/ExigePermissaoAttribute.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/ExigePermissaoAttributeTests.cs`
- Create (para o teste): um controller de apoio só para este teste,
  `tests/Secco.Intranet.Tests/Integration/TestSupport/PermissaoDeTesteController.cs` — **não** entra
  no produto; é montado pela própria fábrica de testes (o projeto de testes referencia
  `Secco.Intranet.Web`, então um controller adicional nele é descoberto pelo `AddControllersWithViews()`
  do host desde que o assembly do projeto de testes seja adicionado como `ApplicationPart` — confira
  se a fábrica já faz isso para os controllers de teste do Diretório/Acesso; se sim, siga o mesmo
  padrão em vez de duplicar a configuração)

**Interfaces:**
- Consumes: `IAuthorizationService.AuthorizeAsync(ClaimsPrincipal, string policyName)` (via DI, Task 2),
  `AcessoAdministrativo.SomenteIntranetAdmin`, `AcessoAdministrativo.ModoAbertoDeDev`.
- Produces: `ExigePermissaoAttribute(string permissao)` — atributo de classe ou de action.

- [ ] **Step 1: Escrever o teste que falha**

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Tests.Support;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

[Collection(nameof(IntranetWebCollection))]
public class ExigePermissaoAttributeTests(IntranetWebFactory factory)
{
	[Fact]
	public async Task SemAPermissao_Devolve403()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste();
		using var client = factory.CriarClienteComRoles("qualquer-role");

		var resposta = await client.GetAsync("/teste-permissao/diretorio-read");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task ComAPermissao_Devolve200()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao("diretorio-user", "diretorio:read");
		using var client = factory.CriarClienteComRoles("diretorio-user");

		var resposta = await client.GetAsync("/teste-permissao/diretorio-read");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task IntranetAdmin_SempreLiberado_MesmoSemAPermissaoGravada()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste();
		using var client = factory.CriarClienteComRoles("intranet-admin");

		var resposta = await client.GetAsync("/teste-permissao/diretorio-read");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task ResolvedorIndisponivel_Nega_NaoQuebraCom500()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverQueLanca();
		using var client = factory.CriarClienteComRoles("diretorio-user");

		var resposta = await client.GetAsync("/teste-permissao/diretorio-read");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}
}
```

Ajuste `factory.CriarClienteComRoles(...)` para o helper real já usado pelos testes de
`SomenteIntranetAdminAttribute`/`ExigeNivelNoDiretorioAttribute` (o cabeçalho `X-Test-Roles` da
`RolesDeTesteMiddleware`) — não crie um novo mecanismo de client de teste. `PermissionResolverQueLanca`
é um segundo dublê pequeno, no mesmo arquivo do Step anterior, cujo `ResolveAsync` lança
`InvalidOperationException` — prova que "resolução indisponível" vira 403, não 500.

O controller de apoio:

```csharp
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Web.Authentication;

namespace Secco.Intranet.Tests.Integration.TestSupport;

[Route("teste-permissao")]
public sealed class PermissaoDeTesteController : Controller
{
	[HttpGet("diretorio-read")]
	[ExigePermissao(IntranetPermissoes.Diretorio.Read)]
	public IActionResult DiretorioRead() => Ok();
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ExigePermissaoAttributeTests`
Expected: FAIL (compilação — `ExigePermissaoAttribute` não existe)

- [ ] **Step 3: Implementar**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Secco.Intranet.Web.Navigation;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Restringe uma rota a quem tem a permissão informada (ADR-0021), com <c>intranet-admin</c>
/// sempre liberado (bypass por identidade, ADR-0008 — nunca precisa ganhar a permissão gravada).
/// Mesmo desenho de <see cref="SomenteIntranetAdminAttribute"/>: filtro declarativo,
/// <see cref="Order"/> antes do antifalsificação, checa o modo aberto de DEV primeiro.
/// </summary>
/// <param name="permissao">Permissão no formato canônico <c>recurso:acao</c>.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ExigePermissaoAttribute(string permissao) : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
	/// <inheritdoc />
	public int Order => int.MinValue;

	/// <inheritdoc />
	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var servicos = context.HttpContext.RequestServices;
		var ambiente = (IWebHostEnvironment)servicos.GetService(typeof(IWebHostEnvironment))!;
		var configuracao = (IConfiguration)servicos.GetService(typeof(IConfiguration))!;

		if (AcessoAdministrativo.ModoAbertoDeDev(ambiente, configuracao)
			|| AcessoAdministrativo.SomenteIntranetAdmin(context.HttpContext.User))
		{
			return;
		}

		var authorizationService = (IAuthorizationService)servicos.GetService(typeof(IAuthorizationService))!;
		var resultado = await authorizationService.AuthorizeAsync(context.HttpContext.User, permissao);

		if (!resultado.Succeeded)
		{
			context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
		}
	}
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ExigePermissaoAttributeTests`
Expected: PASS (4 testes)

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Web/Authentication/ExigePermissaoAttribute.cs tests/Secco.Intranet.Tests/Integration/ExigePermissaoAttributeTests.cs tests/Secco.Intranet.Tests/Integration/TestSupport/PermissaoDeTesteController.cs
git commit -m "feat(acesso): ExigePermissaoAttribute, gate de rota por permissao"
```

---

## Task 4: `IGestaoDeAcesso` ganha `GarantirPermissoesAsync`/`DefinirPermissoesDoPerfilAsync`

**Files:**
- Modify: `src/Secco.Intranet.Application/Acesso/IGestaoDeAcesso.cs`
- Create: `src/Secco.Intranet.Infrastructure/Access/PermissoesDoPerfil.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcesso.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Access/GestaoDeAcessoIndisponivel.cs`
- Modify: os dublês de `IGestaoDeAcesso` em `tests/Secco.Intranet.Tests/Support/` (mesmo arquivo que
  já implementa os outros métodos da interface — adicione os dois novos lá, sem criar um dublê
  paralelo)
- Test: `tests/Secco.Intranet.Tests/Infrastructure/PermissoesDoPerfilTests.cs`

**Interfaces:**
- Produces:
  `Task<Result> GarantirPermissoesAsync(string nome, IReadOnlyCollection<string> permissoesMinimas, CancellationToken ct = default)`
  (mescla — nunca remove),
  `Task<Result> DefinirPermissoesDoPerfilAsync(string nome, IReadOnlyCollection<string> permissoes, CancellationToken ct = default)`
  (substitui — usado só pela tela de edição manual, Task 5).
- Consumes (do client já existente): `ISecureGateClient.GetRolePermissionsAsync(Guid tenantId, string role, CancellationToken ct)`
  → `ICollection<string>`; `ISecureGateClient.SetRolePermissionsAsync(Guid tenantId, string role, SetRolePermissionsRequest body, CancellationToken ct)`.

`PermissoesDoPerfil` é um helper interno de `Secco.Intranet.Infrastructure` (não implementa nenhuma
porta) — existe só para `SecureGateGestaoDeAcesso` **e** `SecureGateSetorAccessProvisioner` (Task 6)
chamarem a mesma lógica de mesclagem sem duplicar código, cada um com o próprio tratamento de erro.

- [ ] **Step 1: Escrever o teste que falha (a mesclagem, isolada do client real)**

```csharp
using System.Net;
using Secco.Intranet.Infrastructure.Access;
using Secco.SecureGate.Client;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Infrastructure;

public class PermissoesDoPerfilTests
{
	[Fact]
	public async Task GarantirAsync_UneComOQueJaExiste_NuncaRemove()
	{
		var chamadasDeSet = new List<ICollection<string>>();
		var client = new ClientFalso(
			permissoesAtuais: ["extra-que-o-admin-somou:read"],
			aoDefinir: permissoes => chamadasDeSet.Add(permissoes));

		await PermissoesDoPerfil.GarantirAsync(
			client, Guid.NewGuid(), "financeiro-admin", ["setor-financeiro:read", "setor-financeiro:write"], CancellationToken.None);

		chamadasDeSet.Should().HaveCount(1);
		chamadasDeSet[0].Should().BeEquivalentTo(
			["extra-que-o-admin-somou:read", "setor-financeiro:read", "setor-financeiro:write"]);
	}

	[Fact]
	public async Task GarantirAsync_JaTemTudo_NaoChamaSet()
	{
		var chamadasDeSet = new List<ICollection<string>>();
		var client = new ClientFalso(
			permissoesAtuais: ["setor-financeiro:read", "setor-financeiro:write"],
			aoDefinir: permissoes => chamadasDeSet.Add(permissoes));

		await PermissoesDoPerfil.GarantirAsync(
			client, Guid.NewGuid(), "financeiro-admin", ["setor-financeiro:read", "setor-financeiro:write"], CancellationToken.None);

		chamadasDeSet.Should().BeEmpty();
	}
}
```

`ClientFalso` é um `ISecureGateClient` mínimo (implementa só o que o teste chama, os outros métodos
lançam `NotImplementedException`) — se já existir um dublê de `ISecureGateClient` reaproveitável em
`tests/Secco.Intranet.Tests/Support/` (confira antes), estenda-o em vez de criar um novo; senão,
crie este pequeno, só para este arquivo de teste, com `GetRolePermissionsAsync` devolvendo
`permissoesAtuais` e `SetRolePermissionsAsync` chamando `aoDefinir(body.Permissions)`.

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter PermissoesDoPerfilTests`
Expected: FAIL (compilação — `PermissoesDoPerfil` não existe)

- [ ] **Step 3: Implementar o helper**

```csharp
using Secco.SecureGate.Client;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Mesclagem de permissão de uma Role (ADR-0021): lê o que já existe, une com o mínimo exigido,
/// grava só se mudou algo. Nunca remove uma permissão que já estava lá — quem chama isso nunca
/// sabe o estado inteiro da Role, só o que quer garantir. Compartilhado por
/// <see cref="SecureGateGestaoDeAcesso"/> e <see cref="SecureGateSetorAccessProvisioner"/>; cada
/// chamador trata exceção do jeito que já trata as próprias chamadas ao client — este helper não
/// captura nada.
/// </summary>
internal static class PermissoesDoPerfil
{
	/// <summary>Garante que a Role tenha, no mínimo, as permissões informadas.</summary>
	/// <param name="client">Client administrativo do SecureGate.</param>
	/// <param name="tenantId">Tenant da Role.</param>
	/// <param name="role">Nome da Role.</param>
	/// <param name="minimas">Permissões que a Role precisa ter ao final.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task GarantirAsync(
		ISecureGateClient client,
		Guid tenantId,
		string role,
		IReadOnlyCollection<string> minimas,
		CancellationToken cancellationToken)
	{
		var atuais = await client.GetRolePermissionsAsync(tenantId, role, cancellationToken).ConfigureAwait(false);
		var uniao = new HashSet<string>(atuais ?? [], StringComparer.Ordinal);
		var mudou = false;

		foreach (var permissao in minimas)
		{
			if (uniao.Add(permissao))
			{
				mudou = true;
			}
		}

		if (!mudou)
		{
			return;
		}

		await client
			.SetRolePermissionsAsync(tenantId, role, new SetRolePermissionsRequest { Permissions = [.. uniao] }, cancellationToken)
			.ConfigureAwait(false);
	}
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter PermissoesDoPerfilTests`
Expected: PASS (2 testes)

- [ ] **Step 5: Expor na porta e no adapter real**

Em `IGestaoDeAcesso.cs`, adicionar:

```csharp
/// <summary>Garante que um perfil tenha, no mínimo, as permissões informadas — mescla, nunca remove.</summary>
/// <param name="nome">Nome do perfil.</param>
/// <param name="permissoesMinimas">Permissões que o perfil precisa ter ao final.</param>
/// <param name="cancellationToken">Token de cancelamento.</param>
Task<Result> GarantirPermissoesAsync(string nome, IReadOnlyCollection<string> permissoesMinimas, CancellationToken cancellationToken = default);

/// <summary>Define a lista inteira de permissões de um perfil, substituindo a anterior.</summary>
/// <param name="nome">Nome do perfil.</param>
/// <param name="permissoes">Lista final de permissões.</param>
/// <param name="cancellationToken">Token de cancelamento.</param>
Task<Result> DefinirPermissoesDoPerfilAsync(string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default);
```

Em `SecureGateGestaoDeAcesso.cs`:

```csharp
/// <inheritdoc />
public Task<Result> GarantirPermissoesAsync(
	string nome, IReadOnlyCollection<string> permissoesMinimas, CancellationToken cancellationToken = default) =>
	EscreverAsync(
		"garantir permissoes",
		(tenantId, token) => PermissoesDoPerfil.GarantirAsync(client, tenantId, nome, permissoesMinimas, token),
		IntranetErrors.Acesso.PerfilNaoEncontrado,
		null,
		cancellationToken);

/// <inheritdoc />
public Task<Result> DefinirPermissoesDoPerfilAsync(
	string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default) =>
	EscreverAsync(
		"definir permissoes",
		(tenantId, token) => client.SetRolePermissionsAsync(
			tenantId, nome, new SetRolePermissionsRequest { Permissions = [.. permissoes] }, token),
		IntranetErrors.Acesso.PerfilNaoEncontrado,
		null,
		cancellationToken);
```

Em `GestaoDeAcessoIndisponivel.cs`, no molde dos outros métodos:

```csharp
/// <inheritdoc />
public Task<Result> GarantirPermissoesAsync(
	string nome, IReadOnlyCollection<string> permissoesMinimas, CancellationToken cancellationToken = default) =>
	Task.FromResult(Result.Failure(Erro));

/// <inheritdoc />
public Task<Result> DefinirPermissoesDoPerfilAsync(
	string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default) =>
	Task.FromResult(Result.Failure(Erro));
```

E no(s) dublê(s) de `IGestaoDeAcesso` usados pelos testes de tela (`tests/Secco.Intranet.Tests/Support/`):
adicione os dois métodos guardando o que foi chamado em propriedades públicas (mesmo padrão dos
outros métodos do dublê — confira como `CriarPerfilAsync` já é registrado lá antes de replicar).

- [ ] **Step 6: Build completo e suíte inteira**

Run: `dotnet build` (0 avisos), `dotnet test tests/Secco.Intranet.Tests` (verde)

- [ ] **Step 7: Commit**

```bash
git add src/Secco.Intranet.Application/Acesso/IGestaoDeAcesso.cs src/Secco.Intranet.Infrastructure/Access/PermissoesDoPerfil.cs src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcesso.cs src/Secco.Intranet.Infrastructure/Access/GestaoDeAcessoIndisponivel.cs tests/Secco.Intranet.Tests/Support/ tests/Secco.Intranet.Tests/Infrastructure/PermissoesDoPerfilTests.cs
git commit -m "feat(acesso): GarantirPermissoesAsync (mesclagem) e DefinirPermissoesDoPerfilAsync na porta de gestao de acesso"
```

---

## Task 5: Tela de edição de permissões de um perfil

**Files:**
- Create: `src/Secco.Intranet.Application/Acesso/EditarPermissoesDoPerfilHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs` (novo erro `PermissaoInvalida`)
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs` (novo verbo)
- Modify: `src/Secco.Intranet.Application/Acesso/AuditoriaDeAcesso.cs` (novo método `PermissoesAsync`)
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs` (registrar o handler)
- Modify: `src/Secco.Intranet.Web/Controllers/AcessoController.cs` (nova action `EditarPermissoes`)
- Modify: `src/Secco.Intranet.Web/Models/Acesso/*.cs` (ver o ViewModel do detalhe de perfil já
  existente — acrescentar o catálogo marcável, não recriar a tela)
- Modify: a view de detalhe de perfil (`src/Secco.Intranet.Web/Views/Acesso/Perfil.cshtml` ou nome
  equivalente — confira o nome real antes de editar)
- Test: `tests/Secco.Intranet.Tests/Acesso/EditarPermissoesDoPerfilHandlerTests.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/EdicaoDePermissoesTests.cs`

**Interfaces:**
- Consumes: `IGestaoDeAcesso.ObterPerfilAsync` (permissões atuais), `IGestaoDeAcesso.DefinirPermissoesDoPerfilAsync` (Task 4),
  `IntranetPermissoes.Catalogo` (Task 1).
- Produces: `EditarPermissoesDoPerfilHandler.HandleAsync(string perfil, IReadOnlyCollection<string> permissoes, CancellationToken ct)` → `Task<Result>`.

- [ ] **Step 1: Escrever o teste do handler que falha**

```csharp
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Support;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Acesso;

public class EditarPermissoesDoPerfilHandlerTests
{
	[Fact]
	public async Task PermissaoDoCatalogo_Grava()
	{
		var gestao = new GestaoDeAcessoDeTeste();
		var handler = new EditarPermissoesDoPerfilHandler(gestao, new TrilhaDeAuditoriaDeTeste());

		var resultado = await handler.HandleAsync("todos", [IntranetPermissoes.Diretorio.Read, IntranetPermissoes.Setor.ReadGlobal]);

		resultado.IsSuccess.Should().BeTrue();
		gestao.PermissoesDefinidas.Should().ContainKey("todos")
			.WhoseValue.Should().BeEquivalentTo([IntranetPermissoes.Diretorio.Read, IntranetPermissoes.Setor.ReadGlobal]);
	}

	[Fact]
	public async Task PermissaoDeSetorExistente_TambemEAceita()
	{
		// setor-{slug}:read/write não estão no Catalogo fixo (são dinâmicas por setor existente),
		// mas o handler valida pelo FORMATO da plataforma, não por uma lista fechada de string —
		// senão nenhum perfil de setor conseguiria ganhar permissão por esta tela.
		var gestao = new GestaoDeAcessoDeTeste();
		var handler = new EditarPermissoesDoPerfilHandler(gestao, new TrilhaDeAuditoriaDeTeste());

		var resultado = await handler.HandleAsync("financeiro-admin", ["setor-financeiro:read", "setor-financeiro:write"]);

		resultado.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task PermissaoForaDoFormato_Recusa_NaoChamaAGravacao()
	{
		var gestao = new GestaoDeAcessoDeTeste();
		var handler = new EditarPermissoesDoPerfilHandler(gestao, new TrilhaDeAuditoriaDeTeste());

		var resultado = await handler.HandleAsync("todos", ["texto livre qualquer"]);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Acesso.PermissaoInvalida);
		gestao.PermissoesDefinidas.Should().NotContainKey("todos");
	}
}
```

`GestaoDeAcessoDeTeste`/`TrilhaDeAuditoriaDeTeste` são os dublês já usados pelos outros testes de
handler de Acesso (`CriarPerfilHandlerTests` etc.) — acrescente `PermissoesDefinidas` (um
`Dictionary<string, IReadOnlyCollection<string>>`) ao dublê existente em vez de criar um novo tipo;
confira o nome exato do dublê já usado antes de escrever este teste.

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter EditarPermissoesDoPerfilHandlerTests`
Expected: FAIL (compilação)

- [ ] **Step 3: Erro novo**

Em `IntranetErrors.cs`, dentro de `Acesso`:

```csharp
/// <summary>Uma das permissões enviadas não está no formato canônico da plataforma.</summary>
public static readonly Error PermissaoInvalida =
	Error.Validation("Intranet.Acesso.PermissaoInvalida", "Uma das permissões enviadas não é válida.");
```

- [ ] **Step 4: Verbo e método de auditoria**

Em `VerbosDeAuditoria.cs`, ao lado de `AcessoPerfilRetirar`:

```csharp
/// <summary>Permissões de um perfil foram editadas.</summary>
public const string AcessoPermissoesEditar = "acesso.perfil-permissoes-editar";
```

Em `AuditoriaDeAcesso.cs`:

```csharp
/// <summary>Registra a edição de permissões de um perfil — a lista final, não um diff.</summary>
public static Task PermissoesAsync(
	ITrilhaDeAuditoria trilha, string perfil, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken) =>
	trilha.RegistrarAsync(
		new RegistroDeAuditoria(
			VerbosDeAuditoria.AcessoPermissoesEditar, RecursosDeAuditoria.Acesso, perfil,
			JsonSerializer.Serialize(new { perfil, permissoes })),
		cancellationToken);
```

- [ ] **Step 5: Implementar o handler**

```csharp
using Secco.SharedKernel.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Edita as permissões de um perfil (ADR-0021) — substitui a lista inteira.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarPermissoesDoPerfilHandler(IGestaoDeAcesso gestao, Auditoria.ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="permissoes">Lista final de permissões, no formato canônico.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(
		string perfil, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default)
	{
		if (permissoes.Any(permissao => !SeccoPermissions.IsValid(permissao)))
		{
			return Result.Failure(IntranetErrors.Acesso.PermissaoInvalida);
		}

		var definido = await gestao.DefinirPermissoesDoPerfilAsync(perfil, permissoes, cancellationToken).ConfigureAwait(false);

		if (definido.IsFailure)
		{
			return definido;
		}

		await AuditoriaDeAcesso.PermissoesAsync(trilha, perfil, permissoes, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
```

Registrar em `IntranetApplicationExtensions.cs`, junto dos outros handlers de Acesso:
`services.AddScoped<EditarPermissoesDoPerfilHandler>();`

- [ ] **Step 6: Rodar e confirmar que o teste do handler passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter EditarPermissoesDoPerfilHandlerTests`
Expected: PASS (3 testes)

- [ ] **Step 7: Ler a tela de detalhe de perfil existente antes de editar**

Leia `src/Secco.Intranet.Web/Controllers/AcessoController.cs` (a action de detalhe de perfil) e a
view correspondente, e o ViewModel em `src/Secco.Intranet.Web/Models/Acesso/`. A tela já mostra
`PerfilDetalheDto.Permissoes` (só leitura). Acrescente:

- No controller, uma action `[HttpPost] EditarPermissoes(string nome, string[] permissoes)` que
  chama `EditarPermissoesDoPerfilHandler`, injetado no construtor, e redireciona de volta ao
  detalhe do perfil com uma mensagem de sucesso/erro (mesmo padrão de `AtribuirPerfil`/`RetirarPerfil`
  já existentes no mesmo controller — siga a forma como eles tratam `Result.IsFailure`).
- No ViewModel do detalhe de perfil, um campo novo com a união de `IntranetPermissoes.Catalogo` e,
  se o nome do perfil bater com um setor existente (`{slug}-admin`/`{slug}-user` — reaproveite
  `ClassificacaoDePerfil.DoSetor`), as duas permissões daquele setor (`IntranetPermissoes.Setor.Read`/
  `Write` do slug), marcando quais o perfil já tem.
- Na view, uma lista de checkboxes (uma por permissão do catálogo resultante) dentro de um
  `<form method="post" asp-action="EditarPermissoes">`, com `asp-antiforgery` (o padrão do resto da
  tela). Perfis reservados (`PerfilDetalheDto.Reservado`) não mostram o formulário — mesma regra que
  já esconde outras ações para eles.

- [ ] **Step 8: Escrever o teste de integração da tela**

```csharp
using System.Net;
using System.Net.Http.Json;
using Secco.Intranet.Tests.Support;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

[Collection(nameof(IntranetWebCollection))]
public class EdicaoDePermissoesTests(IntranetWebFactory factory)
{
	[Fact]
	public async Task IntranetAdmin_EditaPermissoesDeUmPerfilComum()
	{
		var gestao = new GestaoDeAcessoDeTeste().ComPerfil("todos", permissoes: []);
		factory.GestaoDeAcesso = gestao;
		using var client = factory.CriarClienteComRoles("intranet-admin");

		var resposta = await client.PostAsync(
			"/acesso/perfil/todos/permissoes",
			new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["permissoes[0]"] = IntranetPermissoes.Diretorio.Read,
				["permissoes[1]"] = IntranetPermissoes.Setor.ReadGlobal,
			}));

		resposta.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect, HttpStatusCode.Found);
		gestao.PermissoesDefinidas["todos"].Should().BeEquivalentTo(
			[IntranetPermissoes.Diretorio.Read, IntranetPermissoes.Setor.ReadGlobal]);

		factory.GestaoDeAcesso = null;
	}

	[Fact]
	public async Task SemIntranetAdmin_Devolve403()
	{
		using var client = factory.CriarClienteComRoles("diretorio-admin");

		var resposta = await client.PostAsync(
			"/acesso/perfil/todos/permissoes",
			new FormUrlEncodedContent(new Dictionary<string, string> { ["permissoes[0]"] = IntranetPermissoes.Diretorio.Read }));

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}
}
```

Ajuste a rota/nome de campo do formulário para o que a Step 7 realmente produziu — o teste acima é
o comportamento esperado, não o contrato exato de nomes de campo do MVC model binding (isso depende
de como o formulário foi montado). Rode o teste, veja o binding real e ajuste o teste, não o
contrário.

- [ ] **Step 9: Rodar e confirmar que passa; build completo; suíte inteira**

Run: `dotnet test tests/Secco.Intranet.Tests --filter EdicaoDePermissoesTests`, depois
`dotnet build` (0 avisos) e `dotnet test tests/Secco.Intranet.Tests` (verde)

- [ ] **Step 10: Commit**

```bash
git add src/Secco.Intranet.Application/Acesso/EditarPermissoesDoPerfilHandler.cs src/Secco.Intranet.Application/IntranetErrors.cs src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs src/Secco.Intranet.Application/Acesso/AuditoriaDeAcesso.cs src/Secco.Intranet.Application/IntranetApplicationExtensions.cs src/Secco.Intranet.Web/Controllers/AcessoController.cs src/Secco.Intranet.Web/Models/Acesso/ src/Secco.Intranet.Web/Views/Acesso/ tests/Secco.Intranet.Tests/Acesso/EditarPermissoesDoPerfilHandlerTests.cs tests/Secco.Intranet.Tests/Integration/EdicaoDePermissoesTests.cs tests/Secco.Intranet.Tests/Support/
git commit -m "feat(acesso): tela de edicao de permissoes de um perfil"
```

---

## Task 6: Concessão automática ao criar/editar Setor e ao criar perfis do Diretório

**Files:**
- Modify: `src/Secco.Intranet.Infrastructure/Access/SecureGateSetorAccessProvisioner.cs`
- Modify: `src/Secco.Intranet.Application/Acesso/CriarPerfilHandler.cs`
- Test: `tests/Secco.Intranet.Tests/Infrastructure/SecureGateSetorAccessProvisionerTests.cs` (arquivo
  já existente — acrescente os testes novos nele)
- Test: `tests/Secco.Intranet.Tests/Acesso/CriarPerfilHandlerTests.cs` (idem)

**Interfaces:**
- Consumes: `PermissoesDoPerfil.GarantirAsync` (Task 4, direto contra `ISecureGateClient` — o
  provisionador de setor já injeta o client, não `IGestaoDeAcesso`), `IGestaoDeAcesso.GarantirPermissoesAsync`
  (Task 4, para `CriarPerfilHandler`), `IntranetPermissoes` (Task 1), `ClassificacaoDePerfil.DiretorioAdmin`/`DiretorioUsuario`.

- [ ] **Step 1: Escrever o teste do provisionador que falha**

Leia `tests/Secco.Intranet.Tests/Infrastructure/SecureGateSetorAccessProvisionerTests.cs` (já existe
— usa `HttpMessageHandler` falso contra o client gerado, conforme a spec da Área administrativa).
Acrescente:

```csharp
[Fact]
public async Task EnsureSetorRolesAsync_GarantePermissaoDeLeituraEEscritaNasDuasRoles()
{
	// Monte o HttpMessageHandler falso respondendo 201/409 para CreateRole (como os testes
	// existentes já fazem), 200 com [] para GET .../roles/{role}/permissions, e capture o corpo
	// do PUT/POST .../roles/{role}/permissions — siga exatamente o padrão de asserção que os
	// testes vizinhos já usam para inspecionar corpo de requisição.

	var resultado = await provisionador.EnsureSetorRolesAsync("financeiro", CancellationToken.None);

	resultado.IsSuccess.Should().BeTrue();
	// asserção: a chamada de permissions para "financeiro-admin" incluiu "setor-financeiro:read"
	// e "setor-financeiro:write"; a de "financeiro-user" incluiu só "setor-financeiro:read".
}
```

Escreva o corpo real do teste seguindo a estrutura exata dos testes já existentes nesse arquivo
(nomes de método do handler falso, forma de inspecionar request) — não introduza um padrão de teste
novo para um arquivo que já tem um estabelecido.

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter SecureGateSetorAccessProvisionerTests`
Expected: FAIL (a permissão ainda não é garantida — a asserção nova falha)

- [ ] **Step 3: Implementar no provisionador**

Em `SecureGateSetorAccessProvisioner.cs`, dentro de `EnsureRoleAsync`, depois do `try` que cria a
Role (tanto no caminho de sucesso quanto no de `Conflict`, já que os dois significam "a Role
existe"), adicionar a garantia de permissão. A forma mais direta é extrair um método que decide as
permissões mínimas pelo sufixo do nome da Role e chamar `PermissoesDoPerfil.GarantirAsync` depois de
qualquer um dos dois desfechos de sucesso:

```csharp
public async Task<Result> EnsureSetorRolesAsync(string slug, CancellationToken cancellationToken = default)
{
	ArgumentException.ThrowIfNullOrWhiteSpace(slug);

	if (!tenantContext.IsResolved)
	{
		return Result.Failure(IntranetErrors.Setores.AccessProvisioningUnavailable);
	}

	var tenantId = tenantContext.TenantId!.Value;
	var normalizedSlug = slug.Trim().ToLowerInvariant();

	var adminRole = $"{normalizedSlug}-admin";
	var userRole = $"{normalizedSlug}-user";

	var adminResult = await EnsureRoleAsync(tenantId, adminRole, cancellationToken).ConfigureAwait(false);
	if (adminResult.IsFailure)
	{
		return adminResult;
	}

	var userResult = await EnsureRoleAsync(tenantId, userRole, cancellationToken).ConfigureAwait(false);
	if (userResult.IsFailure)
	{
		return userResult;
	}

	return await GarantirPermissoesDoSetorAsync(tenantId, normalizedSlug, adminRole, userRole, cancellationToken)
		.ConfigureAwait(false);
}

private async Task<Result> GarantirPermissoesDoSetorAsync(
	Guid tenantId, string slug, string adminRole, string userRole, CancellationToken cancellationToken)
{
	try
	{
		await PermissoesDoPerfil.GarantirAsync(
			client, tenantId, userRole, [IntranetPermissoes.Setor.Read(slug)], cancellationToken).ConfigureAwait(false);

		await PermissoesDoPerfil.GarantirAsync(
			client, tenantId, adminRole,
			[IntranetPermissoes.Setor.Read(slug), IntranetPermissoes.Setor.Write(slug)],
			cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
	catch (ApiException apiException)
	{
		logger.LogWarning(
			"Falha ao garantir permissao do setor '{Slug}' no SecureGate (status {StatusCode}).",
			slug, apiException.StatusCode);

		return Result.Failure(IntranetErrors.Setores.AccessProvisioningUnavailable);
	}
	catch (HttpRequestException httpRequestException)
	{
		logger.LogWarning(httpRequestException, "Falha de rede ao garantir permissao do setor '{Slug}' no SecureGate.", slug);

		return Result.Failure(IntranetErrors.Setores.AccessProvisioningUnavailable);
	}
}
```

(Adicionar `using Secco.Intranet.Application.Acesso;` no topo, para `IntranetPermissoes`.)

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter SecureGateSetorAccessProvisionerTests`
Expected: PASS

- [ ] **Step 5: Escrever o teste de `CriarPerfilHandler` que falha**

Leia `tests/Secco.Intranet.Tests/Acesso/CriarPerfilHandlerTests.cs` (já existe). Acrescente:

```csharp
[Fact]
public async Task CriarDiretorioAdmin_GarantePermissaoDeLeituraEGerenciamento()
{
	var gestao = new GestaoDeAcessoDeTeste();
	var handler = new CriarPerfilHandler(gestao, new TrilhaDeAuditoriaDeTeste());

	await handler.HandleAsync(ClassificacaoDePerfil.DiretorioAdmin);

	gestao.PermissoesGarantidas.Should().ContainKey(ClassificacaoDePerfil.DiretorioAdmin)
		.WhoseValue.Should().BeEquivalentTo([IntranetPermissoes.Diretorio.Read, IntranetPermissoes.Diretorio.Manage]);
}

[Fact]
public async Task CriarDiretorioUsuario_GaranteSoLeitura()
{
	var gestao = new GestaoDeAcessoDeTeste();
	var handler = new CriarPerfilHandler(gestao, new TrilhaDeAuditoriaDeTeste());

	await handler.HandleAsync(ClassificacaoDePerfil.DiretorioUsuario);

	gestao.PermissoesGarantidas.Should().ContainKey(ClassificacaoDePerfil.DiretorioUsuario)
		.WhoseValue.Should().BeEquivalentTo([IntranetPermissoes.Diretorio.Read]);
}

[Fact]
public async Task CriarPerfilComum_NaoGaranteNenhumaPermissao()
{
	var gestao = new GestaoDeAcessoDeTeste();
	var handler = new CriarPerfilHandler(gestao, new TrilhaDeAuditoriaDeTeste());

	await handler.HandleAsync("gerente-de-compras");

	gestao.PermissoesGarantidas.Should().NotContainKey("gerente-de-compras");
}
```

Acrescente `PermissoesGarantidas` (`Dictionary<string, IReadOnlyCollection<string>>`) ao dublê
`GestaoDeAcessoDeTeste`, registrando toda chamada de `GarantirPermissoesAsync`.

- [ ] **Step 6: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter CriarPerfilHandlerTests`
Expected: FAIL

- [ ] **Step 7: Implementar no handler**

```csharp
public sealed class CriarPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> PermissoesBaseDoProduto =
		new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
		{
			[ClassificacaoDePerfil.DiretorioAdmin] = [IntranetPermissoes.Diretorio.Read, IntranetPermissoes.Diretorio.Manage],
			[ClassificacaoDePerfil.DiretorioUsuario] = [IntranetPermissoes.Diretorio.Read],
		};

	public async Task<Result> HandleAsync(string nome, CancellationToken cancellationToken = default)
	{
		var perfil = nome?.Trim() ?? string.Empty;

		if (!ClassificacaoDePerfil.NomeValido(perfil))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilNomeInvalido);
		}

		if (ClassificacaoDePerfil.EhReservado(perfil))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		var criado = await gestao.CriarPerfilAsync(perfil, cancellationToken).ConfigureAwait(false);

		if (criado.IsFailure)
		{
			return criado;
		}

		if (PermissoesBaseDoProduto.TryGetValue(perfil, out var permissoesBase))
		{
			// Best-effort: se a permissão falhar, o perfil já foi criado com sucesso — a
			// reconciliação (Task 8) cobre o caso de a permissão não ter sido gravada aqui.
			await gestao.GarantirPermissoesAsync(perfil, permissoesBase, cancellationToken).ConfigureAwait(false);
		}

		await AuditoriaDeAcesso.PerfilAsync(trilha, VerbosDeAuditoria.AcessoPerfilCriar, perfil, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
```

- [ ] **Step 8: Rodar e confirmar que passa; build completo; suíte inteira**

Run: `dotnet test tests/Secco.Intranet.Tests --filter CriarPerfilHandlerTests`, depois
`dotnet build` (0 avisos) e `dotnet test tests/Secco.Intranet.Tests` (verde)

- [ ] **Step 9: Commit**

```bash
git add src/Secco.Intranet.Infrastructure/Access/SecureGateSetorAccessProvisioner.cs src/Secco.Intranet.Application/Acesso/CriarPerfilHandler.cs tests/Secco.Intranet.Tests/Infrastructure/SecureGateSetorAccessProvisionerTests.cs tests/Secco.Intranet.Tests/Acesso/CriarPerfilHandlerTests.cs tests/Secco.Intranet.Tests/Support/
git commit -m "feat(acesso): concessao automatica de permissao ao criar setor e perfis do diretorio"
```

---

## Task 7: `IPermissoesDeSetor` — filtragem por permissão

**Files:**
- Create: `src/Secco.Intranet.Web/Navigation/IPermissoesDeSetor.cs`
- Create: `src/Secco.Intranet.Web/Navigation/PermissoesDeSetor.cs`
- Modify: `src/Secco.Intranet.Web/Program.cs` (registrar `AddScoped<IPermissoesDeSetor, PermissoesDeSetor>()`)
- Test: `tests/Secco.Intranet.Tests/Navigation/PermissoesDeSetorTests.cs`

**Interfaces:**
- Consumes: `IAuthorizationService.AuthorizeAsync` (Task 2/3), `AcessoAdministrativo.SomenteIntranetAdmin`,
  `IntranetPermissoes.Setor` (Task 1).
- Produces:
  `Task<IReadOnlySet<string>> SlugsComPermissaoAsync(ClaimsPrincipal usuario, string acao, IReadOnlyList<string> setoresDoTenant, CancellationToken ct)`
  (`acao` é `"read"` ou `"write"`, sem o prefixo de recurso — o serviço monta `setor-{slug}:{acao}` e
  `setores:{acao}` por dentro).

- [ ] **Step 1: Escrever o teste que falha**

```csharp
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Navigation;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Navigation;

[Collection(nameof(IntranetWebCollection))]
public class PermissoesDeSetorTests(IntranetWebFactory factory)
{
	[Fact]
	public async Task SoDevolveOsSlugsComAPermissaoEspecifica()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao("financeiro-user", IntranetPermissoes.Setor.Read("financeiro"));
		using var scope = factory.Services.CreateScope();
		var servico = scope.ServiceProvider.GetRequiredService<IPermissoesDeSetor>();
		var usuario = PrincipalDeTeste.ComRoles("financeiro-user", tenant: factory.TenantAlfa);

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "read", ["financeiro", "ti"], CancellationToken.None);

		slugs.Should().BeEquivalentTo(["financeiro"]);
		factory.ResolvedorDePermissoes = null;
	}

	[Fact]
	public async Task PermissaoGlobal_DevolveTodosOsSlugs_InclusiveUmNaoListadoAntes()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao("todos", IntranetPermissoes.Setor.ReadGlobal);
		using var scope = factory.Services.CreateScope();
		var servico = scope.ServiceProvider.GetRequiredService<IPermissoesDeSetor>();
		var usuario = PrincipalDeTeste.ComRoles("todos", tenant: factory.TenantAlfa);

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "read", ["financeiro", "ti", "criado-depois"], CancellationToken.None);

		slugs.Should().BeEquivalentTo(["financeiro", "ti", "criado-depois"]);
		factory.ResolvedorDePermissoes = null;
	}

	[Fact]
	public async Task IntranetAdmin_DevolveTodosOsSlugs_SemConsultarNada()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverQueLanca();
		using var scope = factory.Services.CreateScope();
		var servico = scope.ServiceProvider.GetRequiredService<IPermissoesDeSetor>();
		var usuario = PrincipalDeTeste.ComRoles("intranet-admin", tenant: factory.TenantAlfa);

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "read", ["financeiro", "ti"], CancellationToken.None);

		slugs.Should().BeEquivalentTo(["financeiro", "ti"]);
		factory.ResolvedorDePermissoes = null;
	}

	[Fact]
	public async Task SemNenhumaPermissao_DevolveVazio()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste();
		using var scope = factory.Services.CreateScope();
		var servico = scope.ServiceProvider.GetRequiredService<IPermissoesDeSetor>();
		var usuario = PrincipalDeTeste.ComRoles("financeiro-user", tenant: factory.TenantAlfa);

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "read", ["financeiro"], CancellationToken.None);

		slugs.Should().BeEmpty();
		factory.ResolvedorDePermissoes = null;
	}
}
```

Ajuste `PrincipalDeTeste.ComRoles(role, tenant: ...)` para a forma real do helper de montar
`ClaimsPrincipal` já usado no projeto (role + claim de tenant) — reaproveite, não recrie.

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter PermissoesDeSetorTests`
Expected: FAIL (compilação)

- [ ] **Step 3: Implementar**

```csharp
using System.Security.Claims;

namespace Secco.Intranet.Web.Navigation;

/// <summary>Em quais setores o usuário tem uma dada ação, por permissão (ADR-0021).</summary>
public interface IPermissoesDeSetor
{
	/// <summary>Slugs, dentre os informados, em que o usuário tem a ação pedida.</summary>
	/// <param name="usuario">Usuário atual.</param>
	/// <param name="acao"><c>"read"</c> ou <c>"write"</c> — sem o prefixo de recurso.</param>
	/// <param name="setoresDoTenant">Slugs a considerar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlySet<string>> SlugsComPermissaoAsync(
		ClaimsPrincipal usuario, string acao, IReadOnlyList<string> setoresDoTenant, CancellationToken cancellationToken = default);
}
```

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Web.Authentication;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Implementação real de <see cref="IPermissoesDeSetor"/>, sobre <see cref="IAuthorizationService"/>
/// (mesma policy dinâmica do <see cref="ExigePermissaoAttribute"/>, Task 3) — reaproveita o cache
/// por (tenant, role) do SDK, uma chamada por slug/global.
/// </summary>
/// <param name="authorizationService">Serviço de autorização do framework.</param>
public sealed class PermissoesDeSetor(IAuthorizationService authorizationService) : IPermissoesDeSetor
{
	/// <inheritdoc />
	public async Task<IReadOnlySet<string>> SlugsComPermissaoAsync(
		ClaimsPrincipal usuario, string acao, IReadOnlyList<string> setoresDoTenant, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(usuario);
		ArgumentNullException.ThrowIfNull(setoresDoTenant);

		if (AcessoAdministrativo.SomenteIntranetAdmin(usuario))
		{
			return setoresDoTenant.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}

		var global = $"setores:{acao}";
		var temGlobal = (await authorizationService.AuthorizeAsync(usuario, global)).Succeeded;

		if (temGlobal)
		{
			return setoresDoTenant.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}

		var resultado = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var slug in setoresDoTenant)
		{
			var permissao = acao == "write" ? IntranetPermissoes.Setor.Write(slug) : IntranetPermissoes.Setor.Read(slug);
			var autorizado = await authorizationService.AuthorizeAsync(usuario, permissao);

			if (autorizado.Succeeded)
			{
				resultado.Add(slug);
			}
		}

		return resultado;
	}
}
```

Registrar em `Program.cs`: `builder.Services.AddScoped<IPermissoesDeSetor, PermissoesDeSetor>();`

- [ ] **Step 4: Rodar e confirmar que passa; build completo**

Run: `dotnet test tests/Secco.Intranet.Tests --filter PermissoesDeSetorTests`, depois `dotnet build` (0 avisos)

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Web/Navigation/IPermissoesDeSetor.cs src/Secco.Intranet.Web/Navigation/PermissoesDeSetor.cs src/Secco.Intranet.Web/Program.cs tests/Secco.Intranet.Tests/Navigation/PermissoesDeSetorTests.cs
git commit -m "feat(acesso): IPermissoesDeSetor, filtragem de setor por permissao"
```

---

## Task 8: Reconciliar permissões (backfill de setores e do Diretório)

**Files:**
- Create: `src/Secco.Intranet.Application/Acesso/ReconciliarPermissoesHandler.cs`
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs` (novo verbo)
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/AcessoController.cs` (nova action `ReconciliarPermissoes`)
- Modify: a view de `Acesso/Index` (aba Perfis) — botão "Reconciliar permissões"
- Test: `tests/Secco.Intranet.Tests/Acesso/ReconciliarPermissoesHandlerTests.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/ReconciliarPermissoesTests.cs`

**Interfaces:**
- Consumes: `SearchSetoresHandler.HandleAsync(SetorSearchCriteria, CancellationToken)` →
  `Task<Result<PagedResult<SetorDto>>>` (já existe, mesmo handler que `NavigationViewComponent` usa —
  ver `src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs:68-75` para o formato exato
  da chamada), `IGestaoDeAcesso.ListarPerfisAsync` (para saber se `diretorio-admin`/`diretorio-user`
  existem), `IGestaoDeAcesso.GarantirPermissoesAsync` (Task 4).
- Produces: `ReconciliarPermissoesHandler.HandleAsync(CancellationToken ct)` → `Task<Result<ReconciliacaoResumo>>`,
  com `ReconciliacaoResumo(int Setores, int PerfisDoDiretorio)`.

- [ ] **Step 1: Escrever o teste do handler que falha**

```csharp
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Pagination;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Acesso;

public class ReconciliarPermissoesHandlerTests
{
	[Fact]
	public async Task GarantePermissaoDeCadaSetorEDosPerfisDoDiretorioSeExistirem()
	{
		var repositorio = new FakeSetorRepository().Com("financeiro").Com("ti");
		var gestao = new GestaoDeAcessoDeTeste().ComPerfis("financeiro-admin", "financeiro-user", "ti-admin", "ti-user", "diretorio-admin", "diretorio-user");
		var handler = new ReconciliarPermissoesHandler(new SearchSetoresHandler(repositorio), gestao);

		var resultado = await handler.HandleAsync(CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Setores.Should().Be(2);
		resultado.Value.PerfisDoDiretorio.Should().Be(2);
		gestao.PermissoesGarantidas.Should().ContainKey("financeiro-admin")
			.WhoseValue.Should().BeEquivalentTo([IntranetPermissoes.Setor.Read("financeiro"), IntranetPermissoes.Setor.Write("financeiro")]);
		gestao.PermissoesGarantidas.Should().ContainKey("diretorio-user")
			.WhoseValue.Should().BeEquivalentTo([IntranetPermissoes.Diretorio.Read]);
	}

	[Fact]
	public async Task SemDiretorioAdminOuUsuario_NaoTentaGarantirPermissaoNeles()
	{
		var repositorio = new FakeSetorRepository();
		var gestao = new GestaoDeAcessoDeTeste(); // sem diretorio-admin/user na lista de perfis
		var handler = new ReconciliarPermissoesHandler(new SearchSetoresHandler(repositorio), gestao);

		var resultado = await handler.HandleAsync(CancellationToken.None);

		resultado.Value.PerfisDoDiretorio.Should().Be(0);
		gestao.PermissoesGarantidas.Should().NotContainKey("diretorio-admin");
	}

	[Fact]
	public async Task Idempotente_RodarDeNovoNaoFalha()
	{
		var repositorio = new FakeSetorRepository().Com("financeiro");
		var gestao = new GestaoDeAcessoDeTeste().ComPerfis("financeiro-admin", "financeiro-user");
		var handler = new ReconciliarPermissoesHandler(new SearchSetoresHandler(repositorio), gestao);

		await handler.HandleAsync(CancellationToken.None);
		var segunda = await handler.HandleAsync(CancellationToken.None);

		segunda.IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Repositório fake mínimo, no mesmo molde do `FakeRepository` privado de
	/// <c>CreateSetorHandlerTests</c> — implementa só o que <see cref="SearchSetoresHandler"/> chama
	/// (<see cref="ISetorRepository.SearchAsync"/>); os demais membros lançam
	/// <see cref="NotImplementedException"/>. Devolve tudo numa página só (os testes não passam de
	/// poucos setores) — se precisar provar paginação de verdade, o teste ajusta o critério.
	/// </summary>
	private sealed class FakeSetorRepository : ISetorRepository
	{
		private readonly List<Setor> _setores = [];

		public FakeSetorRepository Com(string slug)
		{
			_setores.Add(Setor.Criar(slug, slug, "bi-building"));
			return this;
		}

		public Task<PagedResult<Setor>> SearchAsync(SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(new PagedResult<Setor>(_setores, 1, Math.Max(_setores.Count, 1), _setores.Count));

		public Task AddAsync(Setor setor, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
	}
}
```

Confira a assinatura real de `Setor.Criar(...)` (nome do método de fábrica e parâmetros) e o
construtor real de `PagedResult<T>` antes de colar este código — ajuste para o que o Domain e o
SharedKernel realmente expõem hoje; o objetivo do trecho é o comportamento, não os nomes exatos de
construtor.

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ReconciliarPermissoesHandlerTests`
Expected: FAIL (compilação)

- [ ] **Step 3: Verbo de auditoria**

Em `VerbosDeAuditoria.cs`:

```csharp
/// <summary>Permissões de setores e do Diretório foram reconciliadas em lote.</summary>
public const string AcessoPermissoesReconciliar = "acesso.permissoes-reconciliar";
```

- [ ] **Step 4: Implementar o handler**

Usa `SearchSetoresHandler` (já existe, `Secco.Intranet.Application.Setores`) direto — o mesmo handler
que `NavigationViewComponent` já consome. Como a reconciliação precisa de **todo** setor, não só os
primeiros, ela pagina até `TotalPages`, com `ApenasAtivos: false` (um setor desativado também tem
Roles que podem ter membros herdados de antes da desativação — reconciliar não faz mal a elas):

```csharp
using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Quantos setores e quantos perfis do Diretório tiveram permissão garantida.</summary>
public sealed record ReconciliacaoResumo(int Setores, int PerfisDoDiretorio);

/// <summary>
/// Reconcilia permissão de setor e do Diretório (ADR-0021): cobre setores cadastrados antes deste
/// modelo existir, sem nenhuma permissão gravada, e a migração do Diretório de nome de Role para
/// permissão. Idempotente — mesclagem (<see cref="IGestaoDeAcesso.GarantirPermissoesAsync"/>),
/// nunca apaga nada.
/// </summary>
/// <param name="searchSetores">Busca de setores já existente (a mesma do menu).</param>
/// <param name="gestao">Porta de gestão de acesso.</param>
public sealed class ReconciliarPermissoesHandler(SearchSetoresHandler searchSetores, IGestaoDeAcesso gestao)
{
	private const int TamanhoDaPagina = 100;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ReconciliacaoResumo>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var totalSetores = 0;
		var pagina = PageRequest.FirstPage;

		while (true)
		{
			var busca = await searchSetores
				.HandleAsync(new SetorSearchCriteria(Page: new PageRequest(pagina, TamanhoDaPagina)), cancellationToken)
				.ConfigureAwait(false);

			if (busca.IsFailure)
			{
				return Result.Failure<ReconciliacaoResumo>(busca.Error);
			}

			foreach (var setor in busca.Value.Items)
			{
				await gestao.GarantirPermissoesAsync(
					$"{setor.Slug}-user", [IntranetPermissoes.Setor.Read(setor.Slug)], cancellationToken).ConfigureAwait(false);
				await gestao.GarantirPermissoesAsync(
					$"{setor.Slug}-admin",
					[IntranetPermissoes.Setor.Read(setor.Slug), IntranetPermissoes.Setor.Write(setor.Slug)],
					cancellationToken).ConfigureAwait(false);

				totalSetores++;
			}

			if (!busca.Value.HasNextPage)
			{
				break;
			}

			pagina++;
		}

		var perfis = await gestao.ListarPerfisAsync(cancellationToken).ConfigureAwait(false);
		var totalDiretorio = 0;

		if (perfis.IsSuccess)
		{
			var nomes = perfis.Value.Select(p => p.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);

			if (nomes.Contains(ClassificacaoDePerfil.DiretorioUsuario))
			{
				await gestao.GarantirPermissoesAsync(
					ClassificacaoDePerfil.DiretorioUsuario, [IntranetPermissoes.Diretorio.Read], cancellationToken).ConfigureAwait(false);
				totalDiretorio++;
			}

			if (nomes.Contains(ClassificacaoDePerfil.DiretorioAdmin))
			{
				await gestao.GarantirPermissoesAsync(
					ClassificacaoDePerfil.DiretorioAdmin,
					[IntranetPermissoes.Diretorio.Read, IntranetPermissoes.Diretorio.Manage],
					cancellationToken).ConfigureAwait(false);
				totalDiretorio++;
			}
		}

		return Result.Success(new ReconciliacaoResumo(totalSetores, totalDiretorio));
	}
}
```

Confirme o nome exato de `PageRequest.FirstPage` (usado em `PagedResult.cs` da plataforma) antes de
colar — se a versão do SharedKernel referenciada por este projeto não o expuser publicamente, troque
por `1` diretamente. Registrar em `IntranetApplicationExtensions.cs`:
`services.AddScoped<ReconciliarPermissoesHandler>();`

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ReconciliarPermissoesHandlerTests`
Expected: PASS (3 testes)

- [ ] **Step 6: Controller, view e teste de integração**

Adicionar em `AcessoController.cs` uma action `[HttpPost] ReconciliarPermissoes(CancellationToken ct)`
que chama o handler, registra o verbo `AcessoPermissoesReconciliar` (com o resumo nos metadados) e
redireciona para `Index` com uma mensagem ("permissões reconciliadas em N setores e M perfis do
Diretório"). Botão na view de Perfis, fora de qualquer perfil específico.

```csharp
using System.Net;
using Secco.Intranet.Tests.Support;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

[Collection(nameof(IntranetWebCollection))]
public class ReconciliarPermissoesTests(IntranetWebFactory factory)
{
	[Fact]
	public async Task SetorAntigoSemPermissao_PassaAResponderDepoisDeReconciliar()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste(); // "financeiro-user" sem nenhuma permissão ainda
		var gestao = new GestaoDeAcessoDeTeste().ComPerfis("financeiro-admin", "financeiro-user");
		factory.GestaoDeAcesso = gestao;

		using (var clienteFinanceiro = factory.CriarClienteComRoles("financeiro-user"))
		{
			var antes = await clienteFinanceiro.GetAsync("/teste-permissao/diretorio-read"); // placeholder de exemplo — troque pela rota real de leitura de setor, se houver uma mais direta que a Task 3 já expôs
		}

		using (var clienteAdmin = factory.CriarClienteComRoles("intranet-admin"))
		{
			var resposta = await clienteAdmin.PostAsync("/acesso/permissoes/reconciliar", content: null);
			resposta.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect, HttpStatusCode.Found);
		}

		gestao.PermissoesGarantidas.Should().ContainKey("financeiro-user")
			.WhoseValue.Should().Contain(IntranetPermissoes.Setor.Read("financeiro"));

		factory.ResolvedorDePermissoes = null;
		factory.GestaoDeAcesso = null;
	}

	[Fact]
	public async Task SemIntranetAdmin_Devolve403()
	{
		using var client = factory.CriarClienteComRoles("financeiro-admin");

		var resposta = await client.PostAsync("/acesso/permissoes/reconciliar", content: null);

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}
}
```

O primeiro teste tem um trecho marcado para revisão (o "antes" não é essencial ao comportamento —
o que importa é a asserção final de que `GarantirPermissoesAsync` foi chamado). Simplifique removendo
o bloco "antes" se ele não agregar nada ao teste; o essencial é a chamada de reconciliação e a
asserção sobre `PermissoesGarantidas`.

- [ ] **Step 7: Rodar e confirmar que passa; build completo; suíte inteira**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ReconciliarPermissoesTests`, depois
`dotnet build` (0 avisos) e `dotnet test tests/Secco.Intranet.Tests` (verde)

- [ ] **Step 8: Commit**

```bash
git add src/Secco.Intranet.Application/Acesso/ReconciliarPermissoesHandler.cs src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs src/Secco.Intranet.Application/IntranetApplicationExtensions.cs src/Secco.Intranet.Web/Controllers/AcessoController.cs src/Secco.Intranet.Web/Views/Acesso/ tests/Secco.Intranet.Tests/Acesso/ReconciliarPermissoesHandlerTests.cs tests/Secco.Intranet.Tests/Integration/ReconciliarPermissoesTests.cs tests/Secco.Intranet.Tests/Support/
git commit -m "feat(acesso): reconciliar permissoes de setores e perfis do diretorio"
```

---

## Task 9: Migrar Mural, Documentos, `SetorController` e `NavigationViewComponent`

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/MuralController.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/DocumentosController.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/SetorController.cs`
- Modify: `src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs`
- Delete: `src/Secco.Intranet.Web/Navigation/SetorAcesso.cs` (depois que nenhum chamador restar)
- Modify: os testes de autorização já existentes desses quatro consumidores (não crie arquivos de
  teste novos — ajuste os que já provam a matriz de setor para injetar/configurar
  `IPermissoesDeSetor` em vez de roles nomeadas)

**Interfaces:**
- Consumes: `IPermissoesDeSetor.SlugsComPermissaoAsync` (Task 7).

Esta task é maior que as anteriores porque quatro pontos de chamada mudam junto — um reviewer
avalia "a migração terminou e nada ficou em nome de Role" como uma coisa só, não quatro pedaços
independentes.

- [ ] **Step 1: Ler cada um dos quatro arquivos por completo antes de editar**

`SetorAcesso.SlugsDoUsuario`/`AdministraSetor`/`SlugsAdministrados`/`Visiveis` aparecem em:
`MuralController.cs:56,98,111`, `DocumentosController.cs:27`, `SetorController.cs:128,210,263,331`,
`NavigationViewComponent.cs:78`. Leia a volta de cada linha (o método inteiro) antes de trocar —
alguns desses métodos são síncronos hoje e passam a precisar de `async`/`await` porque
`SlugsComPermissaoAsync` é assíncrono; confira se o método que os envolve já é `async Task` (a
maioria dos actions de controller já é) e ajuste a assinatura de quem não for.

- [ ] **Step 2: Ajustar os testes de autorização existentes primeiro (ainda vermelhos)**

Nos testes que hoje montam `X-Test-Roles: financeiro-user` e esperam ver o setor `financeiro`,
acrescente `factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao("financeiro-user", IntranetPermissoes.Setor.Read("financeiro"))`
antes da chamada (e o `Write` equivalente nos testes que hoje usam `{slug}-admin` para uma ação de
escrita). Rode a suíte desses arquivos e confirme que passam a falhar (a implementação ainda checa
nome de Role, não permissão — é esperado neste ponto).

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~Mural|FullyQualifiedName~Documentos|FullyQualifiedName~SetorController|FullyQualifiedName~Navigation"`
Expected: FAIL nos casos que passaram a exigir a permissão nova

- [ ] **Step 3: Migrar `MuralController`**

Troque `[.. SetorAcesso.SlugsDoUsuario(User)]` (linhas 56 e 98) por
`[.. await permissoesDeSetor.SlugsComPermissaoAsync(User, "read", todosOsSlugsDoTenant, cancellationToken)]`
— o método precisa da lista de todos os slugs do tenant; se o método já não a tiver à mão, obtenha-a
da mesma fonte que a Task 8 usa para listar setores. Troque
`SetorAcesso.AdministraSetor(User, publicacao.SetorSlug)` (linha 111) por
`(await permissoesDeSetor.SlugsComPermissaoAsync(User, "write", [publicacao.SetorSlug], cancellationToken)).Contains(publicacao.SetorSlug)`.
Injete `IPermissoesDeSetor permissoesDeSetor` no construtor do controller.

- [ ] **Step 4: Migrar `DocumentosController`**

Mesma troca de `SlugsDoUsuario` por `SlugsComPermissaoAsync(User, "read", ...)` na linha 27.

- [ ] **Step 5: Migrar `SetorController`**

`SlugsAdministrados` (linhas 128, 210, 263) vira `SlugsComPermissaoAsync(User, "write", ...)`;
`AdministraSetor` (linha 331) vira a mesma checagem `Contains` de um slug só, como no Step 3.

- [ ] **Step 6: Migrar `NavigationViewComponent`**

`SetorAcesso.Visiveis(resultado.Value.Items, usuario, exigirVinculo: autenticacaoAtiva)` (linha 78)
precisa da mesma regra de hoje para o modo aberto de DEV (`exigirVinculo: false` devolve todos sem
checar nada). Mantenha esse curto-circuito explícito antes de chamar `IPermissoesDeSetor`:

```csharp
var setoresVisiveis = autenticacaoAtiva
	? resultado.Value.Items.Where(setor => setor.Ativo && slugsComLeitura.Contains(setor.Slug)).ToList()
	: resultado.Value.Items.Where(setor => setor.Ativo).ToList();
```

onde `slugsComLeitura` vem de
`await permissoesDeSetor.SlugsComPermissaoAsync(usuario, "read", [.. resultado.Value.Items.Where(s => s.Ativo).Select(s => s.Slug)], cancellationToken)`
— calculado só quando `autenticacaoAtiva` for verdadeiro, para não gastar uma resolução de permissão
à toa no modo aberto de DEV.

- [ ] **Step 7: Rodar os testes ajustados no Step 2 e confirmar que passam**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~Mural|FullyQualifiedName~Documentos|FullyQualifiedName~SetorController|FullyQualifiedName~Navigation"`
Expected: PASS

- [ ] **Step 8: Confirmar que `SetorAcesso` não tem mais chamador e remover**

Run: `grep -rn "SetorAcesso\." src/` — deve devolver vazio (fora do próprio arquivo). Delete
`src/Secco.Intranet.Web/Navigation/SetorAcesso.cs` e o arquivo de teste
`tests/Secco.Intranet.Tests/Navigation/SetorAcessoTests.cs`, se existir.

- [ ] **Step 9: Build completo com 0 avisos e suíte inteira**

Run: `dotnet build` (0 avisos), `dotnet test tests/Secco.Intranet.Tests` (verde)

- [ ] **Step 10: Commit**

```bash
git add src/Secco.Intranet.Web/Controllers/MuralController.cs src/Secco.Intranet.Web/Controllers/DocumentosController.cs src/Secco.Intranet.Web/Controllers/SetorController.cs src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs tests/
git rm src/Secco.Intranet.Web/Navigation/SetorAcesso.cs
git commit -m "refactor(acesso): migra Mural, Documentos, SetorController e menu para permissao; remove SetorAcesso"
```

---

## Task 10: Migrar `AcessoAoDiretorio` para permissão

**Files:**
- Modify: `src/Secco.Intranet.Web/Navigation/AcessoAoDiretorio.cs`
- Modify: os testes existentes `tests/Secco.Intranet.Tests/Navigation/AcessoAoDiretorioTests.cs`
  (ajustar para configurar permissão em vez de Role, mantendo os mesmos casos)

**Interfaces:**
- Consumes: `IAuthorizationService.AuthorizeAsync` (Task 2/3) — direto, não via `IPermissoesDeSetor`
  (que é específico de setor).
- Produces: contrato público **inalterado** — `Nivel`/`TemNivel` continuam com a mesma assinatura;
  `ExigeNivelNoDiretorioAttribute`, o item de menu e "Meu perfil" não mudam nenhuma linha.

- [ ] **Step 1: Ler o arquivo atual por completo**

Leia `AcessoAoDiretorio.cs` inteiro antes de editar — a assinatura pública de `Nivel`/`TemNivel` tem
que continuar idêntica; só o corpo muda.

- [ ] **Step 2: Ajustar os testes existentes (ainda vermelhos)**

Nos casos de `AcessoAoDiretorioTests.cs` que hoje montam `X-Test-Roles: diretorio-user` e esperam
`NivelDeAcessoAoDiretorio.Usuario`, acrescente
`factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao("diretorio-user", IntranetPermissoes.Diretorio.Read)`
(e `IntranetPermissoes.Diretorio.Manage` para o caso `diretorio-admin` → `Administrador`). Rode e
confirme que falham (a implementação ainda olha nome de Role).

- [ ] **Step 3: Implementar**

Se `Nivel` hoje é síncrono (recebe só `ClaimsPrincipal`), ele precisa virar assíncrono — confirme
todos os chamadores (`ExigeNivelNoDiretorioAttribute`, o item de menu, "Meu perfil") e ajuste a
cadeia inteira para `Task<NivelDeAcessoAoDiretorio>`, resolvendo `IAuthorizationService` do mesmo
jeito que `ExigePermissaoAttribute` (Task 3) já faz:

```csharp
public static async Task<NivelDeAcessoAoDiretorio> NivelAsync(IAuthorizationService authorizationService, ClaimsPrincipal usuario)
{
	if (AcessoAdministrativo.SomenteIntranetAdmin(usuario))
	{
		return NivelDeAcessoAoDiretorio.Administrador;
	}

	if ((await authorizationService.AuthorizeAsync(usuario, IntranetPermissoes.Diretorio.Manage)).Succeeded)
	{
		return NivelDeAcessoAoDiretorio.Administrador;
	}

	if ((await authorizationService.AuthorizeAsync(usuario, IntranetPermissoes.Diretorio.Read)).Succeeded)
	{
		return NivelDeAcessoAoDiretorio.Usuario;
	}

	return NivelDeAcessoAoDiretorio.Nenhum;
}
```

Mantenha `TemNivel` como um método de conveniência que chama `NivelAsync` e compara — ajuste sua
assinatura para assíncrona também. Atualize `ExigeNivelNoDiretorioAttribute` para `IAsyncAuthorizationFilter`
se ainda não for (confira — pode já ser, já que outros atributos deste plano também são).

- [ ] **Step 4: Rodar e confirmar que os testes ajustados no Step 2 passam**

Run: `dotnet test tests/Secco.Intranet.Tests --filter AcessoAoDiretorioTests`
Expected: PASS

- [ ] **Step 5: Rodar a suíte inteira do Diretório**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~Diretorio"`
Expected: PASS — a matriz de autorização da spec de 2026-09-24 continua valendo, agora por permissão

- [ ] **Step 6: Build completo com 0 avisos e suíte inteira**

Run: `dotnet build` (0 avisos), `dotnet test tests/Secco.Intranet.Tests` (verde)

- [ ] **Step 7: Commit**

```bash
git add src/Secco.Intranet.Web/Navigation/AcessoAoDiretorio.cs src/Secco.Intranet.Web/Authentication/ExigeNivelNoDiretorioAttribute.cs tests/Secco.Intranet.Tests/Navigation/AcessoAoDiretorioTests.cs
git commit -m "refactor(diretorio): AcessoAoDiretorio passa a resolver por permissao (diretorio:read/manage)"
```

---

## Task 11: Inventário — nova entrada de leitura

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/InventarioController.cs`
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs` / quem calcula `MostrarInventario`
  (confira o call site real antes de editar — provavelmente no `NavigationViewComponent`)
- Test: os testes de autorização já existentes do Inventário (acrescentar casos, não recriar)

**Interfaces:**
- Consumes: `ExigePermissaoAttribute` (Task 3) **ou** o gate por nome de Role já existente — a ação
  de leitura aceita **qualquer um dos dois**; a de escrita continua só por nome de Role.

- [ ] **Step 1: Ler `InventarioController.cs` por completo**

Confirme quais actions são só leitura (listar, detalhe) e quais escrevem (criar, mudar estado,
baixar) — a permissão nova só se aplica às de leitura.

- [ ] **Step 2: Escrever o teste que falha**

```csharp
[Fact]
public async Task ComInventarioLeitura_AbreAListagem_MasContinuaBloqueadaAEscrita()
{
	factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao("consulta-inventario", IntranetPermissoes.Inventario.Read);
	using var client = factory.CriarClienteComRoles("consulta-inventario");

	var listagem = await client.GetAsync("/inventario");
	listagem.StatusCode.Should().Be(HttpStatusCode.OK);

	var escrita = await client.PostAsync("/inventario/criar", content: new FormUrlEncodedContent(new Dictionary<string, string>()));
	escrita.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.BadRequest); // 400 se o antifalsificação barrar antes — confirme qual já é o comportamento hoje para "sem a role", no teste vizinho, e replique
}

[Fact]
public async Task InventarioAdmin_ContinuaLiberadoEmTudo_SemPrecisarDeInventarioRead()
{
	factory.ResolvedorDePermissoes = new PermissionResolverDeTeste(); // nenhuma permissão gravada
	using var client = factory.CriarClienteComRoles("inventario-admin");

	var listagem = await client.GetAsync("/inventario");

	listagem.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

Ajuste o segundo caso de asserção do primeiro teste para bater exatamente com o que o teste vizinho
já existente espera para "usuário sem a Role tentando escrever" — não invente um novo comportamento
de antifalsificação aqui.

- [ ] **Step 3: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~Inventario"`
Expected: FAIL no primeiro teste novo (listagem ainda exige `inventario-admin`)

- [ ] **Step 4: Implementar**

Na(s) action(s) de leitura do `InventarioController`, o gate atual (provavelmente um atributo ou uma
checagem no início do método usando `AcessoAdministrativo.TemAcesso`) passa a aceitar também
`inventario:read`. Se o gate hoje é um atributo de classe cobrindo o controller inteiro, mova as
actions de leitura para aceitar as duas formas — por exemplo, sobrescrevendo com um atributo de
método mais permissivo nas actions de leitura, ou checando explicitamente as duas condições no
início delas:

```csharp
if (!AcessoAdministrativo.TemAcesso(User, RoleEspecifica)
	&& !(await authorizationService.AuthorizeAsync(User, IntranetPermissoes.Inventario.Read)).Succeeded)
{
	return Forbid();
}
```

Injete `IAuthorizationService` no controller se ainda não estiver. Não altere nada nas actions de
escrita.

Depois, no ponto que hoje calcula `MostrarInventario` para o menu (confira o call site real), a
mesma condição OR passa a valer: `inventario-admin`/`intranet-admin` (como hoje) **ou**
`inventario:read`.

- [ ] **Step 5: Rodar e confirmar que passa; build completo; suíte inteira**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~Inventario"`, depois
`dotnet build` (0 avisos) e `dotnet test tests/Secco.Intranet.Tests` (verde)

- [ ] **Step 6: Commit**

```bash
git add src/Secco.Intranet.Web/Controllers/InventarioController.cs src/Secco.Intranet.Web/Navigation/ tests/Secco.Intranet.Tests/
git commit -m "feat(inventario): nova entrada de leitura por permissao (inventario:read)"
```

---

## Task 12: Fumaça real do SecureGate — permissão de um perfil

**Files:**
- Modify: `tests/Secco.Intranet.Tests/Smoke/SecureGateGestaoDeAcessoFumacaTests.cs`
- Modify: `docs/roteiro-fumaca-securegate.md`

**Interfaces:**
- Consumes: `IGestaoDeAcesso.GarantirPermissoesAsync`/`DefinirPermissoesDoPerfilAsync` (Task 4), contra
  o `SecureGateGestaoDeAcesso` real (não dublê) — mesmo padrão dos testes de fumaça já existentes
  neste arquivo (`[FumacaFact]`, cliente real do `secco-platform` em modo self-issued).

- [ ] **Step 1: Ler o arquivo de fumaça existente por completo**

Leia `SecureGateGestaoDeAcessoFumacaTests.cs` inteiro — reaproveite exatamente a fixture (`f.Gestao`,
criação/limpeza de perfil e usuário) que os testes vizinhos já usam.

- [ ] **Step 2: Escrever o teste de fumaça**

```csharp
[FumacaFact]
public async Task GarantirPermissoes_UneComOQueJaExisteNoSecureGateReal()
{
	var perfil = await f.CriarPerfilAsync("fumaca-permissao");

	var primeira = await f.Gestao.GarantirPermissoesAsync(perfil, [IntranetPermissoes.Diretorio.Read]);
	primeira.IsSuccess.Should().BeTrue();

	var segunda = await f.Gestao.GarantirPermissoesAsync(perfil, [IntranetPermissoes.Diretorio.Manage]);
	segunda.IsSuccess.Should().BeTrue();

	var detalhe = await f.Gestao.ObterPerfilAsync(perfil);
	detalhe.Value.Permissoes.Should().BeEquivalentTo([IntranetPermissoes.Diretorio.Read, IntranetPermissoes.Diretorio.Manage]);

	await f.ExcluirPerfilAsync(perfil);
}
```

Ajuste `f.CriarPerfilAsync`/`f.ExcluirPerfilAsync` para os helpers reais da fixture já usados pelos
testes de perfil vizinhos neste mesmo arquivo (nome exato pode diferir).

- [ ] **Step 3: Rodar contra o SecureGate real (roteiro da seção 0 do documento de fumaça)**

Run: `SECCO_SMOKE_SECUREGATE_URL=http://localhost:4101 dotnet test tests/Secco.Intranet.Tests --filter SecureGateGestaoDeAcessoFumacaTests`
Expected: PASS, incluindo o teste novo

- [ ] **Step 4: Atualizar a tabela de cenários do roteiro**

Em `docs/roteiro-fumaca-securegate.md`, acrescentar uma linha na tabela de "O que cobrem":
`| Garantir permissão de um perfil (mesclagem) | GetRolePermissionsAsync/SetRolePermissionsAsync reais nunca removem o que já estava lá |`
e atualizar a contagem de testes automatizados (de 9 para 10) e a data da última execução completa.

- [ ] **Step 5: Commit**

```bash
git add tests/Secco.Intranet.Tests/Smoke/SecureGateGestaoDeAcessoFumacaTests.cs docs/roteiro-fumaca-securegate.md
git commit -m "test(acesso): fumaca real de GarantirPermissoesAsync contra o SecureGate"
```

---

## Task 13: Documentação — correção da ADR-0001, roadmap e README

**Files:**
- Modify: `docs/adr/secco-intranet-adrs.md` (nota de revisão na ADR-0001)
- Modify: `docs/roadmap.md`
- Modify: `README.md`

- [ ] **Step 1: Nota de revisão na ADR-0001**

No final da seção "Consequências" da ADR-0001, no mesmo estilo da nota de revisão já existente
("revisto em 2026-09-12: ..."), acrescentar:

```markdown
- **Revisto em 2026-09-27:** o formato de claim ilustrado acima (`intranet:{slug}:{recurso}:{ação}`)
  não é válido no formato real da plataforma (`SeccoPermissions`, ADR-0021 da plataforma: um único
  `:`, kebab-case). O formato real e a geração automática (deixou de ser manual) estão no
  [modelo de permissões](../specs/2026-09-27-modelo-de-permissoes-design.md): `setor-{slug}:read` e
  `setor-{slug}:write`, garantidos ao criar ou editar o setor.
```

- [ ] **Step 2: Roadmap**

Em `docs/roadmap.md`, no item "Área administrativa de acesso" (Fase 2) ou onde a spec de permissões
foi referenciada quando escrita, marcar como entregue e linkar o plano. Adicionar/ajustar a linha do
Inventário para citar `inventario:read` como entregue.

- [ ] **Step 3: README**

Se o README já documenta perfis do produto (`diretorio-admin`, `inventario-admin`), acrescentar um
parágrafo curto sobre o perfil agrupador (`todos` como exemplo, não como nome obrigatório) e a ação
"Reconciliar permissões" na tela de Perfis, com uma frase sobre quando rodá-la (depois de atualizar
para esta versão, ou se um setor antigo parecer sem permissão).

- [ ] **Step 4: Rodar a suíte inteira uma última vez**

Run: `dotnet build` (0 avisos), `dotnet test tests/Secco.Intranet.Tests` (verde, fumaças puladas)

- [ ] **Step 5: Commit**

```bash
git add docs/adr/secco-intranet-adrs.md docs/roadmap.md README.md
git commit -m "docs(acesso): corrige formato de claim na ADR-0001 e documenta o modelo de permissoes entregue"
```
