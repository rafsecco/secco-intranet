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
- **Ciclo indireto, não só direto, na função pura:** `RegrasDeItemMenu.CriariaCiclo` (Task
  1) precisa recusar não só "item é pai de si mesmo", mas "item A é avô de si mesmo através
  de B" — um teste com só dois níveis não prova isso. **Nota de escopo:** nenhum handler
  desta rodada chama essa função — o único jeito de definir `ParentId` hoje é
  `CriarItemMenuHandler` (Task 4), e um item recém-criado nunca pode ser ancestral de si
  mesmo (ele não existia antes). A função existe pronta, testada e correta para quando uma
  ação futura de "mover para outro pai" precisar dela — mesmo raciocínio de
  `RegrasDeGestor.CriariaCiclo`, que só passou a valer quando o organograma ganhou edição.
  Não há regressão a temer aqui: é um invariante do **modelo**, protegido desde já, mesmo
  sem uma ação de UI que o exercite ainda.
- **Reconciliação rodada duas vezes em um setor que já tem `Documentos` mas não `Avisos`**
  (estado misto, não "nenhum dos dois"): precisa completar só o que falta, sem tentar
  recriar `Documentos` e sem duplicar (Task 6).
- **Leitura sem `setor-{slug}:read` em `GET /setor/{slug}` "nua"** (sem nenhum segmento de
  caminho) — é o gap que a spec fechou; fácil testar só o caminho profundo
  (`/setor/{slug}/avisos`) e esquecer da raiz (Task 9).

---

## Task 1: Entidade `ItemMenu`, enum `TipoDeItemMenu` e checagem de ciclo

**Files:**
- Create: `src/Secco.Intranet.Domain/Menu/ItemMenu.cs`
- Create: `src/Secco.Intranet.Domain/Menu/RegrasDeItemMenu.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/ItemMenuTests.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/RegrasDeItemMenuTests.cs`

**Interfaces:**
- Produces: `ItemMenu` (construtor `ItemMenu(Guid setorId, Guid? parentId, string nome, string slug, TipoDeItemMenu tipo, string? rota, string? icone, int ordem)`,
  propriedades `Id`/`SetorId`/`ParentId`/`Nome`/`Slug`/`Tipo`/`Rota`/`Icone`/`Ordem`/`Ativo`,
  métodos `Ativar()`/`Desativar()`/`DefinirOrdem(int)`), `TipoDeItemMenu` (`Setor = 0`,
  `Documentos = 1`, `Avisos = 2`, `Personalizado = 3`), `ItemMenu.IconeEhValido(string?)`
  (reaproveita o mesmo formato de `Setor.IconeEhValido`), `RegrasDeItemMenu.CriariaCiclo(IReadOnlyDictionary<Guid, Guid> paiPorItem, Guid itemId, Guid novoPaiId)`.

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

- [ ] **Step 5: Escrever o teste de ciclo que falha**

```csharp
using AwesomeAssertions;
using Secco.Intranet.Domain.Menu;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class RegrasDeItemMenuTests
{
	[Fact]
	public void SemNenhumaRelacao_NaoCriaCiclo()
	{
		var mapa = new Dictionary<Guid, Guid>();

		RegrasDeItemMenu.CriariaCiclo(mapa, Guid.NewGuid(), Guid.NewGuid()).Should().BeFalse();
	}

	[Fact]
	public void ItemComoPaiDeSiMesmo_CriaCiclo()
	{
		var item = Guid.NewGuid();
		var mapa = new Dictionary<Guid, Guid>();

		RegrasDeItemMenu.CriariaCiclo(mapa, item, item).Should().BeTrue();
	}

	[Fact]
	public void CicloIndireto_ANetoDeSiMesmoViaB_EDetectado()
	{
		// B já é filho de A. Tentar fazer A ser filho de B fecharia o ciclo A -> B -> A.
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();
		var mapa = new Dictionary<Guid, Guid> { [b] = a };

		RegrasDeItemMenu.CriariaCiclo(mapa, a, b).Should().BeTrue();
	}

	[Fact]
	public void CicloIndiretoDeTresNiveis_EDetectado()
	{
		// C é filho de B, que é filho de A. Tentar fazer A ser filho de C fecha A -> C -> B -> A.
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();
		var c = Guid.NewGuid();
		var mapa = new Dictionary<Guid, Guid> { [b] = a, [c] = b };

		RegrasDeItemMenu.CriariaCiclo(mapa, a, c).Should().BeTrue();
	}

	[Fact]
	public void NovoPaiEmOutroRamo_NaoCriaCiclo()
	{
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();
		var irmaoDeA = Guid.NewGuid();
		var mapa = new Dictionary<Guid, Guid> { [b] = a };

		RegrasDeItemMenu.CriariaCiclo(mapa, b, irmaoDeA).Should().BeFalse();
	}
}
```

