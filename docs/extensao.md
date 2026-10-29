# Acrescentando itens ao menu principal da Secco.Intranet

Quem adota a intranet pode colocar uma página própria no grupo principal do menu (onde ficam Mural e
Diretório) **sem editar o core**: basta um projeto externo, referenciado pelo `Secco.Intranet.Web`,
com uma classe que implementa `IItemDeMenuPrincipal`. A decisão que sustenta isso está na
[ADR-0011](adr/secco-intranet-adrs.md#adr-0011-ponto-de-extensão-do-menu-principal-em-projeto-próprio-seccointranetwebextensibility).

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

O projeto do contrato não depende de nenhum outro projeto do produto, só do BCL e de
`Microsoft.Extensions.DependencyInjection.Abstractions`.

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
  `diretorio` são reservadas aos itens fixos do core.
- **`Url`:** caminho local, começando com `/` e não com `//`. URL externa (`https://...`) não é
  aceita.
- **`Icone`:** classe do Bootstrap Icons (`bi-*`) ou `null` para item sem ícone.
- **`Ordem` e `Rotulo`:** `Ordem` é a posição padrão entre os extras; no empate, vale o `Rotulo`
  (ordinal, sem diferenciar caixa).
- **`Chave` duplicada:** vale a implementação de menor nome completo de tipo; a outra é ignorada.

Item inválido (`Chave` ou `Url` fora das regras, `Chave` duplicada, exceção ao criar o item ou no
gate) **fica fora do menu e da tela de ordenação, com um aviso no log**, em vez de derrubar a
aplicação: o menu está no layout de todas as telas, inclusive a de erro. Procure o aviso no log do
`Secco.Intranet.Web`, que nomeia o tipo e a regra violada. No modo aberto de DEV o gate não é
chamado e todos os itens aparecem, como os demais itens do menu.

O item fica ativo quando a rota atual corresponde ao caminho da `Url` (sem query string e sem `/`
final).

> **Cuidado com o slug de setor:** a primeira parte da `Url` não deve coincidir com o slug de um
> setor existente. A rota da sua RCL e a página do setor (`/{slug}/...`) disputariam o caminho.

## Quem decide a posição final

A `Ordem` do item só vale até o admin mexer. O `intranet-admin` reordena o grupo principal (Mural,
Diretório e todos os extras registrados) e a seção Setores em `/menu`, por tenant, e vale para todos
os usuários dele. A tela lista todos os extras válidos, sem avaliar o gate, e o botão "Restaurar
padrão" volta o grupo à ordem padrão. Um item novo, depois de uma reordenação, entra no fim do grupo.
Reordenar não muda quem vê o quê.

## O que não dá para fazer

Item com filhos, item em outro grupo (Setores ou Administração), item com URL externa e ordem por
usuário ficam fora do contrato. Mudar `IItemDeMenuPrincipal` é uma mudança incompatível para quem
estende: leia as notas de versão antes de atualizar.
