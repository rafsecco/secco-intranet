# Árvore de itens de menu por setor (ItemMenu) — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Substituir as duas abas fixas de `/setor/{slug}` (Documentos, Avisos) por uma
árvore de `ItemMenu` configurável por setor — a mesma entidade absorve o que o roadmap
chamava de `Recurso`/`SetorRecurso`. Ligar/desligar um recurso vira "existe um `ItemMenu`
filho ativo daquele tipo debaixo da raiz do setor", sem tabela de toggle separada.

**Architecture:** Nova entidade autorrecursiva `ItemMenu` (Domain), um `Setor` sempre nasce
com uma linha raiz própria (`Tipo = Setor`), Documentos/Avisos entram como filhos dela. O
menu lateral/superior do produto (`IntranetNavigation`) não muda — a árvore vive só dentro
de `/setor/{slug}/{**caminho}`, resolvida nó a nó; um nó com filhos redireciona para o
primeiro filho ativo (mesma UX de hoje, onde a URL "nua" já mostra Documentos). A
administração da árvore (criar/editar/(des)ativar/excluir/reordenar item) é exclusiva do
`intranet-admin`, em `SetoresController` — igual ao cadastro do próprio Setor.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core (SQL Server e PostgreSQL), xUnit +
AwesomeAssertions.

**Spec:** [docs/specs/2026-09-30-item-menu-por-setor-design.md](../specs/2026-09-30-item-menu-por-setor-design.md)

## Global Constraints

- Tabulação: `\t` em `.cs`, 4 espaços em `.cshtml` (`.editorconfig` do repo).
- Nomenclatura de banco: notação húngara automática via `SeccoDbContext` — ninguém digita
  nome de coluna/tabela; a convention da plataforma decide (ADR-0017).
- Toda migration nasce em **dois** projetos, `Secco.Intranet.Migrations.SqlServer` e
  `Secco.Intranet.Migrations.Postgres`, com o mesmo nome de migration nos dois.
- Erro de negócio sempre por `Result`/`Error` (ADR-0004) — nunca exceção não tratada
  cruzando a fronteira de um handler.
- `intranet-admin` é sempre bypass por identidade (`SomenteIntranetAdminAttribute`), nunca
  resolvido por permissão.
- Build com **0 avisos**; suíte inteira verde antes de cada commit.
- Commits diretos na `main`, sem branch de feature — um commit por task concluída.
- Ao adicionar uma action nova em `SetoresController`, a rota precisa entrar nas listas de
  `tests/Secco.Intranet.Tests/Integration/SetoresAutorizacaoTests.cs` (Task 11) — senão a
  action nasce sem teste de bloqueio.

## Review Focus

- **Setor com os dois checkboxes desmarcados na criação:** nasce só com a linha raiz, sem
  nenhum filho. `GET /setor/{slug}` não pode dar 500 nem 404 — precisa mostrar uma página
  de estado vazio explicando que não há item de menu ainda (Task 9/10 precisam de teste
  disso, não só do caminho feliz com Documentos+Avisos).
- **Segmento de caminho apontando para um item desativado:** `GET
  /setor/{slug}/documentos` depois de alguém desativar o item `Documentos` daquele setor
  precisa virar 404, do mesmo jeito que um setor inexistente — não pode continuar
  mostrando o conteúdo antigo (Task 8/9 precisam de teste específico para isso).
- **Recurso desligado, mas POST ainda aceito:** desativar `Avisos` tem de fechar também
  `POST /setor/{slug}/avisos`, não só esconder a aba — senão o recurso some da tela e
  continua gravável (Task 9, `RecursoDesligado_RecusaPost_MesmoComTokenValido`).
- **Rota de item Personalizado apontando para fora:** `javascript:…`, `//outro-dominio` e
  esquemas que não são http(s) vão parar num `Redirect` (Task 9); precisam ser recusados na
  criação (Task 4, `Rota_SoCaminhoLocalOuHttp`).
- **Reconciliação rodada duas vezes em um setor que já tem `Documentos` mas não `Avisos`**
  (estado misto, não "nenhum dos dois"): precisa completar só o que falta, sem tentar
  recriar `Documentos` e sem duplicar (Task 6) — e percorrer **todas** as páginas de
  setores, não só a primeira.
- **Leitura sem `setor-{slug}:read`:** `PodeLerAsync` (Task 9) tem o mesmo bypass de
  `PodePublicarAsync` — sem autenticação configurada, libera. A suíte roda em `Testing`,
  onde a autenticação nunca é configurada, então **não existe teste de integração possível
  para o ramo restritivo**; a permissão em si é coberta por `PermissoesDeSetorTests`. Quem
  revisar deve conferir por leitura que `Resolver` chama `PodeLerAsync` **antes** de
  resolver o caminho (inclusive com caminho vazio), e não aceitar um teste "sem permissão →
  404" que passe pelo motivo errado.

Sobre ciclos na árvore (invariante da spec): nenhuma ação desta rodada altera o `ParentId`
de um item existente — ele é definido só na criação, apontando para um nó que já existe. Um
item novo não pode ser ancestral de ninguém, então a árvore é acíclica **por construção**,
sem código de checagem. Quando uma ação de "mover para outro pai" existir, ela traz a
checagem junto (o algoritmo é o de `RegrasDeGestor.CriariaCiclo`, do Diretório); escrever
isso agora seria código morto.

---

## Task 1: Entidade `ItemMenu` e enum `TipoDeItemMenu`

**Files:**
- Create: `src/Secco.Intranet.Domain/Menu/ItemMenu.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/ItemMenuTests.cs`

**Interfaces:**
- Produces: `ItemMenu` (construtor `ItemMenu(Guid setorId, Guid? parentId, string nome, string slug, TipoDeItemMenu tipo, string? rota, string? icone, int ordem)`,
  propriedades `Id`/`SetorId`/`ParentId`/`Nome`/`Slug`/`Tipo`/`Rota`/`Icone`/`Ordem`/`Ativo`,
  métodos `Ativar()`/`Desativar()`/`DefinirOrdem(int)`), `TipoDeItemMenu` (`Setor = 0`,
  `Documentos = 1`, `Avisos = 2`, `Personalizado = 3`), `ItemMenu.IconeEhValido(string?)`
  (reaproveita o mesmo formato de `Setor.IconeEhValido`).

- [ ] **Step 1: Escrever o teste da entidade que falha**

```csharp
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
		var acao = () => new ItemMenu(SetorId, null, "Nome", "slug", TipoDeItemMenu.Personalizado, null, "nao-e-bootstrap", 0);

		acao.Should().Throw<DomainInvariantException>();
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
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ItemMenuTests`
Expected: FAIL (compilação — `Secco.Intranet.Domain.Menu` não existe)

- [ ] **Step 3: Implementar a entidade**

```csharp
using System.Text.RegularExpressions;
using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Menu;

/// <summary>Tipo de um nó da árvore de menu de um setor.</summary>
public enum TipoDeItemMenu
{
	/// <summary>Linha raiz que representa o próprio Setor — única por setor, nunca criável/excluível pela tela.</summary>
	Setor = 0,

	/// <summary>Repositório de documentos do setor.</summary>
	Documentos = 1,

	/// <summary>Avisos (publicações) do setor.</summary>
	Avisos = 2,

	/// <summary>Item livre — rótulo e uma rota opcional, sem recurso embutido.</summary>
	Personalizado = 3,
}

/// <summary>
/// Nó de uma árvore autorrecursiva de itens de menu, escopada a um Setor (ADR do
/// ItemMenu, 2026-09-30). A raiz (<see cref="TipoDeItemMenu.Setor"/>) nasce e morre junto
/// com o <c>Setor</c>; Documentos, Avisos e itens <c>Personalizado</c> são sempre filhos,
/// diretos ou não, dela.
/// </summary>
public sealed class ItemMenu : BaseEntity
{
	private ItemMenu()
	{
		// Construtor de rehidratação do EF Core
		Nome = string.Empty;
		Slug = string.Empty;
	}

	/// <summary>Cria um nó da árvore.</summary>
	/// <param name="setorId">Setor dono da árvore. Denormalizado em toda linha.</param>
	/// <param name="parentId">Pai na árvore; nulo só na linha raiz.</param>
	/// <param name="nome">Rótulo de exibição. Obrigatório sempre, mesmo em <c>Personalizado</c>.</param>
	/// <param name="slug">Identificador único entre irmãos, usado na URL.</param>
	/// <param name="tipo">Tipo do nó.</param>
	/// <param name="rota">Link opcional, só relevante em <see cref="TipoDeItemMenu.Personalizado"/>.</param>
	/// <param name="icone">Classe do Bootstrap Icons; vazio/nulo = sem ícone.</param>
	/// <param name="ordem">Posição entre irmãos.</param>
	/// <exception cref="DomainInvariantException">Nome/slug vazios, ou ícone fora do formato.</exception>
	public ItemMenu(
		Guid setorId, Guid? parentId, string nome, string slug, TipoDeItemMenu tipo, string? rota, string? icone, int ordem)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Um item de menu exige nome não vazio.");
		}

		if (string.IsNullOrWhiteSpace(slug))
		{
			throw new DomainInvariantException("Um item de menu exige slug não vazio.");
		}

		if (!IconeEhValido(icone))
		{
			throw new DomainInvariantException("O ícone precisa ser uma classe do Bootstrap Icons, como 'bi-cash-coin'.");
		}

		SetorId = setorId;
		ParentId = parentId;
		Nome = nome;
		Slug = slug.Trim().ToLowerInvariant();
		Tipo = tipo;
		Rota = string.IsNullOrWhiteSpace(rota) ? null : rota.Trim();
		Icone = string.IsNullOrWhiteSpace(icone) ? null : icone.Trim().ToLowerInvariant();
		Ordem = ordem;
		Ativo = true;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	private static readonly Regex FormatoDoIcone = new("^bi-[a-z0-9-]+$", RegexOptions.Compiled);

	/// <summary>Indica se o ícone informado é aceitável — vazio é válido (sem ícone).</summary>
	/// <param name="icone">Classe informada.</param>
	public static bool IconeEhValido(string? icone) =>
		string.IsNullOrWhiteSpace(icone) || FormatoDoIcone.IsMatch(icone.Trim().ToLowerInvariant());

	/// <summary>Setor dono da árvore (coluna <c>id_fk_setor</c>).</summary>
	public Guid SetorId { get; private set; }

	/// <summary>Pai na árvore; nulo só na linha raiz (coluna <c>id_fk_item_menu_pai</c>).</summary>
	public Guid? ParentId { get; private set; }

	/// <summary>Rótulo de exibição (coluna <c>ds_nome</c>).</summary>
	public string Nome { get; private set; }

	/// <summary>Identificador único entre irmãos (coluna <c>ds_slug</c>).</summary>
	public string Slug { get; private set; }

	/// <summary>Tipo do nó (coluna <c>nu_tipo</c>).</summary>
	public TipoDeItemMenu Tipo { get; private set; }

	/// <summary>Link opcional, só relevante em <see cref="TipoDeItemMenu.Personalizado"/> (coluna <c>ds_rota</c>).</summary>
	public string? Rota { get; private set; }

	/// <summary>Classe do Bootstrap Icons; nulo = sem ícone (coluna <c>ds_icone</c>).</summary>
	public string? Icone { get; private set; }

	/// <summary>Posição entre irmãos (coluna <c>nu_ordem</c>).</summary>
	public int Ordem { get; private set; }

	/// <summary>Ativo — some da árvore quando <c>false</c>, sem apagar (coluna <c>fl_ativo</c>).</summary>
	public bool Ativo { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Desativa o item — some da árvore, sem apagar.</summary>
	public void Desativar() => Ativo = false;

	/// <summary>Reativa o item.</summary>
	public void Ativar() => Ativo = true;

	/// <summary>Troca a posição entre irmãos.</summary>
	/// <param name="ordem">Nova posição.</param>
	public void DefinirOrdem(int ordem) => Ordem = ordem;
}
```

- [ ] **Step 4: Rodar e confirmar que a entidade passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ItemMenuTests`
Expected: PASS

- [ ] **Step 5: Build completo e commit**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Domain/Menu tests/Secco.Intranet.Tests/Unit/ItemMenuTests.cs
git commit -m "feat(menu): entidade ItemMenu da árvore de itens por setor"
```

---

## Task 2: Mapeamento EF e migrations

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Mappings/ItemMenuConfiguration.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs`
- Create (via `dotnet ef`): migrations em `Secco.Intranet.Migrations.SqlServer` e `Secco.Intranet.Migrations.Postgres`
- Test: `tests/Secco.Intranet.Tests/Integration/ItemMenuPersistenceTests.cs`

**Interfaces:**
- Consumes: `ItemMenu`/`TipoDeItemMenu` (Task 1).
- Produces: `IntranetDbContext.ItensMenu` (`DbSet<ItemMenu>`).

- [ ] **Step 1: Escrever o teste de persistência que falha**

```csharp
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class ItemMenuPersistenceTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task PersisteEReleParentIdTipoEIcone()
	{
		Guid setorId;
		Guid raizId;
		Guid filhoId;

		// Banco por tenant (ADR-0005): o escopo manual precisa do tenant, senão o DbContext não
		// tem connection string — mesmo padrão de InventarioPersistenciaTests.
		await using (var scope = factory.Services.CreateAsyncScope())
		{
			scope.ServiceProvider.SetTenant(factory.TenantAlfa);
			var context = scope.ServiceProvider.GetRequiredService<IntranetDbContext>();

			// id_fk_setor é FK de verdade (Restrict) — o setor precisa existir.
			var slug = $"pers-{Guid.NewGuid():N}"[..20];
			var setor = new Setor($"Setor {slug}", slug);
			context.Setores.Add(setor);
			await context.SaveChangesAsync();
			setorId = setor.Id;

			var raiz = new ItemMenu(setorId, null, setor.Nome, slug, TipoDeItemMenu.Setor, null, null, 0);
			context.ItensMenu.Add(raiz);
			await context.SaveChangesAsync();
			raizId = raiz.Id;

			var filho = new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, "bi-folder2", 0);
			context.ItensMenu.Add(filho);
			await context.SaveChangesAsync();
			filhoId = filho.Id;
		}

		await using var outroScope = factory.Services.CreateAsyncScope();
		outroScope.ServiceProvider.SetTenant(factory.TenantAlfa);
		var outroContexto = outroScope.ServiceProvider.GetRequiredService<IntranetDbContext>();

		var lido = await outroContexto.ItensMenu.AsNoTracking().FirstAsync(i => i.Id == filhoId);

		lido.ParentId.Should().Be(raizId);
		lido.SetorId.Should().Be(setorId);
		lido.Tipo.Should().Be(TipoDeItemMenu.Documentos);
		lido.Icone.Should().Be("bi-folder2");
	}
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ItemMenuPersistenceTests`
Expected: FAIL (compilação — `IntranetDbContext.ItensMenu` não existe)

- [ ] **Step 3: Mapeamento EF**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="ItemMenu"/>. Nomes de tabela/colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class ItemMenuConfiguration : IEntityTypeConfiguration<ItemMenu>
{
	public void Configure(EntityTypeBuilder<ItemMenu> builder)
	{
		// Sem navegação: o agregado não precisa carregar Setor nem o pai — só a FK, mesmo
		// padrão de DocumentoConfiguration.
		builder
			.HasOne<Setor>()
			.WithMany()
			.HasForeignKey(item => item.SetorId)
			.OnDelete(DeleteBehavior.Restrict);

		builder
			.HasOne<ItemMenu>()
			.WithMany()
			.HasForeignKey(item => item.ParentId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.Property(item => item.Nome).HasMaxLength(256);
		builder.Property(item => item.Slug).HasMaxLength(128);
		builder.Property(item => item.Rota).HasMaxLength(512);
		builder.Property(item => item.Icone).HasMaxLength(64);

		builder.HasIndex(item => item.SetorId);
		builder.HasIndex(item => item.ParentId);
		// Um Slug só precisa ser único entre irmãos — o índice cobre a combinação; a
		// aplicação (Task 4) confere antes de gravar, isto aqui é defesa em profundidade.
		builder.HasIndex(item => new { item.ParentId, item.Slug });
	}
}
```

- [ ] **Step 4: Registrar o `DbSet`**

Em `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs`, acrescentar (junto dos
outros `DbSet`, com `using Secco.Intranet.Domain.Menu;` no topo):

```csharp
	/// <summary>Árvore de itens de menu por setor (tabela <c>tb_itens_menu</c>).</summary>
	public DbSet<ItemMenu> ItensMenu => Set<ItemMenu>();
```

- [ ] **Step 5: Gerar as migrations nos dois engines**

```bash
dotnet ef migrations add ItemMenu --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add ItemMenu --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```