- [ ] **Step 6: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter RegrasDeItemMenuTests`
Expected: FAIL (compilação — `RegrasDeItemMenu` não existe)

- [ ] **Step 7: Implementar (mesmo padrão de `RegrasDeGestor.CriariaCiclo`)**

```csharp
namespace Secco.Intranet.Domain.Menu;

/// <summary>Regras da árvore de itens de menu que dependem do conjunto todo, não de um nó isolado.</summary>
public static class RegrasDeItemMenu
{
	/// <summary>
	/// Indica se fazer <paramref name="itemId"/> filho de <paramref name="novoPaiId"/> fecharia um
	/// ciclo: sobe a cadeia de pais a partir do novo pai e vê se chega ao próprio item. Mesmo
	/// algoritmo de <c>RegrasDeGestor.CriariaCiclo</c>, aplicado à árvore de menu.
	/// </summary>
	/// <param name="paiPorItem">Mapa atual item → pai (só quem tem pai).</param>
	/// <param name="itemId">Item que está mudando de pai.</param>
	/// <param name="novoPaiId">O pai proposto.</param>
	public static bool CriariaCiclo(IReadOnlyDictionary<Guid, Guid> paiPorItem, Guid itemId, Guid novoPaiId)
	{
		ArgumentNullException.ThrowIfNull(paiPorItem);

		var visitados = new HashSet<Guid>();
		var atual = novoPaiId;

		while (visitados.Add(atual))
		{
			if (atual == itemId)
			{
				return true;
			}

			if (!paiPorItem.TryGetValue(atual, out var proximo))
			{
				return false;
			}

			atual = proximo;
		}

		return false;
	}
}
```

- [ ] **Step 8: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "ItemMenuTests|RegrasDeItemMenuTests"`
Expected: PASS

