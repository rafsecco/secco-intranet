# Controle de Inventário — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recurso de inventário sem setor dono — CRUD, máquina de estado (Disponível/Em uso/Em manutenção/Baixado) e autorização própria (`intranet-admin` OU `inventario-admin`), com a rota inteira bloqueada para qualquer outro usuário.

**Architecture:** Segue o molde já usado por Setor/Documento: entidade rica no Domain, handlers `Result<T>` na Application, EF Core + migrations nos dois providers na Infrastructure, controller fino + views nos dois temas na Web. A novidade é a autorização: em vez de Role derivada de slug (`{slug}-admin`), duas Roles fixas checadas por um helper novo (`AcessoAdministrativo`), e uma peça de infraestrutura de teste nova (simular role via header) porque o ambiente `Testing` nunca registra autenticação real.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 (SqlServer + Postgres), `Secco.SharedKernel.Results`/`Pagination`, xUnit + AwesomeAssertions.

**Spec:** [`docs/specs/2026-09-13-inventario-design.md`](../specs/2026-09-13-inventario-design.md)

## Global Constraints

- **Commits vão direto na `main`**, por caminho explícito — nunca `git add -A`.
- **Controller nunca acessa `DbContext`/repositório** (ADR-0002 regra 1).
- **View recebe ViewModel/DTO, nunca entidade de domínio** (ADR-0002 regra 2).
- **Erro de negócio é `Result<T>`**; `DomainInvariantException` é rede de segurança para chamador interno, não o caminho normal — todo handler pré-checa a invariante antes de chamar o método de domínio (mesmo padrão de `EditarSetorHandler`).
- **`GetByIdAsync` devolve desrastreado (`AsNoTracking`); `GetParaEdicaoAsync` devolve rastreado.** Todo handler de escrita usa `GetParaEdicaoAsync` — usar o errado grava nada e devolve sucesso (bug real já visto neste projeto).
- **Autorização é responsabilidade do Web, não da Application.** Nenhum handler de Inventário recebe `ClaimsPrincipal`; o controller decide antes de chamar o handler.
- **A Área administrativa (ADR-0008) reaproveita `AcessoAdministrativo`** — não duplicar a checagem de `intranet-admin` em outro lugar.
- **Sem tela de conceder `inventario-admin` a um usuário existente** — bloqueado por `secco-platform#26`. Não tentar contornar com uma tabela local (violaria ADR-0001).
- **Nenhum enum recebe `HasConversion`** — segue o padrão (int) já usado no resto do produto.
- Build precisa terminar com **0 avisos**; a suíte inteira verde antes de cada commit.

---

## Task 1: Infra de teste — simular usuário com role específica

O ambiente `Testing` nunca registra autenticação real (`IntranetWebFactory`, comentário na classe) — sem isso, nenhum teste de integração consegue provar "usuário sem a role X recebe 403". Esta peça é só de teste; não existe em produção.

**Files:**
- Create: `tests/Secco.Intranet.Tests/Integration/TestAuthentication/RolesDeTesteMiddleware.cs`
- Create: `tests/Secco.Intranet.Tests/Integration/TestAuthentication/RolesDeTesteStartupFilter.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/RolesDeTesteMiddlewareTests.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs`

**Interfaces:**
- Produces: `RolesDeTesteMiddleware.Header` (const `"X-Test-Roles"`, valor separado por vírgula); `RolesDeTesteStartupFilter` (implementa `IStartupFilter`).

- [ ] **Step 1: Escrever o middleware e o teste unitário dele**

```csharp
// tests/Secco.Intranet.Tests/Integration/TestAuthentication/RolesDeTesteMiddleware.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Tests.Integration.TestAuthentication;

/// <summary>
/// Simula um usuário autenticado com roles específicas, lendo o header <see cref="Header"/>.
/// Existe só nos testes: o pipeline real nunca registra autenticação no ambiente
/// <c>Testing</c> (ver <c>IntranetWebFactory</c>), então esta é a única forma de exercitar
/// autorização por role através do host HTTP real. Sem o header, não faz nada — o
/// <c>ClaimsPrincipal</c> anônimo padrão segue intacto, e os testes que não usam isto
/// continuam se comportando exatamente como hoje.
/// </summary>
/// <param name="next">Próximo middleware do pipeline.</param>
public sealed class RolesDeTesteMiddleware(RequestDelegate next)
{
	/// <summary>Header lido: roles separadas por vírgula.</summary>
	public const string Header = "X-Test-Roles";

	/// <summary>Processa a requisição.</summary>
	/// <param name="context">Contexto HTTP da requisição atual.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		var valor = context.Request.Headers[Header].ToString();

		if (!string.IsNullOrWhiteSpace(valor))
		{
			var roles = valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			var claims = roles.Select(role => new Claim(SeccoClaims.Role, role));
			context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Teste"));
		}

		await next(context).ConfigureAwait(false);
	}
}
```

```csharp
// tests/Secco.Intranet.Tests/Unit/RolesDeTesteMiddlewareTests.cs
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class RolesDeTesteMiddlewareTests
{
	[Fact]
	public async Task SemHeader_NaoAlteraUsuario()
	{
		var context = new DefaultHttpContext();
		var usuarioOriginal = context.User;
		var middleware = new RolesDeTesteMiddleware(_ => Task.CompletedTask);

		await middleware.InvokeAsync(context);

		context.User.Should().BeSameAs(usuarioOriginal);
	}

	[Fact]
	public async Task ComHeader_DefineClaimsDeRole()
	{
		var context = new DefaultHttpContext();
		context.Request.Headers[RolesDeTesteMiddleware.Header] = "intranet-admin, inventario-admin";
		var middleware = new RolesDeTesteMiddleware(_ => Task.CompletedTask);

		await middleware.InvokeAsync(context);

		context.User.FindAll(SeccoClaims.Role).Select(c => c.Value)
			.Should().BeEquivalentTo(["intranet-admin", "inventario-admin"]);
	}
}
```

- [ ] **Step 2: Rodar o teste unitário**

Run: `dotnet test --filter RolesDeTesteMiddlewareTests`
Expected: PASS (2 testes)

- [ ] **Step 3: Registrar o middleware no host de teste via `IStartupFilter`**

`SeccoApiFactory.ConfigureWebHost` é selado — a extensão acontece por `IStartupFilter` registrado em `ConfigureTestServices`, que roda antes do restante do pipeline (a mesma posição de `DevelopmentTenantMiddleware` em produção).

```csharp
// tests/Secco.Intranet.Tests/Integration/TestAuthentication/RolesDeTesteStartupFilter.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Secco.Intranet.Tests.Integration.TestAuthentication;

/// <summary>Insere <see cref="RolesDeTesteMiddleware"/> no início do pipeline de teste.</summary>
internal sealed class RolesDeTesteStartupFilter : IStartupFilter
{
	public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
		app =>
		{
			app.UseMiddleware<RolesDeTesteMiddleware>();
			next(app);
		};
}
```

Em `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs`, adicionar o override (a classe já existe; acrescentar ao lado de `ConfigureTestConfiguration`):

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Tests.Integration.TestAuthentication;
// ... usings existentes seguem

protected override void ConfigureTestServices(IServiceCollection services) =>
	services.AddSingleton<IStartupFilter, RolesDeTesteStartupFilter>();
```

- [ ] **Step 4: Rodar a suíte inteira para confirmar que nada quebrou**

Run: `dotnet test`
Expected: PASS (179 + 2 = 181 testes; nenhum teste existente usa o header novo, então nenhum comportamento muda)

- [ ] **Step 5: Commit**

```bash
git add tests/Secco.Intranet.Tests/Integration/TestAuthentication/RolesDeTesteMiddleware.cs tests/Secco.Intranet.Tests/Integration/TestAuthentication/RolesDeTesteStartupFilter.cs tests/Secco.Intranet.Tests/Unit/RolesDeTesteMiddlewareTests.cs tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs
git commit -m "test: infraestrutura para simular usuario com role especifica

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 2: Domínio — `ItemInventario`

**Files:**
- Create: `src/Secco.Intranet.Domain/Inventario/ItemInventario.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/ItemInventarioTests.cs`

**Interfaces:**
- Produces: `enum StatusDoItem { Disponivel, EmUso, EmManutencao, Baixado }`; `ItemInventario(string nome, string? descricao, string? categoria, string? codigoPatrimonio, Guid? setorId)`; propriedades `Nome`, `Descricao`, `Categoria`, `CodigoPatrimonio`, `SetorId`, `Status`, `AtribuidoAUsuarioId`, `AtribuidoANome`, `CreatedAt`; métodos `Editar(...)`, `Atribuir(Guid usuarioId, string? usuarioNome)`, `Desatribuir()`, `EnviarParaManutencao()`, `VoltarDaManutencao()`, `Baixar()`.

- [ ] **Step 1: Escrever os testes de domínio**

```csharp
// tests/Secco.Intranet.Tests/Unit/ItemInventarioTests.cs
using AwesomeAssertions;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Item de inventário: sem setor dono (revisão da ADR-0001 em 2026-09-12), com a máquina de
/// estado da spec — Baixar() é terminal, o resto é regra local de cada transição.
/// </summary>
public class ItemInventarioTests
{
	private static ItemInventario Criar(string nome = "Notebook Dell Latitude") =>
		new(nome, descricao: null, categoria: null, codigoPatrimonio: null, setorId: null);

	[Fact]
	public void Criar_NomeVazio_Lanca()
	{
		var acao = () => new ItemInventario(" ", null, null, null, null);

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_NasceDisponivel()
	{
		Criar().Status.Should().Be(StatusDoItem.Disponivel);
	}

	[Fact]
	public void Atribuir_APartirDeDisponivel_MudaParaEmUso()
	{
		var item = Criar();
		var usuarioId = Guid.NewGuid();

		item.Atribuir(usuarioId, "ana@exemplo.local");

		item.Status.Should().Be(StatusDoItem.EmUso);
		item.AtribuidoAUsuarioId.Should().Be(usuarioId);
		item.AtribuidoANome.Should().Be("ana@exemplo.local");
	}

	[Fact]
	public void Atribuir_APartirDeEmUso_Reatribui()
	{
		var item = Criar();
		item.Atribuir(Guid.NewGuid(), "ana@exemplo.local");
		var novoUsuarioId = Guid.NewGuid();

		item.Atribuir(novoUsuarioId, "bruno@exemplo.local");

		item.AtribuidoAUsuarioId.Should().Be(novoUsuarioId);
		item.AtribuidoANome.Should().Be("bruno@exemplo.local");
	}

	[Fact]
	public void Atribuir_UsuarioVazio_Lanca()
	{
		var item = Criar();

		var acao = () => item.Atribuir(Guid.Empty, "ana@exemplo.local");

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Atribuir_APartirDeEmManutencao_Lanca()
	{
		var item = Criar();
		item.EnviarParaManutencao();

		var acao = () => item.Atribuir(Guid.NewGuid(), "ana@exemplo.local");

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Desatribuir_APartirDeEmUso_VoltaADisponivel()
	{
		var item = Criar();
		item.Atribuir(Guid.NewGuid(), "ana@exemplo.local");

		item.Desatribuir();

		item.Status.Should().Be(StatusDoItem.Disponivel);
		item.AtribuidoAUsuarioId.Should().BeNull();
		item.AtribuidoANome.Should().BeNull();
	}

	[Fact]
	public void Desatribuir_APartirDeDisponivel_Lanca()
	{
		var item = Criar();

		var acao = item.Desatribuir;

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void EnviarParaManutencao_MantemAtribuido()
	{
		var item = Criar();
		item.Atribuir(Guid.NewGuid(), "ana@exemplo.local");

		item.EnviarParaManutencao();

		item.Status.Should().Be(StatusDoItem.EmManutencao);
		item.AtribuidoANome.Should().Be("ana@exemplo.local");
	}

	[Fact]
	public void VoltarDaManutencao_ComAtribuido_VoltaAEmUso()
	{
		var item = Criar();
		item.Atribuir(Guid.NewGuid(), "ana@exemplo.local");
		item.EnviarParaManutencao();

		item.VoltarDaManutencao();

		item.Status.Should().Be(StatusDoItem.EmUso);
	}

	[Fact]
	public void VoltarDaManutencao_SemAtribuido_VoltaADisponivel()
	{
		var item = Criar();
		item.EnviarParaManutencao();

		item.VoltarDaManutencao();

		item.Status.Should().Be(StatusDoItem.Disponivel);
	}

	[Fact]
	public void VoltarDaManutencao_SemEstarEmManutencao_Lanca()
	{
		var item = Criar();

		var acao = item.VoltarDaManutencao;

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Baixar_ETerminal()
	{
		var item = Criar();

		item.Baixar();

		item.Status.Should().Be(StatusDoItem.Baixado);
	}

	[Theory]
	[InlineData(nameof(ItemInventario.Desatribuir))]
	[InlineData(nameof(ItemInventario.EnviarParaManutencao))]
	[InlineData(nameof(ItemInventario.VoltarDaManutencao))]
	[InlineData(nameof(ItemInventario.Baixar))]
	public void QualquerMetodo_ApósBaixado_Lanca(string metodo)
	{
		var item = Criar();
		item.Baixar();

		Action acao = metodo switch
		{
			nameof(ItemInventario.Desatribuir) => item.Desatribuir,
			nameof(ItemInventario.EnviarParaManutencao) => item.EnviarParaManutencao,
			nameof(ItemInventario.VoltarDaManutencao) => item.VoltarDaManutencao,
			nameof(ItemInventario.Baixar) => item.Baixar,
			_ => throw new InvalidOperationException(),
		};

		acao.Should().Throw<DomainInvariantException>("um item baixado é terminal");
	}

	[Fact]
	public void Editar_AposBaixado_Lanca()
	{
		var item = Criar();
		item.Baixar();

		var acao = () => item.Editar("Novo nome", null, null, null, null);

		acao.Should().Throw<DomainInvariantException>();
	}
}
```

