# Estendendo a Secco.Intranet sem editar o core

Quem adota a intranet acrescenta páginas próprias num projeto externo (uma RCL), referenciado pelo
`Secco.Intranet.Web`. O único projeto do produto que a RCL referencia é o
`Secco.Intranet.Web.Extensibility`, que oferece dois pontos de extensão (para pôr a página
[na árvore de um setor](#página-da-rcl-na-árvore-de-um-setor), nem isso é necessário):

- [**Itens no menu principal**](#passo-a-passo): a página aparece no grupo principal, onde ficam
  Mural e Diretório
  ([ADR-0011](adr/secco-intranet-adrs.md#adr-0011-ponto-de-extensão-do-menu-principal-em-projeto-próprio-seccointranetwebextensibility)).
- [**Exportação em Excel e PDF**](#exportando-excel-e-pdf): a página exporta a listagem dela, com a
  trilha de auditoria e o limite por pessoa do host
  ([ADR-0013](adr/secco-intranet-adrs.md#adr-0013-exportação-trilha-fail-closed-ao-lado-da-fail-open-e-o-extensibility-além-do-menu)).

## Itens no menu principal

Uma classe que implementa `IItemDeMenuPrincipal` põe a página no grupo principal do menu.

> **A regra:** o seu projeto diz *que* o item existe e *quem* o vê; o admin da instalação decide *onde*
> ele fica.

O item é uma folha (sem submenu), aponta para um caminho local e vive sempre no grupo principal. A
página de destino é uma página MVC comum da sua RCL: o MVC descobre os controllers dela por estar
referenciada.

## Passo a passo

### 1. Criar a RCL

Um projeto Razor Class Library com a sua página (controller e views), como qualquer outra RCL. Ele
**não** referencia o `Secco.Intranet.Web`: é o Web que referencia ele.

### 2. Referenciar o contrato

```xml
<ItemGroup>
	<ProjectReference Include="..\Secco.Intranet.Web.Extensibility\Secco.Intranet.Web.Extensibility.csproj" />
</ItemGroup>
```

O projeto do contrato não depende de nenhum outro projeto do produto, só do framework compartilhado do ASP.NET Core
(`Microsoft.AspNetCore.App`).

### 3. Implementar `IItemDeMenuPrincipal`

```csharp
using System.Security.Claims;
using Secco.Intranet.Web.Extensibility;

public sealed class ItemInstitucional : IItemDeMenuPrincipal
{
	public string Chave => "institucional";
	public string Rotulo => "Institucional";
	public string? Icone => "bi-building";
	public string Url => "/institucional";
	public int Ordem => 10;

	// Visível para qualquer usuário autenticado.
	public Task<bool> VisivelParaAsync(ClaimsPrincipal usuario, CancellationToken cancellationToken) =>
		Task.FromResult(usuario.Identity?.IsAuthenticated == true);
}
```

O item é instanciado a cada requisição, então o construtor pode receber serviços scoped. Para decidir
por permissão, injete o `IAuthorizationService`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Secco.Intranet.Web.Extensibility;

public sealed class ItemRelatorios(IAuthorizationService autorizacao) : IItemDeMenuPrincipal
{
	public string Chave => "relatorios";
	public string Rotulo => "Relatórios";
	public string? Icone => "bi-bar-chart";
	public string Url => "/relatorios";
	public int Ordem => 20;

	public async Task<bool> VisivelParaAsync(ClaimsPrincipal usuario, CancellationToken cancellationToken) =>
		(await autorizacao.AuthorizeAsync(usuario, "relatorios:read")).Succeeded;
}
```

`"relatorios:read"` é o nome de uma política de permissão registrada pela sua RCL (ou pelo produto).

Sobre papéis: o tipo de claim de papel é `SeccoClaims.Role` (`Secco.SharedKernel.Constants`). Em
produção o login OIDC o define como `RoleClaimType` (`IntranetAuthenticationExtensions.cs`), então
`usuario.IsInRole(...)` funciona. O middleware de papéis dos testes (`RolesDeTesteMiddleware`) não
faz isso. Se o código também precisa passar nos testes de integração do produto, prefira
`usuario.HasClaim(SeccoClaims.Role, "...")` ou o `IAuthorizationService`.

O gate só decide a visibilidade **no menu**. Proteja também a rota da sua página: esconder o item não
fecha o acesso direto à URL.

### 4. Referenciar a RCL no `Secco.Intranet.Web`

```xml
<ProjectReference Include="..\Minha.Rcl\Minha.Rcl.csproj" />
```

É só isso. O catálogo do core lê as application parts do MVC no primeiro uso e encontra todo tipo
público e concreto que implemente `IItemDeMenuPrincipal`. Não há nada para registrar no `Program.cs`.

Uma RCL sob `src/` entra na imagem sem editar o `Dockerfile`; fora de `src/`, precisa de `COPY`
próprio nele.

### 5. Alternativa: registro explícito

Se preferir não depender da descoberta pelas application parts (por exemplo, com o tipo num
assembly que não é uma application part), registre no DI:

```csharp
services.AddItemDeMenuPrincipal<ItemInstitucional>();
```

O tipo é registrado só para o catálogo; continua sendo instanciado a cada requisição. Um tipo
encontrado pelos dois caminhos aparece uma vez só.

## Regras do item

- **`Chave`:** de 1 a 64 caracteres, só `[a-z0-9-]`. É a identidade do item e o que o banco guarda
  para lembrar a posição escolhida pelo admin, então **não mude depois de publicada**. `mural` e
  `diretorio` são reservadas aos itens fixos do core, e o prefixo `biblioteca-` ao item de cada
  biblioteca de documentos (`biblioteca-{slug}`): item que comece por ele fica fora do menu, com
  aviso no log.
- **`Url`:** caminho local, começando com `/` e não com `//`. URL externa (`https://...`) não é
  aceita.
- **`Icone`:** classe do Bootstrap Icons (`bi-*`) ou `null` para item sem ícone.
- **`Rotulo`:** obrigatório; nulo, vazio ou só espaços deixa o item de fora.
- **`Ordem` e `Rotulo`:** `Ordem` é a posição padrão entre os extras; no empate, vale o `Rotulo`
  (ordinal, sem diferenciar caixa).
- **`Chave` duplicada:** vale a implementação de menor nome completo de tipo; a outra é ignorada.

Item inválido (`Chave`, `Rotulo` ou `Url` fora das regras, `Chave` duplicada, exceção ao criar o item ou no
gate) **fica fora do menu e da tela de ordenação, com um aviso no log**, em vez de derrubar a
aplicação: o menu está no layout de todas as telas, inclusive a de erro. Procure o aviso no log do
`Secco.Intranet.Web`, que nomeia o tipo e a regra violada. No modo aberto de DEV o gate não é
chamado e todos os itens aparecem, como os demais itens do menu.

O item fica ativo quando a rota atual corresponde ao caminho da `Url` (sem query string e sem `/`
final).

> **Slug de setor:** o setor mora na raiz (`/{slug}/...`), então o primeiro segmento fixo de toda
> rota registrada fica reservado automaticamente, inclusive as da sua RCL. Com `[HttpGet("/institucional")]`,
> ninguém consegue criar um setor com slug `institucional`, e você não edita nada no core para isso.
> Um setor que já existia com esse slug antes de a RCL entrar sai do menu, com um aviso no log.

## Quem decide a posição final

A `Ordem` do item só vale até o admin mexer. O `intranet-admin` reordena o grupo principal (Mural,
Diretório e todos os extras registrados) e a seção Setores em `/menu`, por tenant, e vale para todos
os usuários dele. A tela lista todos os extras válidos, sem avaliar o gate, e o botão "Restaurar
padrão" volta o grupo à ordem padrão. Um item novo, depois de uma reordenação, entra no fim do grupo.
Reordenar não muda quem vê o quê.

## O que não dá para fazer

Item com filhos, item em outro grupo (Setores ou Administração), item com URL externa e ordem por
usuário ficam fora do contrato. Mudar `IItemDeMenuPrincipal` é uma mudança incompatível para quem
estende: leia as notas de versão antes de atualizar. Para pôr a página dentro de um setor, veja a
próxima seção.

## Página da RCL na árvore de um setor

Quando a página pertence a um setor, ela entra no menu como um item **Personalizado** da árvore dele,
sem nenhum código de menu na RCL. O `intranet-admin` cria o item em **Setores › Menu do setor**,
informa a rota da página e, se quiser, um item sem rota acima dele para agrupar.

A coluna "Quem vê no menu" define o nível de acesso de cada item da árvore, exceto a raiz:

| Nível | Quem vê o item no menu |
|---|---|
| Quem lê o setor (padrão) | Quem tem `setor-{slug}:read` |
| Quem escreve no setor | Quem tem `setor-{slug}:write` |

O `intranet-admin` vê todos os itens, e no modo aberto de DEV todos aparecem. Um item restrito esconde
também os filhos, e um grupo sem rota some quando nenhum filho é visível.

> **O nível não substitui a autorização da página.** Ele só decide quem vê o link. Quem tem a URL ainda
> chega à página, então ela precisa checar a própria permissão.

```csharp
public sealed class InscritosController(IAuthorizationService autorizacao) : Controller
{
	[HttpGet("/relatorios/inscritos")]
	public async Task<IActionResult> Index()
	{
		// Mesma resposta de "não existe" para quem não pode, como as páginas do core.
		if (!(await autorizacao.AuthorizeAsync(User, "setor-comercial:write")).Succeeded)
		{
			return NotFound();
		}

		return View();
	}
}
```

Marcar Documentos, Avisos ou Vencimentos como "quem escreve no setor" também só tira o link do menu. As
páginas deles exigem apenas a leitura do setor: quem lê continua abrindo pela URL direta, e os avisos
continuam no Mural. Para que um conteúdo seja realmente restrito, ele precisa de uma página com
autorização própria. Quem tem só a escrita do setor vê um desses itens restritos, mas recebe 404 na
página, que exige leitura.

Use o [`IItemDeMenuPrincipal`](#itens-no-menu-principal) quando a página não pertence a um setor, e a
árvore quando pertence: o primeiro fica no grupo principal, sem filhos, e a visibilidade é decidida em
código; na árvore, quem decide é o admin da instalação.

## Exportando Excel e PDF

A RCL exporta a listagem dela em `.xlsx` ou PDF tabular pelo `Secco.SDK.Documents` (ADR-0058 da
plataforma). O host já registra tudo; a RCL **só injeta**:

| Serviço | Vem de | Para quê |
|---|---|---|
| `ISeccoDocumentExporter` | `Secco.SDK.Documents` | Gera o arquivo, com memória constante e as defesas do SDK |
| `ITrilhaDeExportacao` | `Secco.Intranet.Web.Extensibility` | Registra a exportação **antes** do envio e diz se o arquivo pode sair |
| `PoliticaDeExportacao.Nome` | `Secco.Intranet.Web.Extensibility` | Limite de exportações por pessoa, aplicado pelo nome |

> **A regra:** gerar → registrar na trilha → enviar. Se a trilha recusar, o documento é descartado
> e nada sai. Ao contrário da trilha das outras ações, esta é fail-closed.

### 1. Referências

```xml
<ItemGroup>
	<ProjectReference Include="..\Secco.Intranet.Web.Extensibility\Secco.Intranet.Web.Extensibility.csproj" />
	<PackageReference Include="Secco.SDK.Documents" />
</ItemGroup>
```

Use a mesma versão do `Secco.SDK.Documents` que o `Directory.Packages.props` da intranet fixa.

### 2. O formulário

A exportação é sempre `POST` com antiforgery, nunca `GET` nem link: uma página externa conseguiria
disparar um `GET` em nome de quem tem sessão (ADR-0058). Os filtros da tela vão em campos ocultos.
O tag helper do `<form method="post">` acrescenta o token sozinho, e não há script inline, então o
formulário passa na CSP.

```cshtml
<form method="post" asp-action="Exportar">
	<input type="hidden" name="de" value="@Model.De" />
	<input type="hidden" name="ate" value="@Model.Ate" />
	<button type="submit" name="formato" value="xlsx" class="btn btn-outline-secondary">Excel</button>
	<button type="submit" name="formato" value="pdf" class="btn btn-outline-secondary">PDF</button>
</form>
```

O tema ainda não tem parcial para esses botões: use as classes do tema ativo.

### 3. O controller

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Secco.Intranet.Web.Extensibility;
using Secco.SDK.Documents;

public sealed class RelatorioController(
	ISeccoDocumentExporter exportador,
	ITrilhaDeExportacao trilha,
	IAuthorizationService autorizacao) : Controller
{
	private const string Recurso = "relatorio-de-inscritos";

	// Chave do TempData que o tema mostra como erro (a mesma do core).
	private const string MensagemDeErro = "MensagemDeErro";

	[HttpPost("/relatorio/exportar")]
	[ValidateAntiForgeryToken]
	[EnableRateLimiting(PoliticaDeExportacao.Nome)]
	public async Task<IResult> Exportar(string formato, DateOnly? de, DateOnly? ate)
	{
		// Permissão da própria RCL, separada da de ver a listagem.
		if (!(await autorizacao.AuthorizeAsync(User, "relatorio:export")).Succeeded)
		{
			return Results.StatusCode(StatusCodes.Status403Forbidden);
		}

		var filtros = new List<ExportFilter>
		{
			new("De", de?.ToString("dd/MM/yyyy") ?? "—"),
			new("Até", ate?.ToString("dd/MM/yyyy") ?? "—"),
		};

		var gerado = await exportador.ExportAsync(
			new ExportRequest<LinhaDoRelatorio>(
				formato == "pdf" ? ExportFormat.Pdf : ExportFormat.Xlsx,
				"Relatório de inscritos",
				Recurso,
				Colunas(),
				BuscarLinhas(de, ate)) // IAsyncEnumerable, com os mesmos filtros da tela
			{
				Filters = filtros,
				IssuedBy = User.Identity?.Name,
				TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"),
			},
			HttpContext.RequestAborted);

		if (gerado.IsFailure)
		{
			// TooManyRows: o limite de linhas do formato. Peça para restringir o filtro.
			TempData[MensagemDeErro] = gerado.Error.Description;
			return Results.Redirect(Url.Action(nameof(Index), new { de, ate })!);
		}

		var documento = gerado.Value;

		try
		{
			var registrado = await trilha.RegistrarAsync(
				new ExportacaoParaAuditar(
					Recurso,
					formato,
					documento.RowCount,
					filtros.Select(f => new FiltroDeExportacao(f.Name, f.Value)).ToList()),
				HttpContext.RequestAborted);

			if (!registrado)
			{
				await documento.DisposeAsync();
				TempData[MensagemDeErro] = "Não foi possível exportar agora. Tente de novo em alguns minutos.";
				return Results.Redirect(Url.Action(nameof(Index), new { de, ate })!);
			}
		}
		catch
		{
			await documento.DisposeAsync();
			throw;
		}

		return documento.ToHttpResult();
	}
}
```

O `ToHttpResult()` envia com `Content-Disposition: attachment`, `Cache-Control: no-store` e o nome
do arquivo gerado no servidor, e descarta o documento ao terminar. A mensagem de retorno segue o
mesmo caminho das outras ações: `TempData` antes do redirect, mostrada pelo tema. As chaves são
`"MensagemDeErro"` para erro e `"Mensagem"` para confirmação, as mesmas que o core usa. A action
devolve `IResult`, porque é isso que o `ToHttpResult()` do SDK entrega; o MVC executa o `IResult`
normalmente, inclusive o `TempData` do redirect.

A permissão é verificada com o `IAuthorizationService`, e não com `[Authorize(Policy = ...)]`: no
modo aberto de DEV não há esquema de autenticação registrado, e o atributo falharia ao tentar o
desafio. É o mesmo padrão do gate do item de menu.

A ordem gerar → trilha → enviar, com o descarte na recusa e o `TempData` no redirect, está coberta
pelos testes de integração do produto, no `ExportacaoDeTesteController`
(`tests/Secco.Intranet.Tests/Integration/Exportacao`).

### Regras

- **Recurso:** chave fixa no código, de 1 a 64 caracteres, só `[a-z0-9-]`. É o que identifica o
  relatório na trilha (verbo `exportacao.gerar`, recurso `exportacao`, o seu recurso no id). Não mude
  depois de publicada, ou a busca na trilha se divide em duas.
- **Filtros:** os mesmos que o PDF mostra no cabeçalho. A trilha guarda até 200 caracteres de cada
  valor, porque filtro pode trazer dado pessoal. Nunca passe para a trilha o conteúdo exportado nem
  dado que não seja filtro.
- **Permissão:** a exportação tem permissão própria, separada da de ver a listagem (ADR-0058): baixar
  tudo de uma vez é um risco diferente de ver uma página. Esconder o botão não fecha a rota.
- **Limite por pessoa:** acima de `Intranet:Exportacao:PorPessoaPorMinuto` (padrão 10), a resposta é
  `429`. O contador é por instância.
- **Recurso, formato, linhas ou filtros inválidos** lançam `ArgumentException`: é erro de quem
  chama, não falha de infraestrutura.

### O que muda por ambiente

| Situação | O que a trilha faz |
|---|---|
| `Intranet:Auditoria:LogStreamUrl` preenchida | Registra no LogStream. Se ele recusar, estiver fora do ar ou não responder, **recusa** |
| Sem LogStream, em `Development` ou `Testing` | Aceita, com um log `Warning` de que a exportação não foi auditada |
| Sem LogStream, em qualquer outro ambiente | **Recusa** sempre |

Ou seja: fora de Development, exportar exige o LogStream configurado, ao contrário do resto da
auditoria, que só deixa de registrar. Os testes de integração do produto rodam em `Testing` e
exportam sem LogStream; para testar a recusa, atribua uma trilha que falha a
`IntranetWebFactory.TrilhaObrigatoria`.

Mudar `ITrilhaDeExportacao` ou os records dela é mudança incompatível para quem estende, como no
menu.