- [ ] **Step 9: Build completo e commit**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Domain/Menu tests/Secco.Intranet.Tests/Unit/ItemMenuTests.cs tests/Secco.Intranet.Tests/Unit/RegrasDeItemMenuTests.cs
git commit -m "feat(menu): entidade ItemMenu e checagem de ciclo na árvore"
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
using Secco.Intranet.Domain.Menu;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class ItemMenuPersistenceTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task PersisteEReleParenteChildTipoEIcone()
	{
		await using var scope = factory.Services.CreateAsyncScope();
		var context = scope.ServiceProvider.GetRequiredService<Secco.Intranet.Infrastructure.Contexts.IntranetDbContext>();

		var setorId = Guid.NewGuid();
		var raiz = new ItemMenu(setorId, null, "Financeiro", "financeiro", TipoDeItemMenu.Setor, null, null, 0);
		context.ItensMenu.Add(raiz);
		await context.SaveChangesAsync();

		var filho = new ItemMenu(setorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, "bi-folder2", 0);
		context.ItensMenu.Add(filho);
		await context.SaveChangesAsync();

		await using var outroScope = factory.Services.CreateAsyncScope();
		var outroContexto = outroScope.ServiceProvider.GetRequiredService<Secco.Intranet.Infrastructure.Contexts.IntranetDbContext>();

		var lido = await outroContexto.ItensMenu.AsNoTracking().FirstAsync(i => i.Id == filho.Id);

		lido.ParentId.Should().Be(raiz.Id);
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

Confira os dois arquivos gerados: a tabela precisa ter `id_fk_setor`, `id_fk_item_menu_pai`
(nullable), `ds_nome`, `ds_slug`, `nu_tipo`, `ds_rota` (nullable), `ds_icone` (nullable),
`nu_ordem`, `fl_ativo`, `dt_created_at` — se algum nome sair diferente, a convention não
reconheceu a coluna do jeito esperado; pare e investigue antes de seguir.

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
using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Infrastructure.Repositories;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class ItemMenuRepositoryTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<(IItemMenuRepository Repositorio, Secco.Intranet.Infrastructure.Contexts.IntranetDbContext Contexto)> CriarAsync()
	{
		var scope = factory.Services.CreateAsyncScope();
		var contexto = scope.ServiceProvider.GetRequiredService<Secco.Intranet.Infrastructure.Contexts.IntranetDbContext>();

		return (new ItemMenuRepository(contexto), contexto);
	}

	[Fact]
	public async Task ListarPorSetor_DevolveTodaAArvore_InclusiveInativos()
	{
		var (repositorio, contexto) = await CriarAsync();
		var setorId = Guid.NewGuid();
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
		var (repositorio, _) = await CriarAsync();
		var setorId = Guid.NewGuid();
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
		var (repositorio, _) = await CriarAsync();
		var setorId = Guid.NewGuid();
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
- Consumes: `IItemMenuRepository` (Task 3), `ItemMenu`/`TipoDeItemMenu`/`RegrasDeItemMenu` (Task 1).
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

		/// <summary>O pai informado não existe, é de outro setor, ou fecharia um ciclo.</summary>
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
	}
```

- [ ] **Step 4: `CriarItemMenuHandler`**

```csharp
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Cria um item na árvore de um setor.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class CriarItemMenuHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Dados do novo item.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ItemMenuDto>> HandleAsync(CriarItemMenuCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Nome))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.NomeRequired);
		}

		if (string.IsNullOrWhiteSpace(command.Slug))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.SlugRequired);
		}

		if (!ItemMenu.IconeEhValido(command.Icone))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.IconeInvalido);
		}

		var arvore = await repository.ListarPorSetorAsync(command.SetorId, cancellationToken).ConfigureAwait(false);
		var pai = arvore.FirstOrDefault(item => item.Id == command.ParentId);

		if (pai is null)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.PaiInvalido);
		}

		var slugNormalizado = command.Slug.Trim().ToLowerInvariant();

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
			command.Rota, command.Icone, proximaOrdem);

		await repository.AddAsync(item, cancellationToken).ConfigureAwait(false);

		return ItemMenuDto.FromEntity(item);
	}
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
Expected: PASS (7 testes)

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

		var vizinho = irmaos[indiceDoVizinho];
		var atualRastreado = await repository.GetParaEdicaoAsync(item.Id, cancellationToken).ConfigureAwait(false);
		var vizinhoRastreado = await repository.GetParaEdicaoAsync(vizinho.Id, cancellationToken).ConfigureAwait(false);

		(atualRastreado!.DefinirOrdem(vizinho.Ordem), vizinhoRastreado!.DefinirOrdem(item.Ordem));

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
```

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
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Quantos setores ganharam a raiz e/ou algum dos dois itens embutidos.</returns>
	public async Task<int> HandleAsync(CancellationToken cancellationToken = default)
	{
		var setores = await searchSetores.HandleAsync(new SetorSearchCriteria(), cancellationToken).ConfigureAwait(false);
		var alterados = 0;

		foreach (var setor in setores.Value.Items)
		{
			var itens = await repository.ListarPorSetorAsync(setor.Id, cancellationToken).ConfigureAwait(false);
			var raiz = itens.FirstOrDefault(item => item.Tipo == TipoDeItemMenu.Setor);
			var mudou = false;

			if (raiz is null)
			{
				raiz = new ItemMenu(setor.Id, null, setor.Nome, setor.Slug, TipoDeItemMenu.Setor, null, null, 0);
				await repository.AddAsync(raiz, cancellationToken).ConfigureAwait(false);
				mudou = true;
			}

			if (!itens.Any(item => item.Tipo == TipoDeItemMenu.Documentos))
			{
				await repository.AddAsync(
					new ItemMenu(setor.Id, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0),
					cancellationToken).ConfigureAwait(false);
				mudou = true;
			}

			if (!itens.Any(item => item.Tipo == TipoDeItemMenu.Avisos))
			{
				await repository.AddAsync(
					new ItemMenu(setor.Id, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 1),
					cancellationToken).ConfigureAwait(false);
				mudou = true;
			}

			if (mudou)
			{
				alterados++;
			}
		}

		return alterados;
	}
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
- Test: `tests/Secco.Intranet.Tests/Unit/CreateSetorHandlerTests.cs` (crie se não existir; se
  já existir, acrescente os casos abaixo aos que já existem)

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

Run: `dotnet test tests/Secco.Intranet.Tests --filter "CreateSetorHandlerItemMenuTests|CreateSetorHandlerTests"`
Expected: PASS — inclusive os testes **já existentes** de `CreateSetorHandler` (o
construtor ganhou um parâmetro novo; eles precisam ser ajustados para passar um
`ItemMenuRepositorioFalso`, não só os testes novos)

- [ ] **Step 5: Build completo — confirma que nada mais quebrou com a assinatura nova**

Run: `dotnet build`
Expected: 0 avisos, 0 erros — se algo mais construía `CreateSetorHandler` diretamente
(fora do DI), vai aparecer aqui

- [ ] **Step 6: Commit**

```bash
git add src/Secco.Intranet.Application/Setores/CreateSetorHandler.cs tests/Secco.Intranet.Tests/Unit/CreateSetorHandlerItemMenuTests.cs
git commit -m "feat(menu): CreateSetorHandler cria a raiz e Documentos/Avisos opcionais"
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
  (`IReadOnlyList<string>`, os slugs até o nó, para montar URL de cada aba)),
  `ResolverCaminhoDeMenuHandler.HandleAsync(Guid setorId, IReadOnlyList<string> caminho, CancellationToken) -> Task<Result<ResultadoDaResolucao>>`.
  Erro `IntranetErrors.Menu.NotFound` para qualquer segmento sem match ou nó inativo no
  meio do caminho.

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
Expected: PASS (9 testes)

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