- [ ] **Step 2: Rodar os testes para confirmar que falham (a entidade ainda não existe)**

Run: `dotnet test --filter ItemInventarioTests`
Expected: FAIL (erro de compilação — `Secco.Intranet.Domain.Inventario` não existe)

- [ ] **Step 3: Implementar a entidade**

```csharp
// src/Secco.Intranet.Domain/Inventario/ItemInventario.cs
using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Inventario;

/// <summary>Situação de um item de inventário.</summary>
public enum StatusDoItem
{
	/// <summary>Livre para ser atribuído.</summary>
	Disponivel = 0,

	/// <summary>Em uso por alguém (<see cref="ItemInventario.AtribuidoAUsuarioId"/>).</summary>
	EmUso = 1,

	/// <summary>Em manutenção — não pode ser atribuído até voltar.</summary>
	EmManutencao = 2,

	/// <summary>Baixado. Terminal: nenhuma outra transição é aceita.</summary>
	Baixado = 3,
}

/// <summary>
/// Item de inventário. Não pertence a nenhum Setor — <see cref="SetorId"/> é informativo
/// ("este item está com o Financeiro"), não dá autorização; quem administra é a Role
/// <c>inventario-admin</c> (ou <c>intranet-admin</c>), ver <c>AcessoAdministrativo</c>.
/// Decisão revista em 2026-09-12: a ADR-0001 original cogitava o setor Infraestrutura como
/// "dono nato" deste recurso.
/// </summary>
public sealed class ItemInventario : BaseEntity
{
	private ItemInventario()
	{
		// Construtor de rehidratação do EF Core
		Nome = string.Empty;
	}

	/// <summary>Cria um item de inventário, sempre nascendo <see cref="StatusDoItem.Disponivel"/>.</summary>
	/// <param name="nome">Nome de exibição. Obrigatório.</param>
	/// <param name="descricao">Descrição livre. Opcional.</param>
	/// <param name="categoria">Categoria livre. Opcional — sem catálogo fechado (YAGNI).</param>
	/// <param name="codigoPatrimonio">Código de patrimônio livre, sem unicidade. Opcional.</param>
	/// <param name="setorId">Setor onde o item está, informativo. Opcional.</param>
	/// <exception cref="DomainInvariantException">Se o nome for nulo ou vazio.</exception>
	public ItemInventario(string nome, string? descricao, string? categoria, string? codigoPatrimonio, Guid? setorId)
	{
		ValidarNome(nome);

		Nome = nome;
		Descricao = descricao;
		Categoria = categoria;
		CodigoPatrimonio = codigoPatrimonio;
		SetorId = setorId;
		Status = StatusDoItem.Disponivel;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Nome de exibição (coluna <c>ds_nome</c>).</summary>
	public string Nome { get; private set; }

	/// <summary>Descrição livre (coluna <c>ds_descricao</c>).</summary>
	public string? Descricao { get; private set; }

	/// <summary>Categoria livre (coluna <c>ds_categoria</c>).</summary>
	public string? Categoria { get; private set; }

	/// <summary>Código de patrimônio, sem unicidade forçada (coluna <c>ds_codigo_patrimonio</c>).</summary>
	public string? CodigoPatrimonio { get; private set; }

	/// <summary>Setor onde o item está — informativo, não dá autorização (coluna <c>id_fk_setor</c>).</summary>
	public Guid? SetorId { get; private set; }

	/// <summary>Situação atual (coluna <c>ie_status</c>).</summary>
	public StatusDoItem Status { get; private set; }

	/// <summary>
	/// Usuário do SecureGate a quem o item está atribuído (coluna <c>atribuido_a_usuario_id</c>
	/// — sem prefixo <c>id_fk_</c>: não é uma FK reconhecida pelo EF, porque não há tabela
	/// local de usuário para relacionar; identidade vive só no SecureGate, ADR-0006).
	/// </summary>
	public Guid? AtribuidoAUsuarioId { get; private set; }

	/// <summary>
	/// Identificador em cache de quem tem o item — e-mail, não nome: o SecureGate não guarda
	/// nome de exibição (coluna <c>ds_atribuido_a_nome</c>). Mesmo padrão de
	/// <c>Documento.CriadoPor</c>, para não duplicar identidade (ADR-0006).
	/// </summary>
	public string? AtribuidoANome { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Altera os campos descritivos. Não mexe em status nem atribuição.</summary>
	/// <exception cref="DomainInvariantException">Nome vazio, ou item já <see cref="StatusDoItem.Baixado"/>.</exception>
	public void Editar(string nome, string? descricao, string? categoria, string? codigoPatrimonio, Guid? setorId)
	{
		GarantirNaoBaixado();
		ValidarNome(nome);

		Nome = nome;
		Descricao = descricao;
		Categoria = categoria;
		CodigoPatrimonio = codigoPatrimonio;
		SetorId = setorId;
	}

	/// <summary>Atribui o item a um usuário. Válido a partir de Disponível ou Em uso (reatribuição).</summary>
	/// <exception cref="DomainInvariantException">Usuário vazio, item Baixado, ou item Em manutenção.</exception>
	public void Atribuir(Guid usuarioId, string? usuarioNome)
	{
		GarantirNaoBaixado();

		if (Status is not (StatusDoItem.Disponivel or StatusDoItem.EmUso))
		{
			throw new DomainInvariantException("Atribuir só é válido a partir de Disponível ou Em uso.");
		}

		if (usuarioId == Guid.Empty)
		{
			throw new DomainInvariantException("Atribuir exige um usuário válido.");
		}

		AtribuidoAUsuarioId = usuarioId;
		AtribuidoANome = usuarioNome;
		Status = StatusDoItem.EmUso;
	}

	/// <summary>Libera o item. Só válido a partir de Em uso.</summary>
	/// <exception cref="DomainInvariantException">Item não está Em uso, ou está Baixado.</exception>
	public void Desatribuir()
	{
		GarantirNaoBaixado();

		if (Status != StatusDoItem.EmUso)
		{
			throw new DomainInvariantException("Só um item Em uso pode ser desatribuído.");
		}

		AtribuidoAUsuarioId = null;
		AtribuidoANome = null;
		Status = StatusDoItem.Disponivel;
	}

	/// <summary>Envia para manutenção. Não mexe em quem está atribuído — pode voltar para a mesma pessoa.</summary>
	/// <exception cref="DomainInvariantException">Item Baixado.</exception>
	public void EnviarParaManutencao()
	{
		GarantirNaoBaixado();

		Status = StatusDoItem.EmManutencao;
	}

	/// <summary>Volta da manutenção — Em uso se havia atribuição, Disponível caso contrário.</summary>
	/// <exception cref="DomainInvariantException">Item não está Em manutenção.</exception>
	public void VoltarDaManutencao()
	{
		GarantirNaoBaixado();

		if (Status != StatusDoItem.EmManutencao)
		{
			throw new DomainInvariantException("Só um item Em manutenção pode voltar da manutenção.");
		}

		Status = AtribuidoAUsuarioId is null ? StatusDoItem.Disponivel : StatusDoItem.EmUso;
	}

	/// <summary>Dá baixa no item. Terminal: nenhuma outra transição é aceita depois.</summary>
	/// <exception cref="DomainInvariantException">Item já Baixado.</exception>
	public void Baixar()
	{
		GarantirNaoBaixado();

		Status = StatusDoItem.Baixado;
	}

	private void GarantirNaoBaixado()
	{
		if (Status == StatusDoItem.Baixado)
		{
			throw new DomainInvariantException("Um item baixado não aceita mais alterações.");
		}
	}

	private static void ValidarNome(string nome)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Um item de inventário exige nome não vazio.");
		}
	}
}
```

- [ ] **Step 4: Rodar os testes até passarem**

Run: `dotnet test --filter ItemInventarioTests`
Expected: PASS (17 testes)

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Domain/Inventario/ItemInventario.cs tests/Secco.Intranet.Tests/Unit/ItemInventarioTests.cs
git commit -m "feat(inventario): entidade ItemInventario com maquina de estado

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 3: Autorização — `AcessoAdministrativo`

**Files:**
- Create: `src/Secco.Intranet.Web/Navigation/AcessoAdministrativo.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/AcessoAdministrativoTests.cs`

**Interfaces:**
- Produces: `AcessoAdministrativo.RoleIntranetAdmin` (const); `AcessoAdministrativo.TemAcesso(ClaimsPrincipal? usuario, string roleEspecifica) : bool`.

- [ ] **Step 1: Escrever os testes**

```csharp
// tests/Secco.Intranet.Tests/Unit/AcessoAdministrativoTests.cs
using System.Security.Claims;
using AwesomeAssertions;
using Secco.Intranet.Web.Navigation;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Generaliza o padrão de <see cref="SetorAcesso"/> para Roles fixas, não derivadas de slug
/// — o primeiro recurso a sair do molde <c>{slug}-admin</c> do ADR-0001 (ADR-0008).
/// </summary>
public class AcessoAdministrativoTests
{
	private static ClaimsPrincipal Usuario(params string[] roles) =>
		new(new ClaimsIdentity(roles.Select(role => new Claim(SeccoClaims.Role, role)), authenticationType: "Teste"));

	[Fact]
	public void UsuarioNulo_SemAcesso()
	{
		AcessoAdministrativo.TemAcesso(null, "inventario-admin").Should().BeFalse();
	}

	[Fact]
	public void SemNenhumaRole_SemAcesso()
	{
		AcessoAdministrativo.TemAcesso(Usuario(), "inventario-admin").Should().BeFalse();
	}

	[Fact]
	public void ComRoleEspecifica_TemAcesso()
	{
		AcessoAdministrativo.TemAcesso(Usuario("inventario-admin"), "inventario-admin").Should().BeTrue();
	}

	[Fact]
	public void ComIntranetAdmin_TemAcessoMesmoSemARoleEspecifica()
	{
		AcessoAdministrativo.TemAcesso(Usuario("intranet-admin"), "inventario-admin").Should().BeTrue();
	}

	[Fact]
	public void ComOutraRoleQualquer_SemAcesso()
	{
		AcessoAdministrativo.TemAcesso(Usuario("financeiro-admin"), "inventario-admin").Should().BeFalse(
			"admin de setor não é atalho para uma Role fixa — são modelos de autorização independentes");
	}
}
```