Confira os dois arquivos gerados contra migrations anteriores, não contra nomes decorados:
os nomes vêm da convention do `SeccoDbContext` (ADR-0017), então compare cada coluna com a
equivalente já existente — `Ativo` como `Setor.Ativo`, `Nome`/`Slug`/`Icone` como em `Setor`,
`Tipo` (enum) como `ItemInventario.Status` em `*_Inventario.cs`, `Ordem` (int) como qualquer
int já mapeado, `SetorId` como `Documento.SetorId`. O que importa verificar é: as duas FKs
existem (`SetorId` → `tb_setores`, `ParentId` → a própria tabela), `ParentId`/`Rota`/`Icone`
são nullable, e nada saiu `nvarchar(max)`/`text` onde o mapeamento pôs `HasMaxLength`. Se a
FK de `ParentId` sair com um nome estranho, não é defeito — é o que a convention gera para
autorreferência; só registre no commit.

- [ ] **Step 6: Rodar e confirmar que o teste de persistência passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ItemMenuPersistenceTests`
Expected: PASS (a fixture aplica as migrations pendentes antes do teste)

- [ ] **Step 7: Build completo e commit**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Infrastructure src/Secco.Intranet.Migrations.SqlServer src/Secco.Intranet.Migrations.Postgres tests/Secco.Intranet.Tests/Integration/ItemMenuPersistenceTests.cs
git commit -m "feat(menu): mapeamento EF e migrations de ItemMenu (SqlServer e Postgres)"
```

---

## Task 3: `IItemMenuRepository` e adaptador EF

**Files:**
- Create: `src/Secco.Intranet.Application/Menu/IItemMenuRepository.cs`
- Create: `src/Secco.Intranet.Infrastructure/Repositories/ItemMenuRepository.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/ItemMenuRepositoryTests.cs`

**Interfaces:**
- Consumes: `ItemMenu`/`TipoDeItemMenu` (Task 1), `IntranetDbContext.ItensMenu` (Task 2).
- Produces: `IItemMenuRepository` com `AddAsync(ItemMenu, CancellationToken)`,
  `GetByIdAsync(Guid, CancellationToken)` (desrastreado),
  `GetParaEdicaoAsync(Guid, CancellationToken)` (rastreado),
  `ListarPorSetorAsync(Guid setorId, CancellationToken)` (`IReadOnlyList<ItemMenu>`, árvore
  inteira, achatada, inclusive inativos — quem decide o que exibir é o handler),
  `ExisteTipoAsync(Guid setorId, TipoDeItemMenu tipo, CancellationToken)` (para o limite de
  1 Documentos/1 Avisos por setor), `SaveChangesAsync(CancellationToken)`.

- [ ] **Step 1: Escrever o teste que falha**

```csharp
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class ItemMenuRepositoryTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	/// <summary>
	/// Escopo com tenant resolvido, o repositório real do DI e um setor de verdade — id_fk_setor
	/// é FK Restrict, então um SetorId inventado falharia no insert. Quem chama descarta o escopo.
	/// </summary>
	private async Task<(AsyncServiceScope Escopo, IItemMenuRepository Repositorio, Guid SetorId)> CriarAsync()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var slug = $"repo-{Guid.NewGuid():N}"[..20];
		var setor = new Setor($"Setor {slug}", slug);
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
		contexto.Setores.Add(setor);
		await contexto.SaveChangesAsync();

		return (escopo, escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>(), setor.Id);
	}

	[Fact]
	public async Task ListarPorSetor_DevolveTodaAArvore_InclusiveInativos()
	{
		var (escopo, repositorio, setorId) = await CriarAsync();
		await using var _ = escopo;
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var documentos = new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		var avisos = new ItemMenu(setorId, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 1);
		avisos.Desativar();
		await repositorio.AddAsync(raiz);
		await repositorio.AddAsync(documentos);
		await repositorio.AddAsync(avisos);

		var lista = await repositorio.ListarPorSetorAsync(setorId);

		lista.Should().HaveCount(3);
		lista.Should().Contain(item => item.Id == avisos.Id && !item.Ativo);
	}

	[Fact]
	public async Task ExisteTipo_DetectaDocumentosJaCriado_MasNaoAvisos()
	{
		var (escopo, repositorio, setorId) = await CriarAsync();
		await using var _ = escopo;
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var documentos = new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		await repositorio.AddAsync(raiz);
		await repositorio.AddAsync(documentos);

		(await repositorio.ExisteTipoAsync(setorId, TipoDeItemMenu.Documentos)).Should().BeTrue();
		(await repositorio.ExisteTipoAsync(setorId, TipoDeItemMenu.Avisos)).Should().BeFalse();
	}

	[Fact]
	public async Task GetParaEdicao_AlterarESalvar_Persiste()
	{
		var (escopo, repositorio, setorId) = await CriarAsync();
		await using var _ = escopo;
		var item = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		await repositorio.AddAsync(item);

		var rastreado = await repositorio.GetParaEdicaoAsync(item.Id);
		rastreado!.Desativar();
		await repositorio.SaveChangesAsync();

		var relido = await repositorio.GetByIdAsync(item.Id);
		relido!.Ativo.Should().BeFalse();
	}
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ItemMenuRepositoryTests`
Expected: FAIL (compilação — `IItemMenuRepository`/`ItemMenuRepository` não existem)

- [ ] **Step 3: Porta na Application**

```csharp
using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Menu;

/// <summary>Porta de persistência da árvore de itens de menu — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IItemMenuRepository
{
	/// <summary>Persiste um item novo.</summary>
	Task AddAsync(ItemMenu item, CancellationToken cancellationToken = default);

	/// <summary>Busca um item pelo identificador, desrastreado.</summary>
	Task<ItemMenu?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Busca um item <b>rastreado</b>, para alteração.</summary>
	Task<ItemMenu?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Toda a árvore de um setor, achatada, inclusive itens inativos.</summary>
	Task<IReadOnlyList<ItemMenu>> ListarPorSetorAsync(Guid setorId, CancellationToken cancellationToken = default);

	/// <summary>Indica se o setor já tem um item do tipo informado (limite de 1 Documentos/1 Avisos).</summary>
	Task<bool> ExisteTipoAsync(Guid setorId, TipoDeItemMenu tipo, CancellationToken cancellationToken = default);

	/// <summary>Grava alterações pendentes de itens já rastreados.</summary>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Adaptador EF**

```csharp
using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência da árvore de itens de menu no banco do tenant atual.</summary>
internal sealed class ItemMenuRepository(IntranetDbContext context) : IItemMenuRepository
{
	public async Task AddAsync(ItemMenu item, CancellationToken cancellationToken = default)
	{
		context.ItensMenu.Add(item);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<ItemMenu?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.ItensMenu.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);

	public async Task<ItemMenu?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.ItensMenu.FirstOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);

	public async Task<IReadOnlyList<ItemMenu>> ListarPorSetorAsync(Guid setorId, CancellationToken cancellationToken = default) =>
		await context.ItensMenu.AsNoTracking().Where(item => item.SetorId == setorId).ToListAsync(cancellationToken).ConfigureAwait(false);

	public async Task<bool> ExisteTipoAsync(Guid setorId, TipoDeItemMenu tipo, CancellationToken cancellationToken = default) =>
		await context.ItensMenu.AsNoTracking().AnyAsync(item => item.SetorId == setorId && item.Tipo == tipo, cancellationToken).ConfigureAwait(false);

	public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
```

- [ ] **Step 5: Registrar no DI**

Em `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`, junto do
registro de `ISetorRepository`/`SetorRepository`, acrescentar:

```csharp
		services.AddScoped<IItemMenuRepository, ItemMenuRepository>();
```

(com `using Secco.Intranet.Application.Menu;` no topo do arquivo, se ainda não houver).

- [ ] **Step 6: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ItemMenuRepositoryTests`
Expected: PASS

- [ ] **Step 7: Build completo e commit**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Application/Menu src/Secco.Intranet.Infrastructure/Repositories/ItemMenuRepository.cs src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs tests/Secco.Intranet.Tests/Integration/ItemMenuRepositoryTests.cs
git commit -m "feat(menu): porta e adaptador de persistência de ItemMenu"
```

---

## Task 4: DTOs, catálogo de erros e `CriarItemMenuHandler`

**Files:**
- Create: `src/Secco.Intranet.Application/Menu/ItemMenuDtos.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs`
- Create: `src/Secco.Intranet.Application/Menu/CriarItemMenuHandler.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/CriarItemMenuHandlerTests.cs`

**Interfaces:**
- Consumes: `IItemMenuRepository` (Task 3), `ItemMenu`/`TipoDeItemMenu` (Task 1).
- Produces: `ItemMenuDto` (record: `Id`, `ParentId`, `Nome`, `Slug`, `Tipo`, `Rota`,
  `Icone`, `Ordem`, `Ativo`), `CriarItemMenuCommand` (record: `SetorId`, `ParentId`,
  `Nome`, `Slug`, `Tipo`, `Rota`, `Icone`), `CriarItemMenuHandler.HandleAsync(CriarItemMenuCommand, CancellationToken) -> Task<Result<ItemMenuDto>>`,
  `IntranetErrors.Menu.*` (novos códigos, ver Step 3).

- [ ] **Step 1: Escrever o teste que falha**

```csharp
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
		var handler = new CriarItemMenuHandler(repo);

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
		var handler = new CriarItemMenuHandler(repo);

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
		var handler = new CriarItemMenuHandler(repo);

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
		var handler = new CriarItemMenuHandler(repo);

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
		var handler = new CriarItemMenuHandler(repo);

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
		var handler = new CriarItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raizDeOutroSetor.Id, "Nome", "slug", TipoDeItemMenu.Personalizado, null, null));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.PaiInvalido);
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
		var handler = new CriarItemMenuHandler(repo);

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
		var handler = new CriarItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Nome", slug, TipoDeItemMenu.Personalizado, null, null));

		resultado.Error.Should().Be(IntranetErrors.Menu.SlugInvalido);
	}

	[Fact]
	public async Task SlugEmMaiusculas_ENormalizado_NaoRecusado()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var handler = new CriarItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Nome", "Relatorios-2026", TipoDeItemMenu.Personalizado, null, null));

		resultado.Value.Slug.Should().Be("relatorios-2026");
	}

	[Fact]
	public async Task NomeAcimaDoLimite_Recusa_EmVezDeEstourarNoBanco()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = CriarRaiz(repo);
		var handler = new CriarItemMenuHandler(repo);

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
		var handler = new CriarItemMenuHandler(repo);

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
		var handler = new CriarItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(
			new CriarItemMenuCommand(SetorId, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, "/qualquer", null));

		resultado.Value.Rota.Should().BeNull("Rota só tem papel em Personalizado — não guardar lixo nos outros tipos");
	}
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter CriarItemMenuHandlerTests`
Expected: FAIL (compilação — vários tipos não existem ainda)

- [ ] **Step 3: DTOs e erros**

`src/Secco.Intranet.Application/Menu/ItemMenuDtos.cs`:

```csharp
using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Menu;

/// <summary>Um nó da árvore, para leitura.</summary>
public sealed record ItemMenuDto(
	Guid Id, Guid? ParentId, string Nome, string Slug, TipoDeItemMenu Tipo, string? Rota, string? Icone, int Ordem, bool Ativo)
{
	/// <summary>Converte a entidade.</summary>
	public static ItemMenuDto FromEntity(ItemMenu item) =>
		new(item.Id, item.ParentId, item.Nome, item.Slug, item.Tipo, item.Rota, item.Icone, item.Ordem, item.Ativo);
}

/// <summary>Pedido de criação de um item.</summary>
/// <param name="SetorId">Setor dono da árvore.</param>
/// <param name="ParentId">Pai na árvore — nunca nulo pela tela (a raiz sempre existe e é uma opção).</param>
/// <param name="Nome">Rótulo.</param>
/// <param name="Slug">Identificador entre irmãos.</param>
/// <param name="Tipo">Tipo do item — a tela nunca envia <see cref="TipoDeItemMenu.Setor"/>.</param>
/// <param name="Rota">Só relevante em <see cref="TipoDeItemMenu.Personalizado"/>.</param>
/// <param name="Icone">Classe do Bootstrap Icons; vazio = sem ícone.</param>
public sealed record CriarItemMenuCommand(
	Guid SetorId, Guid ParentId, string? Nome, string? Slug, TipoDeItemMenu Tipo, string? Rota, string? Icone);
```

Em `src/Secco.Intranet.Application/IntranetErrors.cs`, acrescentar (novo bloco, junto dos
outros `public static class` do arquivo):

```csharp
	/// <summary>Erros da árvore de itens de menu.</summary>
	public static class Menu
	{
		/// <summary>Nome ausente ou vazio.</summary>
		public static readonly Error NomeRequired =
			Error.Validation("Intranet.Menu.NomeRequired", "O nome é obrigatório.");

		/// <summary>Slug ausente ou vazio.</summary>
		public static readonly Error SlugRequired =
			Error.Validation("Intranet.Menu.SlugRequired", "O slug é obrigatório.");

		/// <summary>Já existe um item com esse slug entre os irmãos do mesmo pai.</summary>
		public static readonly Error SlugJaExisteEntreIrmaos =
			Error.Conflict("Intranet.Menu.SlugJaExisteEntreIrmaos", "Já existe um item com esse identificador neste nível.");

		/// <summary>O setor já tem um item do tipo pedido (Documentos ou Avisos, limitados a um por setor).</summary>
		public static readonly Error TipoJaExiste =
			Error.Conflict("Intranet.Menu.TipoJaExiste", "Este setor já tem um item desse tipo.");

		/// <summary>O pai informado não existe ou é de outro setor.</summary>
		public static readonly Error PaiInvalido =
			Error.Validation("Intranet.Menu.PaiInvalido", "Pai inválido para este item.");

		/// <summary>Ícone fora do formato do Bootstrap Icons.</summary>
		public static readonly Error IconeInvalido =
			Error.Validation("Intranet.Menu.IconeInvalido", "O ícone precisa ser uma classe do Bootstrap Icons, como 'bi-cash-coin'.");

		/// <summary>Item não encontrado no banco do tenant atual.</summary>
		public static readonly Error NotFound =
			Error.NotFound("Intranet.Menu.NotFound", "Item de menu não encontrado.");

		/// <summary>A linha raiz (Tipo = Setor) não é criável/editável/excluível pela tela.</summary>
		public static readonly Error RaizProtegida =
			Error.Validation("Intranet.Menu.RaizProtegida", "O item raiz do setor não pode ser alterado por aqui.");

		/// <summary>Só itens Personalizado se excluem — os embutidos só desativam.</summary>
		public static readonly Error TipoEmbutidoNaoExclui =
			Error.Validation("Intranet.Menu.TipoEmbutidoNaoExclui", "Este item é embutido no produto e só pode ser desativado, não excluído.");

		/// <summary>Tipo ausente do enum, ou <c>Setor</c> (a raiz não se cria pela tela).</summary>
		public static readonly Error TipoInvalido =
			Error.Validation("Intranet.Menu.TipoInvalido", "Tipo de item inválido.");

		/// <summary>Slug fora do formato de segmento de URL.</summary>
		public static readonly Error SlugInvalido =
			Error.Validation(
				"Intranet.Menu.SlugInvalido",
				"O identificador aceita só letras minúsculas sem acento, dígitos e hífen entre eles (ex.: relatorios-2026).");

		/// <summary>Nome, slug, rota ou ícone acima do tamanho da coluna.</summary>
		public static Error CampoMuitoLongo(string campo, int limite) =>
			Error.Validation("Intranet.Menu.CampoMuitoLongo", $"{campo} excede o limite de {limite} caracteres.");

		/// <summary>Rota que não é caminho local nem URL http(s) absoluta (ADR-0020: destino de redirect).</summary>
		public static readonly Error RotaInvalida =
			Error.Validation(
				"Intranet.Menu.RotaInvalida",
				"A rota precisa ser um caminho da própria intranet (começando com /) ou um endereço http/https completo.");
	}