## Task 9: `SetorController` — rota recursiva e leitura exige permissão

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/SetorController.cs`
- Modify: `src/Secco.Intranet.Web/Models/Documentos/SetorDocumentosViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Models/Publicacoes/SetorAvisosViewModel.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/SetorMenuRotaTests.cs`

**Interfaces:**
- Consumes: `ResolverCaminhoDeMenuHandler`/`ResultadoDaResolucao` (Task 8), `IPermissoesDeSetor` (já existente).
- Produces: os dois `ViewModel`s ganham `IReadOnlyList<ItemMenuAbaDto> Abas` (nova classe
  simples, `record ItemMenuAbaDto(string Nome, string? Icone, string Url, bool Ativa)`, em
  `src/Secco.Intranet.Web/Models/ItemMenuAbaDto.cs`) — quem consome (a view, Task 10) monta
  a barra de abas a partir disso, em vez do link fixo Documentos/Avisos de hoje.

Esta task muda o comportamento de rota mais do que o texto do controller deixa óbvio —
leia com atenção antes de editar:

- `[HttpGet("")]`/`[HttpGet("documentos")]`/`[HttpGet("avisos")]` (as três rotas GET de
  hoje) saem. Entra uma rota única, `[HttpGet("{**caminho}")]`, que resolve pelo tipo do
  nó encontrado.
- As rotas **POST** (`documentos`, `documentos/{id}/arquivar`, `avisos`,
  `avisos/{id}/arquivar`) **não mudam** — continuam fixas, porque só pode existir um
  `Documentos`/`Avisos` por setor (Task 4 garante isso).
- A leitura passa a exigir `setor-{slug}:read` — hoje não exige nada além de autenticação
  (achado registrado na spec). Mesma forma de `PodePublicarAsync`, mas com `"read"`.

- [ ] **Step 1: Escrever o teste de rota que falha**

```csharp
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A árvore de itens de menu substitui as duas abas fixas de /setor/{slug}. Cada teste cria
/// o próprio setor, com um slug sufixado por GUID (mesmo padrão de
/// <c>AcessoTelasTests.CriarSetorAsync</c>) — não existe setor fixo "financeiro" na
/// fixture.
/// </summary>
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

	/// <summary>Cria um setor com Documentos e Avisos habilitados (padrão do comando) e devolve o slug único gerado.</summary>
	private async Task<string> CriarSetorAsync(bool habilitarDocumentos = true, bool habilitarAvisos = true)
	{
		var slug = $"menu-{Guid.NewGuid():N}"[..20];

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criado = await escopo.ServiceProvider
			.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug, HabilitarDocumentos: habilitarDocumentos, HabilitarAvisos: habilitarAvisos));

		criado.IsSuccess.Should().BeTrue();

		return slug;
	}

	[Fact]
	public async Task CaminhoVazio_RedirecionaParaOPrimeiroFilhoAtivo()
	{
		// Avisos vem antes de Documentos em ordem alfabética (Task 7).
		var slug = await CriarSetorAsync();

		var resposta = await CriarCliente($"{slug}-user").GetAsync($"/setor/{slug}");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be($"/setor/{slug}/avisos",
			"o redirecionamento leva ao primeiro filho ativo por ordem alfabética");
	}

	[Fact]
	public async Task SetorSemNenhumItemAtivo_MostraOEstadoVazio()
	{
		var slug = await CriarSetorAsync(habilitarDocumentos: false, habilitarAvisos: false);

		var resposta = await CriarCliente($"{slug}-user").GetAsync($"/setor/{slug}");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var corpo = await resposta.Content.ReadAsStringAsync();
		corpo.Should().Contain("Nenhum item ainda");
	}

	[Fact]
	public async Task ItemDesativado_Da404_NoCaminhoQueAntesFuncionava()
	{
		var slug = await CriarSetorAsync();

		await using (var scope = factory.Services.CreateAsyncScope())
		{
			scope.ServiceProvider.SetTenant(factory.TenantAlfa);

			var setorHandler = scope.ServiceProvider.GetRequiredService<GetSetorBySlugHandler>();
			var itens = scope.ServiceProvider.GetRequiredService<IItemMenuRepository>();
			var alternar = scope.ServiceProvider.GetRequiredService<AtivarDesativarItemMenuHandler>();

			var setor = await setorHandler.HandleAsync(slug);
			var arvore = await itens.ListarPorSetorAsync(setor.Value.Id);
			var documentos = arvore.Single(item => item.Tipo == TipoDeItemMenu.Documentos);

			await alternar.HandleAsync(documentos.Id, ativar: false);
		}

		var resposta = await CriarCliente($"{slug}-user").GetAsync($"/setor/{slug}/documentos");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task SegmentoSemMatchNenhum_Da404()
	{
		var slug = await CriarSetorAsync();

		var resposta = await CriarCliente($"{slug}-user").GetAsync($"/setor/{slug}/caminho-que-nao-existe");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task SemPermissaoDeLeituraDoSetor_Da404_MesmoAutenticado()
	{
		var slug = await CriarSetorAsync();
		var outroSlug = await CriarSetorAsync();

		var resposta = await CriarCliente($"{outroSlug}-user").GetAsync($"/setor/{slug}");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound,
			"achado da spec: ler exige setor-{slug}:read, não só estar autenticado");
	}

	[Fact]
	public async Task ComPermissaoDeLeitura_AbreNormalmente()
	{
		var slug = await CriarSetorAsync();

		var resposta = await CriarCliente($"{slug}-admin").GetAsync($"/setor/{slug}/documentos");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}
}
```

`escopo.ServiceProvider.SetTenant(factory.TenantAlfa)` é a mesma extensão que
`AcessoTelasTests.CriarSetorAsync` (e outros 12 arquivos de teste de integração) já usam
para dar ao escopo manual o `ITenantContext` que o middleware normalmente resolveria de uma
requisição HTTP real — sem isso, `CreateSetorHandler` (e qualquer handler que dependa do
tenant atual) falha fora de uma requisição.

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Secco.Intranet.Tests --filter SetorMenuRotaTests`
Expected: FAIL — hoje `GET /setor/financeiro` mostra Documentos (não Avisos), e
`SemPermissaoDeLeitura` passaria com 200 em vez de 404 (comprova o gap que a spec fechou)