- [ ] **Step 2: Rodar os testes para confirmar que falham**

Run: `dotnet test --filter AcessoAdministrativoTests`
Expected: FAIL (compilação — a classe não existe)

- [ ] **Step 3: Implementar**

```csharp
// src/Secco.Intranet.Web/Navigation/AcessoAdministrativo.cs
using System.Security.Claims;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Checa Roles fixas (não derivadas de slug de setor) — o modelo de autorização do
/// Inventário e da futura Área administrativa (ADR-0008). Diferente de
/// <see cref="SetorAcesso"/>, que deriva slug de um sufixo <c>-admin</c>/<c>-user</c>; aqui a
/// Role é um nome exato, e <see cref="RoleIntranetAdmin"/> sempre concede acesso, qualquer que
/// seja a Role específica pedida — é o superusuário da instalação.
/// </summary>
public static class AcessoAdministrativo
{
	/// <summary>Role do superusuário da instalação — sempre tem acesso a tudo (ADR-0008).</summary>
	public const string RoleIntranetAdmin = "intranet-admin";

	/// <summary>
	/// Indica se o usuário administra o recurso: tem <see cref="RoleIntranetAdmin"/> OU a Role
	/// específica informada.
	/// </summary>
	/// <param name="usuario">Usuário atual; <c>null</c> devolve <c>false</c>.</param>
	/// <param name="roleEspecifica">Role fixa do recurso (ex.: <c>inventario-admin</c>).</param>
	public static bool TemAcesso(ClaimsPrincipal? usuario, string roleEspecifica)
	{
		if (usuario is null)
		{
			return false;
		}

		return usuario.FindAll(SeccoClaims.Role).Any(claim =>
			string.Equals(claim.Value, RoleIntranetAdmin, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(claim.Value, roleEspecifica, StringComparison.OrdinalIgnoreCase));
	}
}
```

- [ ] **Step 4: Rodar os testes até passarem**

Run: `dotnet test --filter AcessoAdministrativoTests`
Expected: PASS (5 testes)

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Web/Navigation/AcessoAdministrativo.cs tests/Secco.Intranet.Tests/Unit/AcessoAdministrativoTests.cs
git commit -m "feat(inventario): helper de autorizacao para roles fixas

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 4: Application — portas + `CriarItemInventarioHandler`

**Files:**
- Create: `src/Secco.Intranet.Application/Inventario/IItemInventarioRepository.cs`
- Create: `src/Secco.Intranet.Application/Inventario/ItemInventarioDto.cs`
- Create: `src/Secco.Intranet.Application/Inventario/CriarItemInventarioHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs`
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/CriarItemInventarioHandlerTests.cs`

**Interfaces:**
- Consumes: `ItemInventario` (Task 2), `ITrilhaDeAuditoria`/`RegistroDeAuditoria` (já existentes).
- Produces: `IItemInventarioRepository` (`AddAsync`, `GetByIdAsync` desrastreado, `GetParaEdicaoAsync` rastreado, `SearchAsync`, `SaveChangesAsync`); `ItemInventarioSearchCriteria(string? NomeContains, StatusDoItem? Status, Guid? SetorId, PageRequest? Page)`; `ItemInventarioDto` com `FromEntity`; `CriarItemInventarioCommand(string? Nome, string? Descricao, string? Categoria, string? CodigoPatrimonio, Guid? SetorId)`; `CriarItemInventarioHandler.HandleAsync(...)`.

- [ ] **Step 1: Definir a porta, o DTO e os critérios de busca**

```csharp
// src/Secco.Intranet.Application/Inventario/IItemInventarioRepository.cs
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Filtros da busca de itens de inventário. Todos opcionais; combinados com AND.</summary>
/// <param name="NomeContains">Trecho contido no nome.</param>
/// <param name="Status">Quando informado, restringe a esse status.</param>
/// <param name="ApenasNaoBaixados">
/// Quando <c>true</c> e <see cref="Status"/> não for informado, exclui itens Baixados — é o
/// padrão da listagem (mesmo comportamento de Setor inativo/Documento arquivado).
/// </param>
/// <param name="SetorId">Quando informado, restringe ao setor.</param>
/// <param name="Page">Paginação (1-based).</param>
public sealed record ItemInventarioSearchCriteria(
	string? NomeContains = null,
	StatusDoItem? Status = null,
	bool ApenasNaoBaixados = false,
	Guid? SetorId = null,
	PageRequest? Page = null)
{
	/// <summary>Paginação efetiva (default da plataforma quando não informada).</summary>
	public PageRequest EffectivePage => Page ?? PageRequest.Default;
}

/// <summary>Porta de persistência de itens de inventário — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IItemInventarioRepository
{
	/// <summary>Persiste um item novo.</summary>
	Task AddAsync(ItemInventario item, CancellationToken cancellationToken = default);

	/// <summary>Busca um item desrastreado — caminho de leitura.</summary>
	Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Busca um item <b>rastreado</b>, para alteração. Diferente de <see cref="GetByIdAsync"/>:
	/// alterar aquele resultado e chamar <see cref="SaveChangesAsync"/> não gravaria nada.
	/// </summary>
	Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Busca paginada, mais recentes primeiro.</summary>
	Task<PagedResult<ItemInventario>> SearchAsync(
		ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default);

	/// <summary>Grava alterações pendentes de um item já rastreado.</summary>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

```csharp
// src/Secco.Intranet.Application/Inventario/ItemInventarioDto.cs
using Secco.Intranet.Domain.Inventario;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Representação de leitura de um item de inventário — a entidade nunca cruza a borda HTTP.</summary>
public sealed record ItemInventarioDto(
	Guid Id,
	string Nome,
	string? Descricao,
	string? Categoria,
	string? CodigoPatrimonio,
	Guid? SetorId,
	StatusDoItem Status,
	Guid? AtribuidoAUsuarioId,
	string? AtribuidoANome,
	DateTimeOffset CreatedAt)
{
	/// <summary>Projeta a entidade para o DTO.</summary>
	public static ItemInventarioDto FromEntity(ItemInventario entity) => new(
		entity.Id,
		entity.Nome,
		entity.Descricao,
		entity.Categoria,
		entity.CodigoPatrimonio,
		entity.SetorId,
		entity.Status,
		entity.AtribuidoAUsuarioId,
		entity.AtribuidoANome,
		entity.CreatedAt);
}
```

- [ ] **Step 2: Acrescentar os erros e o verbo de auditoria**

Em `src/Secco.Intranet.Application/IntranetErrors.cs`, acrescentar (dentro da classe `IntranetErrors`, ao lado de `Publicacoes`):

```csharp
	/// <summary>Erros do recurso Inventário.</summary>
	public static class Inventario
	{
		/// <summary>Nome ausente ou vazio.</summary>
		public static readonly Error NomeRequired =
			Error.Validation("Intranet.Inventario.NomeRequired", "O nome é obrigatório.");

		/// <summary>Nome acima do limite configurado.</summary>
		public static Error NomeTooLong(int limit) =>
			Error.Validation("Intranet.Inventario.NomeTooLong", $"O nome excede o limite de {limit} caracteres.");

		/// <summary>Registro não encontrado no banco do tenant atual.</summary>
		public static readonly Error NotFound =
			Error.NotFound("Intranet.Inventario.NotFound", "Item de inventário não encontrado.");

		/// <summary>Tentativa de alterar um item já baixado — baixa é terminal.</summary>
		public static readonly Error ItemBaixado =
			Error.Validation(
				"Intranet.Inventario.ItemBaixado", "Um item baixado não aceita mais alterações.");

		/// <summary>Transição de status pedida não é válida a partir do status atual do item.</summary>
		public static readonly Error TransicaoInvalida =
			Error.Validation(
				"Intranet.Inventario.TransicaoInvalida",
				"Essa ação não é válida para o status atual do item.");

		/// <summary>Ação de atribuir sem um usuário informado.</summary>
		public static readonly Error UsuarioRequired =
			Error.Validation("Intranet.Inventario.UsuarioRequired", "Escolha um usuário para atribuir o item.");
	}
```

Em `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`, acrescentar em `VerbosDeAuditoria`:

```csharp
	/// <summary>Item de inventário criado.</summary>
	public const string InventarioCriar = "inventario.criar";

	/// <summary>Campos descritivos do item alterados.</summary>
	public const string InventarioEditar = "inventario.editar";

	/// <summary>Item atribuído a um usuário.</summary>
	public const string InventarioAtribuir = "inventario.atribuir";

	/// <summary>Item baixado.</summary>
	public const string InventarioBaixar = "inventario.baixar";
```

E em `RecursosDeAuditoria`:

```csharp
	/// <summary>Item de inventário.</summary>
	public const string Inventario = "inventario";
```

- [ ] **Step 3: Escrever o teste do handler de criação**

```csharp
// tests/Secco.Intranet.Tests/Unit/CriarItemInventarioHandlerTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class CriarItemInventarioHandlerTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public RegistroDeAuditoria? Ultimo { get; private set; }

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Ultimo = registro;

			return Task.CompletedTask;
		}
	}

	private sealed class RepositorioFalso : IItemInventarioRepository
	{
		public ItemInventario? Adicionado { get; private set; }

		public Task AddAsync(ItemInventario item, CancellationToken cancellationToken = default)
		{
			Adicionado = item;

			return Task.CompletedTask;
		}

		public Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<ItemInventario?>(null);

		public Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<ItemInventario?>(null);

		public Task<PagedResult<ItemInventario>> SearchAsync(
			ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<ItemInventario>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private static (CriarItemInventarioHandler Handler, RepositorioFalso Repositorio, TrilhaFalsa Trilha) Montar()
	{
		var repositorio = new RepositorioFalso();
		var trilha = new TrilhaFalsa();

		return (new CriarItemInventarioHandler(repositorio, new IntranetOptions(), trilha), repositorio, trilha);
	}

	[Fact]
	public async Task NomeVazio_DevolveErro()
	{
		var (handler, _, _) = Montar();

		var resultado = await handler.HandleAsync(new CriarItemInventarioCommand(" ", null, null, null, null));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Inventario.NomeRequired);
	}

	[Fact]
	public async Task NomeAcimaDoLimite_DevolveErro()
	{
		var (handler, _, _) = Montar();
		var nome = new string('a', 300);

		var resultado = await handler.HandleAsync(new CriarItemInventarioCommand(nome, null, null, null, null));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Code.Should().Be(IntranetErrors.Inventario.NomeTooLong(256).Code);
	}

	[Fact]
	public async Task Valido_GravaERegistraAuditoria()
	{
		var (handler, repositorio, trilha) = Montar();

		var resultado = await handler.HandleAsync(
			new CriarItemInventarioCommand("Notebook Dell", "Descrição", "Equipamento", "PAT-001", null));

		resultado.IsSuccess.Should().BeTrue();
		repositorio.Adicionado.Should().NotBeNull();
		repositorio.Adicionado!.Nome.Should().Be("Notebook Dell");
		trilha.Ultimo!.Verbo.Should().Be(VerbosDeAuditoria.InventarioCriar);
		trilha.Ultimo.Recurso.Should().Be(RecursosDeAuditoria.Inventario);
	}
}
```

- [ ] **Step 4: Rodar para confirmar que falha (o handler ainda não existe)**

Run: `dotnet test --filter CriarItemInventarioHandlerTests`
Expected: FAIL (compilação)

- [ ] **Step 5: Implementar o handler**

```csharp
// src/Secco.Intranet.Application/Inventario/CriarItemInventarioHandler.cs
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Comando de criação de um item de inventário.</summary>
public sealed record CriarItemInventarioCommand(
	string? Nome, string? Descricao, string? Categoria, string? CodigoPatrimonio, Guid? SetorId);

/// <summary>
/// Caso de uso: cria um item de inventário. Sem provisionamento de Role — diferente de
/// Setor, este recurso não pertence a ninguém em específico (ADR-0001 revisada).
/// </summary>
public sealed class CriarItemInventarioHandler(
	IItemInventarioRepository repository, IntranetOptions options, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ItemInventarioDto>> HandleAsync(
		CriarItemInventarioCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Nome))
		{
			return IntranetErrors.Inventario.NomeRequired;
		}

		if (command.Nome.Length > options.MaxNameLength)
		{
			return IntranetErrors.Inventario.NomeTooLong(options.MaxNameLength);
		}

		var item = new ItemInventario(command.Nome, command.Descricao, command.Categoria, command.CodigoPatrimonio, command.SetorId);

		await repository.AddAsync(item, cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.InventarioCriar,
					RecursosDeAuditoria.Inventario,
					item.Id.ToString(),
					JsonSerializer.Serialize(new { nome = item.Nome, categoria = item.Categoria, status = item.Status.ToString() })),
				cancellationToken)
			.ConfigureAwait(false);

		return ItemInventarioDto.FromEntity(item);
	}
}
```

- [ ] **Step 6: Rodar os testes até passarem**

Run: `dotnet test --filter CriarItemInventarioHandlerTests`
Expected: PASS (3 testes)

- [ ] **Step 7: Build completo sem avisos**

Run: `dotnet build`
Expected: 0 Erro(s), 0 Aviso(s)

- [ ] **Step 8: Commit**

```bash
git add src/Secco.Intranet.Application/Inventario/IItemInventarioRepository.cs src/Secco.Intranet.Application/Inventario/ItemInventarioDto.cs src/Secco.Intranet.Application/Inventario/CriarItemInventarioHandler.cs src/Secco.Intranet.Application/IntranetErrors.cs src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs tests/Secco.Intranet.Tests/Unit/CriarItemInventarioHandlerTests.cs
git commit -m "feat(inventario): portas da Application e handler de criacao

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 5: Application — `EditarItemInventarioHandler`

**Files:**
- Create: `src/Secco.Intranet.Application/Inventario/EditarItemInventarioHandler.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/EditarItemInventarioHandlerTests.cs`

**Interfaces:**
- Consumes: `IItemInventarioRepository` (Task 4), `ItemInventario.Editar(...)` (Task 2).
- Produces: `EditarItemInventarioCommand(Guid Id, string? Nome, string? Descricao, string? Categoria, string? CodigoPatrimonio, Guid? SetorId)`; `EditarItemInventarioHandler.HandleAsync(...)`.

- [ ] **Step 1: Escrever os testes**

```csharp
// tests/Secco.Intranet.Tests/Unit/EditarItemInventarioHandlerTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class EditarItemInventarioHandlerTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class RepositorioFalso(ItemInventario? item) : IItemInventarioRepository
	{
		public bool Gravou { get; private set; }

		public Task AddAsync(ItemInventario novo, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(item);

		public Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(item);

		public Task<PagedResult<ItemInventario>> SearchAsync(
			ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<ItemInventario>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			Gravou = true;

			return Task.CompletedTask;
		}
	}

	private static EditarItemInventarioHandler Handler(RepositorioFalso repositorio) =>
		new(repositorio, new IntranetOptions(), new TrilhaFalsa());

	[Fact]
	public async Task ItemInexistente_DevolveNotFound()
	{
		var handler = Handler(new RepositorioFalso(null));

		var resultado = await handler.HandleAsync(new EditarItemInventarioCommand(Guid.NewGuid(), "X", null, null, null, null));

		resultado.Error.Should().Be(IntranetErrors.Inventario.NotFound);
	}

	[Fact]
	public async Task ItemBaixado_DevolveErro()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		item.Baixar();
		var handler = Handler(new RepositorioFalso(item));

		var resultado = await handler.HandleAsync(
			new EditarItemInventarioCommand(item.Id, "Novo nome", null, null, null, null));

		resultado.Error.Should().Be(IntranetErrors.Inventario.ItemBaixado);
	}

	[Fact]
	public async Task Valido_AlteraEGrava()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var repositorio = new RepositorioFalso(item);
		var handler = Handler(repositorio);

		var resultado = await handler.HandleAsync(
			new EditarItemInventarioCommand(item.Id, "Notebook Dell", "Descrição nova", "TI", "PAT-002", null));

		resultado.IsSuccess.Should().BeTrue();
		repositorio.Gravou.Should().BeTrue();
		item.Nome.Should().Be("Notebook Dell");
		item.CodigoPatrimonio.Should().Be("PAT-002");
	}
}
```

- [ ] **Step 2: Rodar para confirmar que falha**

Run: `dotnet test --filter EditarItemInventarioHandlerTests`
Expected: FAIL (compilação)

- [ ] **Step 3: Implementar**

```csharp
// src/Secco.Intranet.Application/Inventario/EditarItemInventarioHandler.cs
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Pedido de edição dos campos descritivos de um item — status não entra aqui.</summary>
public sealed record EditarItemInventarioCommand(
	Guid Id, string? Nome, string? Descricao, string? Categoria, string? CodigoPatrimonio, Guid? SetorId);

