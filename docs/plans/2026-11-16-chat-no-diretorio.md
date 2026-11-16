# Link de chat no Diretório — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** com `Intranet:Chat:Provedor=Teams`, cada pessoa do Organograma, da página da pessoa e da lista do Diretório ganha um botão que abre a conversa no Teams.

**Architecture:** uma peça única no Web (`ILinkDeChat`) monta o endereço a partir do e-mail; as telas só perguntam a ela. O cartão do Organograma recebe o endereço pelo contrato do tema (`NoOrganogramaModel.ChatUrl`). Nenhuma chamada externa.

**Tech Stack:** .NET 10, ASP.NET Core MVC, Razor nos dois temas (RCL), Sass, xUnit, AwesomeAssertions, Playwright (opt-in com `SECCO_UI_TESTS=1`).

**Spec:** [docs/specs/2026-11-16-chat-no-diretorio-design.md](../specs/2026-11-16-chat-no-diretorio-design.md)

## Global Constraints

- Endereço exato: `https://teams.microsoft.com/l/chat/0/0?users=<e-mail escapado com Uri.EscapeDataString>`, mais `&tenantId=<guid em minúsculas, formato D>` quando `Intranet:Chat:TenantId` está preenchido.
- Chaves: `Intranet:Chat:Provedor` (vazio ou `Teams`, sem diferenciar maiúsculas) e `Intranet:Chat:TenantId` (GUID). Valor inválido em qualquer das duas derruba a subida com mensagem que cita a chave, nunca o valor de um segredo.
- Sem botão para a própria pessoa logada nem para pessoa sem e-mail.
- Todo link do chat: `target="_blank" rel="noopener noreferrer"`, `title` e rótulo acessível "Conversar com {nome} no Teams".
- Sem `style=""`, `<script>` inline nem `onclick` (CSP em Enforce).
- O que é do tema muda igual em `Themes.Vertical` e `Themes.Horizontal`; CSS novo exige `npm run build` nos dois (o CI falha se o `theme.css` versionado não bater).
- Testes com AwesomeAssertions; dados fictícios (`ana@exemplo.test`).
- Commits direto na `main`; conferir `git branch --show-current` antes de cada commit.

## Review Focus

1. **E-mail com caracteres que quebram a URL** (`ana+rh@exemplo.test`, `joão@exemplo.test`, `a&b@exemplo.test`): o `users=` sai escapado e o `&tenantId=` continua sendo um parâmetro separado. Teste na Tarefa 1.
2. **`Provedor` com espaços ou caixa diferente** (`" teams "`, `TEAMS`): vale como Teams; `Slack` ou `xyz` derruba a subida. Teste na Tarefa 1.
3. **Usuário sem `sub`** (modo aberto de DEV): ninguém é "a própria pessoa", e todos com e-mail ganham botão, sem erro. Teste na Tarefa 1.
4. **Botão dentro do `<summary>` do cartão:** clicar no chat não pode recolher nem expandir a equipe. Teste de tela na Tarefa 2.
5. **Pessoa cujo e-mail é só espaços ou não tem `@`:** sem botão, sem erro. Teste na Tarefa 1.

---

### Task 1: `ILinkDeChat` e a configuração

**Files:**
- Create: `src/Secco.Intranet.Web/Diretorio/ChatOptions.cs`, `ILinkDeChat.cs`, `LinkDeChat.cs`, `ChatServiceCollectionExtensions.cs`
- Modify: `src/Secco.Intranet.Web/Program.cs` (uma linha: `builder.Services.AddIntranetChat(builder.Configuration);`)
- Test: `tests/Secco.Intranet.Tests/Unit/LinkDeChatTests.cs`, `tests/Secco.Intranet.Tests/Integration/ChatConfiguracaoTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace Secco.Intranet.Web.Diretorio;

public enum ProvedorDeChat { Nenhum = 0, Teams = 1 }

public sealed class ChatOptions
{
    public const string SectionKey = "Intranet:Chat";
    public ProvedorDeChat Provedor { get; set; }        // lido de texto; vazio => Nenhum
    public Guid? TenantId { get; set; }
}

public interface ILinkDeChat
{
    /// Endereço da conversa com a pessoa, ou null (provedor desligado, sem e-mail válido, ou é quem está vendo).
    string? Para(string? email, Guid usuarioId, ClaimsPrincipal quemVe);

    /// Nome do provedor para o rótulo ("Teams"), ou null com o chat desligado.
    string? NomeDoProvedor { get; }
}

public static IServiceCollection AddIntranetChat(this IServiceCollection services, IConfiguration configuration);
```

`quemVe` é comparado por `AcessoAoDiretorio.UsuarioId(quemVe)`. A leitura da configuração segue o molde de `IntranetSecurityHeadersExtensions` (texto lido da configuração, `InvalidOperationException` citando a chave); a validação roda na subida (`ValidateOnStart` ou equivalente), não no primeiro uso.

- [ ] **Step 1: Testes que falham** em `LinkDeChatTests`:

```csharp
Para_ProvedorDesligado_Nulo
Para_Teams_MontaOEndereco                    // "ana@exemplo.test" => "https://teams.microsoft.com/l/chat/0/0?users=ana%40exemplo.test"
Para_Teams_ComTenant_AcrescentaTenantId      // ...&tenantId=11111111-2222-3333-4444-555555555555
Para_EmailComCaracteresEspeciais_Escapa      // [Theory] "ana+rh@exemplo.test" => ana%2Brh%40exemplo.test; "a&b@exemplo.test" => a%26b%40exemplo.test; "joão@exemplo.test" => jo%C3%A3o%40exemplo.test
Para_EmailInvalido_Nulo                      // [Theory] null, "", "   ", "sem-arroba"
Para_APropriaPessoa_Nulo
Para_QuemVeSemSub_DevolveOEndereco
NomeDoProvedor_Teams_ETeams_DesligadoENulo
```