- [ ] **Step 3: `ItemMenuAbaDto` e os dois `ViewModel`s**

Create: `src/Secco.Intranet.Web/Models/ItemMenuAbaDto.cs`

```csharp
namespace Secco.Intranet.Web.Models;

/// <summary>Uma aba da barra de navegação dentro da página de um setor.</summary>
/// <param name="Nome">Rótulo.</param>
/// <param name="Icone">Classe do Bootstrap Icons; nulo = sem ícone.</param>
/// <param name="Url">Link absoluto da aba.</param>
/// <param name="Ativa">Se é a aba correspondente à página atual.</param>
public sealed record ItemMenuAbaDto(string Nome, string? Icone, string Url, bool Ativa);
```

Em `src/Secco.Intranet.Web/Models/Documentos/SetorDocumentosViewModel.cs`, acrescentar o
parâmetro `Abas` (mantendo os demais existentes — confira o record atual antes de editar):

```csharp
public sealed record SetorDocumentosViewModel(
	SetorDto Setor, IReadOnlyList<DocumentoDto> Documentos, bool PodePublicar,
	DocumentoFormViewModel Form, long TamanhoMaximoBytes, IReadOnlyList<ItemMenuAbaDto> Abas);
```

Mesma mudança, mesmo padrão, em `SetorAvisosViewModel` (acrescentar `IReadOnlyList<ItemMenuAbaDto> Abas`
ao final do record).