```

- [ ] **Step 4: `CriarItemMenuHandler`**

```csharp
using System.Text.RegularExpressions;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Cria um item na árvore de um setor.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class CriarItemMenuHandler(IItemMenuRepository repository)
{
	// Limites = HasMaxLength de ItemMenuConfiguration (Task 2). Validar aqui é o que
	// transforma "estourou a coluna" (500) em Result de validação (ADR-0004).
	private const int LimiteNome = 256;
	private const int LimiteSlug = 128;
	private const int LimiteRota = 512;
	private const int LimiteIcone = 64;

	private static readonly Regex FormatoDoSlug = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Dados do novo item.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ItemMenuDto>> HandleAsync(CriarItemMenuCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (!Enum.IsDefined(command.Tipo) || command.Tipo == TipoDeItemMenu.Setor)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.TipoInvalido);
		}

		if (string.IsNullOrWhiteSpace(command.Nome))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.NomeRequired);
		}

		if (command.Nome.Trim().Length > LimiteNome)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.CampoMuitoLongo("O nome", LimiteNome));
		}

		if (string.IsNullOrWhiteSpace(command.Slug))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.SlugRequired);
		}

		var slugNormalizado = command.Slug.Trim().ToLowerInvariant();

		if (slugNormalizado.Length > LimiteSlug)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.CampoMuitoLongo("O identificador", LimiteSlug));
		}

		if (!FormatoDoSlug.IsMatch(slugNormalizado))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.SlugInvalido);
		}

		if (!ItemMenu.IconeEhValido(command.Icone))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.IconeInvalido);
		}

		if (command.Icone?.Trim().Length > LimiteIcone)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.CampoMuitoLongo("O ícone", LimiteIcone));
		}

		// Rota só tem papel em Personalizado; nos outros tipos é descartada, não validada.
		var rota = command.Tipo == TipoDeItemMenu.Personalizado ? command.Rota?.Trim() : null;

		if (!string.IsNullOrEmpty(rota))
		{
			if (rota.Length > LimiteRota)
			{
				return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.CampoMuitoLongo("A rota", LimiteRota));
			}

			if (!RotaEhValida(rota))
			{
				return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.RotaInvalida);
			}
		}

		var arvore = await repository.ListarPorSetorAsync(command.SetorId, cancellationToken).ConfigureAwait(false);
		var pai = arvore.FirstOrDefault(item => item.Id == command.ParentId);

		if (pai is null)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.PaiInvalido);
		}

		if (arvore.Any(item => item.ParentId == command.ParentId
			&& string.Equals(item.Slug, slugNormalizado, StringComparison.Ordinal)))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.SlugJaExisteEntreIrmaos);
		}

		if ((command.Tipo == TipoDeItemMenu.Documentos || command.Tipo == TipoDeItemMenu.Avisos)
			&& await repository.ExisteTipoAsync(command.SetorId, command.Tipo, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.TipoJaExiste);
		}

		var proximaOrdem = arvore.Where(item => item.ParentId == command.ParentId).Select(item => item.Ordem).DefaultIfEmpty(-1).Max() + 1;

		var item = new ItemMenu(
			command.SetorId, command.ParentId, command.Nome.Trim(), slugNormalizado, command.Tipo,
			rota, command.Icone, proximaOrdem);

		await repository.AddAsync(item, cancellationToken).ConfigureAwait(false);

		return ItemMenuDto.FromEntity(item);
	}

	/// <summary>
	/// Rota é destino de <c>Redirect</c> (Task 9): caminho local começando com uma barra só
	/// (<c>//host</c> seria outro domínio), ou URL absoluta http/https. Qualquer outro esquema
	/// (<c>javascript:</c>, <c>data:</c>, <c>ftp:</c>) é recusado.
	/// </summary>
	private static bool RotaEhValida(string rota) =>
		(rota.StartsWith('/') && !rota.StartsWith("//", StringComparison.Ordinal) && !rota.StartsWith("/\\", StringComparison.Ordinal))
		|| (Uri.TryCreate(rota, UriKind.Absolute, out var uri)
			&& (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}
```

Note: a checagem de "pai é de outro setor" já sai coberta por `ListarPorSetorAsync` — se o
`ParentId` informado não pertence à árvore daquele `SetorId`, ele simplesmente não aparece
em `arvore`, e cai no mesmo `PaiInvalido` de "pai inexistente". Não precisa de uma consulta
extra.

- [ ] **Step 5: Rodar e confirmar que ainda falha (falta o dublê de teste)**

Run: `dotnet test tests/Secco.Intranet.Tests --filter CriarItemMenuHandlerTests`
Expected: FAIL (compilação — `ItemMenuRepositorioFalso` não existe)

- [ ] **Step 6: Dublê de repositório em memória**

Create: `tests/Secco.Intranet.Tests/Support/ItemMenuRepositorioFalso.cs`

```csharp
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Tests.Support;

/// <summary>Repositório de <see cref="ItemMenu"/> em memória, para os testes de handler.</summary>
public sealed class ItemMenuRepositorioFalso : IItemMenuRepository
{
	/// <summary>Itens existentes — os testes montam o cenário mexendo aqui direto.</summary>
	public List<ItemMenu> Itens { get; } = [];

	public Task AddAsync(ItemMenu item, CancellationToken cancellationToken = default)
	{
		Itens.Add(item);

		return Task.CompletedTask;
	}

	public Task<ItemMenu?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		Task.FromResult(Itens.FirstOrDefault(item => item.Id == id));

	public Task<ItemMenu?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
		Task.FromResult(Itens.FirstOrDefault(item => item.Id == id));

	public Task<IReadOnlyList<ItemMenu>> ListarPorSetorAsync(Guid setorId, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<ItemMenu>>([.. Itens.Where(item => item.SetorId == setorId)]);

	public Task<bool> ExisteTipoAsync(Guid setorId, TipoDeItemMenu tipo, CancellationToken cancellationToken = default) =>
		Task.FromResult(Itens.Any(item => item.SetorId == setorId && item.Tipo == tipo));

	public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

- [ ] **Step 7: Rodar e confirmar que tudo passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter CriarItemMenuHandlerTests`
Expected: PASS (todos — 6 casos originais mais os de tipo, slug, tamanho e rota)

- [ ] **Step 8: Registrar o handler no DI**

Em `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`, junto dos outros
handlers de setor:

```csharp
		services.AddScoped<CriarItemMenuHandler>();
```

- [ ] **Step 9: Build completo e commit**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Application tests/Secco.Intranet.Tests/Unit/CriarItemMenuHandlerTests.cs tests/Secco.Intranet.Tests/Support/ItemMenuRepositorioFalso.cs
git commit -m "feat(menu): DTOs, catálogo de erros e criação de item na árvore"
```

---

## Task 5: Ativar, desativar, excluir e mover item

**Files:**
- Create: `src/Secco.Intranet.Application/Menu/AtivarDesativarItemMenuHandler.cs`
- Create: `src/Secco.Intranet.Application/Menu/ExcluirItemMenuHandler.cs`
- Create: `src/Secco.Intranet.Application/Menu/MoverItemMenuHandler.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/AtivarDesativarItemMenuHandlerTests.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/ExcluirItemMenuHandlerTests.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/MoverItemMenuHandlerTests.cs`

**Interfaces:**
- Consumes: `IItemMenuRepository` (Task 3), `ItemMenuDto` (Task 4).
- Produces: `AtivarDesativarItemMenuHandler.HandleAsync(Guid id, bool ativar, CancellationToken) -> Task<Result>`,
  `ExcluirItemMenuHandler.HandleAsync(Guid id, CancellationToken) -> Task<Result>`,
  `MoverItemMenuHandler.HandleAsync(Guid id, bool paraCima, CancellationToken) -> Task<Result>`.

- [ ] **Step 1: Escrever os três testes que falham**

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class AtivarDesativarItemMenuHandlerTests
{
	[Fact]
	public async Task Desativar_ItemComum_Desativa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var doc = new ItemMenu(raiz.SetorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(doc);
		var handler = new AtivarDesativarItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(doc.Id, ativar: false);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Single(i => i.Id == doc.Id).Ativo.Should().BeFalse();
	}

	[Fact]
	public async Task Desativar_ARaiz_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		repo.Itens.Add(raiz);
		var handler = new AtivarDesativarItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(raiz.Id, ativar: false);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.RaizProtegida);
	}

	[Fact]
	public async Task ItemInexistente_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var handler = new AtivarDesativarItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(Guid.NewGuid(), ativar: true);

		resultado.Error.Should().Be(IntranetErrors.Menu.NotFound);
	}
}
```

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ExcluirItemMenuHandlerTests
{
	[Fact]
	public async Task Personalizado_Exclui()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var item = new ItemMenu(raiz.SetorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(item);
		var handler = new ExcluirItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(item.Id);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Should().NotContain(i => i.Id == item.Id);
	}

	[Fact]
	public async Task Documentos_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var doc = new ItemMenu(raiz.SetorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(doc);
		var handler = new ExcluirItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(doc.Id);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.TipoEmbutidoNaoExclui);
		repo.Itens.Should().ContainSingle(i => i.Id == doc.Id);
	}

	[Fact]
	public async Task Raiz_Recusa()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		repo.Itens.Add(raiz);
		var handler = new ExcluirItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(raiz.Id);

		resultado.Error.Should().Be(IntranetErrors.Menu.RaizProtegida);
	}
}
```

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class MoverItemMenuHandlerTests
{
	private static (ItemMenuRepositorioFalso Repo, ItemMenu A, ItemMenu B, ItemMenu C) CenarioComTresIrmaos()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var a = new ItemMenu(raiz.SetorId, raiz.Id, "A", "a", TipoDeItemMenu.Personalizado, null, null, 0);
		var b = new ItemMenu(raiz.SetorId, raiz.Id, "B", "b", TipoDeItemMenu.Personalizado, null, null, 1);
		var c = new ItemMenu(raiz.SetorId, raiz.Id, "C", "c", TipoDeItemMenu.Personalizado, null, null, 2);
		repo.Itens.Add(raiz);
		repo.Itens.Add(a);
		repo.Itens.Add(b);
		repo.Itens.Add(c);

		return (repo, a, b, c);
	}

	[Fact]
	public async Task MoverParaCima_TrocaComOAnterior()
	{
		var (repo, a, b, _) = CenarioComTresIrmaos();
		var handler = new MoverItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(b.Id, paraCima: true);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Single(i => i.Id == b.Id).Ordem.Should().Be(0);
		repo.Itens.Single(i => i.Id == a.Id).Ordem.Should().Be(1);
	}

	[Fact]
	public async Task MoverParaBaixo_TrocaComOProximo()
	{
		var (repo, _, b, c) = CenarioComTresIrmaos();
		var handler = new MoverItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(b.Id, paraCima: false);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Single(i => i.Id == b.Id).Ordem.Should().Be(2);
		repo.Itens.Single(i => i.Id == c.Id).Ordem.Should().Be(1);
	}

	[Fact]
	public async Task MoverOPrimeiroParaCima_NaoFazNada()
	{
		var (repo, a, _, _) = CenarioComTresIrmaos();
		var handler = new MoverItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(a.Id, paraCima: true);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Single(i => i.Id == a.Id).Ordem.Should().Be(0);
	}

	[Fact]
	public async Task MoverOUltimoParaBaixo_NaoFazNada()
	{
		var (repo, _, _, c) = CenarioComTresIrmaos();
		var handler = new MoverItemMenuHandler(repo);

		var resultado = await handler.HandleAsync(c.Id, paraCima: false);

		resultado.IsSuccess.Should().BeTrue();
		repo.Itens.Single(i => i.Id == c.Id).Ordem.Should().Be(2);
	}
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "AtivarDesativarItemMenuHandlerTests|ExcluirItemMenuHandlerTests|MoverItemMenuHandlerTests"`
Expected: FAIL (compilação — os três handlers não existem)

- [ ] **Step 3: Implementar os três handlers**

```csharp
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>(Des)ativa um item — nunca a linha raiz.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class AtivarDesativarItemMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Item a alterar.</param>
	/// <param name="ativar"><c>true</c> ativa, <c>false</c> desativa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, bool ativar, CancellationToken cancellationToken = default)
	{
		var item = await repository.GetParaEdicaoAsync(id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return Result.Failure(IntranetErrors.Menu.NotFound);
		}

		if (item.Tipo == Domain.Menu.TipoDeItemMenu.Setor)
		{
			return Result.Failure(IntranetErrors.Menu.RaizProtegida);
		}

		if (ativar)
		{
			item.Ativar();
		}
		else
		{
			item.Desativar();
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
```

```csharp
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Exclui um item — só <see cref="TipoDeItemMenu.Personalizado"/> se exclui de verdade.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ExcluirItemMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Item a excluir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var item = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return Result.Failure(IntranetErrors.Menu.NotFound);
		}

		if (item.Tipo == TipoDeItemMenu.Setor)
		{
			return Result.Failure(IntranetErrors.Menu.RaizProtegida);
		}

		if (item.Tipo != TipoDeItemMenu.Personalizado)
		{
			return Result.Failure(IntranetErrors.Menu.TipoEmbutidoNaoExclui);
		}

		await repository.ExcluirAsync(id, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
```

Isso exige mais um método na porta — voltar em `IItemMenuRepository`
(`src/Secco.Intranet.Application/Menu/IItemMenuRepository.cs`) e acrescentar:

```csharp
	/// <summary>Exclui um item — só chamado para <c>TipoDeItemMenu.Personalizado</c>.</summary>
	Task ExcluirAsync(Guid id, CancellationToken cancellationToken = default);
```

E em `ItemMenuRepository` (`src/Secco.Intranet.Infrastructure/Repositories/ItemMenuRepository.cs`):

```csharp
	public async Task ExcluirAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var item = await context.ItensMenu.FirstOrDefaultAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false);

		if (item is not null)
		{
			context.ItensMenu.Remove(item);
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
	}
```

E em `ItemMenuRepositorioFalso` (`tests/Secco.Intranet.Tests/Support/ItemMenuRepositorioFalso.cs`):

```csharp
	public Task ExcluirAsync(Guid id, CancellationToken cancellationToken = default)
	{
		Itens.RemoveAll(item => item.Id == id);

		return Task.CompletedTask;
	}
```

```csharp
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Troca a posição de um item com o irmão adjacente.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class MoverItemMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso. Mover o primeiro para cima (ou o último para baixo) não faz nada — sucesso, sem efeito.</summary>
	/// <param name="id">Item a mover.</param>
	/// <param name="paraCima"><c>true</c> troca com o irmão anterior; <c>false</c>, com o próximo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid id, bool paraCima, CancellationToken cancellationToken = default)
	{
		var item = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

		if (item is null)
		{
			return Result.Failure(IntranetErrors.Menu.NotFound);
		}

		var irmaos = (await repository.ListarPorSetorAsync(item.SetorId, cancellationToken).ConfigureAwait(false))
			.Where(i => i.ParentId == item.ParentId)
			.OrderBy(i => i.Ordem)
			.ToList();

		var indice = irmaos.FindIndex(i => i.Id == id);
		var indiceDoVizinho = paraCima ? indice - 1 : indice + 1;

		if (indiceDoVizinho < 0 || indiceDoVizinho >= irmaos.Count)
		{
			return Result.Success();
		}

		// Troca de posição na lista e renumera TODOS os irmãos 0..n-1. Trocar só os dois
		// valores de Ordem não basta: depois de uma exclusão as ordens ficam com buraco
		// (ex.: 0, 5, 6, 7), e dar ao par os índices novos quebraria a ordem dos vizinhos.
		// Também resolve empate de Ordem, se algum dia existir.
		(irmaos[indice], irmaos[indiceDoVizinho]) = (irmaos[indiceDoVizinho], irmaos[indice]);

		for (var posicao = 0; posicao < irmaos.Count; posicao++)
		{
			var rastreado = await repository.GetParaEdicaoAsync(irmaos[posicao].Id, cancellationToken).ConfigureAwait(false);
			rastreado!.DefinirOrdem(posicao);
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
```

Os valores esperados dos testes do Step 1 (`MoverParaCima` → B fica 0 e A fica 1;
`MoverParaBaixo` → B fica 2 e C fica 1) continuam os mesmos com a renumeração, porque o
cenário já parte de 0, 1, 2. Acrescente ao arquivo `MoverItemMenuHandlerTests.cs` o caso
que só a renumeração acerta:

```csharp
	[Fact]
	public async Task OrdensComBuraco_MoverOUltimoParaCima_MantemAOrdemDosOutros()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var a = new ItemMenu(raiz.SetorId, raiz.Id, "A", "a", TipoDeItemMenu.Personalizado, null, null, 0);
		var b = new ItemMenu(raiz.SetorId, raiz.Id, "B", "b", TipoDeItemMenu.Personalizado, null, null, 5);
		var c = new ItemMenu(raiz.SetorId, raiz.Id, "C", "c", TipoDeItemMenu.Personalizado, null, null, 6);
		var d = new ItemMenu(raiz.SetorId, raiz.Id, "D", "d", TipoDeItemMenu.Personalizado, null, null, 7);
		repo.Itens.AddRange([raiz, a, b, c, d]);

		await new MoverItemMenuHandler(repo).HandleAsync(d.Id, paraCima: true);

		repo.Itens.Where(i => i.ParentId == raiz.Id).OrderBy(i => i.Ordem).Select(i => i.Nome)
			.Should().Equal("A", "B", "D", "C");
	}
```

Note sobre o dublê: em `ItemMenuRepositorioFalso`, `GetByIdAsync` e `GetParaEdicaoAsync`
devolvem **a mesma instância** — por isso o handler lê tudo que precisa (`irmaos`, índices)
antes de começar a mutar. Não reintroduza leitura de `item.Ordem` depois do laço.

- [ ] **Step 4: Rodar e confirmar que tudo passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "AtivarDesativarItemMenuHandlerTests|ExcluirItemMenuHandlerTests|MoverItemMenuHandlerTests"`
Expected: PASS (11 testes)

- [ ] **Step 5: Registrar os três handlers no DI**

Em `IntranetApplicationExtensions.cs`:

```csharp
		services.AddScoped<AtivarDesativarItemMenuHandler>();
		services.AddScoped<ExcluirItemMenuHandler>();
		services.AddScoped<MoverItemMenuHandler>();
```

- [ ] **Step 6: Build completo e commit**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Application src/Secco.Intranet.Infrastructure/Repositories/ItemMenuRepository.cs tests/Secco.Intranet.Tests/Unit/AtivarDesativarItemMenuHandlerTests.cs tests/Secco.Intranet.Tests/Unit/ExcluirItemMenuHandlerTests.cs tests/Secco.Intranet.Tests/Unit/MoverItemMenuHandlerTests.cs tests/Secco.Intranet.Tests/Support/ItemMenuRepositorioFalso.cs
git commit -m "feat(menu): ativar, desativar, excluir e mover item da árvore"
```

---

## Task 6: Montar a árvore completa e reconciliar setores antigos

**Files:**
- Create: `src/Secco.Intranet.Application/Menu/ObterArvoreDeMenuHandler.cs`
- Create: `src/Secco.Intranet.Application/Menu/ReconciliarItensDeMenuHandler.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/ObterArvoreDeMenuHandlerTests.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/ReconciliarItensDeMenuHandlerTests.cs`

**Interfaces:**
- Consumes: `IItemMenuRepository` (Task 3), `ISetorRepository`/`SearchSetoresHandler`
  (já existentes), `ItemMenuDto` (Task 4).
- Produces: `NoDaArvoreDto` (record recursivo: `Item` (`ItemMenuDto`), `Filhos`
  (`IReadOnlyList<NoDaArvoreDto>`)), `ObterArvoreDeMenuHandler.HandleAsync(Guid setorId, CancellationToken) -> Task<Result<NoDaArvoreDto>>`
  (raiz da árvore, com os filhos aninhados), `ReconciliarItensDeMenuHandler.HandleAsync(CancellationToken) -> Task<int>`
  (quantos setores ganharam algo).

- [ ] **Step 1: Escrever os testes que falham**

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ObterArvoreDeMenuHandlerTests
{
	[Fact]
	public async Task MontaAArvoreAninhada_ComOsFilhosDosFilhos()
	{
		var repo = new ItemMenuRepositorioFalso();
		var setorId = Guid.NewGuid();
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var relatorios = new ItemMenu(setorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		var vendas = new ItemMenu(setorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/relatorios/vendas", null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(relatorios);
		repo.Itens.Add(vendas);
		var handler = new ObterArvoreDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Item.Tipo.Should().Be(TipoDeItemMenu.Setor);
		resultado.Value.Filhos.Should().ContainSingle(f => f.Item.Slug == "relatorios");
		resultado.Value.Filhos.Single().Filhos.Should().ContainSingle(f => f.Item.Slug == "vendas");
	}

	[Fact]
	public async Task SetorSemArvore_Falha()
	{
		var repo = new ItemMenuRepositorioFalso();
		var handler = new ObterArvoreDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(Guid.NewGuid());

		resultado.IsFailure.Should().BeTrue();
	}
}
```

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ReconciliarItensDeMenuHandlerTests
{
	private sealed class FakeSetorRepository : ISetorRepository
	{
		private readonly List<Setor> _setores = [];

		public FakeSetorRepository Com(string slug)
		{
			_setores.Add(new Setor(slug, slug));

			return this;
		}

		public Task<PagedResult<Setor>> SearchAsync(SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(new PagedResult<Setor>(_setores, PageRequest.FirstPage, Math.Max(_setores.Count, 1), _setores.Count));

		public Task AddAsync(Setor setor, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

		public IReadOnlyList<Setor> Todos => _setores;
	}

	[Fact]
	public async Task SetorSemNenhumItem_GanhaRaizEOsDoisFilhos()
	{
		var setores = new FakeSetorRepository().Com("financeiro");
		var itens = new ItemMenuRepositorioFalso();
		var handler = new ReconciliarItensDeMenuHandler(new SearchSetoresHandler(setores), itens);

		var quantos = await handler.HandleAsync();

		quantos.Should().Be(1);
		var setorId = setores.Todos.Single().Id;
		itens.Itens.Should().ContainSingle(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Setor);
		itens.Itens.Should().ContainSingle(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Documentos);
		itens.Itens.Should().ContainSingle(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Avisos);
	}

	[Fact]
	public async Task SetorComDocumentosMasSemAvisos_SoCompletaOQueFalta()
	{
		var setores = new FakeSetorRepository().Com("ti");
		var setorId = setores.Todos.Single().Id;
		var itens = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(setorId, null, "ti", "ti", TipoDeItemMenu.Setor, null, null, 0);
		itens.Itens.Add(raiz);
		itens.Itens.Add(new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0));
		var handler = new ReconciliarItensDeMenuHandler(new SearchSetoresHandler(setores), itens);

		await handler.HandleAsync();

		itens.Itens.Where(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Setor).Should().ContainSingle();
		itens.Itens.Where(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Documentos).Should().ContainSingle();
		itens.Itens.Where(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Avisos).Should().ContainSingle();
	}

	[Fact]
	public async Task RodarDuasVezes_NaoDuplica()
	{
		var setores = new FakeSetorRepository().Com("rh");
		var itens = new ItemMenuRepositorioFalso();
		var handler = new ReconciliarItensDeMenuHandler(new SearchSetoresHandler(setores), itens);

		await handler.HandleAsync();
		await handler.HandleAsync();

		var setorId = setores.Todos.Single().Id;
		itens.Itens.Count(i => i.SetorId == setorId).Should().Be(3);
	}
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "ObterArvoreDeMenuHandlerTests|ReconciliarItensDeMenuHandlerTests"`
Expected: FAIL (compilação)

- [ ] **Step 3: Implementar**

```csharp
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Um nó da árvore, com os filhos já aninhados.</summary>
/// <param name="Item">O nó.</param>
/// <param name="Filhos">Filhos diretos, cada um com os próprios filhos.</param>
public sealed record NoDaArvoreDto(ItemMenuDto Item, IReadOnlyList<NoDaArvoreDto> Filhos);

/// <summary>Monta a árvore inteira de um setor, aninhada, para a tela de administração.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ObterArvoreDeMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorId">Setor dono da árvore.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<NoDaArvoreDto>> HandleAsync(Guid setorId, CancellationToken cancellationToken = default)
	{
		var todos = await repository.ListarPorSetorAsync(setorId, cancellationToken).ConfigureAwait(false);
		var raiz = todos.FirstOrDefault(item => item.Tipo == Domain.Menu.TipoDeItemMenu.Setor);

		if (raiz is null)
		{
			return Result.Failure<NoDaArvoreDto>(IntranetErrors.Menu.NotFound);
		}

		NoDaArvoreDto Montar(Guid id)
		{
			var item = todos.First(i => i.Id == id);
			var filhos = todos.Where(i => i.ParentId == id).OrderBy(i => i.Ordem).Select(i => Montar(i.Id)).ToList();

			return new NoDaArvoreDto(ItemMenuDto.FromEntity(item), filhos);
		}

		return Montar(raiz.Id);
	}
}
```

```csharp
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Menu;

/// <summary>
/// Garante que todo setor tenha, no mínimo, a linha raiz e os itens Documentos/Avisos —
/// mesmo padrão de "Reconciliar permissões" (2026-09-27), para setores criados antes desta
/// feature. Nunca remove nada já customizado.
/// </summary>
/// <param name="searchSetores">Busca dos setores do tenant.</param>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ReconciliarItensDeMenuHandler(SearchSetoresHandler searchSetores, IItemMenuRepository repository)
{
	// Mesmo tamanho de página de ReconciliarPermissoesHandler — e o mesmo laço: sem ele só a
	// primeira página de setores seria reconciliada, em silêncio.
	private const int TamanhoDaPagina = 100;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Quantos setores ganharam a raiz e/ou algum dos dois itens embutidos.</returns>
	public async Task<int> HandleAsync(CancellationToken cancellationToken = default)
	{
		var alterados = 0;
		var pagina = PageRequest.FirstPage;

		while (true)
		{
			var busca = await searchSetores
				.HandleAsync(new SetorSearchCriteria(Page: new PageRequest(pagina, TamanhoDaPagina)), cancellationToken)
				.ConfigureAwait(false);

			foreach (var setor in busca.Value.Items)
			{
				if (await ReconciliarSetorAsync(setor, cancellationToken).ConfigureAwait(false))
				{
					alterados++;
				}
			}

			if (!busca.Value.HasNextPage)
			{
				break;
			}

			pagina++;
		}

		return alterados;
	}

	private async Task<bool> ReconciliarSetorAsync(SetorDto setor, CancellationToken cancellationToken)
	{
		var itens = (await repository.ListarPorSetorAsync(setor.Id, cancellationToken).ConfigureAwait(false)).ToList();
		var raiz = itens.FirstOrDefault(item => item.Tipo == TipoDeItemMenu.Setor);
		var mudou = false;

		if (raiz is null)
		{
			raiz = new ItemMenu(setor.Id, null, setor.Nome, setor.Slug, TipoDeItemMenu.Setor, null, null, 0);
			await repository.AddAsync(raiz, cancellationToken).ConfigureAwait(false);
			itens.Add(raiz);
			mudou = true;
		}

		// Documentos antes de Avisos, de propósito (diferente da criação, que é alfabética):
		// estes setores já existem, e hoje /setor/{slug} abre em Documentos — reconciliar não
		// deve mudar a página de entrada de ninguém. O admin reordena depois, se quiser.
		foreach (var (tipo, nome, slugDesejado) in new[]
		{
			(TipoDeItemMenu.Documentos, "Documentos", "documentos"),
			(TipoDeItemMenu.Avisos, "Avisos", "avisos"),
		})
		{
			if (itens.Any(item => item.Tipo == tipo))
			{
				continue;
			}

			var irmaos = itens.Where(item => item.ParentId == raiz.Id).ToList();
			var ordem = irmaos.Select(item => item.Ordem).DefaultIfEmpty(-1).Max() + 1;
			var item = new ItemMenu(setor.Id, raiz.Id, nome, SlugLivre(slugDesejado, irmaos), tipo, null, null, ordem);

			await repository.AddAsync(item, cancellationToken).ConfigureAwait(false);
			itens.Add(item);
			mudou = true;
		}

		return mudou;
	}

	/// <summary>
	/// Um Personalizado criado pela tela pode já ter tomado o slug "documentos" debaixo da
	/// raiz; nesse caso sufixa (-2, -3…) em vez de gravar dois irmãos com o mesmo slug.
	/// </summary>
	private static string SlugLivre(string desejado, IReadOnlyCollection<ItemMenu> irmaos)
	{
		var candidato = desejado;
		var sufixo = 2;

		while (irmaos.Any(item => string.Equals(item.Slug, candidato, StringComparison.Ordinal)))
		{
			candidato = $"{desejado}-{sufixo++}";
		}

		return candidato;
	}
}
```

Precisa de `using Secco.SharedKernel.Pagination;` no topo (para `PageRequest`). Acrescente
ao arquivo de teste do Step 1 o caso de colisão:

```csharp
	[Fact]
	public async Task PersonalizadoJaUsandoOSlugDocumentos_ReconciliaComSufixo()
	{
		var setores = new FakeSetorRepository().Com("rh-colisao");
		var setorId = setores.Todos.Single().Id;
		var itens = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(setorId, null, "rh", "rh", TipoDeItemMenu.Setor, null, null, 0);
		itens.Itens.Add(raiz);
		itens.Itens.Add(new ItemMenu(setorId, raiz.Id, "Documentos antigos", "documentos", TipoDeItemMenu.Personalizado, "/x", null, 0));
		var handler = new ReconciliarItensDeMenuHandler(new SearchSetoresHandler(setores), itens);

		await handler.HandleAsync();

		itens.Itens.Single(i => i.SetorId == setorId && i.Tipo == TipoDeItemMenu.Documentos).Slug.Should().Be("documentos-2");
	}
```

- [ ] **Step 4: Rodar e confirmar que tudo passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "ObterArvoreDeMenuHandlerTests|ReconciliarItensDeMenuHandlerTests"`
Expected: PASS (5 testes)

- [ ] **Step 5: Registrar no DI**

Em `IntranetApplicationExtensions.cs`:

```csharp
		services.AddScoped<ObterArvoreDeMenuHandler>();
		services.AddScoped<ReconciliarItensDeMenuHandler>();
```

- [ ] **Step 6: Build completo e commit**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Application tests/Secco.Intranet.Tests/Unit/ObterArvoreDeMenuHandlerTests.cs tests/Secco.Intranet.Tests/Unit/ReconciliarItensDeMenuHandlerTests.cs
git commit -m "feat(menu): montar a árvore aninhada e reconciliar setores antigos"
```

---

## Task 7: `CreateSetorHandler` cria a raiz e, opcionalmente, Documentos/Avisos

**Files:**
- Modify: `src/Secco.Intranet.Application/Setores/CreateSetorHandler.cs`
- Modify (test): `tests/Secco.Intranet.Tests/Unit/CreateSetorHandlerTests.cs` (já existe —
  6 call sites a ajustar + 2 testes novos)
- Modify (test): `tests/Secco.Intranet.Tests/Unit/AuditoriaDeSetorTests.cs` (1 call site)

**Interfaces:**
- Consumes: `IItemMenuRepository` (Task 3), `ItemMenu`/`TipoDeItemMenu` (Task 1).
- Produces: `CreateSetorCommand` ganha `HabilitarDocumentos`/`HabilitarAvisos` (`bool`,
  default `true` nos dois).

O arquivo `tests/Secco.Intranet.Tests/Unit/CreateSetorHandlerTests.cs` já existe, com um
`FakeRepository` **privado** (`ISetorRepository`), `FakeSetorAccessProvisioner` e
`TrilhaFalsa`. Todos os 6 testes de lá constroem
`new CreateSetorHandler(repository, Options, provisioner, new TrilhaFalsa())` — 4
argumentos. Esta task acrescenta um 5º parâmetro ao handler
(`IItemMenuRepository`), então **todos os 6 call sites existentes quebram** até serem
atualizados — não é só acrescentar teste novo.

- [ ] **Step 1: Atualizar os 6 testes existentes para o construtor de 5 argumentos**

Em `tests/Secco.Intranet.Tests/Unit/CreateSetorHandlerTests.cs`, trocar cada uma das 6
chamadas `new CreateSetorHandler(repository, Options, provisioner ou new FakeSetorAccessProvisioner(), new TrilhaFalsa())`
para acrescentar `new ItemMenuRepositorioFalso()` como 5º argumento — por exemplo, a
primeira (`Handle_WithValidCommand_PersistsAndReturnsDto`) fica:

```csharp
		var repository = new FakeRepository();
		var handler = new CreateSetorHandler(repository, Options, new FakeSetorAccessProvisioner(), new TrilhaFalsa(), new ItemMenuRepositorioFalso());
```

Repita a mesma troca (só acrescentar `, new ItemMenuRepositorioFalso()` ao final da lista
de argumentos) nas outras 5 chamadas do arquivo. Acrescentar
`using Secco.Intranet.Tests.Support;` ao topo do arquivo, se ainda não houver.

Há um **segundo** arquivo que constrói o handler:
`tests/Secco.Intranet.Tests/Unit/AuditoriaDeSetorTests.cs`, método `Criar_RegistraSetorCriar`
(`new CreateSetorHandler(new RepositorioFalso(setor: null), new IntranetOptions(), new ProvisionerFalso(), trilha)`).
Mesma troca ali — acrescentar `, new ItemMenuRepositorioFalso()` e o `using`. Antes de
seguir, rode `grep -rn "new CreateSetorHandler(" tests/ src/` e confirme que não sobrou
nenhum call site de 4 argumentos (o DI resolve sozinho; só construção manual quebra).

- [ ] **Step 2: Rodar e confirmar que falha só por causa da assinatura nova**

Run: `dotnet test tests/Secco.Intranet.Tests --filter CreateSetorHandlerTests`
Expected: FAIL (compilação — `CreateSetorHandler` ainda não aceita 5 argumentos; é o
Step 4 desta task que muda isso)

- [ ] **Step 3: Escrever os dois testes novos, no mesmo arquivo**

Acrescentar ao final da classe `CreateSetorHandlerTests` (mesmo arquivo, sem criar um
arquivo novo — os dublês já estão ali):

```csharp
	[Fact]
	public async Task Handle_ComOsDoisRecursosHabilitados_CriaRaizEOsDoisEmOrdemAlfabetica()
	{
		var repository = new FakeRepository();
		var itens = new ItemMenuRepositorioFalso();
		var handler = new CreateSetorHandler(repository, Options, new FakeSetorAccessProvisioner(), new TrilhaFalsa(), itens);

		var resultado = await handler.HandleAsync(new CreateSetorCommand("Financeiro", "financeiro"));

		resultado.IsSuccess.Should().BeTrue();
		var setorId = resultado.Value.Id;
		var raiz = itens.Itens.Single(i => i.SetorId == setorId && i.Tipo == Secco.Intranet.Domain.Menu.TipoDeItemMenu.Setor);
		var avisos = itens.Itens.Single(i => i.SetorId == setorId && i.Tipo == Secco.Intranet.Domain.Menu.TipoDeItemMenu.Avisos);
		var documentos = itens.Itens.Single(i => i.SetorId == setorId && i.Tipo == Secco.Intranet.Domain.Menu.TipoDeItemMenu.Documentos);

		avisos.ParentId.Should().Be(raiz.Id);
		documentos.ParentId.Should().Be(raiz.Id);
		avisos.Ordem.Should().BeLessThan(documentos.Ordem, "Avisos vem antes de Documentos em ordem alfabética");
	}

	[Fact]
	public async Task Handle_ComOsDoisRecursosDesabilitados_SoCriaARaiz()
	{
		var repository = new FakeRepository();
		var itens = new ItemMenuRepositorioFalso();
		var handler = new CreateSetorHandler(repository, Options, new FakeSetorAccessProvisioner(), new TrilhaFalsa(), itens);

		var resultado = await handler.HandleAsync(
			new CreateSetorCommand("TI", "ti", HabilitarDocumentos: false, HabilitarAvisos: false));

		resultado.IsSuccess.Should().BeTrue();
		var setorId = resultado.Value.Id;
		itens.Itens.Where(i => i.SetorId == setorId).Should().ContainSingle(i => i.Tipo == Secco.Intranet.Domain.Menu.TipoDeItemMenu.Setor);
	}
```

- [ ] **Step 4: Rodar e confirmar que ainda falha (falta implementar o handler)**

Run: `dotnet test tests/Secco.Intranet.Tests --filter CreateSetorHandlerTests`
Expected: FAIL (compilação — `CreateSetorCommand` não tem `HabilitarDocumentos`/`HabilitarAvisos` ainda)

- [ ] **Step 3: Alterar `CreateSetorHandler`**

Substituir o conteúdo inteiro de `src/Secco.Intranet.Application/Setores/CreateSetorHandler.cs`
por (só o `using` novo, o `record` e a assinatura do construtor mudam; o corpo de
`HandleAsync` ganha o bloco novo logo antes do `return`):

```csharp
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Setores;

/// <summary>Comando de criação de um setor.</summary>
/// <param name="Nome">Nome de exibição. Obrigatório.</param>
/// <param name="Slug">Identificador curto. Obrigatório, único por tenant.</param>
/// <param name="Fixo">Se o setor nasce como fixo do sistema. Default <c>false</c>.</param>
/// <param name="Icone">Classe do Bootstrap Icons para o menu; vazio usa o padrão.</param>
/// <param name="HabilitarDocumentos">Se nasce com o item Documentos na árvore de menu. Default <c>true</c>.</param>
/// <param name="HabilitarAvisos">Se nasce com o item Avisos na árvore de menu. Default <c>true</c>.</param>
public sealed record CreateSetorCommand(
	string? Nome, string? Slug, bool Fixo = false, string? Icone = null,
	bool HabilitarDocumentos = true, bool HabilitarAvisos = true);

/// <summary>
/// Caso de uso: valida unicidade do slug (ADR-0020), provisiona as Roles do setor no
/// SecureGate (ADR-0001), cria a raiz da árvore de menu e os itens Documentos/Avisos
/// escolhidos, e só então persiste a entidade — erros de negócio fluem por
/// <see cref="Result{T}"/>, nunca por exceção (ADR-0004).
/// </summary>
/// <param name="repository">Persistência de setores.</param>
/// <param name="options">Limites de entrada do produto.</param>
/// <param name="accessProvisioner">Provisionamento das Roles do setor no SecureGate.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="itemMenuRepository">Persistência da árvore de itens de menu.</param>
public sealed class CreateSetorHandler(
	ISetorRepository repository,
	IntranetOptions options,
	ISetorAccessProvisioner accessProvisioner,
	ITrilhaDeAuditoria trilha,
	IItemMenuRepository itemMenuRepository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de criação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<SetorDto>> HandleAsync(CreateSetorCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Nome))
		{
			return IntranetErrors.Setores.NomeRequired;
		}

		if (command.Nome.Length > options.MaxNameLength)
		{
			return IntranetErrors.Setores.NomeTooLong(options.MaxNameLength);
		}

		if (string.IsNullOrWhiteSpace(command.Slug))
		{
			return IntranetErrors.Setores.SlugRequired;
		}

		if (!Setor.IconeEhValido(command.Icone))
		{
			return IntranetErrors.Setores.IconeInvalido;
		}

		if (await repository.ExistsBySlugAsync(command.Slug, cancellationToken).ConfigureAwait(false))
		{
			return IntranetErrors.Setores.SlugAlreadyExists(command.Slug);
		}

		var provisioningResult = await accessProvisioner
			.EnsureSetorRolesAsync(command.Slug, cancellationToken)
			.ConfigureAwait(false);

		if (provisioningResult.IsFailure)
		{
			return provisioningResult.Error;
		}

		var setor = new Setor(command.Nome, command.Slug, command.Fixo, command.Icone);

		await repository.AddAsync(setor, cancellationToken).ConfigureAwait(false);

		// A raiz da árvore de menu nasce sempre, independente das escolhas do formulário —
		// só ela é obrigatória, Documentos/Avisos são opcionais (spec, Seção "Criação, seed
		// e migração"). Ordem alfabética entre os dois: "Avisos" antes de "Documentos".
		var raiz = new ItemMenu(setor.Id, null, setor.Nome, setor.Slug, TipoDeItemMenu.Setor, null, null, 0);
		await itemMenuRepository.AddAsync(raiz, cancellationToken).ConfigureAwait(false);

		var ordem = 0;

		if (command.HabilitarAvisos)
		{
			await itemMenuRepository.AddAsync(
				new ItemMenu(setor.Id, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, ordem++),
				cancellationToken).ConfigureAwait(false);
		}

		if (command.HabilitarDocumentos)
		{
			await itemMenuRepository.AddAsync(
				new ItemMenu(setor.Id, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, ordem++),
				cancellationToken).ConfigureAwait(false);
		}

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
						ativo = setor.Ativo,
					})),
				cancellationToken)
			.ConfigureAwait(false);

		return SetorDto.FromEntity(setor);
	}
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "CreateSetorHandlerTests|AuditoriaDeSetorTests"`
Expected: PASS — inclusive os testes **já existentes** de `CreateSetorHandler` (o
construtor ganhou um parâmetro novo; eles precisam ser ajustados para passar um
`ItemMenuRepositorioFalso`, não só os testes novos)

- [ ] **Step 5: Build completo — confirma que nada mais quebrou com a assinatura nova**

Run: `dotnet build`
Expected: 0 avisos, 0 erros — se algo mais construía `CreateSetorHandler` diretamente
(fora do DI), vai aparecer aqui

- [ ] **Step 6: Seeder de desenvolvimento cria a árvore também**

`src/Secco.Intranet.Infrastructure/Seeding/SetoresDesenvolvimentoSeeder.cs` grava os
setores de amostra direto no `DbContext`, sem passar por `CreateSetorHandler` — logo, sem
árvore. Sem este step, depois da Task 9 todo setor de amostra abriria em "Nenhum item
ainda" em DEV. Acrescentar ao fim do laço por tenant (fora do `if (novos.Count == 0)
continue;` — ele precisa rodar também para setores de amostra que já existiam antes desta
feature no banco de DEV de quem desenvolve), com `using Secco.Intranet.Domain.Menu;`:

```csharp
			// Setores sem árvore (os recém-inseridos e os que já existiam antes do ItemMenu)
			// ganham raiz + Documentos + Avisos — mesma regra e mesma ordem da reconciliação
			// (Documentos primeiro, como a página abria antes). Idempotente como o resto.
			var setoresComArvore = await context.ItensMenu
				.Where(item => item.Tipo == TipoDeItemMenu.Setor)
				.Select(item => item.SetorId)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			var semArvore = await context.Setores
				.Where(setor => !setoresComArvore.Contains(setor.Id))
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			foreach (var setor in semArvore)
			{
				var raiz = new ItemMenu(setor.Id, null, setor.Nome, setor.Slug, TipoDeItemMenu.Setor, null, null, 0);
				context.ItensMenu.Add(raiz);
				context.ItensMenu.Add(new ItemMenu(setor.Id, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0));
				context.ItensMenu.Add(new ItemMenu(setor.Id, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 1));
			}

			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