/// <summary>
/// Altera nome, descrição, categoria, código de patrimônio e setor informativo. A invariante
/// "item Baixado não edita" é checada aqui, antes de chamar o domínio (ADR-0004) — a exceção
/// do domínio é rede de segurança, não o caminho normal.
/// </summary>
public sealed class EditarItemInventarioHandler(
	IItemInventarioRepository repository, IntranetOptions options, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ItemInventarioDto>> HandleAsync(
		EditarItemInventarioCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Nome))
		{
			return IntranetErrors.Inventario.NomeRequired;
		}

		if (command.Nome.Length > options.MaxNameLength)
		{
			return IntranetErrors.Inventario.NomeTooLong(options.MaxNameLength);
		}

		var item = await repository.GetParaEdicaoAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return IntranetErrors.Inventario.NotFound;
		}

		if (item.Status == StatusDoItem.Baixado)
		{
			return IntranetErrors.Inventario.ItemBaixado;
		}

		item.Editar(command.Nome, command.Descricao, command.Categoria, command.CodigoPatrimonio, command.SetorId);

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.InventarioEditar,
					RecursosDeAuditoria.Inventario,
					item.Id.ToString(),
					JsonSerializer.Serialize(new { nome = item.Nome, categoria = item.Categoria })),
				cancellationToken)
			.ConfigureAwait(false);

		return ItemInventarioDto.FromEntity(item);
	}
}
```

- [ ] **Step 4: Rodar até passar**

Run: `dotnet test --filter EditarItemInventarioHandlerTests`
Expected: PASS (3 testes)

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Application/Inventario/EditarItemInventarioHandler.cs tests/Secco.Intranet.Tests/Unit/EditarItemInventarioHandlerTests.cs
git commit -m "feat(inventario): handler de edicao dos campos descritivos

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 6: Application — `MudarStatusItemInventarioHandler`

Consolida as cinco transições (Atribuir, Desatribuir, Enviar/Voltar da manutenção, Baixar) num handler só — mesmo espírito de `EditarSetorHandler`, que já combina edição de campo com troca de estado (ativar/desativar) numa chamada.

**Files:**
- Create: `src/Secco.Intranet.Application/Inventario/MudarStatusItemInventarioHandler.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/MudarStatusItemInventarioHandlerTests.cs`

**Interfaces:**
- Consumes: `IItemInventarioRepository`, `ItemInventario.Atribuir/Desatribuir/EnviarParaManutencao/VoltarDaManutencao/Baixar` (Task 2).
- Produces: `enum AcaoDeStatus { Atribuir, Desatribuir, EnviarParaManutencao, VoltarDaManutencao, Baixar }`; `MudarStatusItemInventarioCommand(Guid Id, AcaoDeStatus Acao, Guid? UsuarioId = null, string? UsuarioEmail = null)`; `MudarStatusItemInventarioHandler.HandleAsync(...)`.

- [ ] **Step 1: Escrever os testes**

```csharp
// tests/Secco.Intranet.Tests/Unit/MudarStatusItemInventarioHandlerTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class MudarStatusItemInventarioHandlerTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public RegistroDeAuditoria? Ultimo { get; private set; }

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Ultimo = registro;

			return Task.CompletedTask;
		}
	}

	private sealed class RepositorioFalso(ItemInventario? item) : IItemInventarioRepository
	{
		public bool Gravou { get; private set; }

		public Task AddAsync(ItemInventario novo, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(item);

		public Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(item);

		public Task<PagedResult<ItemInventario>> SearchAsync(
			ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<ItemInventario>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			Gravou = true;

			return Task.CompletedTask;
		}
	}

	private static (MudarStatusItemInventarioHandler Handler, RepositorioFalso Repositorio, TrilhaFalsa Trilha) Montar(
		ItemInventario? item)
	{
		var repositorio = new RepositorioFalso(item);
		var trilha = new TrilhaFalsa();

		return (new MudarStatusItemInventarioHandler(repositorio, trilha), repositorio, trilha);
	}

	[Fact]
	public async Task ItemInexistente_DevolveNotFound()
	{
		var (handler, _, _) = Montar(null);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(Guid.NewGuid(), AcaoDeStatus.Baixar));

		resultado.Error.Should().Be(IntranetErrors.Inventario.NotFound);
	}

	[Fact]
	public async Task ItemBaixado_QualquerAcao_DevolveErro()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		item.Baixar();
		var (handler, _, _) = Montar(item);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.EnviarParaManutencao));

		resultado.Error.Should().Be(IntranetErrors.Inventario.ItemBaixado);
	}

	[Fact]
	public async Task Atribuir_SemUsuario_DevolveErro()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var (handler, _, _) = Montar(item);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.Atribuir));

		resultado.Error.Should().Be(IntranetErrors.Inventario.UsuarioRequired);
	}

	[Fact]
	public async Task Atribuir_ComUsuario_MudaStatusEGravaEAudita()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var (handler, repositorio, trilha) = Montar(item);
		var usuarioId = Guid.NewGuid();

		var resultado = await handler.HandleAsync(
			new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.Atribuir, usuarioId, "ana@exemplo.local"));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Status.Should().Be(StatusDoItem.EmUso);
		repositorio.Gravou.Should().BeTrue();
		trilha.Ultimo!.Verbo.Should().Be(VerbosDeAuditoria.InventarioAtribuir);
	}

	[Fact]
	public async Task Desatribuir_APartirDeDisponivel_DevolveTransicaoInvalida()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var (handler, _, _) = Montar(item);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.Desatribuir));

		resultado.Error.Should().Be(IntranetErrors.Inventario.TransicaoInvalida);
	}

	[Fact]
	public async Task Baixar_GravaEAuditaComVerboProprio()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var (handler, repositorio, trilha) = Montar(item);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.Baixar));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Status.Should().Be(StatusDoItem.Baixado);
		repositorio.Gravou.Should().BeTrue();
		trilha.Ultimo!.Verbo.Should().Be(VerbosDeAuditoria.InventarioBaixar);
	}

	[Fact]
	public async Task VoltarDaManutencao_SemEstarEmManutencao_DevolveTransicaoInvalida()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var (handler, _, _) = Montar(item);

		var resultado = await handler.HandleAsync(new MudarStatusItemInventarioCommand(item.Id, AcaoDeStatus.VoltarDaManutencao));

		resultado.Error.Should().Be(IntranetErrors.Inventario.TransicaoInvalida);
	}
}
```

- [ ] **Step 2: Rodar para confirmar que falha**

Run: `dotnet test --filter MudarStatusItemInventarioHandlerTests`
Expected: FAIL (compilação)

- [ ] **Step 3: Implementar**

```csharp
// src/Secco.Intranet.Application/Inventario/MudarStatusItemInventarioHandler.cs
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Ação de transição de status pedida.</summary>
public enum AcaoDeStatus
{
	/// <summary>Atribui a um usuário (exige <see cref="MudarStatusItemInventarioCommand.UsuarioId"/>).</summary>
	Atribuir,