- [ ] **Step 4: Reescrever `SetorController`**

Trocar as três rotas GET fixas por uma só, e injetar `ResolverCaminhoDeMenuHandler`. O
construtor ganha o parâmetro novo; os demais parâmetros existentes continuam:

```csharp
	[HttpGet("{**caminho}")]
	public async Task<IActionResult> Resolver(string slug, string? caminho, CancellationToken cancellationToken = default)
	{
		var setor = await getSetorHandler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

		if (setor.IsFailure || !setor.Value.Ativo)
		{
			return NotFound();
		}

		if (!await PodeLerAsync(slug, cancellationToken).ConfigureAwait(false))
		{
			return NotFound();
		}

		var segmentos = string.IsNullOrWhiteSpace(caminho)
			? Array.Empty<string>()
			: caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);

		var resolvido = await resolverCaminho.HandleAsync(setor.Value.Id, segmentos, cancellationToken).ConfigureAwait(false);

		if (resolvido.IsFailure)
		{
			return NotFound();
		}

		// Nó com pelo menos um filho ativo (inclusive a raiz, caminho vazio): redireciona
		// para o primeiro por Ordem — mesma UX de hoje, onde a URL "nua" já mostra o
		// primeiro recurso. ResolverCaminhoDeMenuHandler já calculou isso numa consulta só,
		// sem round-trip extra.
		if (resolvido.Value.PrimeiroFilhoAtivo is { } primeiroFilho)
		{
			var caminhoDoFilho = string.Join('/', [.. segmentos, primeiroFilho.Slug]);

			return RedirectToAction(nameof(Resolver), new { slug, caminho = caminhoDoFilho });
		}

		var no = resolvido.Value.No;

		return no.Tipo switch
		{
			Domain.Menu.TipoDeItemMenu.Documentos => await RenderizarDocumentosAsync(slug, resolvido.Value, cancellationToken),
			Domain.Menu.TipoDeItemMenu.Avisos => await RenderizarAvisosAsync(slug, resolvido.Value, cancellationToken),
			Domain.Menu.TipoDeItemMenu.Personalizado when !string.IsNullOrWhiteSpace(no.Rota) => Redirect(no.Rota),
			Domain.Menu.TipoDeItemMenu.Personalizado => View("SemConteudo", setor.Value),
			_ => View("SemItens", setor.Value), // Setor (raiz) sem nenhum item ativo
		};
	}
```

Sem `PrimeiroFilhoAtivo`, os únicos `Tipo` que sobram no `switch` são: `Documentos`/`Avisos`
(sempre folha — nunca têm filho, Task 4 impede um segundo nível debaixo deles por
convenção de uso, ainda que o modelo não proíba tecnicamente), `Personalizado` sem filhos
ativos (com ou sem `Rota`), e `Setor` sem nenhum filho ativo (setor criado com os dois
checkboxes desmarcados, e ninguém ainda adicionou nada pela tela de administração).

Os métodos privados `RenderizarDocumentosAsync`/`RenderizarAvisosAsync` substituem
`MontarAsync`/`MontarAvisosAsync` de hoje: mesmo corpo, trocando a montagem do
`Setor`/`Documentos`/`PodePublicar` por reaproveitar o que `Resolver` já resolveu (nada de
buscar o setor de novo), e acrescentando `Abas` ao `ViewModel` — construído a partir de
`resolvido.Value.Irmaos`, mapeando cada um para
`new ItemMenuAbaDto(irmao.Nome, irmao.Icone, Url.Action(nameof(Resolver), new { slug, caminho = irmao.Slug }), irmao.Id == no.Id)`.

Acrescentar `PodeLerAsync`, espelhando `PodePublicarAsync` já existente:

```csharp
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

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/Secco.Intranet.Tests --filter SetorMenuRotaTests`
Expected: PASS (7 testes)

- [ ] **Step 6: Rodar a suíte inteira — este é o ponto de maior risco de regressão**

Run: `dotnet test`
Expected: PASS — preste atenção especial em qualquer teste existente que dependia da URL
`/setor/{slug}/documentos` ou `/setor/{slug}/avisos` continuar batendo direto (POST não
muda, mas qualquer teste que fazia `GET` numa dessas URLs esperando Documentos/Avisos
específico pode precisar de ajuste, já que agora a resposta depende da árvore)