```

Para isso, trocar o `continue` de `if (novos.Count == 0)` por um `if (novos.Count > 0) { AddRange + SaveChanges }`
e deixar o bloco acima logo depois, sempre executado. (Um setor de amostra que o dev já
tenha customizado nunca cai aqui — ele já tem a raiz.)

Não há teste automatizado para o seeder (roda só em Development, e a suíte roda em
Testing) — a verificação é o Step 5 da Task 10, subindo a aplicação.

- [ ] **Step 7: Commit**

```bash
git add src/Secco.Intranet.Application/Setores/CreateSetorHandler.cs src/Secco.Intranet.Infrastructure/Seeding/SetoresDesenvolvimentoSeeder.cs tests/Secco.Intranet.Tests/Unit/CreateSetorHandlerTests.cs tests/Secco.Intranet.Tests/Unit/AuditoriaDeSetorTests.cs
git commit -m "feat(menu): setor nasce com a raiz da árvore e Documentos/Avisos opcionais"
```

---

## Task 8: `ResolverCaminhoDeMenuHandler` — resolução de rota

**Files:**
- Create: `src/Secco.Intranet.Application/Menu/ResolverCaminhoDeMenuHandler.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/ResolverCaminhoDeMenuHandlerTests.cs`

**Interfaces:**
- Consumes: `IItemMenuRepository` (Task 3), `ItemMenuDto` (Task 4).
- Produces: `ResultadoDaResolucao` (record: `No` (`ItemMenuDto`), `Irmaos`
  (`IReadOnlyList<ItemMenuDto>`, ativos, ordenados — para a barra de abas), `CaminhoCompleto`
  (`IReadOnlyList<string>`, os slugs até o nó, inclusive ele), `PrimeiroFilhoAtivo`
  (`ItemMenuDto?`)),
  `ResolverCaminhoDeMenuHandler.HandleAsync(Guid setorId, IReadOnlyList<string> caminho, CancellationToken) -> Task<Result<ResultadoDaResolucao>>`
  (erro `IntranetErrors.Menu.NotFound` para qualquer segmento sem match ou nó inativo no
  meio do caminho) e
  `ResolverCaminhoDeMenuHandler.CaminhoDoTipoAsync(Guid setorId, TipoDeItemMenu tipo, CancellationToken) -> Task<IReadOnlyList<string>?>`
  (nulo = recurso desligado para o setor).

- [ ] **Step 1: Escrever o teste que falha**

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ResolverCaminhoDeMenuHandlerTests
{
	private static (ItemMenuRepositorioFalso Repo, Guid SetorId, ItemMenu Raiz, ItemMenu Avisos, ItemMenu Documentos) Cenario()
	{
		var repo = new ItemMenuRepositorioFalso();
		var setorId = Guid.NewGuid();
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var avisos = new ItemMenu(setorId, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 0);
		var documentos = new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 1);
		repo.Itens.Add(raiz);
		repo.Itens.Add(avisos);
		repo.Itens.Add(documentos);

		return (repo, setorId, raiz, avisos, documentos);
	}

	[Fact]
	public async Task CaminhoVazio_ResolveARaiz()
	{
		var (repo, setorId, raiz, avisos, documentos) = Cenario();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, []);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.No.Id.Should().Be(raiz.Id);
		resultado.Value.Irmaos.Select(i => i.Slug).Should().Equal("avisos", "documentos");
	}

	[Fact]
	public async Task CaminhoDeUmSegmento_ResolveOFilho()
	{
		var (repo, setorId, _, _, documentos) = Cenario();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, ["documentos"]);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.No.Id.Should().Be(documentos.Id);
		resultado.Value.CaminhoCompleto.Should().Equal("documentos");
	}

	[Fact]
	public async Task SegmentoSemMatch_Falha()
	{
		var (repo, setorId, _, _, _) = Cenario();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, ["nao-existe"]);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.NotFound);
	}

	[Fact]
	public async Task ItemDesativado_Falha()
	{
		var (repo, setorId, _, avisos, _) = Cenario();
		repo.Itens.Single(i => i.Id == avisos.Id).Desativar();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, ["avisos"]);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Menu.NotFound);
	}

	[Fact]
	public async Task ItemDesativado_NaoAparaceNaListaDeIrmaos()
	{
		var (repo, setorId, _, avisos, documentos) = Cenario();
		repo.Itens.Single(i => i.Id == avisos.Id).Desativar();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, []);

		resultado.Value.Irmaos.Should().ContainSingle(i => i.Id == documentos.Id);
	}

	[Fact]
	public async Task CaminhoDeDoisNiveis_ResolveONeto()
	{
		var repo = new ItemMenuRepositorioFalso();
		var setorId = Guid.NewGuid();
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var relatorios = new ItemMenu(setorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		var vendas = new ItemMenu(setorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/x", null, 0);
		repo.Itens.Add(raiz);
		repo.Itens.Add(relatorios);
		repo.Itens.Add(vendas);
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, ["relatorios", "vendas"]);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.No.Id.Should().Be(vendas.Id);
		resultado.Value.CaminhoCompleto.Should().Equal("relatorios", "vendas");
	}
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ResolverCaminhoDeMenuHandlerTests`
Expected: FAIL (compilação)