	/// <summary>Libera o item atribuído.</summary>
	Desatribuir,

	/// <summary>Envia para manutenção.</summary>
	EnviarParaManutencao,

	/// <summary>Volta da manutenção.</summary>
	VoltarDaManutencao,

	/// <summary>Dá baixa — terminal.</summary>
	Baixar,
}

/// <summary>Pedido de transição de status de um item de inventário.</summary>
/// <param name="Id">Identificador do item.</param>
/// <param name="Acao">Transição pedida.</param>
/// <param name="UsuarioId">Obrigatório quando <paramref name="Acao"/> é <see cref="AcaoDeStatus.Atribuir"/>.</param>
/// <param name="UsuarioEmail">E-mail em cache do usuário — o SecureGate não guarda nome de exibição.</param>
public sealed record MudarStatusItemInventarioCommand(
	Guid Id, AcaoDeStatus Acao, Guid? UsuarioId = null, string? UsuarioEmail = null);

/// <summary>
/// Executa uma das cinco transições de <see cref="ItemInventario"/>. Toda invariante que o
/// domínio recusaria com exceção é checada antes (ADR-0004): a exceção do domínio é rede de
/// segurança para chamador interno, não o caminho normal.
/// </summary>
public sealed class MudarStatusItemInventarioHandler(IItemInventarioRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ItemInventarioDto>> HandleAsync(
		MudarStatusItemInventarioCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var item = await repository.GetParaEdicaoAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return IntranetErrors.Inventario.NotFound;
		}

		if (item.Status == StatusDoItem.Baixado)
		{
			return IntranetErrors.Inventario.ItemBaixado;
		}

		switch (command.Acao)
		{
			case AcaoDeStatus.Atribuir:
				if (command.UsuarioId is null || command.UsuarioId == Guid.Empty)
				{
					return IntranetErrors.Inventario.UsuarioRequired;
				}

				if (item.Status is not (StatusDoItem.Disponivel or StatusDoItem.EmUso))
				{
					return IntranetErrors.Inventario.TransicaoInvalida;
				}

				item.Atribuir(command.UsuarioId.Value, command.UsuarioEmail);
				break;

			case AcaoDeStatus.Desatribuir:
				if (item.Status != StatusDoItem.EmUso)
				{
					return IntranetErrors.Inventario.TransicaoInvalida;
				}

				item.Desatribuir();
				break;

			case AcaoDeStatus.EnviarParaManutencao:
				item.EnviarParaManutencao();
				break;

			case AcaoDeStatus.VoltarDaManutencao:
				if (item.Status != StatusDoItem.EmManutencao)
				{
					return IntranetErrors.Inventario.TransicaoInvalida;
				}

				item.VoltarDaManutencao();
				break;

			case AcaoDeStatus.Baixar:
				item.Baixar();
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(command), command.Acao, "Ação de status desconhecida.");
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		var verbo = command.Acao switch
		{
			AcaoDeStatus.Atribuir => VerbosDeAuditoria.InventarioAtribuir,
			AcaoDeStatus.Baixar => VerbosDeAuditoria.InventarioBaixar,
			_ => VerbosDeAuditoria.InventarioEditar,
		};

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					verbo,
					RecursosDeAuditoria.Inventario,
					item.Id.ToString(),
					JsonSerializer.Serialize(new { acao = command.Acao.ToString(), status = item.Status.ToString() })),
				cancellationToken)
			.ConfigureAwait(false);

		return ItemInventarioDto.FromEntity(item);
	}
}
```

- [ ] **Step 4: Rodar até passar**

Run: `dotnet test --filter MudarStatusItemInventarioHandlerTests`
Expected: PASS (7 testes)

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Application/Inventario/MudarStatusItemInventarioHandler.cs tests/Secco.Intranet.Tests/Unit/MudarStatusItemInventarioHandlerTests.cs
git commit -m "feat(inventario): handler unico para as cinco transicoes de status

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 7: Application — busca e leitura pontual

**Files:**
- Create: `src/Secco.Intranet.Application/Inventario/SearchItensInventarioHandler.cs`
- Create: `src/Secco.Intranet.Application/Inventario/GetItemInventarioByIdHandler.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/SearchItensInventarioHandlerTests.cs`

**Interfaces:**
- Consumes: `IItemInventarioRepository.SearchAsync/GetByIdAsync`.
- Produces: `SearchItensInventarioHandler.HandleAsync(ItemInventarioSearchCriteria, CancellationToken) : Task<Result<PagedResult<ItemInventarioDto>>>`; `GetItemInventarioByIdHandler.HandleAsync(Guid, CancellationToken) : Task<Result<ItemInventarioDto>>`.

- [ ] **Step 1: Escrever o teste da busca**

```csharp
// tests/Secco.Intranet.Tests/Unit/SearchItensInventarioHandlerTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class SearchItensInventarioHandlerTests
{
	private sealed class RepositorioFalso(PagedResult<ItemInventario> pagina) : IItemInventarioRepository
	{
		public Task AddAsync(ItemInventario item, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<ItemInventario?>(null);

		public Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<ItemInventario?>(null);

		public Task<PagedResult<ItemInventario>> SearchAsync(
			ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(pagina);

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	[Fact]
	public async Task DevolveAPaginaProjetadaComoDto()
	{
		var item = new ItemInventario("Notebook", null, null, null, null);
		var pagina = PagedResult.Create<ItemInventario>([item], new PageRequest(1), 1);
		var handler = new SearchItensInventarioHandler(new RepositorioFalso(pagina));

		var resultado = await handler.HandleAsync(new ItemInventarioSearchCriteria());

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Items.Should().ContainSingle(dto => dto.Nome == "Notebook");
	}
}
```

- [ ] **Step 2: Rodar para confirmar que falha**

Run: `dotnet test --filter SearchItensInventarioHandlerTests`
Expected: FAIL (compilação)

- [ ] **Step 3: Implementar os dois handlers**

```csharp
// src/Secco.Intranet.Application/Inventario/SearchItensInventarioHandler.cs
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Busca paginada de itens de inventário. Sem caminho de falha de negócio hoje.</summary>
public sealed class SearchItensInventarioHandler(IItemInventarioRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<PagedResult<ItemInventarioDto>>> HandleAsync(
		ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(criteria);

		var pagina = await repository.SearchAsync(criteria, cancellationToken).ConfigureAwait(false);

		return PagedResult.Create(
			[.. pagina.Items.Select(ItemInventarioDto.FromEntity)], new PageRequest(pagina.Page, pagina.Size), pagina.TotalCount);
	}
}
```

```csharp
// src/Secco.Intranet.Application/Inventario/GetItemInventarioByIdHandler.cs
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Inventario;

/// <summary>Leitura pontual de um item de inventário.</summary>
public sealed class GetItemInventarioByIdHandler(IItemInventarioRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ItemInventarioDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var item = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

		return item is null ? IntranetErrors.Inventario.NotFound : ItemInventarioDto.FromEntity(item);
	}
}
```

- [ ] **Step 4: Rodar até passar**

Run: `dotnet test --filter SearchItensInventarioHandlerTests`
Expected: PASS (1 teste)

- [ ] **Step 5: Registrar os cinco handlers em `IntranetApplicationExtensions`**

Em `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`, acrescentar (usando `Secco.Intranet.Application.Inventario;` no topo):

```csharp
		services.AddScoped<CriarItemInventarioHandler>();
		services.AddScoped<EditarItemInventarioHandler>();
		services.AddScoped<MudarStatusItemInventarioHandler>();
		services.AddScoped<SearchItensInventarioHandler>();
		services.AddScoped<GetItemInventarioByIdHandler>();
```

- [ ] **Step 6: Build completo**

Run: `dotnet build`
Expected: 0 Erro(s), 0 Aviso(s)

- [ ] **Step 7: Commit**

```bash
git add src/Secco.Intranet.Application/Inventario/SearchItensInventarioHandler.cs src/Secco.Intranet.Application/Inventario/GetItemInventarioByIdHandler.cs src/Secco.Intranet.Application/IntranetApplicationExtensions.cs tests/Secco.Intranet.Tests/Unit/SearchItensInventarioHandlerTests.cs
git commit -m "feat(inventario): handlers de busca e leitura pontual

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 8: Infraestrutura — mapeamento EF, migrations e repositório real

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Mappings/ItemInventarioConfiguration.cs`
- Create: `src/Secco.Intranet.Infrastructure/Repositories/ItemInventarioRepository.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`
- Create: migrations em `src/Secco.Intranet.Migrations.SqlServer/Migrations/` e `src/Secco.Intranet.Migrations.Postgres/Migrations/` (geradas por `dotnet ef`)
- Create: `tests/Secco.Intranet.Tests/Integration/InventarioPersistenciaTests.cs`

**Interfaces:**
- Produces: `IntranetDbContext.ItensInventario : DbSet<ItemInventario>`; `ItemInventarioRepository : IItemInventarioRepository`.

- [ ] **Step 1: Adicionar o `DbSet` e o mapeamento**

Em `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs`, acrescentar (junto dos outros `DbSet`, com `using Secco.Intranet.Domain.Inventario;`):

```csharp
	/// <summary>Itens de inventário, sem setor dono (tabela <c>tb_itens_inventario</c>).</summary>
	public DbSet<ItemInventario> ItensInventario => Set<ItemInventario>();
```

```csharp
// src/Secco.Intranet.Infrastructure/Mappings/ItemInventarioConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Inventario;
using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="ItemInventario"/>. Nomes de tabela/colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class ItemInventarioConfiguration : IEntityTypeConfiguration<ItemInventario>
{
	public void Configure(EntityTypeBuilder<ItemInventario> builder)
	{
		// SetorId é informativo (ver comentário na entidade) — sem navegação, porque o
		// agregado não precisa carregar o Setor; a relação existe só para a convention
		// reconhecer a coluna como id_fk_setor (mesmo padrão de DocumentoConfiguration).
		// Nullable: diferente de Documento, aqui o setor não é obrigatório.
		builder
			.HasOne<Setor>()
			.WithMany()
			.HasForeignKey(item => item.SetorId)
			.OnDelete(DeleteBehavior.Restrict)
			.IsRequired(false);

		builder.Property(item => item.Nome).HasMaxLength(256);
		builder.Property(item => item.Descricao).HasMaxLength(4_096);
		builder.Property(item => item.Categoria).HasMaxLength(128);
		builder.Property(item => item.CodigoPatrimonio).HasMaxLength(128);
		builder.Property(item => item.AtribuidoANome).HasMaxLength(256);

		// O filtro mais comum da listagem (Decisões da spec: baixado some por padrão).
		builder.HasIndex(item => item.Status);
		builder.HasIndex(item => item.SetorId);
		builder.HasIndex(item => item.Nome);
	}
}
```

- [ ] **Step 2: Implementar o repositório**

```csharp
// src/Secco.Intranet.Infrastructure/Repositories/ItemInventarioRepository.cs
using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de itens de inventário no banco do tenant atual.</summary>
internal sealed class ItemInventarioRepository(IntranetDbContext context) : IItemInventarioRepository
{
	public async Task AddAsync(ItemInventario item, CancellationToken cancellationToken = default)
	{
		context.ItensInventario.Add(item);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<ItemInventario?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.ItensInventario
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
			.ConfigureAwait(false);

	public async Task<ItemInventario?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.ItensInventario
			.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
			.ConfigureAwait(false);

	public async Task<PagedResult<ItemInventario>> SearchAsync(
		ItemInventarioSearchCriteria criteria, CancellationToken cancellationToken = default)
	{
		var query = context.ItensInventario.AsNoTracking();

		if (!string.IsNullOrWhiteSpace(criteria.NomeContains))
		{
			query = query.Where(item => item.Nome.Contains(criteria.NomeContains));
		}

		if (criteria.Status.HasValue)
		{
			query = query.Where(item => item.Status == criteria.Status.Value);
		}
		else if (criteria.ApenasNaoBaixados)
		{
			query = query.Where(item => item.Status != StatusDoItem.Baixado);
		}

		if (criteria.SetorId.HasValue)
		{
			query = query.Where(item => item.SetorId == criteria.SetorId.Value);
		}

		var page = criteria.EffectivePage;
		var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

		var items = await query
			.OrderByDescending(item => item.CreatedAt)
			.ThenByDescending(item => item.Id)
			.Skip(page.Skip)
			.Take(page.Size)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return PagedResult.Create(items, page, totalCount);
	}

	public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
```

- [ ] **Step 3: Registrar o repositório em `IntranetInfrastructureExtensions.cs`**

Ao lado de `services.AddScoped<ISetorRepository, SetorRepository>();`:

```csharp
		services.AddScoped<IItemInventarioRepository, ItemInventarioRepository>();
```

(acrescentar `using Secco.Intranet.Application.Inventario;` no topo do arquivo, se ainda não houver `using` equivalente)

- [ ] **Step 4: Build para confirmar que compila antes de gerar migration**

Run: `dotnet build`
Expected: 0 Erro(s), 0 Aviso(s)

- [ ] **Step 5: Gerar as migrations nos dois providers**

Run:
```bash
dotnet ef migrations add Inventario --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add Inventario --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```
Expected: dois arquivos de migration novos por provider (`*_Inventario.cs`, `*_Inventario.Designer.cs`) e o `IntranetDbContextModelSnapshot.cs` de cada provider atualizado.

- [ ] **Step 6: Conferir a migration gerada**

Abrir os dois `*_Inventario.cs` e confirmar: tabela `tb_itens_inventario`; `id_pk_item_inventario` como PK; `ie_status` como `int`; `id_fk_setor` nullable; `atribuido_a_usuario_id` nullable **sem** prefixo `id_fk_` (não é FK reconhecida pelo EF — não há tabela local de usuário, ver comentário na entidade); índices em `ie_status`, `id_fk_setor`, `ds_nome`; FK para `tb_setores` com `ON DELETE NO ACTION` (Restrict). Se algum nome não bater com o esperado, revisar `ItemInventarioConfiguration` e regenerar (`dotnet ef migrations remove` nos dois providers antes de regenerar).

- [ ] **Step 7: Escrever o teste de persistência real — o ponto que já causou perda silenciosa de dado neste projeto**

```csharp
// tests/Secco.Intranet.Tests/Integration/InventarioPersistenciaTests.cs
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Editar/mudar status precisa <b>gravar</b>. Contra fake isso não se prova — ver
/// <c>EditarSetorPersistenciaTests</c>, cujo motivo de existir foi um bug real
/// (repositório desrastreado usado em caminho de escrita, sucesso devolvido sem gravar).
/// </summary>
public class InventarioPersistenciaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<Guid> CriarAsync()
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criado = await escopo.ServiceProvider
			.GetRequiredService<CriarItemInventarioHandler>()
			.HandleAsync(new CriarItemInventarioCommand("Notebook Dell", null, "Equipamento", "PAT-001", null));

		criado.IsSuccess.Should().BeTrue();

		return criado.Value.Id;
	}

	[Fact]
	public async Task Editar_GravaDeVerdade()
	{
		var id = await CriarAsync();

		using (var escopo = factory.Services.CreateScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

			var resultado = await escopo.ServiceProvider
				.GetRequiredService<EditarItemInventarioHandler>()
				.HandleAsync(new EditarItemInventarioCommand(id, "Notebook Dell G15", "Nova descrição", "TI", "PAT-002", null));

			resultado.IsSuccess.Should().BeTrue();
		}

		// Escopo novo, DbContext novo: só sobrevive o que foi para o banco.
		using var leitura = factory.Services.CreateScope();
		leitura.ServiceProvider.SetTenant(factory.TenantAlfa);

		var lido = await leitura.ServiceProvider
			.GetRequiredService<GetItemInventarioByIdHandler>()
			.HandleAsync(id);

		lido.IsSuccess.Should().BeTrue();
		lido.Value.Nome.Should().Be("Notebook Dell G15");
		lido.Value.CodigoPatrimonio.Should().Be("PAT-002");
	}

	[Fact]
	public async Task MudarStatus_GravaDeVerdade()
	{
		var id = await CriarAsync();
		var usuarioId = Guid.NewGuid();

		using (var escopo = factory.Services.CreateScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

			var resultado = await escopo.ServiceProvider
				.GetRequiredService<MudarStatusItemInventarioHandler>()
				.HandleAsync(new MudarStatusItemInventarioCommand(id, AcaoDeStatus.Atribuir, usuarioId, "ana@exemplo.local"));

			resultado.IsSuccess.Should().BeTrue();
		}

		using var leitura = factory.Services.CreateScope();
		leitura.ServiceProvider.SetTenant(factory.TenantAlfa);

		var lido = await leitura.ServiceProvider
			.GetRequiredService<GetItemInventarioByIdHandler>()
			.HandleAsync(id);

		lido.Value.Status.Should().Be(StatusDoItem.EmUso);
		lido.Value.AtribuidoAUsuarioId.Should().Be(usuarioId);
		lido.Value.AtribuidoANome.Should().Be("ana@exemplo.local");
	}
}
```

Se `IntranetWebFactory` não tiver um `EnsureDatabaseMigratedAsync()` público, conferir o nome real do método usado por `EditarSetorPersistenciaTests` (mesmo arquivo) e usar o mesmo — não inventar um novo.

- [ ] **Step 8: Rodar o teste de persistência**

Run: `dotnet test --filter InventarioPersistenciaTests`
Expected: PASS (2 testes) — precisa do SQL Server de teste (Testcontainers) disponível; se falhar por conexão, confirmar Docker rodando.

- [ ] **Step 9: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASS, todos os testes anteriores + os novos desta task.

- [ ] **Step 10: Commit**

```bash
git add src/Secco.Intranet.Infrastructure/Mappings/ItemInventarioConfiguration.cs src/Secco.Intranet.Infrastructure/Repositories/ItemInventarioRepository.cs src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs src/Secco.Intranet.Migrations.SqlServer/Migrations/ src/Secco.Intranet.Migrations.Postgres/Migrations/ tests/Secco.Intranet.Tests/Integration/InventarioPersistenciaTests.cs
git commit -m "feat(inventario): mapeamento EF, migrations e repositorio real

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 9: Web — `InventarioController` e autorização de ponta a ponta

**Files:**
- Create: `src/Secco.Intranet.Web/Models/Inventario/ItemInventarioFormViewModel.cs`
- Create: `src/Secco.Intranet.Web/Models/Inventario/ItemInventarioListViewModel.cs`
- Create: `src/Secco.Intranet.Web/Controllers/InventarioController.cs`
- Create: `tests/Secco.Intranet.Tests/Integration/InventarioAutorizacaoTests.cs`

**Interfaces:**
- Consumes: `AcessoAdministrativo.TemAcesso` (Task 3), os cinco handlers (Tasks 4–7), `IDiretorioDeUsuarios` (já existente, para a lista de usuários a atribuir), `FeedbackViewComponent.ChaveDaMensagem` (já existente).

- [ ] **Step 1: ViewModels**

```csharp
// src/Secco.Intranet.Web/Models/Inventario/ItemInventarioFormViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace Secco.Intranet.Web.Models.Inventario;

/// <summary>Dados do formulário de criação/edição de item de inventário (ADR-0002 regra 2).</summary>
public sealed class ItemInventarioFormViewModel
{
	/// <summary>Identificador — vazio na criação.</summary>
	public Guid Id { get; set; }

	/// <summary>Nome de exibição.</summary>
	[Required(ErrorMessage = "O nome é obrigatório.")]
	[StringLength(256)]
	public string? Nome { get; set; }

	/// <summary>Descrição livre.</summary>
	[StringLength(4_096)]
	public string? Descricao { get; set; }

	/// <summary>Categoria livre.</summary>
	[StringLength(128)]
	public string? Categoria { get; set; }

	/// <summary>Código de patrimônio, sem unicidade.</summary>
	[StringLength(128)]
	public string? CodigoPatrimonio { get; set; }

	/// <summary>Setor onde o item está — informativo.</summary>
	public Guid? SetorId { get; set; }
}
```

```csharp
// src/Secco.Intranet.Web/Models/Inventario/ItemInventarioListViewModel.cs
using Secco.Intranet.Application.Inventario;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Models.Inventario;

/// <summary>Modelo da listagem paginada de itens de inventário.</summary>
/// <param name="Page">Página de resultados retornada pela busca.</param>
/// <param name="Nome">Filtro de nome aplicado, para re-popular a busca na view.</param>
public sealed record ItemInventarioListViewModel(PagedResult<ItemInventarioDto> Page, string? Nome);
```

- [ ] **Step 2: Controller**

```csharp
// src/Secco.Intranet.Web/Controllers/InventarioController.cs
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Inventario;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Controller fino do recurso Inventário (ADR-0002 regra 1). Diferente de Setor, toda action
/// — inclusive <see cref="Index"/> — é gated: sem <c>intranet-admin</c> ou
/// <c>inventario-admin</c> a rota devolve 403, não só o link escondido no menu (ADR-0008,
/// spec de 2026-09-13).
/// </summary>
public sealed class InventarioController(
	CriarItemInventarioHandler criarHandler,
	EditarItemInventarioHandler editarHandler,
	MudarStatusItemInventarioHandler mudarStatusHandler,
	SearchItensInventarioHandler searchHandler,
	GetItemInventarioByIdHandler getByIdHandler,
	IDiretorioDeUsuarios diretorio,
	IConfiguration configuration,
	IWebHostEnvironment environment) : Controller
{
	private const string RoleEspecifica = "inventario-admin";

	/// <summary>Listagem paginada.</summary>
	[HttpGet]
	public async Task<IActionResult> Index(string? nome, int page = 1, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var criteria = new ItemInventarioSearchCriteria(NomeContains: nome, ApenasNaoBaixados: true, Page: new PageRequest(page));
		var resultado = await searchHandler.HandleAsync(criteria, cancellationToken);

		return View(new ItemInventarioListViewModel(resultado.Value, nome));
	}

	/// <summary>Detalhe de um item.</summary>
	[HttpGet]
	public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await getByIdHandler.HandleAsync(id, cancellationToken);

		return resultado.IsSuccess ? View(resultado.Value) : NotFound();
	}

	/// <summary>Formulário de criação.</summary>
	[HttpGet]
	public IActionResult Create()
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		return View(new ItemInventarioFormViewModel());
	}

	/// <summary>Processa a criação.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Create(ItemInventarioFormViewModel form, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		ArgumentNullException.ThrowIfNull(form);

		if (!ModelState.IsValid)
		{
			return View(form);
		}

		var resultado = await criarHandler.HandleAsync(
			new CriarItemInventarioCommand(form.Nome, form.Descricao, form.Categoria, form.CodigoPatrimonio, form.SetorId),
			cancellationToken);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View(form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Item \"{resultado.Value.Nome}\" cadastrado.";

		return RedirectToAction(nameof(Details), new { id = resultado.Value.Id });
	}

	/// <summary>Formulário de edição.</summary>
	[HttpGet]
	public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await getByIdHandler.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		var item = resultado.Value;

		return View(new ItemInventarioFormViewModel
		{
			Id = item.Id,
			Nome = item.Nome,
			Descricao = item.Descricao,
			Categoria = item.Categoria,
			CodigoPatrimonio = item.CodigoPatrimonio,
			SetorId = item.SetorId,
		});
	}

	/// <summary>Processa a edição.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Edit(ItemInventarioFormViewModel form, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		ArgumentNullException.ThrowIfNull(form);

		if (!ModelState.IsValid)
		{
			return View(form);
		}

		var resultado = await editarHandler.HandleAsync(
			new EditarItemInventarioCommand(form.Id, form.Nome, form.Descricao, form.Categoria, form.CodigoPatrimonio, form.SetorId),
			cancellationToken);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View(form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Item \"{resultado.Value.Nome}\" salvo.";

		return RedirectToAction(nameof(Details), new { id = form.Id });
	}

	/// <summary>Atribui o item a um usuário do tenant.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Atribuir(Guid id, Guid usuarioId, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var usuarios = await diretorio.ListarDoTenantAtualAsync(cancellationToken);
		var usuario = usuarios.FirstOrDefault(u => u.Id == usuarioId);

		var resultado = await mudarStatusHandler.HandleAsync(
			new MudarStatusItemInventarioCommand(id, AcaoDeStatus.Atribuir, usuarioId, usuario?.Email), cancellationToken);

		return AposMudarStatus(id, resultado.IsFailure ? resultado.Error.Description : null);
	}

	/// <summary>Libera o item atribuído.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Desatribuir(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await mudarStatusHandler.HandleAsync(
			new MudarStatusItemInventarioCommand(id, AcaoDeStatus.Desatribuir), cancellationToken);

		return AposMudarStatus(id, resultado.IsFailure ? resultado.Error.Description : null);
	}

	/// <summary>Envia o item para manutenção.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EnviarParaManutencao(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await mudarStatusHandler.HandleAsync(
			new MudarStatusItemInventarioCommand(id, AcaoDeStatus.EnviarParaManutencao), cancellationToken);

		return AposMudarStatus(id, resultado.IsFailure ? resultado.Error.Description : null);
	}

	/// <summary>Volta o item da manutenção.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> VoltarDaManutencao(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await mudarStatusHandler.HandleAsync(
			new MudarStatusItemInventarioCommand(id, AcaoDeStatus.VoltarDaManutencao), cancellationToken);

		return AposMudarStatus(id, resultado.IsFailure ? resultado.Error.Description : null);
	}

	/// <summary>Dá baixa no item.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Baixar(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await mudarStatusHandler.HandleAsync(
			new MudarStatusItemInventarioCommand(id, AcaoDeStatus.Baixar), cancellationToken);

		return AposMudarStatus(id, resultado.IsFailure ? resultado.Error.Description : null);
	}

	/// <summary>
	/// Limitação conhecida e deliberada: <c>FeedbackViewComponent</c> hoje só produz
	/// <c>ToastVariante.Sucesso</c> — uma falha de transição (ex.: <c>TransicaoInvalida</c>)
	/// aparece no mesmo toast verde, só com texto de erro. Corrigir isso é dar ao componente
	/// de feedback o primeiro produtor real de <c>ToastVariante.Erro</c>, o que é maior que
	/// este recurso e mexe em algo compartilhado com Setor/Documento — fica para uma spec
	/// própria. Aceitável aqui porque o gatilho é defensivo (double-click, aba parada): os
	/// botões da tela já escondem/desabilitam a maioria das transições inválidas.
	/// </summary>
	private IActionResult AposMudarStatus(Guid id, string? mensagemDeErro)
	{
		TempData[FeedbackViewComponent.ChaveDaMensagem] = mensagemDeErro ?? "Item atualizado.";

		return RedirectToAction(nameof(Details), new { id });
	}

	/// <summary>
	/// Gate único de toda action. O bypass de "modo aberto de DEV" só vale em
	/// <see cref="IWebHostEnvironment.IsDevelopment"/> de verdade — nunca no ambiente
	/// <c>Testing</c>, que também não configura autenticação: se o bypass valesse lá, não
	/// haveria como testar "sem a role, bloqueado" (ver docs/specs/2026-09-13-inventario-design.md).
	/// </summary>
	private bool PodeAdministrar() =>
		(environment.IsDevelopment() && !IntranetAuthenticationExtensions.IsConfigured(configuration))
		|| AcessoAdministrativo.TemAcesso(User, RoleEspecifica);
}
```

- [ ] **Step 3: Escrever os testes de integração de autorização**

```csharp
// tests/Secco.Intranet.Tests/Integration/InventarioAutorizacaoTests.cs
using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A autorização do Inventário precisa ser real na rota, não só um botão escondido — ver
/// docs/specs/2026-09-13-inventario-design.md. Usa <see cref="RolesDeTesteMiddleware"/>
/// porque o ambiente Testing nunca registra autenticação de verdade.
/// </summary>
public class InventarioAutorizacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente(params string[] roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	[Fact]
	public async Task SemRole_Bloqueado()
	{
		var resposta = await CriarCliente().GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task ComRoleDeSetor_AindaBloqueado()
	{
		var resposta = await CriarCliente("financeiro-admin").GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, "admin de setor não é atalho para inventario-admin");
	}

	[Fact]
	public async Task CriarItem_SemRole_Bloqueado()
	{
		var client = CriarCliente();

		var resposta = await client.PostAsync(
			"/Inventario/Create", new FormUrlEncodedContent([new KeyValuePair<string, string>("Nome", "Notebook")]));

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}
}
```

Deliberadamente **não** há teste de "liberado" (200) aqui: `Index`/`Create` chamam `View(...)`, e a view ainda não existe (Task 10) — um teste que espera 200 quebraria por falta de view, não por autorização, e o objetivo desta task é só provar o bloqueio. Os testes de acesso liberado entram na Task 10, junto com as views que eles precisam.

- [ ] **Step 4: Rodar os testes**

Run: `dotnet test --filter InventarioAutorizacaoTests`
Expected: PASS (3 testes) — nenhum deles chega a `View(...)`, então não dependem das views ainda não criadas.

- [ ] **Step 5: Registrar `InventarioController` — nenhuma linha extra necessária**

Controllers MVC são descobertos automaticamente por convenção (`AddControllersWithViews()` já registrado em `Program.cs`); não há lista explícita a editar, diferente dos handlers da Application.

- [ ] **Step 6: Commit**

```bash
git add src/Secco.Intranet.Web/Models/Inventario/ src/Secco.Intranet.Web/Controllers/InventarioController.cs tests/Secco.Intranet.Tests/Integration/InventarioAutorizacaoTests.cs
git commit -m "feat(inventario): controller com autorizacao real em toda action

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 10: Web — Views (dois temas) e Navegação

**Files:**
- Create: `src/Secco.Intranet.Web/Views/Inventario/Index.cshtml`
- Create: `src/Secco.Intranet.Web/Views/Inventario/Create.cshtml`
- Create: `src/Secco.Intranet.Web/Views/Inventario/Edit.cshtml`
- Create: `src/Secco.Intranet.Web/Views/Inventario/Details.cshtml`
- Modify: `src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs`
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs`
- Modify: `tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs`

As views de página moram no core (não em `Themes/*`) — o mesmo lugar de `Views/Setores/*` — porque usam só os partials do contrato de tema (`_PageHeader`, `_EmptyState`, `_Pagination`, `_Badge`), nunca markup de tema à mão (ADR-0004). Nenhum arquivo novo entra em `Themes/Vertical`/`Themes/Horizontal`.

- [ ] **Step 1: `Views/Inventario/Index.cshtml`**

```cshtml
@model ItemInventarioListViewModel
@using Secco.Intranet.Domain.Inventario
@{
    ViewData["Title"] = "Inventário";

    string? PaginaUrl(int pagina) => Url.Action("Index", new { nome = Model.Nome, page = pagina });

    var novoItem = new PageActionModel("Novo item", Url.Action("Create")!, "bi-plus-lg", Primaria: true);

    var cabecalho = new PageHeaderModel(
        "Inventário",
        "Itens sem setor dono — administrado por quem tem a Role inventario-admin.",
        Acoes: new[] { novoItem });

    var buscando = !string.IsNullOrWhiteSpace(Model.Nome);

    var vazio = new EmptyStateModel(
        "bi-box-seam",
        buscando ? "Nenhum item com esse nome" : "Nenhum item cadastrado",
        buscando ? "Ajuste a busca ou cadastre um item novo." : "Cadastre o primeiro item do inventário.",
        novoItem);

    var paginacao = new PaginationModel(
        Model.Page.Page,
        Model.Page.TotalPages,
        Model.Page.HasPreviousPage ? PaginaUrl(Model.Page.Page - 1) : null,
        Model.Page.HasNextPage ? PaginaUrl(Model.Page.Page + 1) : null);

    static BadgeModel BadgeDoStatus(StatusDoItem status) => status switch
    {
        StatusDoItem.Disponivel => new BadgeModel("Disponível", BadgeVariante.Sucesso),
        StatusDoItem.EmUso => new BadgeModel("Em uso", BadgeVariante.Neutro),
        StatusDoItem.EmManutencao => new BadgeModel("Em manutenção", BadgeVariante.Aviso),
        _ => new BadgeModel("Baixado", BadgeVariante.Perigo),
    };
}

<partial name="_PageHeader" model="cabecalho" />

<form method="get" asp-action="Index" class="row g-2 mb-3" role="search">
    <div class="col-sm-auto flex-grow-1">
        <label class="visually-hidden" for="nome">Buscar por nome</label>
        <input class="form-control" type="search" id="nome" name="nome" value="@Model.Nome" placeholder="Buscar por nome" />
    </div>
    <div class="col-sm-auto">
        <button class="btn btn-outline-secondary w-100" type="submit">Buscar</button>
    </div>
</form>

@if (Model.Page.Items.Count == 0)
{
    <partial name="_EmptyState" model="vazio" />
}
else
{
    <ul class="sc-list">
        @foreach (var item in Model.Page.Items)
        {
            <li class="sc-list__item">
                <span class="sc-list__icon" aria-hidden="true"><i class="bi bi-box-seam"></i></span>
                <div class="sc-list__text">
                    <p class="sc-list__title">
                        <a class="text-reset text-decoration-none" asp-action="Details" asp-route-id="@item.Id">@item.Nome</a>
                    </p>
                    <span class="sc-list__sub">
                        @if (!string.IsNullOrWhiteSpace(item.CodigoPatrimonio))
                        {
                            <span class="sc-meta">@item.CodigoPatrimonio</span>
                        }
                        <partial name="_Badge" model="BadgeDoStatus(item.Status)" />
                    </span>
                </div>
            </li>
        }
    </ul>

    <partial name="_Pagination" model="paginacao" />
}
```

- [ ] **Step 2: `Views/Inventario/Create.cshtml`**

```cshtml
@model ItemInventarioFormViewModel
@{
    ViewData["Title"] = "Novo item";

    var cabecalho = new PageHeaderModel("Novo item de inventário");
}

<partial name="_PageHeader" model="cabecalho" />

<div class="sc-panel sc-form">
    <div asp-validation-summary="ModelOnly" class="text-danger"></div>

    <form method="post" asp-action="Create">
        <div class="sc-form__field">
            <label class="form-label" asp-for="Nome">Nome</label>
            <input class="form-control" asp-for="Nome" autocomplete="off" />
            <span class="text-danger" asp-validation-for="Nome"></span>
        </div>

        <div class="sc-form__field">
            <label class="form-label" asp-for="Descricao">Descrição</label>
            <textarea class="form-control" asp-for="Descricao" rows="3"></textarea>
            <span class="text-danger" asp-validation-for="Descricao"></span>
        </div>

        <div class="sc-form__field">
            <label class="form-label" asp-for="Categoria">Categoria</label>
            <input class="form-control" asp-for="Categoria" autocomplete="off" />
        </div>

        <div class="sc-form__field">
            <label class="form-label" asp-for="CodigoPatrimonio">Código de patrimônio</label>
            <input class="form-control font-monospace" asp-for="CodigoPatrimonio" autocomplete="off" />
        </div>

        <div class="sc-form__actions">
            <button class="btn btn-primary" type="submit">Cadastrar item</button>
            <a class="btn btn-outline-secondary" asp-action="Index">Cancelar</a>
        </div>
    </form>
</div>
```

- [ ] **Step 3: `Views/Inventario/Edit.cshtml`**

Idêntica ao `Create.cshtml`, trocando `ViewData["Title"]`/título para "Editar item", `asp-action="Edit"` no `<form>`, e acrescentando um campo oculto para o `Id`:

```cshtml
@model ItemInventarioFormViewModel
@{
    ViewData["Title"] = "Editar item";

    var cabecalho = new PageHeaderModel("Editar item de inventário");
}

<partial name="_PageHeader" model="cabecalho" />

<div class="sc-panel sc-form">
    <div asp-validation-summary="ModelOnly" class="text-danger"></div>

    <form method="post" asp-action="Edit">
        <input type="hidden" asp-for="Id" />

        <div class="sc-form__field">
            <label class="form-label" asp-for="Nome">Nome</label>
            <input class="form-control" asp-for="Nome" autocomplete="off" />
            <span class="text-danger" asp-validation-for="Nome"></span>
        </div>

        <div class="sc-form__field">
            <label class="form-label" asp-for="Descricao">Descrição</label>
            <textarea class="form-control" asp-for="Descricao" rows="3"></textarea>
        </div>

        <div class="sc-form__field">
            <label class="form-label" asp-for="Categoria">Categoria</label>
            <input class="form-control" asp-for="Categoria" autocomplete="off" />
        </div>

        <div class="sc-form__field">
            <label class="form-label" asp-for="CodigoPatrimonio">Código de patrimônio</label>
            <input class="form-control font-monospace" asp-for="CodigoPatrimonio" autocomplete="off" />
        </div>

        <div class="sc-form__actions">
            <button class="btn btn-primary" type="submit">Salvar</button>
            <a class="btn btn-outline-secondary" asp-action="Details" asp-route-id="@Model.Id">Cancelar</a>
        </div>
    </form>
</div>
```

- [ ] **Step 4: `Views/Inventario/Details.cshtml`**

```cshtml
@model ItemInventarioDto
@using Secco.Intranet.Domain.Inventario
@{
    ViewData["Title"] = Model.Nome;

    var cabecalho = new PageHeaderModel(
        Model.Nome,
        Acoes: new[]
        {
            new PageActionModel("Editar", Url.Action("Edit", new { id = Model.Id })!, "bi-pencil", Primaria: true),
            new PageActionModel("Voltar", Url.Action("Index")!, "bi-arrow-left"),
        });

    var situacao = Model.Status switch
    {
        StatusDoItem.Disponivel => new BadgeModel("Disponível", BadgeVariante.Sucesso),
        StatusDoItem.EmUso => new BadgeModel("Em uso", BadgeVariante.Neutro),
        StatusDoItem.EmManutencao => new BadgeModel("Em manutenção", BadgeVariante.Aviso),
        _ => new BadgeModel("Baixado", BadgeVariante.Perigo),
    };
}

<partial name="_PageHeader" model="cabecalho" />

<div class="sc-panel">
    <dl class="row mb-0">
        <dt class="col-sm-3">Situação</dt>
        <dd class="col-sm-9"><partial name="_Badge" model="situacao" /></dd>

        <dt class="col-sm-3">Categoria</dt>
        <dd class="col-sm-9">@(Model.Categoria ?? "—")</dd>

        <dt class="col-sm-3">Código de patrimônio</dt>
        <dd class="col-sm-9"><span class="sc-meta">@(Model.CodigoPatrimonio ?? "—")</span></dd>

        <dt class="col-sm-3">Atribuído a</dt>
        <dd class="col-sm-9">@(Model.AtribuidoANome ?? "Ninguém")</dd>

        <dt class="col-sm-3">Descrição</dt>
        <dd class="col-sm-9 mb-0">@(Model.Descricao ?? "—")</dd>
    </dl>
</div>

@if (Model.Status != StatusDoItem.Baixado)
{
    <div class="sc-panel mt-3 d-flex flex-wrap gap-2">
        @if (Model.Status != StatusDoItem.EmManutencao)
        {
            <form method="post" asp-action="Desatribuir" asp-route-id="@Model.Id" class="d-inline">
                <button class="btn btn-outline-secondary" type="submit" @(Model.Status != StatusDoItem.EmUso ? "disabled" : null)>
                    Desatribuir
                </button>
            </form>
            <form method="post" asp-action="EnviarParaManutencao" asp-route-id="@Model.Id" class="d-inline">
                <button class="btn btn-outline-secondary" type="submit">Enviar para manutenção</button>
            </form>
        }
        else
        {
            <form method="post" asp-action="VoltarDaManutencao" asp-route-id="@Model.Id" class="d-inline">
                <button class="btn btn-outline-secondary" type="submit">Voltar da manutenção</button>
            </form>
        }
        <form method="post" asp-action="Baixar" asp-route-id="@Model.Id" class="d-inline">
            <button class="btn btn-outline-danger" type="submit">Dar baixa</button>
        </form>
    </div>
}
```

Nota: o formulário de **atribuir** (escolher o usuário) fica fora desta rodada por simplicidade de view — o botão "Desatribuir"/"Enviar para manutenção" já cobre o essencial. Se quiser o seletor de usuário nesta mesma tela, acrescentar um `<select>` populado por `IDiretorioDeUsuarios` (já injetado no controller) num partial próprio antes de fechar a task — decisão de escopo, não de arquitetura.

- [ ] **Step 5: Navegação — item de menu condicional**

Em `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs`, acrescentar ao `NavigationRequest` um campo novo:

```csharp
public sealed record NavigationRequest(
	IReadOnlyList<SetorDto> Setores,
	string CaminhoAtual,
	bool MostrarAdministracao,
	bool DemoHabilitado,
	bool MostrarInventario);
```

E em `Build`, acrescentar a `principais` (antes do `if (request.DemoHabilitado)` ou depois, tanto faz):

```csharp
		if (request.MostrarInventario)
		{
			principais.Add(new NavigationItemModel(
				"Inventário", "bi-box-seam", "/inventario", Corresponde(caminho, "/inventario")));
		}
```

Em `src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs`, no `InvokeAsync`, acrescentar o cálculo (usando `AcessoAdministrativo`, já existente — `using Secco.Intranet.Web.Navigation;` já está no arquivo):

```csharp
		var request = new NavigationRequest(
			await CarregarSetoresAsync(HttpContext.User, autenticacaoAtiva).ConfigureAwait(false),
			HttpContext.Request.Path.Value ?? "/",
			MostrarAdministracao: !autenticacaoAtiva || SetorAcesso.AdministraAlgumSetor(HttpContext.User),
			demoOptions.Habilitado,
			MostrarInventario: !autenticacaoAtiva || AcessoAdministrativo.TemAcesso(HttpContext.User, "inventario-admin"));
```

- [ ] **Step 6: Atualizar o teste de navegação existente**

Em `tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs`, todo `new NavigationRequest(...)` já existente ganha um parâmetro a mais — adicionar `MostrarInventario: false` nos que não testam isso, e acrescentar dois testes novos:

```csharp
	[Fact]
	public void Build_ComMostrarInventario_IncluiItemDeMenu()
	{
		var request = new NavigationRequest([], "/", MostrarAdministracao: false, DemoHabilitado: false, MostrarInventario: true);

		var menu = IntranetNavigation.Build(request);

		menu.Grupos.SelectMany(g => g.Itens).Should().Contain(item => item.Texto == "Inventário");
	}

	[Fact]
	public void Build_SemMostrarInventario_NaoIncluiItemDeMenu()
	{
		var request = new NavigationRequest([], "/", MostrarAdministracao: false, DemoHabilitado: false, MostrarInventario: false);

		var menu = IntranetNavigation.Build(request);

		menu.Grupos.SelectMany(g => g.Itens).Should().NotContain(item => item.Texto == "Inventário");
	}
```

- [ ] **Step 7: Acrescentar os testes de acesso liberado — agora que as views existem**

Em `tests/Secco.Intranet.Tests/Integration/InventarioAutorizacaoTests.cs` (criado na Task 9), acrescentar dentro da classe, ao lado de `CriarItem_SemRole_Bloqueado` (precisa de `using System.Text.RegularExpressions;` no topo do arquivo):

```csharp
	[Fact]
	public async Task ComIntranetAdmin_Liberado()
	{
		var resposta = await CriarCliente("intranet-admin").GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task ComInventarioAdmin_Liberado()
	{
		var resposta = await CriarCliente("inventario-admin").GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task FluxoCompleto_ComInventarioAdmin_CriaEExibe()
	{
		var client = CriarCliente("inventario-admin");
		var titulo = $"Item {Guid.NewGuid():N}"[..20];

		var paginaCriacao = await client.GetStringAsync("/Inventario/Create");
		var token = Regex.Match(paginaCriacao, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

		var resposta = await client.PostAsync("/Inventario/Create", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", titulo),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "salvar redireciona para os detalhes");

		var html = await resposta.Content.ReadAsStringAsync();
		html.Should().Contain(titulo);
	}
```

- [ ] **Step 8: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASS — incluindo agora `InventarioAutorizacaoTests.FluxoCompleto_ComInventarioAdmin_CriaEExibe` e `ComIntranetAdmin_Liberado`/`ComInventarioAdmin_Liberado`, que dependiam destas views.

- [ ] **Step 9: Build sem avisos**

Run: `dotnet build`
Expected: 0 Erro(s), 0 Aviso(s)

- [ ] **Step 10: Verificação visual manual (opcional, recomendado)**

```bash
docker compose up -d
dotnet run --project src/Secco.Intranet.Web
```

Acessar `/Inventario` — em Development sem `Secco:SecureGate` configurado (modo aberto), a tela deve abrir normalmente; cadastrar um item, atribuir/desatribuir, mandar para manutenção e voltar, dar baixa, e confirmar que ele some da listagem padrão depois de baixado.

- [ ] **Step 11: Commit**

```bash
git add src/Secco.Intranet.Web/Views/Inventario/ src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs tests/Secco.Intranet.Tests/Integration/InventarioAutorizacaoTests.cs
git commit -m "feat(inventario): views e item de menu condicional

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 11: Fechamento — roadmap e verificação final

**Files:**
- Modify: `docs/roadmap.md`

- [ ] **Step 1: Marcar o item concluído**

Em `docs/roadmap.md`, Fase 1, trocar o item de Inventário (hoje `[ ]`) para `[x]`, resumindo o que foi entregue e referenciando a spec:

```markdown
- [x] Controle de inventário — recurso sem setor dono, autorização por Role tenant-scoped
      própria (`inventario-admin`, mais `intranet-admin` como superusuário da instalação —
      ADR-0008). Máquina de estado Disponível/Em uso/Em manutenção/Baixado; toda rota
      bloqueada para quem não tem a Role, não só o item de menu escondido —
      [spec](specs/2026-09-13-inventario-design.md). Tela de conceder `inventario-admin` a
      um usuário existente fica fora, bloqueada por
      [secco-platform#26](https://github.com/rafsecco/secco-platform/issues/26)
```

- [ ] **Step 2: Rodar a suíte inteira e o build uma última vez**

Run: `dotnet build && dotnet test`
Expected: 0 avisos no build; todos os testes passando.

- [ ] **Step 3: Conferir que nenhum arquivo de tema foi tocado por engano**

Run: `git status --short src/Secco.Intranet.Themes.Vertical src/Secco.Intranet.Themes.Horizontal`
Expected: vazio — as views de Inventário vivem no core, não em `Themes/*` (ver nota da Task 10).

- [ ] **Step 4: Commit**

```bash
git add docs/roadmap.md
git commit -m "docs(roadmap): fecha o item de Controle de Inventario

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
