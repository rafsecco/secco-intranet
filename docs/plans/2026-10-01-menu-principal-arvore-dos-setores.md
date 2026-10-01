# Menu principal com a árvore dos setores — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A árvore `ItemMenu` de cada setor passa a ser a seção "Setores" do menu principal (setor = nível 0 sem link, submenus flutuantes), com URLs `/{setor}/{item}/…` sem prefixo, sem abas na página, e com as ações da árvore auditadas.

**Architecture:** O core monta a árvore de navegação (`IntranetNavigation.Build`) a partir das árvores de todos os setores visíveis, carregadas numa consulta só; o contrato `NavigationItemModel` ganha `Filhos`. Os temas renderizam listas aninhadas no servidor e o `theme.js` de cada um abre os submenus com `position: fixed`. `SetorController` sai de `setor/{slug}` para `{slug}` na raiz, protegido por uma lista de slugs reservados verificada contra todos os endpoints.

**Tech Stack:** .NET 10 MVC, Razor (local functions com markup), EF Core, xUnit + AwesomeAssertions, Sass/npm nos temas, JS ES5 sem dependência.

**Spec:** [docs/specs/2026-10-01-menu-principal-arvore-dos-setores-design.md](../specs/2026-10-01-menu-principal-arvore-dos-setores-design.md)

## Global Constraints

- Indentação: tabs (largura 4) em `.cs`/`.cshtml`; 2 espaços em `.js`/`.scss`/`.json`.
- Arquivos que já têm BOM continuam com BOM (ao reescrever por script, use `utf-8-sig`).
- Commits direto na `main`, mensagem terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Sem push.
- Ao final, nenhum `/setor/` em `src/` nem `tests/` (exceto o slug reservado `"setor"` em `SlugsReservados`).
- Toda alteração em `wwwroot/scss/` de um tema exige `npm --prefix src/Secco.Intranet.Themes.<Nome> run build`; o CSS compilado é versionado.
- Views são compiladas no build: para ver mudança de `.cshtml` no navegador, reinicie a aplicação.
- Testes de integração precisam do SQL de DEV (`docker compose up -d`).
- Comando de teste: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~<Classe>"`.
- Nada em código, doc ou commit faz referência a sistema de terceiros analisado.

## Review Focus

1. **Caminho desconhecido na raiz** (`/qualquer-coisa`, `/favicon.ico` inexistente) passa a cair no `SetorController`: deve responder 404, nunca 500. Teste em Task 3.
2. **Rotas literais continuam vencendo `{slug}`** — `/setores`, `/acesso/...`, `/diretorio`, `/publicacoes/{id}`, `/documentos/{id}/download`, `/health/live` não podem ser capturadas pelo setor. Teste em Task 3.
3. **Cadeia de agrupadores vazios** (Personalizado sem rota → Personalizado sem rota → nada ativo) some inteira do menu, e o setor junto se só tinha isso. Teste em Task 5.
4. **Item ativo com ancestral desativado** não aparece no menu nem abre por URL. Testes em Task 3 e Task 5.
5. **Slug reservado com caixa diferente** (`Mural`, ` setores `) também é recusado na criação do setor. Teste em Task 2.

---

### Task 1: Auditoria das ações da árvore