- [ ] **Step 3: Implementar**

```csharp
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Resultado de resolver um caminho na árvore de um setor.</summary>
/// <param name="No">O nó resolvido.</param>
/// <param name="Irmaos">Irmãos ativos do nó (inclusive ele), ordenados — para a barra de abas.</param>
/// <param name="CaminhoCompleto">Os slugs percorridos até o nó.</param>
/// <param name="PrimeiroFilhoAtivo">
/// O primeiro filho ativo do nó, por <c>Ordem</c> — nulo se o nó não tem nenhum filho ativo
/// (é folha, ou tem só filhos desativados). Quem chama usa isto para decidir entre
/// redirecionar para o filho (nó com filhos) ou renderizar o próprio nó (folha).
/// </param>
public sealed record ResultadoDaResolucao(
	ItemMenuDto No, IReadOnlyList<ItemMenuDto> Irmaos, IReadOnlyList<string> CaminhoCompleto, ItemMenuDto? PrimeiroFilhoAtivo);

/// <summary>Desce a árvore de um setor segmento a segmento, casando pelo <c>Slug</c> de um filho ativo.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ResolverCaminhoDeMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorId">Setor dono da árvore.</param>
	/// <param name="caminho">Segmentos do caminho, na ordem; vazio resolve a raiz.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ResultadoDaResolucao>> HandleAsync(
		Guid setorId, IReadOnlyList<string> caminho, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(caminho);

		var todos = await repository.ListarPorSetorAsync(setorId, cancellationToken).ConfigureAwait(false);
		var raiz = todos.FirstOrDefault(item => item.Tipo == TipoDeItemMenu.Setor);

		if (raiz is null)
		{
			return Result.Failure<ResultadoDaResolucao>(IntranetErrors.Menu.NotFound);
		}

		var atual = raiz;

		foreach (var segmento in caminho)
		{
			var proximo = todos.FirstOrDefault(item =>
				item.ParentId == atual.Id && item.Ativo && string.Equals(item.Slug, segmento, StringComparison.Ordinal));

			if (proximo is null)
			{
				return Result.Failure<ResultadoDaResolucao>(IntranetErrors.Menu.NotFound);
			}

			atual = proximo;
		}

		var irmaos = todos
			.Where(item => item.ParentId == atual.ParentId && item.Ativo)
			.OrderBy(item => item.Ordem)
			.Select(ItemMenuDto.FromEntity)
			.ToList();

		var primeiroFilhoAtivo = todos
			.Where(item => item.ParentId == atual.Id && item.Ativo)
			.OrderBy(item => item.Ordem)
			.Select(ItemMenuDto.FromEntity)
			.FirstOrDefault();

		return new ResultadoDaResolucao(ItemMenuDto.FromEntity(atual), irmaos, caminho, primeiroFilhoAtivo);
	}

	/// <summary>
	/// Caminho (slugs a partir da raiz) do item de um tipo embutido — Documentos ou Avisos,
	/// no máximo um por setor. Nulo se o setor não tem esse item, ou se ele ou qualquer
	/// ancestral está desativado: nesse caso o recurso está desligado para o setor.
	/// </summary>
	/// <param name="setorId">Setor dono da árvore.</param>
	/// <param name="tipo">Tipo procurado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlyList<string>?> CaminhoDoTipoAsync(
		Guid setorId, TipoDeItemMenu tipo, CancellationToken cancellationToken = default)
	{
		var todos = await repository.ListarPorSetorAsync(setorId, cancellationToken).ConfigureAwait(false);
		var atual = todos.FirstOrDefault(item => item.Tipo == tipo);
		var caminho = new List<string>();

		while (atual is not null && atual.Tipo != TipoDeItemMenu.Setor)
		{
			if (!atual.Ativo)
			{
				return null;
			}

			caminho.Insert(0, atual.Slug);
			var paiId = atual.ParentId;
			atual = todos.FirstOrDefault(item => item.Id == paiId);
		}

		// Sem item do tipo, ou cadeia que não termina na raiz (dado corrompido): desligado.
		return atual is null ? null : caminho;
	}
}
```