- [ ] **Step 7: Build completo e commit**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Web/Controllers/SetorController.cs src/Secco.Intranet.Web/Models tests/Secco.Intranet.Tests/Integration/SetorMenuRotaTests.cs
git commit -m "feat(menu): SetorController resolve a árvore em vez de abas fixas; leitura exige setor-{slug}:read"
```

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

`SemConteudo.cshtml` (item `Personalizado` sem `Rota`):

```cshtml
@using Secco.Intranet.Application.Setores
@model SetorDto
@{
    ViewData["Title"] = Model.Nome;
    var cabecalho = new Secco.Intranet.Web.Theming.Contracts.PageHeaderModel(Model.Nome, "Este item ainda não tem conteúdo.", Model.Slug);
}

<partial name="_PageHeader" model="cabecalho" />
<partial name="_EmptyState" model='@(new Secco.Intranet.Web.Theming.Contracts.EmptyStateModel("bi-tools", "Sem conteúdo ainda", "Quem administra este item pode configurar uma rota, ou este espaço aguarda desenvolvimento próprio."))' />
```

`SemItens.cshtml` (setor sem nenhum item ativo):

```cshtml
@using Secco.Intranet.Application.Setores
@model SetorDto
@{
    ViewData["Title"] = Model.Nome;
    var cabecalho = new Secco.Intranet.Web.Theming.Contracts.PageHeaderModel(Model.Nome, "Este setor ainda não tem nenhum item de menu.", Model.Slug);
}

<partial name="_PageHeader" model="cabecalho" />
<partial name="_EmptyState" model='@(new Secco.Intranet.Web.Theming.Contracts.EmptyStateModel("bi-folder-x", "Nenhum item ainda", "Um intranet-admin pode adicionar itens na administração de Setores."))' />
```

- [ ] **Step 4: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 5: Rodar a aplicação e conferir visualmente**

Run: `dotnet run --project src/Secco.Intranet.Web` (ambiente Development, seed automático)
Abra `http://localhost:5xxx/setor/financeiro` (ou o setor que o seed criar) e confirme: a
barra de abas aparece, reflete os itens reais (não mais hardcoded), e navegar entre
Avisos/Documentos funciona.

- [ ] **Step 6: Build completo e commit**

Run: `dotnet build`
Expected: 0 avisos, 0 erros

```bash
git add src/Secco.Intranet.Web/Views/Setor
git commit -m "feat(menu): barra de abas dinâmica e telas de item sem conteúdo/setor sem itens"
```

---

## Task 11: `SetoresController.Menu` — administração da árvore

**Files:**
- Create: `src/Secco.Intranet.Web/Models/SetorMenuViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/SetoresController.cs`
- Create: `src/Secco.Intranet.Web/Views/Setores/Menu.cshtml`
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

- [ ] **Step 4: View `Menu.cshtml`**