e em `ChatConfiguracaoTests` (host de teste com configuração em memória):

```csharp
Subida_ProvedorDesconhecido_Falha            // [Theory] "Slack", "xyz"; a mensagem contém "Intranet:Chat:Provedor"
Subida_ProvedorComEspacosECaixa_ValeTeams    // [Theory] " teams ", "TEAMS"
Subida_TenantIdQueNaoEGuid_Falha             // a mensagem contém "Intranet:Chat:TenantId"
Subida_SemSecao_ChatDesligado
```

- [ ] **Step 2:** rodar as duas classes → falham por não compilar.
- [ ] **Step 3: Implementar** os quatro arquivos e a linha no `Program.cs`. `LinkDeChat` é singleton.
- [ ] **Step 4:** rodar as duas classes e `dotnet build Secco.Intranet.slnx` → passam, sem avisos.
- [ ] **Step 5: Commit** `feat(diretorio): link de chat do Teams montado a partir do e-mail`.

### Task 2: botão nas três telas, temas e documentação

**Files:**
- Modify: `src/Secco.Intranet.Web.Theming/Contracts/NoOrganogramaModel.cs`
- Modify: `src/Secco.Intranet.Web/Views/Diretorio/Organograma.cshtml`, `Pessoa.cshtml`, `Index.cshtml`
- Modify: `_OrganogramaCartao.cshtml` e `wwwroot/scss/_components.scss` nos dois temas, mais o `wwwroot/css/theme.css` gerado
- Modify: `CHANGELOG.md`, `docs/guia-de-deploy.md`, `docs/temas.md`, `docs/roadmap.md`, a spec (estado)
- Test: `tests/Secco.Intranet.Tests/Integration/ChatNoDiretorioTests.cs`, `tests/Secco.Intranet.Tests/Ui/OrganogramaTelaTests.cs`

**Interfaces:**
- Consumes: `ILinkDeChat.Para(email, usuarioId, User)` e `NomeDoProvedor`, injetados nas views com `@inject`.
- Produces: `NoOrganogramaModel(string Nome, string? Cargo, string Url, AvatarModel Avatar, bool GestorInativo, IReadOnlyList<NoOrganogramaModel> Equipe, string? ChatUrl = null)`.

Marcação do botão, igual nas três telas (no Organograma, dentro de `_OrganogramaCartao`, com a classe `sc-org__chat`; nas views do core, com `btn btn-sm btn-outline-secondary`):

```cshtml
<a class="sc-org__chat" href="@Model.ChatUrl" target="_blank" rel="noopener noreferrer"
   title="Conversar com @Model.Nome no Teams">
    <i class="bi bi-chat-dots" aria-hidden="true"></i>
    <span class="visually-hidden">Conversar com @Model.Nome no Teams</span>
</a>
```

O nome do provedor no texto vem de `NomeDoProvedor` nas views do core; no cartão do tema o texto é fixo "no Teams" enquanto só houver esse provedor (o contrato carrega só a URL).

- [ ] **Step 1: Testes de integração que falham** em `ChatNoDiretorioTests` (fábrica com `Intranet:Chat:Provedor=Teams`, pessoas fictícias no Diretório como os testes de Diretório já criam):

```csharp
Organograma_ComTeams_CartaoTemOLinkDeChat        // [Theory] nos dois temas; href começa com https://teams.microsoft.com/l/chat/0/0?users=, com target e rel
Pessoa_ComTeams_TemOBotao
Lista_ComTeams_CadaLinhaTemOBotao
Pessoa_APropria_NaoTemOBotao
Organograma_SemConfiguracao_NaoTemLinkDeChat
Lista_SemConfiguracao_NaoTemBotao
```

- [ ] **Step 2:** rodar → falham.
- [ ] **Step 3: Implementar** o contrato, o mapeamento em `Organograma.cshtml`, o botão nas três telas e nos dois cartões de tema.
- [ ] **Step 4: CSS** `.sc-org__chat` nos dois `_components.scss`, junto das regras de `.sc-org` (ícone discreto, alinhado à direita do cartão, com foco visível), e `npm ci && npm run build` nos dois temas.
- [ ] **Step 5: Teste de tela** em `OrganogramaTelaTests`, `[UiTheory]` nos dois temas, com o chat ligado na fixture ou em fixture própria: `Chat_CliqueNoBotao_AbreOutraPaginaENaoRecolheAEquipe` (interceptar a requisição a `teams.microsoft.com` com `RouteAsync` e responder vazio, para o teste não sair para a internet; `RunAndWaitForPopupAsync`; depois `details[open]` continua com a mesma contagem) e `Chat_SemViolacaoDeCsp`.
- [ ] **Step 6: Documentação.** `CHANGELOG.md` em `[Não lançado]`, "Quebras — Link de chat no Diretório" (fork: `NoOrganogramaModel.ChatUrl` e a classe `sc-org__chat`). `docs/guia-de-deploy.md`: as duas variáveis (`Intranet__Chat__Provedor`, `Intranet__Chat__TenantId`) na tabela. `docs/temas.md`: `ChatUrl` no contrato do Organograma. `docs/roadmap.md`: item feito "Link de chat no Diretório (Teams)", com links para spec e plano. Spec: estado "entregue".
- [ ] **Step 7: Suíte inteira:** `SECCO_UI_TESTS=1 dotnet test Secco.Intranet.slnx` → tudo passa; `dotnet build` sem avisos.
- [ ] **Step 8: Commit** `feat(diretorio): botao de chat no organograma, na pagina da pessoa e na lista`.