Acrescente os testes de `CaminhoDoTipoAsync` à mesma classe de teste:

```csharp
	[Fact]
	public async Task CaminhoDoTipo_DocumentosNoNivel1_DevolveUmSegmento()
	{
		var (repo, setorId, _, _, _) = Cenario();

		var caminho = await new ResolverCaminhoDeMenuHandler(repo).CaminhoDoTipoAsync(setorId, TipoDeItemMenu.Documentos);

		caminho.Should().Equal("documentos");
	}

	[Fact]
	public async Task CaminhoDoTipo_ItemDesativado_DevolveNulo()
	{
		var (repo, setorId, _, avisos, _) = Cenario();
		repo.Itens.Single(i => i.Id == avisos.Id).Desativar();

		var caminho = await new ResolverCaminhoDeMenuHandler(repo).CaminhoDoTipoAsync(setorId, TipoDeItemMenu.Avisos);

		caminho.Should().BeNull();
	}

	[Fact]
	public async Task CaminhoDoTipo_AninhadoDebaixoDeAncestralDesativado_DevolveNulo()
	{
		var repo = new ItemMenuRepositorioFalso();
		var setorId = Guid.NewGuid();
		var raiz = new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0);
		var grupo = new ItemMenu(setorId, raiz.Id, "Recursos", "recursos", TipoDeItemMenu.Personalizado, null, null, 0);
		var documentos = new ItemMenu(setorId, grupo.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		grupo.Desativar();
		repo.Itens.AddRange([raiz, grupo, documentos]);

		var caminho = await new ResolverCaminhoDeMenuHandler(repo).CaminhoDoTipoAsync(setorId, TipoDeItemMenu.Documentos);

		caminho.Should().BeNull("o pai está desativado — o recurso ficou inalcançável, logo desligado");
	}

	[Fact]
	public async Task CaminhoDoTipo_SemOItem_DevolveNulo()
	{
		var repo = new ItemMenuRepositorioFalso();
		var setorId = Guid.NewGuid();
		repo.Itens.Add(new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0));

		var caminho = await new ResolverCaminhoDeMenuHandler(repo).CaminhoDoTipoAsync(setorId, TipoDeItemMenu.Documentos);

		caminho.Should().BeNull();
	}
```

Note: quando `atual` é a raiz (`caminho` vazio), `atual.ParentId` é `null` — o filtro
`item.ParentId == atual.ParentId` então pega exatamente os filhos diretos da raiz, que é o
resultado certo para "abas do nível 1". Para qualquer outro nó, pega os irmãos dele, mesma
lógica. `primeiroFilhoAtivo` usa `atual.Id` (não `atual.ParentId`) — é sobre os **filhos**
do nó resolvido, não sobre os irmãos dele.

- [ ] **Step 4: Acrescentar os dois testes de `PrimeiroFilhoAtivo` e rodar**

Acrescentar à classe `ResolverCaminhoDeMenuHandlerTests` (mesmo arquivo do Step 1):

```csharp
	[Fact]
	public async Task CaminhoVazio_PrimeiroFilhoAtivoEAvisos_PorSerOPrimeiroEmOrdem()
	{
		var (repo, setorId, _, avisos, _) = Cenario();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, []);

		resultado.Value.PrimeiroFilhoAtivo.Should().NotBeNull();
		resultado.Value.PrimeiroFilhoAtivo!.Id.Should().Be(avisos.Id);
	}

	[Fact]
	public async Task NoFolha_PrimeiroFilhoAtivoENulo()
	{
		var (repo, setorId, _, _, documentos) = Cenario();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, ["documentos"]);

		resultado.Value.PrimeiroFilhoAtivo.Should().BeNull();
	}

	[Fact]
	public async Task NoComTodosOsFilhosDesativados_PrimeiroFilhoAtivoENulo()
	{
		var (repo, setorId, _, avisos, documentos) = Cenario();
		repo.Itens.Single(i => i.Id == avisos.Id).Desativar();
		repo.Itens.Single(i => i.Id == documentos.Id).Desativar();
		var handler = new ResolverCaminhoDeMenuHandler(repo);

		var resultado = await handler.HandleAsync(setorId, []);

		resultado.Value.PrimeiroFilhoAtivo.Should().BeNull();
	}
```

Run: `dotnet test tests/Secco.Intranet.Tests --filter ResolverCaminhoDeMenuHandlerTests`
Expected: PASS (13 testes — 9 de resolução/`PrimeiroFilhoAtivo` e 4 de `CaminhoDoTipoAsync`)

- [ ] **Step 5: Registrar no DI e commit**

```csharp
		services.AddScoped<ResolverCaminhoDeMenuHandler>();
```

Run: `dotnet build` — Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Application tests/Secco.Intranet.Tests/Unit/ResolverCaminhoDeMenuHandlerTests.cs
git commit -m "feat(menu): resolução de caminho na árvore, segmento a segmento"
```

---

## Task 9: `SetorController` — rota recursiva, recurso desligado e leitura por permissão

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/SetorController.cs`
- Create: `src/Secco.Intranet.Web/Models/ItemMenuAbaDto.cs`
- Modify: `src/Secco.Intranet.Web/Models/Documentos/SetorDocumentosViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Models/Publicacoes/SetorAvisosViewModel.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/SetorMenuRotaTests.cs`

**Interfaces:**
- Consumes: `ResolverCaminhoDeMenuHandler` (`HandleAsync`, `CaminhoDoTipoAsync`) e
  `ResultadoDaResolucao` (Task 8), `IPermissoesDeSetor` (já existente).
- Produces: `ItemMenuAbaDto(string Nome, string? Icone, string Url, bool Ativa)`; os dois
  `ViewModel`s ganham `IReadOnlyList<ItemMenuAbaDto> Abas` como último parâmetro — a view
  (Task 10) monta a barra de abas a partir disso.

O que muda de comportamento, e o que **não** muda:

- As três rotas GET de hoje (`""`, `documentos`, `avisos`) saem; entra
  `[HttpGet("{**caminho}")] Resolver`. Como os setores nascem com `Documentos`/`Avisos` de
  slug `documentos`/`avisos` no nível 1 (Task 7) e os antigos são reconciliados com esses
  mesmos slugs (Task 6), as URLs `/setor/{slug}/documentos` e `/setor/{slug}/avisos`
  continuam valendo — `DocumentoFluxoTests`/`PublicacaoFluxoTests` não precisam mudar.
- `/setor/{slug}` "nua" deixa de mostrar sempre Documentos: redireciona para o primeiro
  filho ativo. Setor novo → Avisos (ordem alfabética, Task 7); setor antigo reconciliado →
  Documentos, como hoje (Task 6).
- As rotas **POST** continuam fixas (`documentos`, `documentos/{id}/arquivar`, `avisos`,
  `avisos/{id}/arquivar`) — só pode existir um item de cada tipo por setor. Mas agora
  respondem **404 quando o recurso está desligado** (item desativado, ou ausente): desligar
  Documentos sem bloquear o POST deixaria o recurso "escondido" e ainda gravável.
- Depois de publicar/arquivar, o redirect vai para o caminho real do item na árvore (que
  pode não ser o nível 1), e não mais para `nameof(Documentos)`/`nameof(Avisos)` — esses
  métodos deixam de existir e o `nameof` não compilaria.
- Leitura passa a checar `setor-{slug}:read` (`PodeLerAsync`), com **o mesmo bypass de
  `PodePublicarAsync`** (sem autenticação configurada, libera) — consistente com o menu,
  que no mesmo modo mostra todo setor. **Consequência para teste:** o ambiente `Testing`
  nunca tem autenticação configurada, então o ramo restritivo **não é alcançável por HTTP
  nesta suíte** — exatamente a limitação que `MenuVisibilidadeDeSetorTests` já documenta
  para o menu e para `PodePublicarAsync`. Não escreva um teste de integração "sem
  permissão → 404": ele passaria ou falharia pelo motivo errado. A função de permissão em
  si já é coberta por `PermissoesDeSetorTests`; o que fica sem prova por HTTP é só a
  chamada dela pelo controller, e isso vai registrado no próprio arquivo de teste (Step 1).

- [ ] **Step 1: Escrever os testes de rota que falham**

```csharp
using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A árvore de itens de menu substitui as duas abas fixas de /setor/{slug}. Cada teste cria o
/// próprio setor com slug sufixado por GUID — não existe setor fixo na fixture.
/// </summary>
/// <remarks>
/// Sem teste de "usuário sem setor-{slug}:read recebe 404": PodeLerAsync tem o mesmo bypass de
/// PodePublicarAsync (sem autenticação configurada, libera), e o ambiente Testing nunca liga a
/// autenticação — o ramo restritivo não é alcançável por HTTP aqui. Mesma limitação registrada
/// em MenuVisibilidadeDeSetorTests; a permissão em si está coberta em PermissoesDeSetorTests.
/// </remarks>
public class SetorMenuRotaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
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

	private static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	private async Task<string> CriarSetorAsync(bool habilitarDocumentos = true, bool habilitarAvisos = true)
	{
		var slug = $"menu-{Guid.NewGuid():N}"[..20];

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criado = await escopo.ServiceProvider
			.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand(
				$"Setor {slug}", slug, HabilitarDocumentos: habilitarDocumentos, HabilitarAvisos: habilitarAvisos));

		criado.IsSuccess.Should().BeTrue();

		return slug;
	}

	private async Task DesativarAsync(string slug, TipoDeItemMenu tipo)
	{
		await using var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var setor = await escopo.ServiceProvider.GetRequiredService<GetSetorBySlugHandler>().HandleAsync(slug);
		var arvore = await escopo.ServiceProvider.GetRequiredService<IItemMenuRepository>().ListarPorSetorAsync(setor.Value.Id);
		var item = arvore.Single(i => i.Tipo == tipo);

		(await escopo.ServiceProvider.GetRequiredService<AtivarDesativarItemMenuHandler>()
			.HandleAsync(item.Id, ativar: false)).IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task CaminhoVazio_RedirecionaParaOPrimeiroFilhoAtivo()
	{
		var slug = await CriarSetorAsync();

		var resposta = await CriarCliente().GetAsync($"/setor/{slug}");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/setor/{slug}/avisos",
			"Avisos vem antes de Documentos em ordem alfabética (Task 7)");
	}

	[Fact]
	public async Task Documentos_ContinuaNaMesmaUrl_ComAbaDinamica()
	{
		var slug = await CriarSetorAsync();

		var html = await CriarCliente().GetStringAsync($"/setor/{slug}/documentos");

		html.Should().Contain($"href=\"/setor/{slug}/avisos\"", "a aba de Avisos vem da árvore, não mais de um link fixo");
	}

	[Fact]
	public async Task SetorSemNenhumItemAtivo_MostraOEstadoVazio()
	{
		var slug = await CriarSetorAsync(habilitarDocumentos: false, habilitarAvisos: false);

		var resposta = await CriarCliente().GetAsync($"/setor/{slug}");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("Nenhum item ainda");
	}

	[Fact]
	public async Task ItemDesativado_Da404_NoCaminhoQueAntesFuncionava()
	{
		var slug = await CriarSetorAsync();
		await DesativarAsync(slug, TipoDeItemMenu.Documentos);

		var resposta = await CriarCliente().GetAsync($"/setor/{slug}/documentos");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task ItemDesativado_SaiDaBarraDeAbasDosIrmaos()
	{
		var slug = await CriarSetorAsync();
		await DesativarAsync(slug, TipoDeItemMenu.Documentos);

		var html = await CriarCliente().GetStringAsync($"/setor/{slug}/avisos");

		html.Should().NotContain($"href=\"/setor/{slug}/documentos\"");
	}

	[Fact]
	public async Task RecursoDesligado_RecusaPost_MesmoComTokenValido()
	{
		var slug = await CriarSetorAsync();
		await DesativarAsync(slug, TipoDeItemMenu.Avisos);
		var client = CriarCliente();
		// O token antifalsificação não é por ação: o da página de Documentos (ainda ligada) vale.
		var token = await TokenAsync(client, $"/setor/{slug}/documentos");

		var resposta = await client.PostAsync($"/setor/{slug}/avisos", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Form.Titulo", "Não deveria entrar"),
			new KeyValuePair<string, string>("Form.Corpo", "Avisos está desligado neste setor."),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound,
			"desligar o recurso precisa fechar a escrita também, não só esconder a aba");
	}

	[Fact]
	public async Task SegmentoSemMatchNenhum_Da404()
	{
		var slug = await CriarSetorAsync();

		var resposta = await CriarCliente().GetAsync($"/setor/{slug}/caminho-que-nao-existe");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
}
```

Confira o nome dos campos do formulário de aviso em `PublicacaoFluxoTests.PublicarAsync`
antes de rodar (`Form.Titulo`, `Form.Corpo`, `Form.PublicadoEm`…) — o teste acima só
precisa de um POST que **passaria** se o recurso estivesse ligado; se o binder exigir mais
campos para chegar ao ponto da checagem, copie exatamente o conjunto de lá. A checagem de
recurso desligado (Step 3) vem **antes** de `ModelState.IsValid`, então campos faltando não
mascaram o 404.

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter SetorMenuRotaTests`
Expected: FAIL — `CaminhoVazio_…` recebe `/setor/{slug}` (hoje a raiz é a própria página de
Documentos, não redireciona), `SetorSemNenhumItemAtivo_…` mostra Documentos em vez do
estado vazio, os de item desativado dão 200, e `RecursoDesligado_…` aceita o POST.

- [ ] **Step 3: `ItemMenuAbaDto` e os `ViewModel`s**

Create `src/Secco.Intranet.Web/Models/ItemMenuAbaDto.cs`:

```csharp
namespace Secco.Intranet.Web.Models;

/// <summary>Uma aba da barra de navegação dentro da página de um setor.</summary>
/// <param name="Nome">Rótulo.</param>
/// <param name="Icone">Classe do Bootstrap Icons; nulo = sem ícone.</param>
/// <param name="Url">Link da aba.</param>
/// <param name="Ativa">Se é a aba da página atual.</param>
public sealed record ItemMenuAbaDto(string Nome, string? Icone, string Url, bool Ativa);
```

Em `SetorDocumentosViewModel.cs`, o record passa a ser (só o último parâmetro é novo; o
`DocumentoFormViewModel` do mesmo arquivo não muda):

```csharp
public sealed record SetorDocumentosViewModel(
	SetorDto Setor,
	IReadOnlyList<DocumentoDto> Documentos,
	bool PodePublicar,
	DocumentoFormViewModel Form,
	long TamanhoMaximoBytes,
	IReadOnlyList<ItemMenuAbaDto> Abas);
```

Em `SetorAvisosViewModel.cs`:

```csharp
public sealed record SetorAvisosViewModel(
	SetorDto Setor,
	IReadOnlyList<PublicacaoDto> Publicacoes,
	bool PodePublicar,
	PublicacaoFormViewModel Form,
	DateTimeOffset Agora,
	IReadOnlyList<ItemMenuAbaDto> Abas);