```cshtml
@using Secco.Intranet.Application.Menu
@using Secco.Intranet.Domain.Menu
@model SetorMenuViewModel
@{
    ViewData["Title"] = $"Menu de {Model.Setor.Nome}";
    var cabecalho = new Secco.Intranet.Web.Theming.Contracts.PageHeaderModel($"Menu de {Model.Setor.Nome}", "Itens de menu deste setor.", Model.Setor.Slug);

    void Linha(NoDaArvoreDto no, int profundidade)
    {
        <li class="sc-list__item" style="padding-left: @(profundidade * 1.5)rem">
            <div class="sc-list__text">
                <p class="sc-list__title">
                    @if (!string.IsNullOrWhiteSpace(no.Item.Icone))
                    {
                        <i class="bi @no.Item.Icone" aria-hidden="true"></i>
                    }
                    @no.Item.Nome
                    <span class="sc-meta">(@no.Item.Tipo)</span>
                    @if (!no.Item.Ativo)
                    {
                        <partial name="_Badge" model='@(new Secco.Intranet.Web.Theming.Contracts.BadgeModel("Desativado", Secco.Intranet.Web.Theming.Contracts.BadgeVariante.Neutro))' />
                    }
                </p>
                @if (no.Item.Tipo != TipoDeItemMenu.Setor)
                {
                    <div class="d-flex gap-2">
                        <form method="post" asp-action="AlternarItemDeMenu" class="d-inline">
                            <input type="hidden" name="setorId" value="@Model.Setor.Id" />
                            <input type="hidden" name="itemId" value="@no.Item.Id" />
                            <input type="hidden" name="ativar" value="@(!no.Item.Ativo)" />
                            <button class="btn btn-sm btn-outline-secondary" type="submit">@(no.Item.Ativo ? "Desativar" : "Ativar")</button>
                        </form>
                        <form method="post" asp-action="MoverItemDeMenu" class="d-inline">
                            <input type="hidden" name="setorId" value="@Model.Setor.Id" />
                            <input type="hidden" name="itemId" value="@no.Item.Id" />
                            <input type="hidden" name="paraCima" value="true" />
                            <button class="btn btn-sm btn-outline-secondary" type="submit">&uarr;</button>
                        </form>
                        <form method="post" asp-action="MoverItemDeMenu" class="d-inline">
                            <input type="hidden" name="setorId" value="@Model.Setor.Id" />
                            <input type="hidden" name="itemId" value="@no.Item.Id" />
                            <input type="hidden" name="paraCima" value="false" />
                            <button class="btn btn-sm btn-outline-secondary" type="submit">&darr;</button>
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

        foreach (var filho in no.Filhos)
        {
            Linha(filho, profundidade + 1);
        }
    }
}

<partial name="_PageHeader" model="cabecalho" />

<ul class="sc-list mb-3">
    @{ Linha(Model.Raiz, 0); }
</ul>

<div class="sc-panel">
    <h2 class="h6 mb-3">Novo item</h2>
    <form method="post" asp-action="CriarItemDeMenu" class="row g-2">
        <input type="hidden" name="setorId" value="@Model.Setor.Id" />
        <div class="col-sm-3">
            <label class="form-label" for="parentId">Pai</label>
            <select class="form-select" id="parentId" name="parentId">
                @{ void Opcao(NoDaArvoreDto no, int profundidade) {
                    <option value="@no.Item.Id">@(new string('-', profundidade)) @no.Item.Nome</option>
                    foreach (var filho in no.Filhos) { Opcao(filho, profundidade + 1); }
                } }
                @{ Opcao(Model.Raiz, 0); }
            </select>
        </div>
        <div class="col-sm-3">
            <label class="form-label" for="nome">Nome</label>
            <input class="form-control" id="nome" name="nome" required />
        </div>
        <div class="col-sm-2">
            <label class="form-label" for="slug">Slug</label>
            <input class="form-control" id="slug" name="slug" required />
        </div>
        <div class="col-sm-2">
            <label class="form-label" for="tipo">Tipo</label>
            <select class="form-select" id="tipo" name="tipo">
                <option value="@((int)TipoDeItemMenu.Personalizado)">Personalizado</option>
                <option value="@((int)TipoDeItemMenu.Documentos)">Documentos</option>
                <option value="@((int)TipoDeItemMenu.Avisos)">Avisos</option>
            </select>
        </div>
        <div class="col-sm-2">
            <label class="form-label" for="icone">Ícone</label>
            <input class="form-control" id="icone" name="icone" placeholder="bi-cash-coin" />
        </div>
        <div class="col-sm-4">
            <label class="form-label" for="rota">Rota (só Personalizado)</label>
            <input class="form-control" id="rota" name="rota" placeholder="/algum/caminho" />
        </div>
        <div class="col-sm-2 d-flex align-items-end">
            <button class="btn btn-primary w-100" type="submit">Criar</button>
        </div>
    </form>
</div>
```

> Nota: o dropdown "Tipo" acima não filtra Documentos/Avisos já existentes no setor — a
> spec (Seção Administração) pede isso ("o dropdown não oferece Documentos/Avisos se
> aquele setor já os tem"). Deixado como melhoria de UX fora do critério de teste
> automatizado desta task (o handler já recusa no back-end, Task 4,
> `TipoJaExiste`) — quem quiser fechar isso por completo pode filtrar as `<option>` em
> JavaScript ou repassando `Model.Raiz` para calcular quais tipos já existem antes de
> renderizar as opções. Registrar como débito consciente, não esquecimento.

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
Expected: PASS — confira explicitamente, um por um, os cinco pontos do "Review Focus":
setor com os dois checkboxes desmarcados (Task 9/10), item desativado no meio do caminho
vira 404 (Task 9), ciclo indireto detectado (Task 1), reconciliação em estado misto (Task
6), leitura sem `setor-{slug}:read` na raiz "nua" (Task 9). Se qualquer um não tiver um
teste que o exercite claramente, volte e escreva antes de considerar esta task concluída.

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