**Files:**
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`
- Create: `src/Secco.Intranet.Application/Menu/AuditoriaDeMenu.cs`
- Modify: `src/Secco.Intranet.Application/Menu/CriarItemMenuHandler.cs`, `AtivarDesativarItemMenuHandler.cs`, `MoverItemMenuHandler.cs`, `ExcluirItemMenuHandler.cs`, `ReconciliarItensDeMenuHandler.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/AuditoriaDeMenuTests.cs`
- Modify (call sites): `tests/Secco.Intranet.Tests/Unit/CriarItemMenuHandlerTests.cs`, `AtivarDesativarItemMenuHandlerTests.cs`, `MoverItemMenuHandlerTests.cs`, `ExcluirItemMenuHandlerTests.cs`, `ReconciliarItensDeMenuHandlerTests.cs`

**Interfaces:**
- Produces: `VerbosDeAuditoria.MenuItemCriar|MenuItemAtivar|MenuItemDesativar|MenuItemMover|MenuItemExcluir|MenuReconciliar`, `RecursosDeAuditoria.Menu`. Todo handler da árvore recebe `ITrilhaDeAuditoria trilha` como **último** parâmetro do construtor.
- Consumes: `TrilhaDeAcessoFalsa` (tests/Support/DublesDeAcesso.cs), `ItemMenuRepositorioFalso`.

- [ ] **Step 1: Escrever os testes que falham**

`tests/Secco.Intranet.Tests/Unit/AuditoriaDeMenuTests.cs`:

```csharp
using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Ações na árvore mudam o que um setor inteiro enxerga (desativar Documentos tira o recurso do
/// ar), e excluir não se desfaz — por isso cada uma deixa registro, e só quando de fato aconteceu.
/// </summary>
public class AuditoriaDeMenuTests
{
	private static (ItemMenuRepositorioFalso Repo, TrilhaDeAcessoFalsa Trilha, ItemMenu Raiz) Cenario()
	{
		var repo = new ItemMenuRepositorioFalso();
		var raiz = new ItemMenu(Guid.NewGuid(), null, "Financeiro", "financeiro", TipoDeItemMenu.Setor, null, null, 0);
		repo.Itens.Add(raiz);

		return (repo, new TrilhaDeAcessoFalsa(), raiz);
	}

	[Fact]
	public async Task Criar_RegistraItemCriar_ComSlugETipo()
	{
		var (repo, trilha, raiz) = Cenario();

		var criado = await new CriarItemMenuHandler(repo, trilha).HandleAsync(new CriarItemMenuCommand(
			raiz.SetorId, raiz.Id, "Painel BI", "painel-bi", TipoDeItemMenu.Personalizado, "/bi", null));

		criado.IsSuccess.Should().BeTrue();
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuItemCriar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Menu);
		registro.RecursoId.Should().Be(criado.Value.Id.ToString());
		using var json = JsonDocument.Parse(registro.Metadata!);
		json.RootElement.GetProperty("slug").GetString().Should().Be("painel-bi");
		json.RootElement.GetProperty("tipo").GetString().Should().Be("Personalizado");
		json.RootElement.GetProperty("rota").GetString().Should().Be("/bi");
	}

	[Fact]
	public async Task Criar_QueFalha_NaoRegistra()
	{
		var (repo, trilha, raiz) = Cenario();

		var criado = await new CriarItemMenuHandler(repo, trilha).HandleAsync(new CriarItemMenuCommand(
			raiz.SetorId, raiz.Id, "X", "Slug Inválido", TipoDeItemMenu.Personalizado, null, null));

		criado.IsFailure.Should().BeTrue();
		trilha.Registros.Should().BeEmpty();
	}

	[Theory]
	[InlineData(false, VerbosDeAuditoria.MenuItemDesativar)]
	[InlineData(true, VerbosDeAuditoria.MenuItemAtivar)]
	public async Task AtivarDesativar_RegistraOVerboDaAcao(bool ativar, string verbo)
	{
		var (repo, trilha, raiz) = Cenario();
		var documentos = new ItemMenu(raiz.SetorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		repo.Itens.Add(documentos);

		(await new AtivarDesativarItemMenuHandler(repo, trilha).HandleAsync(documentos.Id, ativar)).IsSuccess.Should().BeTrue();

		trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(verbo);
	}

	[Fact]
	public async Task Desativar_ARaiz_NaoRegistra()
	{
		var (repo, trilha, raiz) = Cenario();

		(await new AtivarDesativarItemMenuHandler(repo, trilha).HandleAsync(raiz.Id, ativar: false)).IsFailure.Should().BeTrue();

		trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Mover_ComVizinho_RegistraDeEPara()
	{
		var (repo, trilha, raiz) = Cenario();
		var avisos = new ItemMenu(raiz.SetorId, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 0);
		var documentos = new ItemMenu(raiz.SetorId, raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 1);
		repo.Itens.AddRange([avisos, documentos]);

		(await new MoverItemMenuHandler(repo, trilha).HandleAsync(documentos.Id, paraCima: true)).IsSuccess.Should().BeTrue();

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuItemMover);
		using var json = JsonDocument.Parse(registro.Metadata!);
		json.RootElement.GetProperty("de").GetInt32().Should().Be(1);
		json.RootElement.GetProperty("para").GetInt32().Should().Be(0);
	}

	[Fact]
	public async Task Mover_PrimeiroParaCima_NaoRegistra()
	{
		var (repo, trilha, raiz) = Cenario();
		var avisos = new ItemMenu(raiz.SetorId, raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 0);
		repo.Itens.Add(avisos);

		(await new MoverItemMenuHandler(repo, trilha).HandleAsync(avisos.Id, paraCima: true)).IsSuccess.Should().BeTrue();

		trilha.Registros.Should().BeEmpty("nada mudou de lugar");
	}

	[Fact]
	public async Task Excluir_RegistraComNomeESlug()
	{
		var (repo, trilha, raiz) = Cenario();
		var item = new ItemMenu(raiz.SetorId, raiz.Id, "Painel BI", "painel-bi", TipoDeItemMenu.Personalizado, "/bi", null, 0);
		repo.Itens.Add(item);

		(await new ExcluirItemMenuHandler(repo, trilha).HandleAsync(item.Id)).IsSuccess.Should().BeTrue();

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuItemExcluir);
		registro.RecursoId.Should().Be(item.Id.ToString());
		registro.Metadata.Should().Contain("painel-bi");
	}

	[Fact]
	public async Task Excluir_ComFilhos_NaoRegistra()
	{
		var (repo, trilha, raiz) = Cenario();
		var pai = new ItemMenu(raiz.SetorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 0);
		repo.Itens.AddRange([pai, new ItemMenu(raiz.SetorId, pai.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/v", null, 0)]);

		(await new ExcluirItemMenuHandler(repo, trilha).HandleAsync(pai.Id)).IsFailure.Should().BeTrue();

		trilha.Registros.Should().BeEmpty();
	}
}
```

Em `tests/Secco.Intranet.Tests/Unit/ReconciliarItensDeMenuHandlerTests.cs`, acrescente ao fim da classe (reaproveita o `FakeSetorRepository` que já existe ali):

```csharp
	[Fact]
	public async Task Reconciliar_QueCriouAlgo_RegistraUmaVez()
	{
		var setores = new FakeSetorRepository().Com("financeiro").Com("ti");
		var trilha = new TrilhaDeAcessoFalsa();
		var handler = new ReconciliarItensDeMenuHandler(new SearchSetoresHandler(setores), new ItemMenuRepositorioFalso(), trilha);

		await handler.HandleAsync();

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MenuReconciliar);
		registro.RecursoId.Should().Be("reconciliacao");
		registro.Metadata.Should().Contain("\"setores\":2");
	}

	[Fact]
	public async Task Reconciliar_SemNadaAFazer_NaoRegistra()
	{
		var trilha = new TrilhaDeAcessoFalsa();
		var handler = new ReconciliarItensDeMenuHandler(
			new SearchSetoresHandler(new FakeSetorRepository()), new ItemMenuRepositorioFalso(), trilha);

		(await handler.HandleAsync()).Should().Be(0);

		trilha.Registros.Should().BeEmpty();
	}
```

(Acrescente `using Secco.Intranet.Application.Auditoria;` no topo desse arquivo.)

- [ ] **Step 2: Atualizar os call sites existentes**

Todos os `new XHandler(repo)` dos testes passam a receber a trilha. Mecânico:

```bash
cd tests/Secco.Intranet.Tests/Unit
sed -i -E 's/new (CriarItemMenuHandler|AtivarDesativarItemMenuHandler|MoverItemMenuHandler|ExcluirItemMenuHandler)\(([a-zA-Z]+)\)/new \1(\2, new TrilhaDeAcessoFalsa())/g' \
  CriarItemMenuHandlerTests.cs AtivarDesativarItemMenuHandlerTests.cs MoverItemMenuHandlerTests.cs ExcluirItemMenuHandlerTests.cs
sed -i -E 's/new ReconciliarItensDeMenuHandler\((new SearchSetoresHandler\([a-zA-Z]+\)), ([a-zA-Z]+)\)/new ReconciliarItensDeMenuHandler(\1, \2, new TrilhaDeAcessoFalsa())/g' \
  ReconciliarItensDeMenuHandlerTests.cs
grep -n "Handler(repo)\|Handler(itens)" *ItemMenu*Tests.cs ReconciliarItensDeMenuHandlerTests.cs
```

Expected: o último `grep` não imprime nada. `sed -i` remove o BOM? Não — mas confira com `head -c3 arquivo | xxd`; se algum arquivo tinha `efbbbf` e perdeu, restaure.

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~AuditoriaDeMenuTests|FullyQualifiedName~ItemMenuHandlerTests|FullyQualifiedName~ReconciliarItensDeMenuHandlerTests"`
Expected: falha de compilação — `MenuItemCriar` não existe e os construtores não aceitam a trilha.

- [ ] **Step 4: Verbos e recurso**

Em `VerbosDeAuditoria.cs`, depois de `SetorReativar`:

```csharp
	/// <summary>Item criado na árvore de menu de um setor.</summary>
	public const string MenuItemCriar = "menu.item-criar";

	/// <summary>Item da árvore reativado.</summary>
	public const string MenuItemAtivar = "menu.item-ativar";

	/// <summary>Item da árvore desativado — tira o item (e o recurso, se for Documentos/Avisos) do ar.</summary>
	public const string MenuItemDesativar = "menu.item-desativar";

	/// <summary>Item trocou de posição com um irmão.</summary>
	public const string MenuItemMover = "menu.item-mover";

	/// <summary>Item personalizado excluído — irreversível.</summary>
	public const string MenuItemExcluir = "menu.item-excluir";

	/// <summary>Reconciliação em lote das árvores de menu dos setores antigos.</summary>
	public const string MenuReconciliar = "menu.reconciliar";
```

Em `RecursosDeAuditoria`, depois de `Setor`:

```csharp
	/// <summary>Item da árvore de menu de um setor.</summary>
	public const string Menu = "menu";
```

- [ ] **Step 5: Helper de auditoria do menu**

`src/Secco.Intranet.Application/Menu/AuditoriaDeMenu.cs`:

```csharp
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Menu;

/// <summary>Registro na trilha das ações da árvore de menu — só depois que a ação foi salva.</summary>
internal static class AuditoriaDeMenu
{
	/// <summary>Registra uma ação sobre um item; <paramref name="dados"/> vira o JSON do registro.</summary>
	public static Task ItemAsync(
		ITrilhaDeAuditoria trilha, string verbo, ItemMenu item, object dados, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Menu, item.Id.ToString(), JsonSerializer.Serialize(dados)),
			cancellationToken);

	/// <summary>Registra uma reconciliação em lote.</summary>
	public static Task ReconciliacaoAsync(ITrilhaDeAuditoria trilha, int setores, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(
				VerbosDeAuditoria.MenuReconciliar, RecursosDeAuditoria.Menu, "reconciliacao",
				JsonSerializer.Serialize(new { setores })),
			cancellationToken);
}
```

- [ ] **Step 6: Handlers registram**

Em cada handler: acrescente `using Secco.Intranet.Application.Auditoria;`, o parâmetro `ITrilhaDeAuditoria trilha` por último no construtor primário, e o `<param name="trilha">Trilha de auditoria.</param>` no XML doc.

`CriarItemMenuHandler` — troque o trecho final:

```csharp
		await repository.AddAsync(item, cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.ItemAsync(
			trilha, VerbosDeAuditoria.MenuItemCriar, item,
			new { setorId = item.SetorId, parentId = item.ParentId, nome = item.Nome, slug = item.Slug, tipo = item.Tipo.ToString(), rota = item.Rota },
			cancellationToken).ConfigureAwait(false);

		return ItemMenuDto.FromEntity(item);
```

`AtivarDesativarItemMenuHandler` — depois do `SaveChangesAsync`:

```csharp
		await AuditoriaDeMenu.ItemAsync(
			trilha, ativar ? VerbosDeAuditoria.MenuItemAtivar : VerbosDeAuditoria.MenuItemDesativar, item,
			new { setorId = item.SetorId, nome = item.Nome, tipo = item.Tipo.ToString() },
			cancellationToken).ConfigureAwait(false);
```

`MoverItemMenuHandler` — guarde o índice original antes da troca e registre depois do `SaveChangesAsync`:

```csharp
		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.ItemAsync(
			trilha, VerbosDeAuditoria.MenuItemMover, item,
			new { setorId = item.SetorId, nome = item.Nome, de = indice, para = indiceDoVizinho },
			cancellationToken).ConfigureAwait(false);
```

(`indice` e `indiceDoVizinho` já existem no método e não mudam com a troca de elementos da lista.)

`ExcluirItemMenuHandler` — depois do `ExcluirAsync`:

```csharp
		await AuditoriaDeMenu.ItemAsync(
			trilha, VerbosDeAuditoria.MenuItemExcluir, item,
			new { setorId = item.SetorId, nome = item.Nome, slug = item.Slug, rota = item.Rota },
			cancellationToken).ConfigureAwait(false);
```

`ReconciliarItensDeMenuHandler` — construtor `(SearchSetoresHandler searchSetores, IItemMenuRepository repository, ITrilhaDeAuditoria trilha)`; antes do `return alterados;`:

```csharp
		if (alterados > 0)
		{
			await AuditoriaDeMenu.ReconciliacaoAsync(trilha, alterados, cancellationToken).ConfigureAwait(false);
		}
```

Aproveite para trocar o comentário sobre a ordem Documentos/Avisos (ele cita a página de entrada `/setor/{slug}`, que deixa de existir):

```csharp
		// Documentos antes de Avisos, de propósito (diferente da criação, que é alfabética):
		// estes setores já existiam com Documentos como primeira entrada, e reconciliar não
		// deve mudar a ordem que as pessoas já conhecem. O admin reordena depois, se quiser.
```

O DI (`IntranetApplicationExtensions`) não muda: `ITrilhaDeAuditoria` já está registrado.

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~AuditoriaDeMenuTests|FullyQualifiedName~ItemMenuHandlerTests|FullyQualifiedName~ReconciliarItensDeMenu|FullyQualifiedName~SetorMenuAdministracaoTests"`
Expected: PASS (inclusive os de integração, que agora resolvem a trilha pelo DI).

- [ ] **Step 8: Commit**

```bash
git add src/Secco.Intranet.Application tests/Secco.Intranet.Tests/Unit
git commit -m "feat(menu): auditar criar, ativar/desativar, mover, excluir e reconciliar itens da arvore

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Slugs reservados

**Files:**
- Create: `src/Secco.Intranet.Application/Setores/SlugsReservados.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs` (classe `Setores`)
- Modify: `src/Secco.Intranet.Application/Setores/CreateSetorHandler.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/CreateSetorHandlerTests.cs`
- Create: `tests/Secco.Intranet.Tests/Integration/SlugsReservadosTests.cs`

**Interfaces:**
- Produces: `SlugsReservados.Contem(string slug) : bool`, `SlugsReservados.Todos : IReadOnlySet<string>`, `IntranetErrors.Setores.SlugReservado(string slug) : Error`.

- [ ] **Step 1: Testes que falham**

Em `CreateSetorHandlerTests.cs`, acrescente (usa os dublês privados que o arquivo já tem — `FakeRepository`, `FakeSetorAccessProvisioner`, `TrilhaFalsa` — e `Options`):

```csharp
	[Theory]
	[InlineData("mural")]
	[InlineData("Setores")]
	[InlineData(" acesso ")]
	[InlineData("setor")]
	public async Task Handle_ComSlugReservado_RecusaSemProvisionarRoles(string slug)
	{
		var repository = new FakeRepository();
		var provisioner = new FakeSetorAccessProvisioner();
		var handler = new CreateSetorHandler(repository, Options, provisioner, new TrilhaFalsa(), new ItemMenuRepositorioFalso());

		var resultado = await handler.HandleAsync(new CreateSetorCommand("Qualquer", slug));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Code.Should().Be("Intranet.Setor.SlugReservado");
		provisioner.LastSlug.Should().BeNull("a recusa vem antes de criar Role no SecureGate");
		repository.Added.Should().BeEmpty();
	}
```

`tests/Secco.Intranet.Tests/Integration/SlugsReservadosTests.cs`:

```csharp
using AwesomeAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Setores;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O setor mora na raiz (/{slug}/…). Todo endpoint cujo primeiro segmento é fixo — rota de
/// atributo, rota convencional, health check — precisa estar reservado, senão um setor com esse
/// slug ficaria inalcançável. Um controller novo que esqueça de reservar o nome quebra aqui.
/// </summary>
public class SlugsReservadosTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>
{
	[Fact]
	public void TodoPrimeiroSegmentoFixo_EstaReservado()
	{
		var fontes = factory.Services.GetServices<EndpointDataSource>();

		var segmentos = fontes
			.SelectMany(fonte => fonte.Endpoints)
			.OfType<RouteEndpoint>()
			.Select(endpoint => PrimeiroSegmentoFixo(endpoint.RoutePattern))
			.OfType<string>()
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		segmentos.Should().NotBeEmpty("o app tem rotas fixas — lista vazia quer dizer que a varredura quebrou");
		segmentos.Should().Contain(s => s.Equals("setores", StringComparison.OrdinalIgnoreCase));
		segmentos.Where(s => !SlugsReservados.Contem(s)).Should().BeEmpty(
			"cada um destes caminhos colidiria com um setor de mesmo slug");
	}

	private static string? PrimeiroSegmentoFixo(RoutePattern padrao)
	{
		if (padrao.PathSegments.Count == 0)
		{
			return null;
		}

		var parte = padrao.PathSegments[0].Parts[0];

		return parte switch
		{
			RoutePatternLiteralPart literal => literal.Content,
			// Rota convencional: {controller} vem preenchido pelo endpoint (RequiredValues).
			RoutePatternParameterPart parametro
				when padrao.RequiredValues.TryGetValue(parametro.Name, out var valor) && valor is string texto
				=> texto,
			_ => null,
		};
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~SlugsReservados|FullyQualifiedName~CreateSetorHandlerTests"`
Expected: não compila — `SlugsReservados` não existe.

- [ ] **Step 3: A lista**

`src/Secco.Intranet.Application/Setores/SlugsReservados.cs`:

```csharp
namespace Secco.Intranet.Application.Setores;

/// <summary>
/// Primeiros segmentos de URL que um setor não pode usar como slug: o setor mora na raiz
/// (<c>/{slug}/…</c>) e as rotas fixas do produto vencem a dele. <c>SlugsReservadosTests</c>
/// confere esta lista contra todos os endpoints — controller novo precisa entrar aqui.
/// </summary>
public static class SlugsReservados
{
	/// <summary>Todos os slugs reservados, sem diferenciar caixa.</summary>
	public static IReadOnlySet<string> Todos { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		// Controllers (rota de atributo e convencional).
		"acesso", "conta", "diretorio", "documentos", "home", "inventario", "mural", "publicacoes", "setores",
		// Prefixo antigo da página do setor: reservado para nunca virar um setor que confunda links velhos.
		"setor",
		// Infraestrutura e arquivos estáticos.
		"health", "_content", "css", "js", "lib", "img", "favicon.ico",
	};

	/// <summary>Se o slug (já aparado) é reservado.</summary>
	/// <param name="slug">Slug candidato.</param>
	public static bool Contem(string? slug) => slug is not null && Todos.Contains(slug.Trim());
}
```

Se o teste estrutural apontar um segmento que falta (ex.: `error`, `openapi`), acrescente-o ao grupo certo — a lista de cima é o que a leitura do código mostrou, o teste é a fonte da verdade.

- [ ] **Step 4: O erro e a recusa**

Em `IntranetErrors.Setores`, depois de `SlugAlreadyExists`:

```csharp
		/// <summary>Slug que colide com uma rota do produto (o setor mora na raiz da URL).</summary>
		public static Error SlugReservado(string slug) =>
			Error.Validation("Intranet.Setor.SlugReservado", $"O slug '{slug}' é usado pelo próprio sistema. Escolha outro.");
```

Em `CreateSetorHandler.HandleAsync`, logo depois do `if (string.IsNullOrWhiteSpace(command.Slug))` (antes do ícone e de qualquer provisionamento):

```csharp
		// O setor mora na raiz da URL; um slug igual a uma rota do produto ficaria inalcançável.
		if (SlugsReservados.Contem(command.Slug))
		{
			return IntranetErrors.Setores.SlugReservado(command.Slug.Trim());
		}
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~SlugsReservados|FullyQualifiedName~CreateSetorHandlerTests"`
Expected: PASS. (Os slugs do seeder de DEV — `infraestrutura`, `recursos-humanos`, `financeiro`, `diretoria` — não são reservados, então o seeder não muda.)

- [ ] **Step 6: Commit**

```bash
git add src/Secco.Intranet.Application tests/Secco.Intranet.Tests
git commit -m "feat(setores): recusar slug que colide com rota do produto

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Página do setor na raiz, sem abas

**Files:**
- Modify: `src/Secco.Intranet.Application/Menu/ResolverCaminhoDeMenuHandler.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/SetorController.cs`
- Modify: `src/Secco.Intranet.Web/Models/Documentos/SetorDocumentosViewModel.cs`, `src/Secco.Intranet.Web/Models/Publicacoes/SetorAvisosViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Views/Setor/Documentos.cshtml`, `Avisos.cshtml`
- Modify: `src/Secco.Intranet.Web/Views/Setores/Menu.cshtml` (texto de apoio)
- Delete: `src/Secco.Intranet.Web/Views/Setor/_AbasDoSetor.cshtml`, `SemItens.cshtml`, `SemConteudo.cshtml`, `src/Secco.Intranet.Web/Models/ItemMenuAbaDto.cs`
- Modify (tests): `Unit/ResolverCaminhoDeMenuHandlerTests.cs`, `Integration/SetorMenuRotaTests.cs`, `Integration/SetorMenuAdministracaoTests.cs`, `Integration/ReconciliarItensDeMenuTests.cs`, `Integration/DocumentoFluxoTests.cs`, `Integration/PublicacaoFluxoTests.cs`, `Unit/CriarItemMenuHandlerTests.cs` (comentário)

**Interfaces:**
- Produces: `ResultadoDaResolucao(ItemMenuDto No, IReadOnlyList<ItemMenuDto> Ancestrais, IReadOnlyList<string> CaminhoCompleto)` — `Ancestrais` = nós entre a raiz (exclusive) e o nó (exclusive), de cima para baixo. URLs do setor: GET `/{slug}/{**caminho}`, POST `/{slug}/documentos`, `/{slug}/documentos/{id}/arquivar`, `/{slug}/avisos`, `/{slug}/avisos/{id}/arquivar`.
- Nota: nesta task o menu ainda aponta para `/setor/{slug}` (link morto por um commit). A Task 5 troca o menu inteiro.

- [ ] **Step 1: Testes do resolver**

Em `Unit/ResolverCaminhoDeMenuHandlerTests.cs`, **apague** `FolhaDoNivel1_IrmaosSaoAsAbasDoNivel_EmOrdem`, `ItemDesativado_NaoAparaceNaListaDeIrmaos`, `CaminhoVazio_PrimeiroFilhoAtivoEAvisos_PorSerOPrimeiroEmOrdem`, `NoFolha_PrimeiroFilhoAtivoENulo` e `NoComTodosOsFilhosDesativados_PrimeiroFilhoAtivoENulo`, e acrescente:

```csharp
	[Fact]
	public async Task Neto_DevolveOsAncestraisSemARaiz()
	{
		var (repo, setorId, raiz, _, _) = Cenario();
		var relatorios = new ItemMenu(setorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 2);
		var vendas = new ItemMenu(setorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/v", null, 0);
		repo.Itens.AddRange([relatorios, vendas]);

		var resultado = await new ResolverCaminhoDeMenuHandler(repo).HandleAsync(setorId, ["relatorios", "vendas"]);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.No.Id.Should().Be(vendas.Id);
		resultado.Value.Ancestrais.Select(a => a.Slug).Should().Equal("relatorios");
		resultado.Value.CaminhoCompleto.Should().Equal("relatorios", "vendas");
	}

	[Fact]
	public async Task FilhoDaRaiz_NaoTemAncestrais()
	{
		var (repo, setorId, _, avisos, _) = Cenario();

		var resultado = await new ResolverCaminhoDeMenuHandler(repo).HandleAsync(setorId, ["avisos"]);

		resultado.Value.No.Id.Should().Be(avisos.Id);
		resultado.Value.Ancestrais.Should().BeEmpty();
	}

	[Fact]
	public async Task AncestralDesativado_Falha()
	{
		var (repo, setorId, raiz, _, _) = Cenario();
		var relatorios = new ItemMenu(setorId, raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado, null, null, 2);
		relatorios.Desativar();
		repo.Itens.AddRange([relatorios, new ItemMenu(setorId, relatorios.Id, "Vendas", "vendas", TipoDeItemMenu.Personalizado, "/v", null, 0)]);

		var resultado = await new ResolverCaminhoDeMenuHandler(repo).HandleAsync(setorId, ["relatorios", "vendas"]);

		resultado.IsFailure.Should().BeTrue();
	}
```

- [ ] **Step 2: Testes de rota (reescrever `SetorMenuRotaTests`)**

Mantenha o cabeçalho, `CriarCliente`, `TokenAsync`, `CriarSetorAsync` e `DesativarAsync`. Troque o `<summary>` da classe por:

```csharp
/// <summary>
/// A página do setor mora na raiz: /{setor}/{item}/…, sem prefixo, sem abas e sem
/// redirecionamento — quem navega pela árvore é o menu principal. Cada teste cria o próprio
/// setor com slug sufixado por GUID — não existe setor fixo na fixture.
/// </summary>
```

Apague todos os `[Fact]` e coloque:

```csharp
	[Fact]
	public async Task Documentos_AbreNaRaiz_SemAbas()
	{
		var slug = await CriarSetorAsync();

		var html = await CriarCliente().GetStringAsync($"/{slug}/documentos");

		html.Should().Contain($"action=\"/{slug}/documentos\"", "o formulário de publicação posta na rota nova");
		// A ausência das abas é garantida pela remoção da partial (grep do Step 9): checar
		// href aqui quebraria na Task 5, quando o próprio menu passa a ter o link de Avisos.
	}

	[Fact]
	public async Task RaizDoSetor_Da404_PorqueSoAgrupa()
	{
		var slug = await CriarSetorAsync();

		(await CriarCliente().GetAsync($"/{slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task PrefixoAntigo_Da404()
	{
		var slug = await CriarSetorAsync();

		(await CriarCliente().GetAsync($"/setor/{slug}/documentos")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task ItemDesativado_Da404()
	{
		var slug = await CriarSetorAsync();
		await DesativarAsync(slug, TipoDeItemMenu.Documentos);

		(await CriarCliente().GetAsync($"/{slug}/documentos")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task SegmentoSemMatchNenhum_Da404()
	{
		var slug = await CriarSetorAsync();

		(await CriarCliente().GetAsync($"/{slug}/caminho-que-nao-existe")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Theory]
	[InlineData("/nao-existe-setor-assim")]
	[InlineData("/nao-existe/nem/isto")]
	[InlineData("/favicon-inexistente.ico")]
	public async Task CaminhoDesconhecidoNaRaiz_Da404_NaoErro(string caminho) =>
		(await CriarCliente().GetAsync(caminho)).StatusCode.Should().Be(HttpStatusCode.NotFound);

	[Theory]
	[InlineData("/Setores")]
	[InlineData("/Acesso")]
	[InlineData("/diretorio")]
	[InlineData("/health/live")]
	public async Task RotaFixa_ContinuaVencendoOSetor(string caminho)
	{
		var resposta = await CriarCliente("intranet-admin").GetAsync(caminho);

		resposta.StatusCode.Should().NotBe(HttpStatusCode.NotFound, $"{caminho} é rota do produto, não setor");
	}

	[Fact]
	public async Task Documentos_TrazOTrilhoDoSetorNoCabecalho()
	{
		var slug = await CriarSetorAsync();

		var html = await CriarCliente().GetStringAsync($"/{slug}/documentos");

		html.Should().Contain($"Setor {slug}", "o subtítulo mostra de que setor é a página");
	}

	[Fact]
	public async Task RecursoDesligado_RecusaPost_MesmoComTokenValido()
	{
		var slug = await CriarSetorAsync();
		await DesativarAsync(slug, TipoDeItemMenu.Avisos);
		var client = CriarCliente();
		// O token antifalsificação não é por ação: o da página de Documentos (ainda ligada) vale.
		var token = await TokenAsync(client, $"/{slug}/documentos");

		var resposta = await client.PostAsync($"/{slug}/avisos", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Form.Titulo", "Não deveria entrar"),
			new KeyValuePair<string, string>("Form.Corpo", "Avisos está desligado neste setor."),
			new KeyValuePair<string, string>("Form.Tipo", "0"),
			new KeyValuePair<string, string>("Form.Visibilidade", "1"),
			new KeyValuePair<string, string>("Form.Prioridade", "0"),
			new KeyValuePair<string, string>("Form.PublicadoEm", DateTime.Now.ToString("yyyy-MM-ddTHH:mm")),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound, "desligar o recurso fecha a escrita também");
	}
```

`/diretorio` exige nível de acesso; se ele responder 403/redirect com a role `intranet-admin`, a asserção `NotBe(404)` continua valendo — o que se prova é que não caiu no setor.

- [ ] **Step 3: Demais testes com a URL antiga**

```bash
cd tests/Secco.Intranet.Tests/Integration
sed -i 's#/setor/{slug}#/{slug}#g' DocumentoFluxoTests.cs PublicacaoFluxoTests.cs SetorMenuAdministracaoTests.cs ReconciliarItensDeMenuTests.cs
```

Depois, ajustes à mão:

`SetorMenuAdministracaoTests.CriarItemPersonalizado_PeloFormulario_AparecenaPaginaDoSetor` — troque o bloco entre `// Sem rota, o item personalizado…` e `// Excluir é irreversível…` por:

```csharp
		// Sem rota e sem filhos, o item só agrupa: não há página para ele.
		(await client.GetAsync($"/{slug}/relatorios")).StatusCode.Should().Be(HttpStatusCode.NotFound);
```

`ReconciliarItensDeMenuTests.SetorAntigo_SemArvore_PassaAAbrirDepoisDeReconciliar` — a primeira asserção vira `GetAsync($"/{slug}/documentos")` com 404, e o final vira:

```csharp
		var pagina = await client.GetAsync($"/{slug}/documentos");
		pagina.StatusCode.Should().Be(HttpStatusCode.OK, "setor antigo reconciliado ganha Documentos de volta");
```

`Unit/CriarItemMenuHandlerTests.cs:165` — comentário `(/setor/x/{slug})` vira `(/x/{slug})`.

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~ResolverCaminhoDeMenu|FullyQualifiedName~SetorMenu|FullyQualifiedName~Reconciliar|FullyQualifiedName~DocumentoFluxo|FullyQualifiedName~PublicacaoFluxo"`
Expected: não compila (`Ancestrais` não existe).

- [ ] **Step 5: Resolver devolve ancestrais**

Em `ResolverCaminhoDeMenuHandler.cs`, troque o record e o corpo de `HandleAsync` depois da checagem da raiz:

```csharp
/// <summary>Resultado de resolver um caminho na árvore de um setor.</summary>
/// <param name="No">O nó resolvido.</param>
/// <param name="Ancestrais">Nós entre a raiz (exclusive) e o nó (exclusive), de cima para baixo — o trilho da página.</param>
/// <param name="CaminhoCompleto">Os slugs percorridos até o nó.</param>
public sealed record ResultadoDaResolucao(ItemMenuDto No, IReadOnlyList<ItemMenuDto> Ancestrais, IReadOnlyList<string> CaminhoCompleto);
```

```csharp
		var atual = raiz;
		var ancestrais = new List<ItemMenuDto>();

		foreach (var segmento in caminho)
		{
			var proximo = todos.FirstOrDefault(item =>
				item.ParentId == atual.Id && item.Ativo && string.Equals(item.Slug, segmento, StringComparison.Ordinal));

			if (proximo is null)
			{
				return Result.Failure<ResultadoDaResolucao>(IntranetErrors.Menu.NotFound);
			}

			if (atual.Tipo != TipoDeItemMenu.Setor)
			{
				ancestrais.Add(ItemMenuDto.FromEntity(atual));
			}

			atual = proximo;
		}

		return new ResultadoDaResolucao(ItemMenuDto.FromEntity(atual), ancestrais, caminho);
```

`CaminhoDoTipoAsync` não muda.

- [ ] **Step 6: ViewModels sem abas, com título e trilho**

`SetorDocumentosViewModel` — troque o último parâmetro `IReadOnlyList<ItemMenuAbaDto> Abas` por `string Titulo, string Trilho`, e o XML doc `<param name="Abas">` por:

```csharp
/// <param name="Titulo">Nome do item da árvore que abriu a página.</param>
/// <param name="Trilho">Setor e ancestrais do item ("Financeiro › Relatórios"), para o subtítulo.</param>
```

Mesma troca em `SetorAvisosViewModel`. Apague `src/Secco.Intranet.Web/Models/ItemMenuAbaDto.cs`.

- [ ] **Step 7: Controller**

Em `SetorController.cs`:

1. `[Route("setor/{slug}")]` → `[Route("{slug}")]`.
2. Resumo da classe:

```csharp
/// <summary>
/// Página de um item da árvore de menu de um setor, em <c>/{setor}/{item}/…</c> na raiz da URL —
/// as rotas fixas do produto vencem por precedência, e <c>SlugsReservados</c> impede setor com o
/// nome delas. O menu principal é quem navega a árvore; aqui só se resolve o nó pelo tipo.
/// Separada de <c>SetoresController</c>, que é a administração do cadastro.
/// </summary>
```

3. Em `Resolver`, apague o bloco `if (resolvido.Value.PrimeiroFilhoAtivo is { } primeiroFilho) { … }` e troque o `switch` por:

```csharp
		return no.Tipo switch
		{
			TipoDeItemMenu.Documentos => View(
				"Documentos",
				await MontarDocumentosAsync(setor.Value, resolvido.Value, new DocumentoFormViewModel(), cancellationToken).ConfigureAwait(false)),
			TipoDeItemMenu.Avisos => View(
				"Avisos",
				await MontarAvisosAsync(setor.Value, resolvido.Value, new PublicacaoFormViewModel(), cancellationToken).ConfigureAwait(false)),
			// Rota validada na criação do item: caminho local ou http(s) absoluto.
			TipoDeItemMenu.Personalizado when !string.IsNullOrWhiteSpace(no.Rota) => Redirect(no.Rota),
			// A raiz e o Personalizado sem rota só agrupam: o menu não oferece link para eles.
			_ => NotFound(),
		};
```

e atualize o `<summary>` de `Resolver` para "Qualquer nó da árvore do setor: renderiza pelo tipo; nó que só agrupa responde 404."

4. Apague `MontarAbas`. Acrescente:

```csharp
	/// <summary>"Financeiro › Relatórios": o setor e os ancestrais do nó, para o subtítulo da página.</summary>
	private static string Trilho(SetorDto setor, ResultadoDaResolucao resolucao) =>
		string.Join(" › ", [setor.Nome, .. resolucao.Ancestrais.Select(ancestral => ancestral.Nome)]);
```

5. Em `MontarDocumentosAsync` e `MontarAvisosAsync`, o último argumento `MontarAbas(setor.Slug, resolucao)` vira `resolucao.No.Nome, Trilho(setor, resolucao)`.

- [ ] **Step 8: Views**

`Views/Setor/Documentos.cshtml` — topo:

```cshtml
@{
    ViewData["Title"] = $"{Model.Titulo} · {Model.Setor.Nome}";

    var cabecalho = new PageHeaderModel(Model.Titulo, Model.Trilho, Model.Setor.Slug);
```

e apague a linha `<partial name="_AbasDoSetor" model="Model.Abas" />`. Mesma coisa em `Avisos.cshtml` (o `ViewData["Title"]` dele também vira `$"{Model.Titulo} · {Model.Setor.Nome}"`).

Apague `Views/Setor/_AbasDoSetor.cshtml`, `SemItens.cshtml` e `SemConteudo.cshtml`.

`Views/Setores/Menu.cshtml` linha 9 — o texto de apoio vira:

```cshtml
        "Itens do menu deste setor. Documentos e Avisos só desativam; itens personalizados também se excluem.",
```

- [ ] **Step 9: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~ResolverCaminhoDeMenu|FullyQualifiedName~SetorMenu|FullyQualifiedName~Reconciliar|FullyQualifiedName~DocumentoFluxo|FullyQualifiedName~PublicacaoFluxo|FullyQualifiedName~SetoresAutorizacao"`
Expected: PASS.

Run: `grep -rn "/setor/\|ItemMenuAbaDto\|_AbasDoSetor\|SemItens\|SemConteudo\|PrimeiroFilhoAtivo\|Irmaos" src tests --include=*.cs --include=*.cshtml`
Expected: só `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs` (Task 5), `tests/.../Unit/NavegacaoETemaTests.cs` (Task 5) e `tests/.../Integration/MenuVisibilidadeDeSetorTests.cs` (Task 5).

- [ ] **Step 10: Commit**

```bash
git add -A src tests
git commit -m "feat(menu): pagina do item em /{setor}/{item} na raiz, sem abas nem redirecionamento

Agrupador (raiz do setor, personalizado sem rota) responde 404; o
subtitulo da pagina mostra o trilho do setor e dos ancestrais.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Carregar as árvores de vários setores numa consulta

**Files:**
- Modify: `src/Secco.Intranet.Application/Menu/IItemMenuRepository.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Repositories/ItemMenuRepository.cs`
- Modify: `tests/Secco.Intranet.Tests/Support/ItemMenuRepositorioFalso.cs`
- Create: `src/Secco.Intranet.Application/Menu/ListarArvoresDosSetoresHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/ItemMenuRepositoryTests.cs`, create `tests/Secco.Intranet.Tests/Unit/ListarArvoresDosSetoresHandlerTests.cs`

**Interfaces:**
- Produces: `IItemMenuRepository.ListarPorSetoresAsync(IReadOnlyCollection<Guid> setorIds, CancellationToken) : Task<IReadOnlyList<ItemMenu>>`; `ListarArvoresDosSetoresHandler.HandleAsync(IReadOnlyCollection<Guid> setorIds, CancellationToken) : Task<IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>>` — chave = SetorId; só itens ativos; setor sem item ativo não aparece no dicionário.

- [ ] **Step 1: Testes que falham**

Em `ItemMenuRepositoryTests.cs`:

```csharp
	[Fact]
	public async Task ListarPorSetores_TrazSoOsSetoresPedidos()
	{
		var (escopoA, repositorio, setorA) = await CriarAsync();
		await using var _ = escopoA;
		var (escopoB, _, setorB) = await CriarAsync();
		await using var __ = escopoB;
		var (escopoC, _, setorC) = await CriarAsync();
		await using var ___ = escopoC;

		foreach (var setorId in new[] { setorA, setorB, setorC })
		{
			await repositorio.AddAsync(new ItemMenu(setorId, null, "X", "x", TipoDeItemMenu.Setor, null, null, 0));
		}

		var lista = await repositorio.ListarPorSetoresAsync([setorA, setorB]);

		lista.Select(item => item.SetorId).Should().BeEquivalentTo([setorA, setorB]);
	}

	[Fact]
	public async Task ListarPorSetores_ListaVazia_NaoConsulta()
	{
		var (escopo, repositorio, _) = await CriarAsync();
		await using var _ = escopo;

		(await repositorio.ListarPorSetoresAsync([])).Should().BeEmpty();
	}
```

`tests/Secco.Intranet.Tests/Unit/ListarArvoresDosSetoresHandlerTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ListarArvoresDosSetoresHandlerTests
{
	[Fact]
	public async Task AgrupaPorSetor_SoComItensAtivos()
	{
		var repo = new ItemMenuRepositorioFalso();
		var financeiro = Guid.NewGuid();
		var ti = Guid.NewGuid();
		var raizF = new ItemMenu(financeiro, null, "Financeiro", "financeiro", TipoDeItemMenu.Setor, null, null, 0);
		var docsF = new ItemMenu(financeiro, raizF.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		var avisosF = new ItemMenu(financeiro, raizF.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 1);
		avisosF.Desativar();
		var raizT = new ItemMenu(ti, null, "TI", "ti", TipoDeItemMenu.Setor, null, null, 0);
		repo.Itens.AddRange([raizF, docsF, avisosF, raizT]);

		var arvores = await new ListarArvoresDosSetoresHandler(repo).HandleAsync([financeiro, ti]);

		arvores[financeiro].Select(item => item.Id).Should().BeEquivalentTo([raizF.Id, docsF.Id]);
		arvores[ti].Should().ContainSingle(item => item.Id == raizT.Id);
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~ItemMenuRepositoryTests|FullyQualifiedName~ListarArvoresDosSetores"`
Expected: não compila.

- [ ] **Step 3: Porta, implementação e dublê**

`IItemMenuRepository`, depois de `ListarPorSetorAsync`:

```csharp
	/// <summary>As árvores de vários setores numa consulta só — o menu principal carrega todas por página.</summary>
	/// <param name="setorIds">Setores cujas árvores trazer; vazio devolve vazio sem consultar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<ItemMenu>> ListarPorSetoresAsync(IReadOnlyCollection<Guid> setorIds, CancellationToken cancellationToken = default);
```

`ItemMenuRepository`:

```csharp
	public async Task<IReadOnlyList<ItemMenu>> ListarPorSetoresAsync(
		IReadOnlyCollection<Guid> setorIds, CancellationToken cancellationToken = default)
	{
		if (setorIds.Count == 0)
		{
			return [];
		}

		return await context.ItensMenu
			.AsNoTracking()
			.Where(item => setorIds.Contains(item.SetorId))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
	}
```

`ItemMenuRepositorioFalso`:

```csharp
	public Task<IReadOnlyList<ItemMenu>> ListarPorSetoresAsync(IReadOnlyCollection<Guid> setorIds, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<ItemMenu>>([.. Itens.Where(item => setorIds.Contains(item.SetorId))]);
```

- [ ] **Step 4: Handler**

`src/Secco.Intranet.Application/Menu/ListarArvoresDosSetoresHandler.cs`:

```csharp
namespace Secco.Intranet.Application.Menu;

/// <summary>
/// Itens ativos das árvores de vários setores, agrupados por setor — a matéria-prima do menu
/// principal. Inativos ficam de fora aqui; quem monta o menu ainda poda o filho de um inativo,
/// porque o filho pode estar ativo debaixo de um pai desligado.
/// </summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class ListarArvoresDosSetoresHandler(IItemMenuRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorIds">Setores visíveis para o usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>> HandleAsync(
		IReadOnlyCollection<Guid> setorIds, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(setorIds);

		var itens = await repository.ListarPorSetoresAsync(setorIds, cancellationToken).ConfigureAwait(false);

		return itens
			.Where(item => item.Ativo)
			.GroupBy(item => item.SetorId)
			.ToDictionary(
				grupo => grupo.Key,
				grupo => (IReadOnlyList<ItemMenuDto>)[.. grupo.Select(ItemMenuDto.FromEntity)]);
	}
}
```

Em `IntranetApplicationExtensions.cs`, ao lado de `ResolverCaminhoDeMenuHandler`: `services.AddScoped<ListarArvoresDosSetoresHandler>();`

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~ItemMenuRepositoryTests|FullyQualifiedName~ListarArvoresDosSetores"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(menu): carregar as arvores dos setores visiveis numa consulta so

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Árvore dos setores no menu principal (servidor e marcação)

**Files:**
- Modify: `src/Secco.Intranet.Web.Theming/Contracts/NavigationModel.cs`
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs`
- Modify: `src/Secco.Intranet.Web/ViewComponents/NavigationViewComponent.cs`
- Modify: `src/Secco.Intranet.Themes.Vertical/Themes/Vertical/Views/Shared/Components/Navigation/Default.cshtml`
- Modify: `src/Secco.Intranet.Themes.Horizontal/Themes/Horizontal/Views/Shared/Components/Navigation/Default.cshtml`
- Modify: `tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs`, `tests/Secco.Intranet.Tests/Integration/MenuVisibilidadeDeSetorTests.cs`
- Modify: `docs/specs/2026-09-30-item-menu-por-setor-design.md` (nota de revisão no topo), `docs/roadmap.md`

**Interfaces:**
- Consumes: `ListarArvoresDosSetoresHandler` (Task 4), rotas `/{slug}/…` (Task 3).
- Produces: `NavigationItemModel(string Texto, string? Icone, string? Url, bool Ativo = false, string? SetorSlug = null, IReadOnlyList<NavigationItemModel>? Filhos = null)`; `NavigationRequest(..., IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>? Arvores = null)`. Marcação: nó com filhos = `<li class="sc-nav__node">`; gatilho = elemento com `data-sc-submenu` e `aria-controls` apontando para `<ul class="sc-nav__sub" id="sc-nav-sub-N" data-nivel="N">`; nível 1 começa com `<li class="sc-nav__sub-title">` (nome do setor). Task 6 depende exatamente destes nomes.

- [ ] **Step 1: Testes da montagem (unidade)**

Em `NavegacaoETemaTests.cs`, apague `Build_NaPaginaDeUmSetor_MarcaAqueleItemComoAtivo` e acrescente (com `using Secco.Intranet.Application.Menu;` e `using Secco.Intranet.Domain.Menu;`):

```csharp
	private static ItemMenuDto No(Guid? pai, string nome, string slug, TipoDeItemMenu tipo, int ordem = 0, string? rota = null) =>
		new(Guid.NewGuid(), pai, nome, slug, tipo, rota, null, ordem, Ativo: true);

	private static (SetorDto Setor, Dictionary<Guid, IReadOnlyList<ItemMenuDto>> Arvores) Financeiro(params Func<ItemMenuDto, ItemMenuDto[]>[] filhosDaRaiz)
	{
		var setor = Setor("Financeiro", "financeiro");
		var raiz = No(null, "Financeiro", "financeiro", TipoDeItemMenu.Setor);
		var itens = new List<ItemMenuDto> { raiz };

		foreach (var filhos in filhosDaRaiz)
		{
			itens.AddRange(filhos(raiz));
		}

		return (setor, new Dictionary<Guid, IReadOnlyList<ItemMenuDto>> { [setor.Id] = itens });
	}

	private static NavigationItemModel SetorNoMenu(NavigationModel menu) =>
		menu.Grupos.Single(grupo => grupo.Titulo == "Setores").Itens.Single();

	[Fact]
	public void Build_SetorViraAgrupadorSemUrl_ComOsFilhosEmOrdem()
	{
		var (setor, arvores) = Financeiro(raiz =>
		[
			No(raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, ordem: 1),
			No(raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, ordem: 0),
		]);

		var item = SetorNoMenu(IntranetNavigation.Build(new NavigationRequest([setor], "/", false, false, false, arvores)));

		item.Url.Should().BeNull("o setor só agrupa");
		item.SetorSlug.Should().Be("financeiro");
		item.Filhos!.Select(filho => filho.Url).Should().Equal("/financeiro/avisos", "/financeiro/documentos");
	}

	[Fact]
	public void Build_AtivoMarcaOCaminhoInteiro()
	{
		ItemMenuDto relatorios = null!;
		var (setor, arvores) = Financeiro(raiz =>
		{
			relatorios = No(raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado);
			return [relatorios, No(relatorios.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos)];
		});

		var item = SetorNoMenu(IntranetNavigation.Build(
			new NavigationRequest([setor], "/financeiro/relatorios/documentos", false, false, false, arvores)));

		item.Ativo.Should().BeTrue();
		item.Filhos!.Single().Ativo.Should().BeTrue();
		item.Filhos!.Single().Filhos!.Single().Ativo.Should().BeTrue();
	}

	[Fact]
	public void Build_PersonalizadoComRota_ApontaParaARota()
	{
		var (setor, arvores) = Financeiro(raiz => [No(raiz.Id, "Painel BI", "painel-bi", TipoDeItemMenu.Personalizado, rota: "https://bi.exemplo/x")]);

		var item = SetorNoMenu(IntranetNavigation.Build(new NavigationRequest([setor], "/", false, false, false, arvores)));

		item.Filhos!.Single().Url.Should().Be("https://bi.exemplo/x");
	}

	[Fact]
	public void Build_CadeiaDeAgrupadoresVazios_SomeJuntoComOSetor()
	{
		var (setor, arvores) = Financeiro(raiz =>
		{
			var relatorios = No(raiz.Id, "Relatórios", "relatorios", TipoDeItemMenu.Personalizado);
			return [relatorios, No(relatorios.Id, "Mensais", "mensais", TipoDeItemMenu.Personalizado)];
		});

		var menu = IntranetNavigation.Build(new NavigationRequest([setor], "/", false, false, false, arvores));

		menu.Grupos.Single(grupo => grupo.Titulo == "Setores").Itens.Should().BeEmpty();
	}

	[Fact]
	public void Build_FilhoCujoPaiNaoVeio_FicaDeFora()
	{
		// O handler já tira inativos; um filho ativo de pai inativo chega órfão e não pode subir de nível.
		var (setor, arvores) = Financeiro(raiz =>
			[No(raiz.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos), No(Guid.NewGuid(), "Órfão", "orfao", TipoDeItemMenu.Documentos)]);

		var item = SetorNoMenu(IntranetNavigation.Build(new NavigationRequest([setor], "/", false, false, false, arvores)));

		item.Filhos!.Select(filho => filho.Texto).Should().Equal("Avisos");
	}

	[Fact]
	public void Build_SetorSemArvore_NaoAparece()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([Setor("Financeiro", "financeiro")], "/", false, false, false));

		menu.Grupos.Single(grupo => grupo.Titulo == "Setores").Itens.Should().BeEmpty();
	}
```

- [ ] **Step 2: Teste de marcação (integração), nos dois temas**

Reescreva o `[Fact]` de `MenuVisibilidadeDeSetorTests` e acrescente o do tema Horizontal:

```csharp
	[Fact]
	public async Task ModoAberto_MostraOSetorComoAgrupador_ComOsItensDaArvore()
	{
		var slug = $"menu-{Guid.NewGuid():N}"[..20];
		await CriarSetorAsync(slug);

		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		// Sem X-Test-Roles: nenhuma role — se o menu ainda filtrasse por role, não veria nada.

		var html = await client.GetStringAsync("/");

		AssertarArvore(html, slug);
	}

	[Fact]
	public async Task TemaHorizontal_RenderizaAMesmaArvore()
	{
		var slug = $"menu-{Guid.NewGuid():N}"[..20];
		await CriarSetorAsync(slug);

		var client = factory
			.WithWebHostBuilder(builder => builder.UseSetting("Intranet:Theme:Nome", "Horizontal"))
			.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		var html = await client.GetStringAsync("/");

		html.Should().Contain("sc-topnav", "garante que o tema trocou de fato");
		AssertarArvore(html, slug);
	}

	private static void AssertarArvore(string html, string slug)
	{
		html.Should().Contain($"href=\"/{slug}/avisos\"").And.Contain($"href=\"/{slug}/documentos\"");
		html.Should().NotContain($"href=\"/{slug}\"", "o setor não é link");
		Regex.IsMatch(html, $"<button[^>]*data-sc-submenu[^>]*aria-expanded=\"false\"[^>]*>(?:(?!</button>).)*Setor {slug}",
			RegexOptions.Singleline).Should().BeTrue("o setor é um botão expansível com o nome dele");
	}
```

(Acrescente `using System.Text.RegularExpressions;` e `using Microsoft.AspNetCore.Hosting;`.) Se `UseSetting` não trocar o tema (`sc-topnav` ausente), veja como `ThemeOptions` é lido em `Program.cs`/extensões do tema e use o mesmo caminho de configuração — o teste precisa provar a troca, não pular.

Mantenha o comentário longo do fim da classe sobre o bypass de permissão: "setor sem leitura não aparece" continua coberto só no nível de `IPermissoesDeSetor` (o filtro do componente não muda nesta task).

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~NavegacaoETemaTests|FullyQualifiedName~MenuVisibilidadeDeSetor"`
Expected: não compila (`NavigationRequest` sem `Arvores`, `Filhos` inexistente).

- [ ] **Step 4: Contrato**

`NavigationModel.cs`:

```csharp
/// <summary>Um item do menu, já resolvido para o usuário.</summary>
/// <param name="Texto">Rótulo.</param>
/// <param name="Icone">Classe do Bootstrap Icons; nulo = sem ícone.</param>
/// <param name="Url">Destino; nulo quando o item só agrupa os filhos (não é link).</param>
/// <param name="Ativo">Se é a página atual <b>ou um ancestral dela</b> — o tema destaca o caminho todo e põe <c>aria-current</c> só no item ativo sem filho ativo.</param>
/// <param name="SetorSlug">Slug do setor, no item de nível 0 de um setor; define o matiz.</param>
/// <param name="Filhos">Subitens, já na ordem; nulo ou vazio = folha.</param>
public sealed record NavigationItemModel(
	string Texto,
	string? Icone,
	string? Url,
	bool Ativo = false,
	string? SetorSlug = null,
	IReadOnlyList<NavigationItemModel>? Filhos = null);
```

(Mantenha `NavigationGroupModel` e `NavigationModel` como estão; ajuste só o XML doc existente se ele citar a forma antiga.)

- [ ] **Step 5: Montagem**

`IntranetNavigation.cs` — `NavigationRequest` ganha o parâmetro final:

```csharp
/// <param name="Arvores">Itens ativos da árvore de cada setor, por SetorId (de <c>ListarArvoresDosSetoresHandler</c>); setor sem entrada não aparece.</param>
public sealed record NavigationRequest(
	IReadOnlyList<SetorDto> Setores,
	string CaminhoAtual,
	bool MostrarAdministracao,
	bool MostrarDiretorio,
	bool MostrarInventario,
	IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>? Arvores = null);
```

Atualize o `<summary>` da classe (a frase "A tabela autorecursiva ItemMenu substitui esta fonte na Fase 2" vira "A seção Setores vem da árvore ItemMenu de cada setor; os itens fixos continuam aqui."). Troque o bloco `var setores = …` por:

```csharp
		var setores = request.Setores
			.Select(setor => SetorNoMenu(setor, request.Arvores, caminho))
			.OfType<NavigationItemModel>()
			.ToList();
```

e acrescente os métodos:

```csharp
	/// <summary>O setor como nó de nível 0, sem link; nulo se nada debaixo dele leva a algum lugar.</summary>
	private static NavigationItemModel? SetorNoMenu(
		SetorDto setor, IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>? arvores, string caminho)
	{
		if (arvores is null || !arvores.TryGetValue(setor.Id, out var itens))
		{
			return null;
		}

		var raiz = itens.FirstOrDefault(item => item.Tipo == TipoDeItemMenu.Setor);

		if (raiz is null)
		{
			return null;
		}

		var filhos = Filhos(raiz.Id, $"/{setor.Slug}", itens, caminho);

		return filhos.Count == 0
			? null
			: new NavigationItemModel(setor.Nome, setor.Icone, Url: null, filhos.Any(filho => filho.Ativo), setor.Slug, filhos);
	}

	/// <summary>
	/// Filhos de um nó, em ordem, já podados: nó sem destino e sem filhos visíveis some (de baixo
	/// para cima, então uma cadeia de agrupadores vazios some inteira).
	/// </summary>
	private static List<NavigationItemModel> Filhos(
		Guid paiId, string caminhoDoPai, IReadOnlyList<ItemMenuDto> itens, string caminhoAtual)
	{
		var resultado = new List<NavigationItemModel>();

		foreach (var item in itens.Where(item => item.ParentId == paiId).OrderBy(item => item.Ordem))
		{
			var caminhoDoItem = $"{caminhoDoPai}/{item.Slug}";
			var netos = Filhos(item.Id, caminhoDoItem, itens, caminhoAtual);
			var url = Destino(item, caminhoDoItem);

			if (url is null && netos.Count == 0)
			{
				continue;
			}

			var ativo = Corresponde(caminhoAtual, caminhoDoItem)
				|| (item.Rota is { } rota && rota.StartsWith('/') && Corresponde(caminhoAtual, rota))
				|| netos.Any(neto => neto.Ativo);

			resultado.Add(new NavigationItemModel(item.Nome, item.Icone, url, ativo, Filhos: netos.Count == 0 ? null : netos));
		}

		return resultado;
	}

	/// <summary>Documentos/Avisos abrem no caminho da árvore; Personalizado vai direto para a rota, se tiver.</summary>
	private static string? Destino(ItemMenuDto item, string caminhoDoItem) => item.Tipo switch
	{
		TipoDeItemMenu.Documentos or TipoDeItemMenu.Avisos => caminhoDoItem,
		TipoDeItemMenu.Personalizado when !string.IsNullOrWhiteSpace(item.Rota) => item.Rota,
		_ => null,
	};
```

Usings novos: `Secco.Intranet.Application.Menu`, `Secco.Intranet.Domain.Menu`.

- [ ] **Step 6: Componente carrega as árvores**

Em `NavigationViewComponent.InvokeAsync`, carregue setores e árvores juntos:

```csharp
		var setores = await CarregarSetoresAsync(HttpContext.User, autenticacaoAtiva).ConfigureAwait(false);

		var request = new NavigationRequest(
			setores,
			HttpContext.Request.Path.Value ?? "/",
			MostrarAdministracao: …,   // como está
			MostrarDiretorio: …,       // como está
			MostrarInventario: …,      // como está
			Arvores: await CarregarArvoresAsync(setores).ConfigureAwait(false));
```

e o método, ao lado de `CarregarSetoresAsync`:

```csharp
	private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>?> CarregarArvoresAsync(IReadOnlyList<SetorDto> setores)
	{
		if (setores.Count == 0)
		{
			return null;
		}

		try
		{
			return await serviceProvider
				.GetRequiredService<ListarArvoresDosSetoresHandler>()
				.HandleAsync([.. setores.Select(setor => setor.Id)], HttpContext.RequestAborted)
				.ConfigureAwait(false);
		}
#pragma warning disable CA1031 // Mesmo motivo de CarregarSetoresAsync: o menu está no layout.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			logger.LogWarning(exception, "Não foi possível carregar a árvore dos setores do menu; exibindo apenas os itens fixos.");
			return null;
		}
	}
```

(`setores.Count == 0` cobre também o caso sem tenant, em que `CarregarSetoresAsync` já devolve vazio.)

- [ ] **Step 7: Marcação — tema Vertical**

`Themes/Vertical/Views/Shared/Components/Navigation/Default.cshtml` inteiro:

```cshtml
@model NavigationModel
@{
    var sequencia = 0;

    // Um nó e, recursivamente, os filhos. Com Url é link; sem Url só agrupa e vira botão — o
    // leitor de tela anuncia "expansível", não "link". Ativo marca o caminho todo; aria-current
    // fica só na página atual (ativo sem filho ativo). Sem JS os submenus aparecem abertos e
    // recuados; o theme.js é quem os transforma em painéis flutuantes.
    void Renderizar(NavigationItemModel item, int nivel)
    {
        var filhos = item.Filhos ?? [];
        var temFilhos = filhos.Count > 0;
        var atual = item.Ativo && !filhos.Any(filho => filho.Ativo);
        var temSetor = nivel == 0 && !string.IsNullOrWhiteSpace(item.SetorSlug);
        string? estilo = temSetor ? $"--sc-setor-hue:{SetorHue.From(item.SetorSlug)}" : null;
        string? painel = temFilhos ? $"sc-nav-sub-{++sequencia}" : null;
        var classe = item.Ativo ? "sc-nav__item is-active" : "sc-nav__item";

        <li class="@(temFilhos ? "sc-nav__node" : null)">
            @if (item.Url is null)
            {
                @* O title cobre a barra recolhida, onde o rótulo some e sobra só o ícone. *@
                <button class="@classe sc-nav__item--grupo" type="button" style="@estilo" title="@item.Texto"
                        data-sc-submenu aria-controls="@painel" aria-expanded="false" aria-haspopup="true">
                    @if (!string.IsNullOrWhiteSpace(item.Icone))
                    {
                        <i class="bi @item.Icone sc-nav__icon @(temSetor ? "sc-nav__icon--setor" : null)" aria-hidden="true"></i>
                    }
                    <span class="sc-nav__label">@item.Texto</span>
                    <i class="bi bi-chevron-right sc-nav__chevron" aria-hidden="true"></i>
                </button>
            }
            else
            {
                <div class="sc-nav__row">
                    <a class="@classe" href="@item.Url" style="@estilo" title="@item.Texto"
                       aria-current="@(atual ? "page" : null)">
                        @if (!string.IsNullOrWhiteSpace(item.Icone))
                        {
                            <i class="bi @item.Icone sc-nav__icon @(temSetor ? "sc-nav__icon--setor" : null)" aria-hidden="true"></i>
                        }
                        <span class="sc-nav__label">@item.Texto</span>
                    </a>
                    @if (temFilhos)
                    {
                        <button class="sc-nav__toggle" type="button" data-sc-submenu aria-controls="@painel"
                                aria-expanded="false" aria-label="Abrir @item.Texto">
                            <i class="bi bi-chevron-right" aria-hidden="true"></i>
                        </button>
                    }
                </div>
            }
            @if (temFilhos)
            {
                <ul class="sc-nav__sub" id="@painel" data-nivel="@(nivel + 1)">
                    @if (nivel == 0)
                    {
                        <li class="sc-nav__sub-title" aria-hidden="true">@item.Texto</li>
                    }
                    @foreach (var filho in filhos)
                    {
                        Renderizar(filho, nivel + 1);
                    }
                </ul>
            }
        </li>
    }
}
<ul class="sc-nav">
    @foreach (var grupo in Model.Grupos)
    {
        if (grupo.Itens.Count == 0)
        {
            continue;
        }

        if (grupo.Titulo is not null)
        {
            <li class="sc-nav__group-title" aria-hidden="true">@grupo.Titulo</li>
        }

        foreach (var item in grupo.Itens)
        {
            Renderizar(item, 0);
        }
    }
</ul>
```

- [ ] **Step 8: Marcação — tema Horizontal**

Mesmo arquivo do Vertical, com duas diferenças: no laço de grupos, antes do título, mantenha `<li class="sc-nav__divider" aria-hidden="true"></li>`; e tire os `title="@item.Texto"` (o Horizontal sempre mostra o rótulo). O comentário do `title` sai junto.

- [ ] **Step 9: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~NavegacaoETemaTests|FullyQualifiedName~MenuVisibilidadeDeSetor|FullyQualifiedName~WebSmoke|FullyQualifiedName~FeedbackDeAcao"`
Expected: PASS.

Run: `grep -rn "/setor/" src tests --include=*.cs --include=*.cshtml`
Expected: nenhuma linha.

- [ ] **Step 10: Documentação**

No topo de `docs/specs/2026-09-30-item-menu-por-setor-design.md`, logo abaixo de `**Estado:**`:

```markdown
**Revisada por:** [2026-10-01-menu-principal-arvore-dos-setores-design.md](2026-10-01-menu-principal-arvore-dos-setores-design.md) — a navegação (página do setor com abas, redirecionamento ao primeiro filho, prefixo `/setor/`) foi substituída; dados e administração continuam valendo.
```

Em `docs/roadmap.md`, no item `ItemMenu` (hoje "além da página do setor"), registre que a seção Setores do menu principal já vem da árvore e que o que resta é transformar os itens fixos (Mural, Diretório, Inventário, Administração) em nós, se um dia fizer sentido.

- [ ] **Step 11: Commit**

```bash
git add src tests docs
git commit -m "feat(menu): secao Setores do menu principal vem da arvore ItemMenu

Setor vira no de nivel 0 sem link; itens aninhados em listas, com
botao expansivel para agrupador. Contrato NavigationItemModel ganha
Filhos e aceita Url/Icone nulos.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Submenus flutuantes nos dois temas

**Files:**
- Modify: `src/Secco.Intranet.Themes.Vertical/Themes/Vertical/Views/Shared/_Layout.cshtml`, `src/Secco.Intranet.Themes.Horizontal/Themes/Horizontal/Views/Shared/_Layout.cshtml` (classe `sc-js`)
- Modify: `src/Secco.Intranet.Themes.Vertical/wwwroot/scss/_components.scss`, `src/Secco.Intranet.Themes.Horizontal/wwwroot/scss/_components.scss`
- Modify: `src/Secco.Intranet.Themes.Vertical/wwwroot/js/theme.js`, `src/Secco.Intranet.Themes.Horizontal/wwwroot/js/theme.js`
- Regenerate: `wwwroot/css/theme.css` dos dois temas (`npm run build`)
- Modify: `docs/temas.md`

**Interfaces:**
- Consumes: marcação da Task 5 (`.sc-nav__node`, `[data-sc-submenu][aria-controls]`, `.sc-nav__sub[data-nivel]`, `.sc-nav__sub-title`, `.sc-nav__row`, `.sc-nav__toggle`, `.sc-nav__item--grupo`, `.sc-nav__chevron`).

Não há teste automatizado de JS neste projeto; a verificação é no navegador (Step 6). Os testes .NET continuam como rede para a marcação.

- [ ] **Step 1: Classe `sc-js` antes da primeira pintura**

Nos dois `_Layout.cshtml`, no `<script>` inline do `<head>`, como **primeira** linha dentro da função (antes do `try`):

```js
            // Com JS os submenus viram painéis; sem ele ficam abertos e recuados (ver theme.js).
            document.documentElement.classList.add('sc-js');
```

- [ ] **Step 2: Estilo — Vertical**

Em `src/Secco.Intranet.Themes.Vertical/wwwroot/scss/_components.scss`, depois do bloco `.sc-nav { … }` (antes de `.sc-sidebar__footer`):

```scss
// --- Arvore dos setores ------------------------------------------------------
// Sem JS os submenus ficam abertos e recuados. Com JS (html.sc-js) viram paineis flutuantes,
// com position: fixed — a barra lateral rola (overflow-y), e um painel absoluto seria cortado.
.sc-nav__row {
  display: flex;
  align-items: center;

  > .sc-nav__item {
    flex: 1 1 auto;
    min-width: 0;
  }
}

.sc-nav__item--grupo {
  width: 100%;
  border: 0;
  background-color: transparent;
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.sc-nav__chevron {
  margin-left: auto;
  font-size: .75rem;
}

.sc-nav__toggle {
  flex: 0 0 auto;
  padding: .375rem;
  border: 0;
  border-radius: .5rem;
  background-color: transparent;
  color: var(--sc-text-muted);
  font-size: .75rem;
  cursor: pointer;

  &:hover {
    background-color: var(--sc-surface-muted);
    color: var(--sc-text);
  }
}

.sc-nav__sub {
  display: flex;
  flex-direction: column;
  gap: .125rem;
  margin: 0;
  padding: 0 0 0 1rem;
  list-style: none;
}

.sc-nav__sub-title {
  display: none;
}

html.sc-js .sc-nav__sub {
  display: none;
}

html.sc-js .sc-nav__sub.is-open {
  display: flex;
}

@media (min-width: 768px) {
  html.sc-js .sc-nav__sub.is-open {
    position: fixed;
    z-index: 1050;
    min-width: 14rem;
    max-height: calc(100vh - 1rem);
    overflow-y: auto;
    padding: .375rem;
    border: 1px solid var(--sc-border);
    border-radius: .75rem;
    background-color: var(--sc-surface);
    box-shadow: var(--sc-shadow);
  }

  // Na barra recolhida o painel do setor e o unico lugar onde o nome dele aparece.
  html[data-sc-sidebar="collapsed"] .sc-nav__sub-title {
    display: block;
    padding: .375rem .625rem .25rem;
    color: var(--sc-text-muted);
    font-family: var(--bs-font-monospace);
    font-size: .6875rem;
    letter-spacing: .08em;
    text-transform: uppercase;
  }

  // O trilho de icones esconde rotulo e centraliza — dentro do painel nada disso vale.
  html[data-sc-sidebar="collapsed"] .sc-nav__sub .sc-nav__label {
    display: inline;
  }

  html[data-sc-sidebar="collapsed"] .sc-nav__sub .sc-nav__item {
    justify-content: flex-start;
  }

  html[data-sc-sidebar="collapsed"] .sc-nav > li > .sc-nav__item--grupo .sc-nav__chevron {
    display: none;
  }
}
```

O bloco `@media (prefers-reduced-motion: reduce)` do topo do arquivo já zera transições globalmente; os painéis não têm animação própria, então não há o que acrescentar.

- [ ] **Step 3: Estilo — Horizontal**

Em `src/Secco.Intranet.Themes.Horizontal/wwwroot/scss/_components.scss`, depois do bloco `.sc-nav { … }`:

```scss
// --- Arvore dos setores ------------------------------------------------------
// Sem JS os submenus ficam abertos e recuados. Com JS (html.sc-js) viram paineis flutuantes,
// com position: fixed — a barra rola na horizontal (overflow-x), e um painel absoluto seria cortado.
.sc-nav__row {
  display: flex;
  align-items: center;
}

.sc-nav__item--grupo {
  border: 0;
  background-color: transparent;
  font: inherit;
  cursor: pointer;
}

.sc-nav__chevron {
  font-size: .75rem;
}

// Na barra o nivel 0 abre para baixo: a seta aponta para baixo so ali.
.sc-nav > li > .sc-nav__item--grupo .sc-nav__chevron {
  transform: rotate(90deg);
}

.sc-nav__toggle {
  flex: 0 0 auto;
  padding: .375rem;
  border: 0;
  border-radius: var(--bs-border-radius);
  background-color: transparent;
  color: var(--sc-text-muted);
  font-size: .75rem;
  cursor: pointer;

  &:hover {
    background-color: var(--sc-surface-muted);
    color: var(--sc-text);
  }
}

.sc-nav__sub {
  display: flex;
  flex-direction: column;
  gap: .125rem;
  margin: 0;
  padding: 0 0 0 1rem;
  list-style: none;

  .sc-nav__item--grupo {
    width: 100%;
    text-align: left;

    .sc-nav__chevron {
      margin-left: auto;
    }
  }
}

.sc-nav__sub-title {
  display: none;
}

html.sc-js .sc-nav__sub {
  display: none;
}

html.sc-js .sc-nav__sub.is-open {
  display: flex;
}

@media (min-width: 768px) {
  html.sc-js .sc-nav__sub.is-open {
    position: fixed;
    z-index: 1050;
    min-width: 14rem;
    max-height: calc(100vh - 1rem);
    overflow-y: auto;
    padding: .375rem;
    border: 1px solid var(--sc-border);
    border-radius: var(--bs-border-radius-lg);
    background-color: var(--sc-surface);
    box-shadow: var(--sc-shadow);
  }
}
```

- [ ] **Step 4: Comportamento — os dois `theme.js`**

Nos dois arquivos, acrescente antes do bloco do `data-confirmar` (última seção do IIFE). A **única** diferença entre os temas é a constante `ABRE_ABAIXO`: `false` no Vertical, `true` no Horizontal.

```js
  // Submenus da arvore dos setores. O servidor entrega a arvore inteira em listas aninhadas;
  // sem este script ela aparece aberta e recuada. Aqui cada lista vira painel flutuante com
  // position: fixed — o menu mora num conteiner com rolagem, e um painel absoluto seria
  // cortado. Em tela estreita (gaveta/painel do celular) o submenu abre recuado no lugar.
  var ABRE_ABAIXO = false; // Horizontal: true (o nivel 1 sai debaixo da barra)
  var menu = document.querySelector('.sc-nav');

  if (menu) {
    var largo = window.matchMedia('(min-width: 768px)');
    var comMouse = window.matchMedia('(hover: hover)');
    var ATRASO_AO_SAIR = 300;

    var painelDe = function (gatilho) {
      return document.getElementById(gatilho.getAttribute('aria-controls'));
    };

    var gatilhoDe = function (no) {
      return no.querySelector(':scope > [data-sc-submenu], :scope > .sc-nav__row > [data-sc-submenu]');
    };

    var posicionar = function (gatilho, painel) {
      painel.style.top = '';
      painel.style.left = '';

      if (!largo.matches) {
        return;
      }

      var base = gatilho.closest('li').getBoundingClientRect();
      var largura = painel.offsetWidth;
      var altura = painel.offsetHeight;
      var abaixo = ABRE_ABAIXO && painel.getAttribute('data-nivel') === '1';
      var topo = abaixo ? base.bottom : base.top;
      var esquerda = abaixo ? base.left : base.right;

      // Sem espaco, abre para o lado oposto.
      if (esquerda + largura > window.innerWidth) {
        esquerda = Math.max(0, (abaixo ? base.right : base.left) - largura);
      }

      if (topo + altura > window.innerHeight) {
        topo = Math.max(0, window.innerHeight - altura);
      }

      painel.style.top = topo + 'px';
      painel.style.left = esquerda + 'px';
    };

    var fechar = function (gatilho) {
      var painel = painelDe(gatilho);

      if (!painel) {
        return;
      }

      Array.prototype.forEach.call(painel.querySelectorAll('[data-sc-submenu][aria-expanded="true"]'), fechar);
      gatilho.setAttribute('aria-expanded', 'false');
      painel.classList.remove('is-open');
    };

    var fecharIrmaos = function (gatilho) {
      var meu = gatilho.closest('li');

      Array.prototype.forEach.call(meu.parentElement.children, function (irmao) {
        var outro = irmao !== meu ? gatilhoDe(irmao) : null;

        if (outro && outro.getAttribute('aria-expanded') === 'true') {
          fechar(outro);
        }
      });
    };

    var fecharTudo = function () {
      Array.prototype.forEach.call(menu.querySelectorAll(':scope > li > [data-sc-submenu], :scope > li > .sc-nav__row > [data-sc-submenu]'), fechar);
    };

    var abrir = function (gatilho) {
      var painel = painelDe(gatilho);

      if (!painel) {
        return;
      }

      fecharIrmaos(gatilho);
      gatilho.setAttribute('aria-expanded', 'true');
      painel.classList.add('is-open');
      posicionar(gatilho, painel);
    };

    var itensDe = function (painel) {
      return Array.prototype.filter.call(
        painel.querySelectorAll(':scope > li > a, :scope > li > button, :scope > li > .sc-nav__row > a'),
        function (elemento) { return elemento.offsetParent !== null; });
    };

    // Clique, toque e Enter/Espaco (o botao converte em click) abrem e fecham.
    menu.addEventListener('click', function (evento) {
      var gatilho = evento.target.closest('[data-sc-submenu]');

      if (!gatilho || !menu.contains(gatilho)) {
        return;
      }

      if (gatilho.getAttribute('aria-expanded') === 'true') {
        fechar(gatilho);

        return;
      }

      abrir(gatilho);

      // detail 0 = veio do teclado: o foco desce para o primeiro item.
      if (evento.detail === 0) {
        var primeiro = itensDe(painelDe(gatilho))[0];

        if (primeiro) {
          primeiro.focus();
        }
      }
    });

    // Mouse: abre ao passar; fecha com atraso, para atravessar na diagonal ate o painel. O
    // painel e descendente do <li> no DOM, entao entrar nele nao conta como sair do <li>.
    Array.prototype.forEach.call(menu.querySelectorAll('.sc-nav__node'), function (no) {
      var gatilho = gatilhoDe(no);
      var espera = null;

      no.addEventListener('mouseenter', function () {
        if (!comMouse.matches || !largo.matches || !gatilho) {
          return;
        }

        window.clearTimeout(espera);
        abrir(gatilho);
      });

      no.addEventListener('mouseleave', function () {
        if (!comMouse.matches || !largo.matches || !gatilho) {
          return;
        }

        espera = window.setTimeout(function () { fechar(gatilho); }, ATRASO_AO_SAIR);
      });
    });

    // Teclado: setas navegam no painel, seta para a direita abre, Esc/seta para a esquerda
    // fecham e devolvem o foco a quem abriu.
    menu.addEventListener('keydown', function (evento) {
      var alvo = evento.target;
      var painel = alvo.closest('.sc-nav__sub.is-open');

      if (evento.key === 'ArrowRight' && alvo.hasAttribute('data-sc-submenu') && alvo.getAttribute('aria-expanded') !== 'true') {
        evento.preventDefault();
        abrir(alvo);
        var primeiro = itensDe(painelDe(alvo))[0];

        if (primeiro) {
          primeiro.focus();
        }

        return;
      }

      if (!painel) {
        return;
      }

      var gatilho = menu.querySelector('[aria-controls="' + painel.id + '"]');

      if (evento.key === 'Escape' || evento.key === 'ArrowLeft') {
        evento.preventDefault();
        fechar(gatilho);
        gatilho.focus();

        return;
      }

      if (evento.key === 'ArrowDown' || evento.key === 'ArrowUp') {
        evento.preventDefault();
        var itens = itensDe(painel);
        var posicao = itens.indexOf(alvo.closest('a, button'));
        var proximo = evento.key === 'ArrowDown' ? posicao + 1 : posicao - 1;

        itens[(proximo + itens.length) % itens.length].focus();
      }
    });

    // Fora do menu, ou mudou a geometria: fecha (posicao fixa calculada ficaria errada).
    document.addEventListener('click', function (evento) {
      if (!menu.contains(evento.target)) {
        fecharTudo();
      }
    });

    window.addEventListener('resize', fecharTudo);
    window.addEventListener('scroll', function (evento) {
      // Rolar dentro de um painel aberto nao o fecha.
      if (!(evento.target instanceof Element && evento.target.closest('.sc-nav__sub.is-open'))) {
        fecharTudo();
      }
    }, true);
  }
```

No `theme.js` do Horizontal, troque `var ABRE_ABAIXO = false; // Horizontal: true (...)` por `var ABRE_ABAIXO = true; // o nivel 1 sai debaixo da barra`. No do Vertical, o comentário vira `// Vertical: o nivel 1 abre a direita da barra`.

Ponto de atenção para o Vertical: o painel da gaveta (`data-sc-sidebar-open`) foca `barra.querySelector('a, button')` ao abrir — continua certo, porque o primeiro elemento focável segue sendo o item Mural.

- [ ] **Step 5: Compilar o CSS**

```bash
npm --prefix src/Secco.Intranet.Themes.Vertical run build
npm --prefix src/Secco.Intranet.Themes.Horizontal run build
git status --short src/Secco.Intranet.Themes.*/wwwroot/css
```

Expected: os dois `theme.css` modificados.

- [ ] **Step 6: Verificar no navegador**

Suba a aplicação (`dotnet run --project src/Secco.Intranet.Web`, SQL de DEV no ar) e, com Playwright, em cada tema (troque `Intranet:Theme:Nome` e reinicie):

1. Desktop 1280×800: passar o mouse em "Financeiro" abre o painel com Avisos/Documentos ao lado (Vertical) / abaixo (Horizontal); sair e voltar em menos de 300 ms mantém aberto; clicar em Documentos navega para `/financeiro/documentos` e o setor fica destacado.
2. Crie em `/Setores/Menu/{id}` um "Relatórios" (Personalizado, sem rota) com um filho "Vendas" (rota `/`): o painel de Relatórios abre ao lado do painel do setor.
3. Teclado: Tab até o setor, Enter abre e foca o primeiro item; ↓/↑ navegam; → em "Relatórios" abre o nível 2; Esc fecha e devolve o foco.
4. Vertical com a barra recolhida: o painel do setor mostra o nome dele no topo e os rótulos dos itens.
5. 375×800: na gaveta/painel, tocar no setor abre os itens recuados embaixo dele, sem flutuar.
6. JS desligado (Playwright `javaScriptEnabled: false`): a árvore aparece toda aberta e recuada, e os links funcionam.
7. Modo escuro: painel com fundo e borda do tema, contraste legível.

Tire screenshot dos casos 1, 4 e 5 em cada tema e anexe ao relato da task. Qualquer divergência volta para o Step 2–4 antes do commit.

- [ ] **Step 7: Documentar o contrato**

Em `docs/temas.md`, depois do parágrafo sobre `id="conteudo"`:

```markdown
O menu (`NavigationModel`) é uma árvore: `NavigationItemModel.Filhos` traz os subitens, e
`Url` nulo quer dizer que o item só agrupa — é o caso de todo setor, que é o nível 0 da seção
"Setores" e não tem página própria. `Ativo` marca o caminho inteiro até a página atual; use
`aria-current="page"` só no item ativo sem filho ativo. Os dois temas publicados renderizam a
árvore inteira no servidor, em listas aninhadas (agrupador = `<button aria-expanded>`), e o
`theme.js` transforma cada lista em painel flutuante. Duas armadilhas que o tema precisa
resolver: o menu mora num contêiner com rolagem, então o painel precisa de `position: fixed`
com a posição calculada no script (um painel `absolute` é cortado); e sem script a árvore tem
de aparecer aberta, para tudo continuar alcançável.
```

- [ ] **Step 8: Suíte completa**

Run: `dotnet build` e `dotnet test tests/Secco.Intranet.Tests`
Expected: build sem warnings novos; todos os testes passam (os 11 skipped de antes continuam skipped).

- [ ] **Step 9: Commit**

```bash
git add src/Secco.Intranet.Themes.Vertical src/Secco.Intranet.Themes.Horizontal docs/temas.md
git commit -m "feat(tema): submenus flutuantes da arvore dos setores nos dois temas

Painel com position fixed calculado no theme.js (o menu rola e cortaria
um painel absoluto); hover com atraso, toque, teclado e aria-expanded.
Em tela estreita abre recuado; sem JS a arvore aparece aberta.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