```

Os dois arquivos precisam de `using Secco.Intranet.Web.Models;` (namespace de
`ItemMenuAbaDto`) — eles vivem em `Secco.Intranet.Web.Models.Documentos`/`.Publicacoes`.

- [ ] **Step 4: `SetorController` — GET único e helpers**

Construtor: acrescentar `ResolverCaminhoDeMenuHandler resolverCaminho` ao fim da lista
(documentar no `<param>` como os outros). `using` novos: `Secco.Intranet.Application.Menu`,
`Secco.Intranet.Domain.Menu`, `Secco.Intranet.Web.Models`.

Apagar os métodos `Documentos(string slug, …)` (com `[HttpGet("")]`/`[HttpGet("documentos")]`),
`Avisos(string slug, …)` (`[HttpGet("avisos")]`), `MontarAsync` e `MontarAvisosAsync`.
Acrescentar:

```csharp
	/// <summary>
	/// Qualquer nó da árvore do setor: nó com filhos redireciona para o primeiro filho ativo;
	/// folha renderiza pelo tipo.
	/// </summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="caminho">Slugs da árvore separados por barra; vazio é a raiz.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{**caminho}")]
	public async Task<IActionResult> Resolver(string slug, string? caminho, CancellationToken cancellationToken = default)
	{
		var setor = await getSetorHandler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

		if (setor.IsFailure || !setor.Value.Ativo || !await PodeLerAsync(slug, cancellationToken).ConfigureAwait(false))
		{
			// Sem permissão responde igual a inexistente — não revela que o setor existe.
			return NotFound();
		}

		IReadOnlyList<string> segmentos = string.IsNullOrWhiteSpace(caminho)
			? []
			: caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);

		var resolvido = await resolverCaminho.HandleAsync(setor.Value.Id, segmentos, cancellationToken).ConfigureAwait(false);

		if (resolvido.IsFailure)
		{
			return NotFound();
		}

		if (resolvido.Value.PrimeiroFilhoAtivo is { } primeiroFilho)
		{
			return RedirectToAction(nameof(Resolver), new { slug, caminho = string.Join('/', [.. segmentos, primeiroFilho.Slug]) });
		}

		var no = resolvido.Value.No;

		return no.Tipo switch
		{
			TipoDeItemMenu.Documentos => View(
				"Documentos",
				await MontarDocumentosAsync(setor.Value, resolvido.Value, new DocumentoFormViewModel(), cancellationToken).ConfigureAwait(false)),
			TipoDeItemMenu.Avisos => View(
				"Avisos",
				await MontarAvisosAsync(setor.Value, resolvido.Value, new PublicacaoFormViewModel(), cancellationToken).ConfigureAwait(false)),
			// Rota já validada na criação (Task 4): caminho local ou http(s) absoluto.
			TipoDeItemMenu.Personalizado when !string.IsNullOrWhiteSpace(no.Rota) => Redirect(no.Rota),
			TipoDeItemMenu.Personalizado => View("SemConteudo", setor.Value),
			// Tipo Setor sem nenhum filho ativo: setor criado sem recursos, ou todos desligados.
			_ => View("SemItens", setor.Value),
		};
	}

	/// <summary>
	/// Setor e posição na árvore do item de um tipo embutido. Nulo quando o setor não existe
	/// ou está inativo, ou quando o recurso está desligado (item ausente, ou ele/um ancestral
	/// desativado) — quem chama responde 404 nos três casos.
	/// </summary>
	private async Task<(SetorDto Setor, ResultadoDaResolucao Resolucao)?> ResolverTipoAsync(
		string slug, TipoDeItemMenu tipo, CancellationToken cancellationToken)
	{
		var setor = await getSetorHandler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

		if (setor.IsFailure || !setor.Value.Ativo)
		{
			return null;
		}

		var caminho = await resolverCaminho.CaminhoDoTipoAsync(setor.Value.Id, tipo, cancellationToken).ConfigureAwait(false);

		if (caminho is null)
		{
			return null;
		}

		var resolvido = await resolverCaminho.HandleAsync(setor.Value.Id, caminho, cancellationToken).ConfigureAwait(false);

		return resolvido.IsFailure ? null : (setor.Value, resolvido.Value);
	}

	private RedirectToActionResult VoltarPara(string slug, ResultadoDaResolucao resolucao) =>
		RedirectToAction(nameof(Resolver), new { slug, caminho = string.Join('/', resolucao.CaminhoCompleto) });

	/// <summary>Abas = irmãos ativos do nó; a URL de cada um é o caminho do pai + o slug dele.</summary>
	private IReadOnlyList<ItemMenuAbaDto> MontarAbas(string slug, ResultadoDaResolucao resolucao)
	{
		var caminhoDoPai = resolucao.CaminhoCompleto.Take(resolucao.CaminhoCompleto.Count - 1).ToList();

		return
		[
			.. resolucao.Irmaos.Select(irmao => new ItemMenuAbaDto(
				irmao.Nome,
				irmao.Icone,
				Url.Action(nameof(Resolver), new { slug, caminho = string.Join('/', [.. caminhoDoPai, irmao.Slug]) })!,
				irmao.Id == resolucao.No.Id)),
		];
	}

	private async Task<SetorDocumentosViewModel> MontarDocumentosAsync(
		SetorDto setor, ResultadoDaResolucao resolucao, DocumentoFormViewModel form, CancellationToken cancellationToken)
	{
		var documentos = await listarHandler
			.HandleAsync(new ListarDocumentosQuery(setor.Slug), cancellationToken)
			.ConfigureAwait(false);

		return new SetorDocumentosViewModel(
			setor,
			documentos.IsSuccess ? documentos.Value : [],
			await PodePublicarAsync(setor.Slug, cancellationToken).ConfigureAwait(false),
			form,
			documentoOptions.TamanhoMaximoBytes,
			MontarAbas(setor.Slug, resolucao));
	}

	private async Task<SetorAvisosViewModel> MontarAvisosAsync(
		SetorDto setor, ResultadoDaResolucao resolucao, PublicacaoFormViewModel form, CancellationToken cancellationToken)
	{
		var publicacoes = await listarPublicacoesDoSetorHandler
			.HandleAsync(setor.Slug, cancellationToken)
			.ConfigureAwait(false);

		return new SetorAvisosViewModel(
			setor,
			publicacoes.IsSuccess ? publicacoes.Value : [],
			await PodePublicarAsync(setor.Slug, cancellationToken).ConfigureAwait(false),
			form,
			DateTimeOffset.UtcNow,
			MontarAbas(setor.Slug, resolucao));
	}

	/// <summary>
	/// Ler exige a permissão de leitura do setor (ADR-0021). Mesmo bypass de
	/// <see cref="PodePublicarAsync"/>: sem autenticação configurada (DEV aberto/Testing) não
	/// há permissão a resolver — consistente com o menu, que nesse modo mostra todo setor.
	/// </summary>
	private async Task<bool> PodeLerAsync(string slug, CancellationToken cancellationToken)
	{
		if (!IntranetAuthenticationExtensions.IsConfigured(configuration))
		{
			return true;
		}

		var slugsComLeitura = await permissoesDeSetor
			.SlugsComPermissaoAsync(User, "read", [slug], cancellationToken)
			.ConfigureAwait(false);

		return slugsComLeitura.Contains(slug);
	}
```

- [ ] **Step 5: `SetorController` — as quatro ações POST**

`Publicar` passa a ser (a parte de gravação no meio não muda):

```csharp
	public async Task<IActionResult> Publicar(
		string slug,
		DocumentoFormViewModel form,
		IFormFile? arquivo,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (!await PodePublicarAsync(slug, cancellationToken).ConfigureAwait(false))
		{
			// Mesma resposta de setor inexistente: quem não administra o setor não deve
			// conseguir distinguir "não posso" de "não existe".
			return NotFound();
		}

		// Recurso desligado também é 404 — antes de validar o formulário, para campos
		// faltando não mascararem a recusa.
		var alvo = await ResolverTipoAsync(slug, TipoDeItemMenu.Documentos, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var (setor, resolucao) = alvo.Value;

		if (arquivo is null || arquivo.Length == 0)
		{
			ModelState.AddModelError(string.Empty, "Escolha um arquivo para publicar.");
		}

		if (!ModelState.IsValid)
		{
			return View("Documentos", await MontarDocumentosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
		}

		await using var conteudo = arquivo!.OpenReadStream();

		var resultado = await publicarDocumentoHandler.HandleAsync(
			new PublicarDocumentoCommand(
				slug,
				form.Titulo,
				form.Descricao,
				arquivo.FileName,
				arquivo.Length,
				conteudo,
				form.Visibilidade,
				User.Identity?.Name ?? "desconhecido"),
			cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View("Documentos", await MontarDocumentosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Documento \"{resultado.Value.Titulo}\" publicado.";

		return VoltarPara(slug, resolucao);
	}
```

`Arquivar`: primeira coisa do método, o mesmo bloco `alvo`/`NotFound`/desconstrução com
`TipoDeItemMenu.Documentos`; o `return RedirectToAction(nameof(Documentos), new { slug });`
do final vira `return VoltarPara(slug, resolucao);`.

`SalvarAviso`: depois do `PodePublicarAsync`, o mesmo bloco com `TipoDeItemMenu.Avisos`.
No ramo `!ModelState.IsValid`, trocar o `MontarAvisosAsync(slug, form, …)` antigo por
`return View("Avisos", await MontarAvisosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));`.
As duas chamadas a `ComErroAsync(slug, form, mensagem, …)` passam a
`ComErroAsync(setor, resolucao, form, mensagem, …)`, e o redirect final
`RedirectToAction(nameof(Avisos), new { slug })` vira `VoltarPara(slug, resolucao)`.
`ComErroAsync` fica:

```csharp
	private async Task<IActionResult> ComErroAsync(
		SetorDto setor,
		ResultadoDaResolucao resolucao,
		PublicacaoFormViewModel form,
		string mensagem,
		CancellationToken cancellationToken)
	{
		ModelState.AddModelError(string.Empty, mensagem);

		return View("Avisos", await MontarAvisosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
	}
```

`ArquivarAviso`: bloco `alvo` com `TipoDeItemMenu.Avisos` no começo; redirect final vira
`VoltarPara(slug, resolucao)`.

Ao terminar, `grep -n "nameof(Documentos)\|nameof(Avisos)\|MontarAsync(" src/Secco.Intranet.Web/Controllers/SetorController.cs`
não pode devolver nada.

- [ ] **Step 6: Build e testes parciais**

Run: `dotnet build`
Expected: 0 avisos, 0 erros — `asp-action="Documentos"`/`"Avisos"` nas views apontam para
actions que não existem mais, mas isso não é erro de compilação: o tag helper gera `href`
vazio em runtime. É exatamente o que a Task 10 troca.

Run: `dotnet test tests/Secco.Intranet.Tests --filter "SetorMenuRotaTests|DocumentoFluxoTests|PublicacaoFluxoTests"`
Expected: PASS, **exceto** `Documentos_ContinuaNaMesmaUrl_ComAbaDinamica` (a aba ainda é
o link fixo, com `href` vazio). **Não commite entre a Task 9 e a Task 10**: as duas são uma
mudança só; o commit fica no fim da Task 10.

---

## Task 10: Views — barra de abas dinâmica e telas de estado vazio/personalizado

**Files:**
- Create: `src/Secco.Intranet.Web/Views/Setor/_AbasDoSetor.cshtml`
- Modify: `src/Secco.Intranet.Web/Views/Setor/Documentos.cshtml`
- Modify: `src/Secco.Intranet.Web/Views/Setor/Avisos.cshtml`
- Create: `src/Secco.Intranet.Web/Views/Setor/SemConteudo.cshtml`
- Create: `src/Secco.Intranet.Web/Views/Setor/SemItens.cshtml`

**Interfaces:**
- Consumes: `ItemMenuAbaDto` (Task 9), `SetorDto` (já existente).

- [ ] **Step 1: Partial da barra de abas**

```cshtml
@using Secco.Intranet.Web.Models
@model IReadOnlyList<ItemMenuAbaDto>

<nav class="sc-tabs" aria-label="Itens do setor">
    @foreach (var aba in Model)
    {
        <a class="sc-tabs__item @(aba.Ativa ? "is-active" : null)" href="@aba.Url" aria-current="@(aba.Ativa ? "page" : null)">
            @if (!string.IsNullOrWhiteSpace(aba.Icone))
            {
                <i class="bi @aba.Icone" aria-hidden="true"></i>
            }
            @aba.Nome
        </a>
    }
</nav>
```

- [ ] **Step 2: Trocar o `<nav class="sc-tabs">` hardcoded em `Documentos.cshtml`**

Trocar o bloco:

```cshtml
<nav class="sc-tabs" aria-label="Recursos do setor">
    <a class="sc-tabs__item is-active" asp-action="Documentos" asp-route-slug="@Model.Setor.Slug" aria-current="page">Documentos</a>
    <a class="sc-tabs__item" asp-action="Avisos" asp-route-slug="@Model.Setor.Slug">Avisos</a>
</nav>
```

por:

```cshtml
<partial name="_AbasDoSetor" model="Model.Abas" />
```

Mesma troca em `Avisos.cshtml` (confirme o bloco equivalente lá antes de editar — o texto
exato pode variar um pouco).

- [ ] **Step 3: Views novas**

`SemConteudo.cshtml` (item `Personalizado` sem `Rota`). Mesmo formato das views
existentes (`Documentos.cshtml`): os modelos de partial montados no bloco `@{ }` e
passados por nome — nada de expressão C# inline em atributo com aspas simples:

```cshtml
@using Secco.Intranet.Application.Setores
@model SetorDto
@{
    ViewData["Title"] = Model.Nome;

    var cabecalho = new PageHeaderModel(Model.Nome, "Este item ainda não tem conteúdo.", Model.Slug);
    var vazio = new EmptyStateModel(
        "bi-tools",
        "Sem conteúdo ainda",
        "Um intranet-admin pode configurar uma rota para este item, ou ele aguarda desenvolvimento próprio.");
}

<partial name="_PageHeader" model="cabecalho" />
<partial name="_EmptyState" model="vazio" />
```

`SemItens.cshtml` (setor sem nenhum item ativo):

```cshtml
@using Secco.Intranet.Application.Setores
@model SetorDto
@{
    ViewData["Title"] = Model.Nome;

    var cabecalho = new PageHeaderModel(Model.Nome, "Este setor ainda não tem nenhum item de menu.", Model.Slug);
    var vazio = new EmptyStateModel(
        "bi-folder-x",
        "Nenhum item ainda",
        "Um intranet-admin pode adicionar itens na administração de Setores.");
}

<partial name="_PageHeader" model="cabecalho" />
<partial name="_EmptyState" model="vazio" />
```

- [ ] **Step 4: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASS — inclusive `Documentos_ContinuaNaMesmaUrl_ComAbaDinamica`, que ficou
pendente na Task 9.

- [ ] **Step 5: Rodar a aplicação e conferir visualmente**

Run: `dotnet run --project src/Secco.Intranet.Web` (ambiente Development; o seed cria os
setores de amostra **com** a árvore — Task 7, Step 6). Abra `/setor/financeiro`: redireciona
para Documentos (setor de amostra, mesma regra da reconciliação), a barra de abas vem da
árvore, e Avisos/Documentos alternam. Confira nos **dois** temas (`Vertical` e
`Horizontal`) — a barra de abas é conteúdo de página, mas o CSS de `sc-tabs` vive no tema.

- [ ] **Step 6: Build completo e um commit só para Tasks 9 e 10**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Web tests/Secco.Intranet.Tests/Integration/SetorMenuRotaTests.cs
git commit -m "feat(menu): página do setor resolve a árvore; recurso desligado fecha leitura e escrita"
```

---

## Task 11: `SetoresController.Menu` — administração da árvore

**Files:**
- Create: `src/Secco.Intranet.Web/Models/SetorMenuViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/SetoresController.cs`
- Create: `src/Secco.Intranet.Web/Views/Setores/Menu.cshtml`
- Modify: `src/Secco.Intranet.Web/Views/Setores/Details.cshtml` (link "Itens de menu")
- Modify: `src/Secco.Intranet.Web/Models/SetorFormViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Views/Setores/Create.cshtml`
- Modify: `tests/Secco.Intranet.Tests/Integration/SetoresAutorizacaoTests.cs`

**Interfaces:**
- Consumes: `ObterArvoreDeMenuHandler`, `CriarItemMenuHandler`,
  `AtivarDesativarItemMenuHandler`, `ExcluirItemMenuHandler`, `MoverItemMenuHandler`
  (Tasks 4–6, já registrados no DI).

- [ ] **Step 1: `SetorFormViewModel` ganha os dois checkboxes**

```csharp
	/// <summary>Se o setor nasce com o item Documentos na árvore de menu.</summary>
	public bool HabilitarDocumentos { get; set; } = true;

	/// <summary>Se o setor nasce com o item Avisos na árvore de menu.</summary>
	public bool HabilitarAvisos { get; set; } = true;
```

Em `SetoresController.Create(SetorFormViewModel form, ...)`, passar os dois campos ao
comando:

```csharp
		var result = await createHandler.HandleAsync(
			new CreateSetorCommand(form.Nome, form.Slug, form.Fixo, form.Icone, form.HabilitarDocumentos, form.HabilitarAvisos),
			cancellationToken);
```

Em `Views/Setores/Create.cshtml`, acrescentar os dois checkboxes ao formulário existente
(confira o markup atual antes de editar — mesmo padrão `form-check` já usado em
`Views/Acesso/Perfil.cshtml`):

```cshtml
<div class="form-check">
    <input class="form-check-input" type="checkbox" asp-for="HabilitarDocumentos" />
    <label class="form-check-label" asp-for="HabilitarDocumentos">Habilitar Documentos</label>
</div>
<div class="form-check">
    <input class="form-check-input" type="checkbox" asp-for="HabilitarAvisos" />
    <label class="form-check-label" asp-for="HabilitarAvisos">Habilitar Avisos</label>
</div>
```

- [ ] **Step 2: `SetorMenuViewModel`**

```csharp
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;

namespace Secco.Intranet.Web.Models;

/// <summary>Modelo da tela de administração da árvore de itens de um setor.</summary>
/// <param name="Setor">O setor dono da árvore.</param>
/// <param name="Raiz">A árvore inteira, aninhada.</param>
public sealed record SetorMenuViewModel(SetorDto Setor, NoDaArvoreDto Raiz);
```

- [ ] **Step 3: Ações em `SetoresController`**

Acrescentar `using Secco.Intranet.Application.Menu;` ao topo de
`src/Secco.Intranet.Web/Controllers/SetoresController.cs` (os outros `using` já existentes
continuam) — é o namespace de todos os cinco handlers novos injetados no construtor.

```csharp
	/// <summary>Árvore de itens de menu de um setor.</summary>
	/// <param name="id">Identificador do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Menu(Guid id, CancellationToken cancellationToken = default)
	{
		var setor = await getByIdHandler.HandleAsync(id, cancellationToken);

		if (setor.IsFailure)
		{
			return NotFound();
		}

		var arvore = await obterArvore.HandleAsync(setor.Value.Id, cancellationToken);

		return arvore.IsFailure ? NotFound() : View(new SetorMenuViewModel(setor.Value, arvore.Value));
	}

	/// <summary>Cria um item na árvore.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> CriarItemDeMenu(
		Guid setorId, Guid parentId, string? nome, string? slug,
		Secco.Intranet.Domain.Menu.TipoDeItemMenu tipo, string? rota, string? icone,
		CancellationToken cancellationToken = default)
	{
		var resultado = await criarItem.HandleAsync(
			new CriarItemMenuCommand(setorId, parentId, nome, slug, tipo, rota, icone), cancellationToken);

		if (resultado.IsFailure)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}
		else
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Item \"{nome}\" criado.";
		}

		return RedirectToAction(nameof(Menu), new { id = setorId });
	}

	/// <summary>(Des)ativa um item.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AlternarItemDeMenu(Guid setorId, Guid itemId, bool ativar, CancellationToken cancellationToken = default)
	{
		var resultado = await alternarItem.HandleAsync(itemId, ativar, cancellationToken);

		TempData[resultado.IsSuccess ? FeedbackViewComponent.ChaveDaMensagem : FeedbackViewComponent.ChaveDaMensagemDeErro] =
			resultado.IsSuccess ? (ativar ? "Item ativado." : "Item desativado.") : resultado.Error.Description;

		return RedirectToAction(nameof(Menu), new { id = setorId });
	}

	/// <summary>Exclui um item Personalizado.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ExcluirItemDeMenu(Guid setorId, Guid itemId, CancellationToken cancellationToken = default)
	{
		var resultado = await excluirItem.HandleAsync(itemId, cancellationToken);

		TempData[resultado.IsSuccess ? FeedbackViewComponent.ChaveDaMensagem : FeedbackViewComponent.ChaveDaMensagemDeErro] =
			resultado.IsSuccess ? "Item excluído." : resultado.Error.Description;

		return RedirectToAction(nameof(Menu), new { id = setorId });
	}

	/// <summary>Move um item para cima ou para baixo entre os irmãos.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> MoverItemDeMenu(Guid setorId, Guid itemId, bool paraCima, CancellationToken cancellationToken = default)
	{
		await moverItem.HandleAsync(itemId, paraCima, cancellationToken);

		return RedirectToAction(nameof(Menu), new { id = setorId });
	}
```

O construtor de `SetoresController` ganha `ObterArvoreDeMenuHandler obterArvore`,
`CriarItemMenuHandler criarItem`, `AtivarDesativarItemMenuHandler alternarItem`,
`ExcluirItemMenuHandler excluirItem`, `MoverItemMenuHandler moverItem` — acrescentar aos
parâmetros existentes, sem remover nenhum.

- [ ] **Step 4: View `Menu.cshtml` e o link para ela**

A árvore é achatada no bloco `@{ }` numa lista `(nó, profundidade)` e renderizada com dois
`foreach` simples — sem função local com markup dentro de `@{ }`, e sem expressão C# inline
em atributo com aspas simples (o `model='@(...)'` que já deu problema neste projeto). O
dropdown de Tipo **só oferece Documentos/Avisos se o setor ainda não tem** (exigência da
spec, Seção Administração); o handler continua recusando no back-end (`TipoJaExiste`) para
POST forjado.

```cshtml
@using Secco.Intranet.Application.Menu
@using Secco.Intranet.Domain.Menu
@model SetorMenuViewModel
@{
    ViewData["Title"] = $"Menu de {Model.Setor.Nome}";

    var cabecalho = new PageHeaderModel(
        $"Menu de {Model.Setor.Nome}",
        "Itens da página deste setor. Documentos e Avisos só desativam; itens personalizados também se excluem.",
        Model.Setor.Slug,
        new[]
        {
            new PageActionModel("Voltar para o setor", Url.Action("Details", new { id = Model.Setor.Id })!, "bi-arrow-left"),
        });

    static IEnumerable<(NoDaArvoreDto No, int Profundidade)> Achatar(NoDaArvoreDto no, int profundidade) =>
        new[] { (no, profundidade) }.Concat(no.Filhos.SelectMany(filho => Achatar(filho, profundidade + 1)));

    var linhas = Achatar(Model.Raiz, 0).ToList();
    var tiposExistentes = linhas.Select(linha => linha.No.Item.Tipo).ToHashSet();
    var desativado = new BadgeModel("Desativado", BadgeVariante.Neutro);
}

<partial name="_PageHeader" model="cabecalho" />

<ul class="sc-list mb-3">
    @foreach (var (no, profundidade) in linhas)
    {
        <li class="sc-list__item" style="padding-left: @(profundidade * 1.5)rem">
            <div class="sc-list__text">
                <p class="sc-list__title">
                    @if (!string.IsNullOrWhiteSpace(no.Item.Icone))
                    {
                        <i class="bi @no.Item.Icone" aria-hidden="true"></i>
                    }
                    @no.Item.Nome
                    <span class="sc-meta">@no.Item.Tipo · @no.Item.Slug</span>
                    @if (!no.Item.Ativo)
                    {
                        <partial name="_Badge" model="desativado" />
                    }
                </p>
                @if (no.Item.Tipo != TipoDeItemMenu.Setor)
                {
                    <div class="d-flex gap-2">
                        <form method="post" asp-action="AlternarItemDeMenu" class="d-inline">
                            <input type="hidden" name="setorId" value="@Model.Setor.Id" />
                            <input type="hidden" name="itemId" value="@no.Item.Id" />
                            <input type="hidden" name="ativar" value="@(no.Item.Ativo ? "false" : "true")" />
                            <button class="btn btn-sm btn-outline-secondary" type="submit">@(no.Item.Ativo ? "Desativar" : "Ativar")</button>
                        </form>
                        <form method="post" asp-action="MoverItemDeMenu" class="d-inline">
                            <input type="hidden" name="setorId" value="@Model.Setor.Id" />
                            <input type="hidden" name="itemId" value="@no.Item.Id" />
                            <input type="hidden" name="paraCima" value="true" />
                            <button class="btn btn-sm btn-outline-secondary" type="submit" aria-label="Mover para cima">&uarr;</button>
                        </form>
                        <form method="post" asp-action="MoverItemDeMenu" class="d-inline">
                            <input type="hidden" name="setorId" value="@Model.Setor.Id" />
                            <input type="hidden" name="itemId" value="@no.Item.Id" />
                            <input type="hidden" name="paraCima" value="false" />
                            <button class="btn btn-sm btn-outline-secondary" type="submit" aria-label="Mover para baixo">&darr;</button>
                        </form>
                        @if (no.Item.Tipo == TipoDeItemMenu.Personalizado)
                        {
                            <form method="post" asp-action="ExcluirItemDeMenu" class="d-inline">
                                <input type="hidden" name="setorId" value="@Model.Setor.Id" />
                                <input type="hidden" name="itemId" value="@no.Item.Id" />
                                <button class="btn btn-sm btn-outline-danger" type="submit">Excluir</button>
                            </form>
                        }
                    </div>
                }
            </div>
        </li>
    }
</ul>

<div class="sc-panel">
    <h2 class="h6 mb-3">Novo item</h2>
    <form method="post" asp-action="CriarItemDeMenu" class="row g-2">
        <input type="hidden" name="setorId" value="@Model.Setor.Id" />
        <div class="col-sm-3">
            <label class="form-label" for="parentId">Pai</label>
            <select class="form-select" id="parentId" name="parentId">
                @foreach (var (no, profundidade) in linhas)
                {
                    <option value="@no.Item.Id">@(new string('—', profundidade)) @no.Item.Nome</option>
                }
            </select>
        </div>
        <div class="col-sm-3">
            <label class="form-label" for="nome">Nome</label>
            <input class="form-control" id="nome" name="nome" maxlength="256" required />
        </div>
        <div class="col-sm-2">
            <label class="form-label" for="slug">Identificador</label>
            <input class="form-control" id="slug" name="slug" maxlength="128" pattern="[a-z0-9]+(-[a-z0-9]+)*" placeholder="relatorios" required />
        </div>
        <div class="col-sm-2">
            <label class="form-label" for="tipo">Tipo</label>
            <select class="form-select" id="tipo" name="tipo">
                <option value="@TipoDeItemMenu.Personalizado">Personalizado</option>
                @if (!tiposExistentes.Contains(TipoDeItemMenu.Documentos))
                {
                    <option value="@TipoDeItemMenu.Documentos">Documentos</option>
                }
                @if (!tiposExistentes.Contains(TipoDeItemMenu.Avisos))
                {
                    <option value="@TipoDeItemMenu.Avisos">Avisos</option>
                }
            </select>
        </div>
        <div class="col-sm-2">
            <label class="form-label" for="icone">Ícone</label>
            <input class="form-control" id="icone" name="icone" maxlength="64" placeholder="bi-cash-coin" />
        </div>
        <div class="col-sm-4">
            <label class="form-label" for="rota">Rota (só Personalizado)</label>
            <input class="form-control" id="rota" name="rota" maxlength="512" placeholder="/relatorios ou https://…" />
        </div>
        <div class="col-sm-2 d-flex align-items-end">
            <button class="btn btn-primary w-100" type="submit">Criar</button>
        </div>
    </form>
</div>
```

O enum vai no `value` pelo **nome** (`Personalizado`), não pelo número — o model binder de
enum aceita os dois, e o nome não quebra se a ordem do enum mudar um dia.

Link de entrada: em `Views/Setores/Details.cshtml`, acrescentar uma ação ao cabeçalho (entre
"Editar" e "Voltar para setores"):

```cshtml
            new PageActionModel("Itens de menu", Url.Action("Menu", new { id = Model.Id })!, "bi-list-nested"),
```

- [ ] **Step 5: Atualizar `SetoresAutorizacaoTests`**

Em `tests/Secco.Intranet.Tests/Integration/SetoresAutorizacaoTests.cs`, acrescentar as
rotas novas às listas existentes:

```csharp
	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Get_SemIntranetAdmin_Bloqueado(string[] roles)
	{
		var client = CriarCliente(roles);
		var id = Guid.NewGuid();

		foreach (var url in new[] { "/Setores", "/Setores/Create", $"/Setores/Edit/{id}", $"/Setores/Details/{id}", $"/Setores/Menu/{id}" })
		{
			var resposta = await client.GetAsync(url);

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {url} exige intranet-admin");
		}
	}
```

E em `Post_SemIntranetAdmin_BloqueadoMesmoSemToken`, acrescentar as quatro rotas POST
novas (`CriarItemDeMenu`, `AlternarItemDeMenu`, `ExcluirItemDeMenu`, `MoverItemDeMenu`) ao
mesmo padrão do teste existente — cada uma checando 403, não 400.

- [ ] **Step 6: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 7: Build completo, subir e conferir visualmente**

Run: `dotnet build` — Expected: 0 avisos, 0 erros

Run: `dotnet run --project src/Secco.Intranet.Web`, entre como `intranet-admin` (modo
aberto de DEV), abra `/setores/{id}/menu` de um setor existente, crie um item
Personalizado, ative/desative, mova, exclua. Confirme que `/setor/{slug}` reflete a
mudança.

- [ ] **Step 8: Commit**

```bash
git add src/Secco.Intranet.Web tests/Secco.Intranet.Tests/Integration/SetoresAutorizacaoTests.cs
git commit -m "feat(menu): tela de administração da árvore de itens em SetoresController"
```

---

## Task 12: Ação "Reconciliar itens de menu" e revisão final

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/SetoresController.cs`
- Modify: `src/Secco.Intranet.Web/Views/Setores/Index.cshtml`
- Test: `tests/Secco.Intranet.Tests/Integration/ReconciliarItensDeMenuTests.cs`

**Interfaces:**
- Consumes: `ReconciliarItensDeMenuHandler` (Task 6).

- [ ] **Step 1: Escrever o teste de integração que falha**

```csharp
using System.Net;
using AwesomeAssertions;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class ReconciliarItensDeMenuTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task ComIntranetAdmin_RodaERedireciona()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		var resposta = await client.PostAsync("/Setores/ReconciliarItensDeMenu", new FormUrlEncodedContent([]));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
	}

	[Fact]
	public async Task SemIntranetAdmin_Bloqueado()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		var resposta = await client.PostAsync("/Setores/ReconciliarItensDeMenu", new FormUrlEncodedContent([]));

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ReconciliarItensDeMenuTests`
Expected: FAIL (404 — a rota não existe)

- [ ] **Step 3: Ação no controller**

```csharp
	/// <summary>Garante a raiz e os itens Documentos/Avisos em todo setor que ainda não os tem.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ReconciliarItensDeMenu(CancellationToken cancellationToken = default)
	{
		var quantos = await reconciliar.HandleAsync(cancellationToken);

		TempData[FeedbackViewComponent.ChaveDaMensagem] = quantos == 0
			? "Nenhum setor precisava de reconciliação."
			: $"{quantos} setor(es) ganharam item(ns) de menu que faltavam.";

		return RedirectToAction(nameof(Index));
	}
```

`ReconciliarItensDeMenuHandler reconciliar` entra no construtor de `SetoresController`,
junto dos outros handlers já acrescentados na Task 11.

- [ ] **Step 4: Botão em `Views/Setores/Index.cshtml`**

Acrescentar, no mesmo padrão do botão "Reconciliar permissões" de
`Views/Acesso/Index.cshtml` (Task de outra spec, já entregue — reaproveite o mesmo
`sc-panel`):

```cshtml
<div class="sc-panel mt-3">
    <form method="post" asp-action="ReconciliarItensDeMenu">
        <button class="btn btn-outline-secondary" type="submit">Reconciliar itens de menu</button>
        <div class="form-text">
            Garante a raiz e os itens Documentos/Avisos em todo setor que ainda não os tem — nunca
            remove nada já customizado.
        </div>
    </form>
</div>
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter ReconciliarItensDeMenuTests`
Expected: PASS

- [ ] **Step 6: Acrescentar a rota às listas de `SetoresAutorizacaoTests`**

Mesma mecânica do Step 5 da Task 11 — `/Setores/ReconciliarItensDeMenu` entra na lista de
`Post_SemIntranetAdmin_BloqueadoMesmoSemToken`.

- [ ] **Step 7: Suíte completa, um review final contra o "Review Focus" do topo deste plano**

Run: `dotnet test`
Expected: PASS — confira explicitamente, um por um, os pontos do "Review Focus": setor
com os dois checkboxes desmarcados (Task 9), item desativado vira 404 e sai das abas (Task
9), POST em recurso desligado recusado (Task 9), rota maliciosa recusada (Task 4),
reconciliação em estado misto e com colisão de slug (Task 6). O único sem teste
automatizado é a leitura sem `setor-{slug}:read` — confira por leitura que `Resolver`
chama `PodeLerAsync` antes de resolver o caminho. Se algum dos outros não tiver um teste
que o exercite claramente, volte e escreva antes de considerar esta task concluída.

- [ ] **Step 8: Build completo e commit**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Web tests/Secco.Intranet.Tests/Integration/ReconciliarItensDeMenuTests.cs tests/Secco.Intranet.Tests/Integration/SetoresAutorizacaoTests.cs
git commit -m "feat(menu): reconciliar itens de menu para setores criados antes desta feature"
```

---

## Depois de tudo

Atualizar `docs/roadmap.md` (o item `Recurso`/`SetorRecurso` e o de "Tela de administração
de setores (cadastro + toggle de recursos)" saem como entregues, substituídos por uma
única linha apontando para esta feature) e `docs/plataforma.md` não muda (nada aqui
depende da plataforma). Isso não é uma task numerada porque é documentação de fechamento,
não implementação — trate como o último passo da Task 12, sem teste associado.
