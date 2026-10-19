# Credenciais do sistema administrado — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** o `intranet-admin` emite, altera, rotaciona e revoga as credenciais OAuth (máquina e login) de um
tenant administrado, com o secret exibido uma única vez.

**Architecture:** o mesmo molde do subsistema 2.
- **Porta:** `IGestaoDeCredenciaisDoTenant`, com o tenant explícito em toda assinatura.
- **Handlers:** na Application (`Tenants/Credenciais`). Eles conferem as regras antes da porta e
  registram na trilha.
- **Adaptador:** `SecureGateGestaoDeCredenciaisDoTenant` usa o client de máquina (`securegate:admin`).
- **Web:** `TenantsCredenciaisController`, sob `/tenants/{tenantId}/credenciais`.

**Tech Stack:** .NET 10, ASP.NET Core MVC, `Secco.SecureGate.Client` 0.19.0 (NSwag), xUnit + AwesomeAssertions.

**Spec:** `docs/specs/2026-10-18-area-administrativa-tenants-credenciais-design.md`

## Global Constraints

- Commits direto na `main`, com a linha `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Indentação: tab em `.cs`/`.cshtml`. Comentários e textos de tela em português, no tom dos arquivos vizinhos.
- Toda escrita devolve `Result`/`Result<T>` (`Secco.SharedKernel.Results`). Sem falha aberta.
- O secret **nunca** vai para TempData, cookie, banco, log, trilha, `ToString()` ou redirect. Ele só aparece
  na resposta do próprio POST, com `Response.Headers.CacheControl = "no-store"` (o mesmo de
  `TenantsController.cs:147`).
- `tenantId` e `clientId` só da rota. `clientId` precisa casar com `^cli_[a-z2-7]{16}$`, senão 404 sem
  chamar a porta.
- Escopos possíveis: só `logstream` e `notificationhub`, e só se o recurso correspondente estiver ligado.
  Situação `Ligado` ou `LigadoSemResponder` em `TenantAdministradoDetalheDto.Recursos` conta como ligado.
- Limites (iguais aos da plataforma):
  - nome 1–100 caracteres, sem caractere de controle;
  - até 20 perfis;
  - até 10 redirect URIs e 10 de logout, cada uma com até 2000 caracteres.
- Redirect URI: absoluta, `https`, sem user-info e sem `#`. `http://` só com `CredenciaisOptions.PermitirEnderecoHttp`
  (Web registra `= environment.IsDevelopment()`).
- Códigos de erro da plataforma são lidos por `OperacoesDeAcessoNoSecureGate.TemCodigo`, que olha o
  `ProblemDetails.Title`. O `Response` do client real vem vazio.
- Filtros da classe do controller, nesta ordem: `[SomenteIntranetAdmin]`, `[ExigeSegundoFator]`,
  `[TenantAdministrado]`. **Sem** `[SecureGateLigado]`.
- Todo POST com `[ValidateAntiForgeryToken]`.
- Rodar a suíte com `dotnet test tests/Secco.Intranet.Tests`. Os testes de UI exigem `SECCO_UI_TESTS=1`
  (rodar na Task 4 e na final).

## Review Focus

1. **Tenant desativado ao emitir:** a plataforma responde `409 SecureGate.Client.TenantInactive`, e a tela
   deve dizer isso, não "SecureGate indisponível". O teste fica no adaptador (Task 3).
2. **Perfil excluído entre abrir o formulário e emitir:** `400 SecureGate.Client.RoleNotFound` vira
   `Credenciais.PerfilRecusado`, com o formulário de volta e os dados preenchidos (Task 3 e Task 4).
3. **Redirect URIs com espaços, linhas vazias e duplicadas:** a lista sai aparada, sem vazias e sem
   duplicadas antes de validar (Task 1).
4. **Plataforma devolvendo tipo desconhecido (`type` diferente de `client_credentials`, `authorization_code`
   ou nulo):** o adaptador falha com indisponibilidade, em vez de mostrar como máquina (Task 3).
5. **Credencial de login cujo recurso foi desligado depois:** a alteração deve aceitar manter o que já tinha?
   **Não**: escopo de recurso desligado é recusado também na alteração, e a mensagem diz qual recurso ligar
   (Task 2).

---

### Task 1: Contrato da Application — DTOs, porta, regras, erros e no-op

**Files:**
- Create: `src/Secco.Intranet.Application/Tenants/Credenciais/CredenciaisDtos.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Credenciais/IGestaoDeCredenciaisDoTenant.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Credenciais/RegrasDeCredencial.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Credenciais/CredenciaisOptions.cs`
- Create: `src/Secco.Intranet.Infrastructure/Access/GestaoDeCredenciaisDoTenantIndisponivel.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs` (classe nova `Credenciais`, depois de `Tenants`)
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/RegrasDeCredencialTests.cs`

**Interfaces:**
- Produces (namespace `Secco.Intranet.Application.Tenants.Credenciais`):
  - `enum TipoDeCredencial { Maquina, Login }`
  - `sealed record CredencialDto(string ClientId, string Nome, TipoDeCredencial Tipo, IReadOnlyList<string> Escopos, IReadOnlyList<string> Perfis, IReadOnlyList<string> RedirectUris, IReadOnlyList<string> RedirectUrisDeLogout, DateTimeOffset? CriadaEm)`
  - `sealed record DadosDaCredencial(TipoDeCredencial Tipo, string Nome, IReadOnlyList<string> Escopos, IReadOnlyList<string> Perfis, IReadOnlyList<string> RedirectUris, IReadOnlyList<string> RedirectUrisDeLogout)`
  - `sealed class SecretDaCredencial(string clientId, string valor)`, com as propriedades `ClientId` e `Valor`.
    `ToString()` devolve `$"SecretDaCredencial {{ ClientId = {ClientId} }}"`.
  - `sealed record CredencialEmitidaDto(CredencialDto Credencial, SecretDaCredencial Secret)`, em que
    `ToString()` herda o seguro do `SecretDaCredencial`.
  - `interface IGestaoDeCredenciaisDoTenant`:
    - `Task<Result<IReadOnlyList<CredencialDto>>> ListarAsync(Guid tenantId, CancellationToken ct = default)`
    - `Task<Result<CredencialDto>> ObterAsync(Guid tenantId, string clientId, CancellationToken ct = default)`
    - `Task<Result<CredencialEmitidaDto>> EmitirAsync(Guid tenantId, DadosDaCredencial dados, CancellationToken ct = default)`
    - `Task<Result> AlterarAsync(Guid tenantId, string clientId, DadosDaCredencial dados, CancellationToken ct = default)`
    - `Task<Result<SecretDaCredencial>> RotacionarSecretAsync(Guid tenantId, string clientId, CancellationToken ct = default)`
    - `Task<Result> RevogarAsync(Guid tenantId, string clientId, CancellationToken ct = default)`
  - `sealed class CredenciaisOptions { public bool PermitirEnderecoHttp { get; init; } }`
  - `static class RegrasDeCredencial`:
    - constantes `NomeMaximo = 100`, `PerfisMaximo = 20`, `EnderecosMaximo = 10` e `EnderecoMaximo = 2000`;
    - `bool ClientIdValido(string? clientId)`, com o regex compilado `^cli_[a-z2-7]{16}$`;
    - `bool NomeValido(string nome)`, para o nome já aparado;
    - `IReadOnlyList<string> Linhas(string? texto)`: separa por `\n`, apara, descarta vazias e duplicadas
      (ordinal) e mantém a ordem;
    - `bool EnderecoValido(string uri, bool permitirHttp)`;
    - `IReadOnlyList<string> EscoposLigados(IEnumerable<RecursoDoTenantDto> recursos)`: o
      `RecursosDaPlataforma.Produto(r.Recurso)` dos recursos com produto não nulo e situação diferente de
      `NaoLigado`;
    - `bool SecureGateLigado(IEnumerable<RecursoDoTenantDto> recursos)`.
  - `IntranetErrors.Credenciais`:
    - `NaoConfigurado` (Unavailable);
    - `NaoEncontrada` (NotFound);
    - `NomeInvalido`, `SemEscopo`, `EscopoNaoLigado`, `PerfilRecusado`, `PerfisSemSecureGate`,
      `LoginSemSecureGate`, `LoginSemEndereco`, `LoginComPerfis`, `MaquinaComEndereco`, `EnderecoRecusado`,
      `LimiteExcedido` e `ConfirmacaoNaoConfere` (Validation);
    - `NomeEmUso` e `TenantDesativado` (Conflict);
    - o método `Error Recusada(string? detalhe)` (Validation, código `Intranet.Credenciais.Recusada`, com
      texto padrão quando `detalhe` é nulo).
  - Em `VerbosDeAuditoria`: `TenantCredencialEmitir = "tenant.credencial.emitir"`, `TenantCredencialAlterar =
    "tenant.credencial.alterar"`, `TenantCredencialRotacionar = "tenant.credencial.rotacionar"` e
    `TenantCredencialRevogar = "tenant.credencial.revogar"`.
  - `GestaoDeCredenciaisDoTenantIndisponivel : IGestaoDeCredenciaisDoTenant`, que devolve
    `Credenciais.NaoConfigurado` em tudo.

- [ ] **Step 1: Write the failing tests** em `RegrasDeCredencialTests`:

```csharp
[Theory]
[InlineData("cli_abcdefghijklmnop", true)]
[InlineData("cli_abcdefghijklmno2", true)]
[InlineData("cli_ABCDEFGHIJKLMNOP", false)]
[InlineData("cli_abcdefghijklmno1", false)] // 1 não é base32
[InlineData("cli_abc", false)]
[InlineData("secco-intranet", false)]
[InlineData("../cli_abcdefghijklmnop", false)]
[InlineData(null, false)]
public void ClientIdValido(string? clientId, bool esperado) =>
	RegrasDeCredencial.ClientIdValido(clientId).Should().Be(esperado);

[Theory]
[InlineData("https://compras.empresa.com/signin-oidc", false, true)]
[InlineData("http://localhost:5001/signin-oidc", false, false)]
[InlineData("http://localhost:5001/signin-oidc", true, true)]
[InlineData("https://user:pw@compras.empresa.com/cb", false, false)]
[InlineData("https://compras.empresa.com/cb#x", false, false)]
[InlineData("/signin-oidc", false, false)]
[InlineData("javascript:alert(1)", true, false)]
public void EnderecoValido(string uri, bool permitirHttp, bool esperado) =>
	RegrasDeCredencial.EnderecoValido(uri, permitirHttp).Should().Be(esperado);

[Fact]
public void EnderecoValido_AcimaDe2000_Recusa() =>
	RegrasDeCredencial.EnderecoValido("https://a.com/" + new string('x', 2000), false).Should().BeFalse();

[Fact]
public void Linhas_AparaDescartaVaziasEDuplicadas() =>
	RegrasDeCredencial.Linhas(" https://a.com/cb \r\n\r\nhttps://a.com/cb\nhttps://b.com/cb\n")
		.Should().Equal("https://a.com/cb", "https://b.com/cb");

[Theory]
[InlineData("Integração do Compras", true)]
[InlineData("", false)]
[InlineData("nome\u0007com controle", false)]
public void NomeValido(string nome, bool esperado) => RegrasDeCredencial.NomeValido(nome).Should().Be(esperado);

[Fact]
public void NomeValido_101_Recusa() => RegrasDeCredencial.NomeValido(new string('a', 101)).Should().BeFalse();

[Fact]
public void EscoposLigados_SoRecursoComBancoENaoDesligado()
{
	RecursoDoTenantDto[] recursos =
	[
		new(RecursoDaPlataforma.SecureGate, SituacaoDoRecurso.Ligado, null),
		new(RecursoDaPlataforma.LogStream, SituacaoDoRecurso.LigadoSemResponder, "x"),
		new(RecursoDaPlataforma.NotificationHub, SituacaoDoRecurso.NaoLigado, null),
	];

	RegrasDeCredencial.EscoposLigados(recursos).Should().Equal("logstream");
	RegrasDeCredencial.SecureGateLigado(recursos).Should().BeTrue();
}

[Fact]
public void SecretDaCredencial_ToString_NaoTrazOValor()
{
	var secret = new SecretDaCredencial("cli_abcdefghijklmnop", "s3cr3t-valor");

	secret.ToString().Should().NotContain("s3cr3t-valor").And.Contain("cli_abcdefghijklmnop");
	new CredencialEmitidaDto(Credencial(), secret).ToString().Should().NotContain("s3cr3t-valor");
}
```

(`Credencial()` é um helper local que monta um `CredencialDto` de máquina qualquer. Confira os nomes exatos
dos membros de `RecursoDaPlataforma` no enum antes de escrever.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Secco.Intranet.Tests --filter FullyQualifiedName~RegrasDeCredencialTests`
Expected: falha de compilação (tipos inexistentes).

- [ ] **Step 3: Implement** os tipos do bloco Interfaces. `EnderecoValido` segue o
  `ProductClientRules.IsValidRedirectUri` da plataforma:
  - `Uri.TryCreate(uri, UriKind.Absolute, out var u)`;
  - `u.UserInfo.Length == 0`;
  - sem `#`;
  - tamanho de no máximo `EnderecoMaximo`;
  - esquema `https`, ou `http` se `permitirHttp`.

  As mensagens dos erros são frases para a tela, no tom das de `IntranetErrors.Tenants`. Exemplos:
  - `EscopoNaoLigado`: "Um dos recursos escolhidos não está ligado neste tenant. Ligue o recurso na página
    do tenant antes de dar acesso a ele.";
  - `TenantDesativado`: "O tenant está desativado: a plataforma não emite credencial para ele.".

- [ ] **Step 4: Run to verify it passes** — mesmo comando, esperado PASS.

- [ ] **Step 5: Commit** — `feat(credenciais): contrato, regras e erros das credenciais do tenant`

---

### Task 2: Handlers — leitura, opções do formulário e escritas com trilha

**Files:**
- Create: `src/Secco.Intranet.Application/Tenants/Credenciais/LeituraDeCredenciaisHandler.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Credenciais/CredenciaisDoTenantHandler.cs`
- Create: `src/Secco.Intranet.Application/Tenants/Credenciais/AuditoriaDeCredenciais.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs` (registrar os dois handlers como scoped)
- Create: `tests/Secco.Intranet.Tests/Support/DublesDeCredenciais.cs` (`GestaoDeCredenciaisDoTenantFalsa`)
- Test: `tests/Secco.Intranet.Tests/Unit/CredenciaisDoTenantHandlerTests.cs`

**Interfaces:**
- Consumes:
  - da Task 1, a porta, os DTOs, `RegrasDeCredencial`, `CredenciaisOptions`, os erros e os verbos;
  - `ObterTenantAdministradoHandler.HandleAsync(Guid, CancellationToken) → Result<TenantAdministradoDetalheDto>`,
    que dá `Ativo` e `Recursos`;
  - `IGestaoDeAcessoDoTenant.ListarPerfisAsync(Guid, CancellationToken) → Result<IReadOnlyList<PerfilDoTenantDto>>`;
  - `ClassificacaoDePerfil.EhReservado(string)`;
  - `ITrilhaDeAuditoria`.
- Produces:
  - `sealed record OpcoesDaCredencial(TenantAdministradoDetalheDto Tenant, IReadOnlyList<string> EscoposDisponiveis, bool SecureGateLigado, IReadOnlyList<PerfilDoTenantDto> PerfisDisponiveis)`.
    Os perfis vêm sem os reservados, e a lista fica vazia (sem chamar a porta de acesso) quando o
    SecureGate está desligado.
  - `sealed record FormularioDaCredencial(string? Nome, IReadOnlyList<string>? Escopos, IReadOnlyList<string>? Perfis, string? RedirectUris, string? RedirectUrisDeLogout)`.
    As URIs vêm em texto, uma por linha.
  - `LeituraDeCredenciaisHandler(IGestaoDeCredenciaisDoTenant gestao, ObterTenantAdministradoHandler tenant, IGestaoDeAcessoDoTenant acesso)`:
    - `Task<Result<IReadOnlyList<CredencialDto>>> ListarAsync(Guid tenantId, CancellationToken ct = default)`
    - `Task<Result<CredencialDto>> ObterAsync(Guid tenantId, string? clientId, CancellationToken ct = default)`.
      Com `clientId` fora do formato, devolve `NaoEncontrada` sem chamar a porta.
    - `Task<Result<OpcoesDaCredencial>> OpcoesAsync(Guid tenantId, CancellationToken ct = default)`
  - `CredenciaisDoTenantHandler(IGestaoDeCredenciaisDoTenant gestao, LeituraDeCredenciaisHandler leitura, CredenciaisOptions opcoes, ITrilhaDeAuditoria trilha)`:
    - `Task<Result<CredencialEmitidaDto>> EmitirAsync(Guid tenantId, TipoDeCredencial tipo, FormularioDaCredencial formulario, CancellationToken ct = default)`
    - `Task<Result> AlterarAsync(Guid tenantId, string? clientId, FormularioDaCredencial formulario, CancellationToken ct = default)`.
      O tipo vem do `ObterAsync`.
    - `Task<Result<SecretDaCredencial>> RotacionarAsync(Guid tenantId, string? clientId, CancellationToken ct = default)`
    - `Task<Result> RevogarAsync(Guid tenantId, string? clientId, string? confirmacao, CancellationToken ct = default)`
  - `GestaoDeCredenciaisDoTenantFalsa`:
    - guarda as credenciais em memória e registra as chamadas em `List<string> Chamadas`
      (`"Emitir:{tenantId}"` etc.);
    - tem as propriedades `DadosEmitidos`/`DadosAlterados` (`DadosDaCredencial?`) e `Error? FalharCom`;
    - `ComCredencial(CredencialDto)` acrescenta uma credencial;
    - emitir gera `clientId` `cli_` + 16 letras e secret `"secret-de-teste-" + n`.

A validação (privada, compartilhada por emitir e alterar) segue as regras da spec, nesta ordem, e devolve o
primeiro erro:

1. Nome aparado e `NomeValido`, senão `NomeInvalido`.
2. Escopos: distintos, todos em `EscoposLigados`, senão `EscopoNaoLigado`. Para `Maquina`, pelo menos um,
   senão `SemEscopo`.
3. Para `Login`:
   - exige `SecureGateLigado`, senão `LoginSemSecureGate`;
   - perfis não vazios dão `LoginComPerfis`;
   - URIs por `RegrasDeCredencial.Linhas`, com pelo menos uma redirect, senão `LoginSemEndereco`;
   - mais de 10 de cada tipo dá `LimiteExcedido`;
   - alguma URI inválida dá `EnderecoRecusado`.
4. Para `Maquina`:
   - texto de URI não vazio dá `MaquinaComEndereco`;
   - perfis distintos, no máximo 20 (`LimiteExcedido`);
   - perfil com SecureGate desligado dá `PerfisSemSecureGate`;
   - perfil fora de `PerfisDisponiveis` (inexistente ou reservado) dá `PerfilRecusado`.

A trilha (`AuditoriaDeCredenciais`, no molde de `AuditoriaDoAcessoDoTenant`, com o recurso
`RecursosDeAuditoria.Tenant` e o id do tenant como alvo) só registra **depois** do sucesso na porta:

| Ação | Dados registrados |
|---|---|
| emitir | `{ tenantId, clientId, tipo, escopos, perfis, enderecos = quantidade de redirect URIs }` |
| alterar | `{ tenantId, clientId, escopos, perfis, redirectUris, redirectUrisDeLogout }` |
| rotacionar | `{ tenantId, clientId }` |
| revogar | `{ tenantId, clientId, nome }` |

- [ ] **Step 1: Write the failing tests** em `CredenciaisDoTenantHandlerTests`. Monte os handlers com:
  - `GestaoDeCredenciaisDoTenantFalsa`;
  - `GestaoDeAcessoDoTenantFalsa` (já existe, com `ComPerfil`);
  - um `ObterTenantAdministradoHandler` sobre os dubles de cadastro e de gestão de tenants que
    `LeituraDeTenantsHandlersTests` já usa (reaproveite, não crie outros);
  - uma trilha em memória (a que os testes de `PerfisDoTenantHandlerTests` usam).

  Cenário base: LogStream ligado, NotificationHub desligado, SecureGate ligado, perfis `compras-leitor` e
  `secco-admin` (reservado). Testes, cada um com o resultado esperado e "nenhuma chamada de escrita à porta"
  quando é recusa:
  - `Emitir_Maquina_ComLogstream_GravaETrilhaSemSecret`: sucesso. `DadosEmitidos` com
    `Escopos = ["logstream"]`. A trilha tem um registro com o verbo `tenant.credencial.emitir`, cujo JSON
    contém o `clientId` e **não** contém `secret-de-teste`.
  - `Emitir_EscopoDeRecursoDesligado_Recusa` (`notificationhub`) → `EscopoNaoLigado`.
  - `Emitir_EscopoForaDaLista_Recusa` (`securegate:admin`) → `EscopoNaoLigado`.
  - `Emitir_MaquinaSemEscopo_Recusa` → `SemEscopo`.
  - `Emitir_Login_ComSecureGateDesligado_Recusa` (cenário com o SecureGate desligado) → `LoginSemSecureGate`.
  - `Emitir_Login_SemEndereco_Recusa` → `LoginSemEndereco`.
  - `Emitir_Login_ComPerfis_Recusa` → `LoginComPerfis`.
  - `Emitir_Login_HttpForaDeDevelopment_Recusa` (`PermitirEnderecoHttp = false`, `http://localhost/cb`)
    → `EnderecoRecusado`. Com `true` passa.
  - `Emitir_Login_OnzeEnderecos_Recusa` → `LimiteExcedido`.
  - `Emitir_Maquina_ComEndereco_Recusa` → `MaquinaComEndereco`.
  - `Emitir_PerfilReservado_Recusa` (`secco-admin`) e `Emitir_PerfilInexistente_Recusa` → `PerfilRecusado`.
  - `Emitir_PerfilComSecureGateDesligado_Recusa` → `PerfisSemSecureGate`.
  - `Emitir_NomeVazioOuSoEspacos_Recusa` → `NomeInvalido`.
  - `Alterar_UsaOTipoDaCredencialLida`: com uma credencial de login existente, o formulário traz perfis.
    O resultado é `LoginComPerfis`, provando que o tipo veio da credencial lida e não de um campo forjado.
  - `Alterar_RecursoDesligadoDepois_Recusa`: a credencial tem `notificationhub`, que agora está desligado
    → `EscopoNaoLigado` (Review Focus 5).
  - `Revogar_ConfirmacaoDiferente_Recusa` → `ConfirmacaoNaoConfere`. Com o nome exato, sucesso e trilha
    `tenant.credencial.revogar`.
  - `Rotacionar_TrilhaSemSecret`.
  - `Obter_ClientIdForaDoFormato_NaoEncontrada_SemChamarAPorta`.
  - `Opcoes_SecureGateDesligado_SemPerfisESemChamarAcesso`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Secco.Intranet.Tests --filter FullyQualifiedName~CredenciaisDoTenantHandlerTests`
Expected: falha de compilação.

- [ ] **Step 3: Implement** os handlers, a auditoria e o duble, e registre os handlers em
  `IntranetApplicationExtensions`.

- [ ] **Step 4: Run to verify it passes** — esperado PASS.

- [ ] **Step 5: Commit** — `feat(credenciais): handlers com regras e trilha`

---

### Task 3: Adaptador do SecureGate e composição

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeCredenciaisDoTenant.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (`CriarGestaoDeCredenciaisDoTenant`,
  no mesmo padrão de `CriarGestaoDeAcessoDoTenant`)
- Test: `tests/Secco.Intranet.Tests/Unit/SecureGateGestaoDeCredenciaisDoTenantTests.cs`

**Interfaces:**
- Consumes: a porta e os DTOs da Task 1, `OperacoesDeAcessoNoSecureGate.LerAsync/EscreverAsync/TemCodigo`,
  e `ISecureGateClient`:
  - `CreateProductClientAsync(Guid tenantId, ProductClientRequest body)`
  - `ListProductClientsAsync(Guid tenantId)`
  - `GetProductClientAsync(Guid tenantId, string clientId)`
  - `UpdateProductClientAsync(Guid tenantId, string clientId, ProductClientRequest body)`
  - `RotateProductClientSecretAsync(Guid tenantId, string clientId)`
  - `DeleteProductClientAsync(Guid tenantId, string clientId)`

  Confira os nomes e as sobrecargas com `CancellationToken` no pacote antes. O `ProductClientRequest` tem
  `Name`, `Scopes`, `Roles`, `Type`, `RedirectUris` e `PostLogoutRedirectUris`.
- Produces: `SecureGateGestaoDeCredenciaisDoTenant(ISecureGateClient client, ILogger<SecureGateGestaoDeCredenciaisDoTenant> logger) : IGestaoDeCredenciaisDoTenant`.

Mapeamentos:
- **Tipo para a plataforma:** `Maquina` → `"client_credentials"` e `Login` → `"authorization_code"`.
- **Tipo vindo da plataforma:**
  - `null` ou `"client_credentials"` → `Maquina`;
  - `"authorization_code"` → `Login`;
  - qualquer outro → a leitura falha com `IntranetErrors.Acesso.Indisponivel` e um log de aviso que leva só
    o `clientId` (Review Focus 4). Na listagem, o item desconhecido é omitido, com o mesmo log.
- **No request:** para `Login`, `Roles = []`. Para `Maquina`, `RedirectUris` e `PostLogoutRedirectUris`
  vão `null`.

Tradução de erro pelo `Title`:

| Situação | Erro |
|---|---|
| 404, qualquer código | `NaoEncontrada` |
| 409 `SecureGate.Client.NameAlreadyExists` | `NomeEmUso` |
| 409 `SecureGate.Client.TenantInactive` | `TenantDesativado` |
| 400 `SecureGate.Client.RoleNotFound` / `RoleNotAssignable` | `PerfilRecusado` |
| 400 `SecureGate.Client.RedirectUrisInvalid` / `PostLogoutRedirectUrisInvalid` | `EnderecoRecusado` |
| outro 400 com `ProblemDetails` | `Recusada(problem.Detail)` |
| rede, timeout, 5xx | `Acesso.Indisponivel` (já é o padrão do núcleo) |

**O secret nunca entra em log**: o núcleo já só registra a operação e o status.

- [ ] **Step 1: Write the failing tests**, no molde de `SecureGateGestaoDeAcessoDoTenantTests`. O
  `ClientFalso` herda `SecureGateClient` e sobrescreve os seis métodos, registrando `"{Op}:{tenantId}"` e
  guardando o último `ProductClientRequest`. Para lançar erro, use um helper
  `Problema(int status, string titulo, string? detalhe = null)` que cria
  `new ApiException<ProblemDetails>("x", status, null /* Response vazio, como o client real */, headers, new ProblemDetails { Title = titulo, Detail = detalhe }, null)`.

  Testes:
  - `Emitir_UsaOTenantRecebido_EMapeiaMaquina`: tipo `client_credentials`, `RedirectUris` nulo. O
    resultado traz `Secret.Valor` igual ao `ClientSecret` devolvido.
  - `Emitir_Login_MandaAuthorizationCodeERolesVazio`.
  - `Obter_TipoDesconhecido_Indisponivel` (Review Focus 4).
  - `Listar_OmiteTipoDesconhecido`.
  - `[Theory]` da tabela de tradução, com status e título → erro esperado, cobrindo cada linha. Inclui
    `409 TenantInactive` → `TenantDesativado` (Review Focus 1) e `400 RoleNotFound` → `PerfilRecusado`
    (Review Focus 2).
  - `Recusa400Generica_LevaODetail`.
  - `FalhaDeRede_Indisponivel`.
  - `Rotacionar_DevolveSecretNovo` e `Revogar_ChamaDelete`, ambos com o tenant recebido.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Secco.Intranet.Tests --filter FullyQualifiedName~SecureGateGestaoDeCredenciaisDoTenantTests`
Expected: falha de compilação.

- [ ] **Step 3: Implement** o adaptador e registre-o em `IntranetInfrastructureExtensions`. Com
  `SecureGateClientCredentialsOptions.IsConfigured`, usa o real. Senão, `new GestaoDeCredenciaisDoTenantIndisponivel()`.

- [ ] **Step 4: Run to verify it passes** — esperado PASS.

- [ ] **Step 5: Commit** — `feat(credenciais): adaptador do SecureGate para clients de tenant`

---

### Task 4: Telas, controller e testes de integração

**Files:**
- Create: `src/Secco.Intranet.Web/Controllers/TenantsCredenciaisController.cs`
- Create: `src/Secco.Intranet.Web/Models/Tenants/TenantsCredenciaisViewModels.cs`
- Create: `src/Secco.Intranet.Web/Views/TenantsCredenciais/Index.cshtml`, `Nova.cshtml`, `Detalhe.cshtml`, `Secret.cshtml`
- Modify: `src/Secco.Intranet.Web/Views/Tenants/Detalhe.cshtml` (painel **Credenciais** com o link)
- Modify: a composição da Web onde os serviços dependem do ambiente (o mesmo lugar de `AddIntranetAuthentication`):
  `services.AddSingleton(new CredenciaisOptions { PermitirEnderecoHttp = environment.IsDevelopment() })`
- Modify: `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs`: a propriedade
  `IGestaoDeCredenciaisDoTenant? GestaoDeCredenciaisDoTenant` e o registro
  `services.AddScoped<IGestaoDeCredenciaisDoTenant>(_ => GestaoDeCredenciaisDoTenant ?? new GestaoDeCredenciaisDoTenantIndisponivel())`.
  Também `CredenciaisOptions` com `PermitirEnderecoHttp = false`.
- Test: `tests/Secco.Intranet.Tests/Integration/TenantsCredenciaisTests.cs`

**Interfaces:**
- Consumes: os handlers da Task 2 e `ObterCabecalhoDoTenantHandler`. A Authority vem de
  `IConfiguration["Secco:SecureGate:Authority"]`, injetada no controller.
- Produces: estas rotas (todas com `[FromRoute] Guid tenantId`):

| Rota | Comportamento |
|---|---|
| `GET /tenants/{tenantId}/credenciais` | Lista |
| `GET …/credenciais/nova?tipo=maquina\|login` | Tipo inválido ou ausente vale `maquina` |
| `POST …/credenciais/nova` | Recebe o `tipo` e os campos do `FormularioDaCredencial`. Sucesso: `View("Secret")` com `no-store`. Erro: `View("Nova")` com o erro e os campos preservados |
| `GET …/credenciais/{clientId}` | Detalhe |
| `POST …/credenciais/{clientId}` | Altera. Redireciona com TempData de sucesso, ou de erro, que **nunca** carrega dado do secret |
| `POST …/credenciais/{clientId}/rotacionar` | `View("Secret")` com `no-store` |
| `POST …/credenciais/{clientId}/revogar` | Recebe `confirmacao`. Redireciona para a lista |

  Falhas seguem o `Falha` do `TenantsAcessoController`: NotFound → 404, o resto →
  `~/Views/Tenants/Indisponivel.cshtml`.

Conteúdo das telas (parciais do contrato de tema, nas classes que as telas do `TenantsAcesso` já usam):
- **`Index`:** o cabeçalho do tenant (como no `TenantsAcesso/Index`). A tabela tem nome, tipo
  ("Máquina"/"Login de pessoas"), `clientId`, escopos e data, mais as ações "Emitir credencial de máquina"
  e "Emitir credencial de login".
- **`Nova`:**
  - nome;
  - caixas de escopo (só `EscoposDisponiveis`), cada uma com o nome do recurso por
    `RecursosDaPlataforma.Nome`;
  - para máquina, as caixas de perfil com a quantidade de permissões, e o aviso "Ligue o SecureGate do
    tenant para dar perfis" quando ele está desligado;
  - para login, as caixas de texto de redirect e de logout, uma URI por linha;
  - para login com o SecureGate desligado, a tela mostra só o aviso, sem o formulário.
- **`Detalhe`:**
  - os dados atuais, com as redirect URIs em destaque (`<code>`);
  - o formulário de alteração;
  - o botão Rotacionar, com `confirm()` e o aviso "o sistema para de obter token até receber o secret novo";
  - a área Revogar, com o campo de confirmação pelo nome e o aviso "tokens já emitidos valem até 5 minutos".
- **`Secret`:**
  - `clientId`, secret e Authority;
  - o bloco de variáveis `Secco__SecureGate__Authority=…`, `Secco__SecureGate__ClientId=…` e
    `Secco__SecureGate__ClientSecret=…`;
  - o aviso "Copie agora: o secret não aparece de novo. Entregue ao time do sistema pelo cofre, nunca por
    e-mail ou repositório.";
  - o link de volta para a lista.

- [ ] **Step 1: Write the failing tests** em `TenantsCredenciaisTests`, usando os auxiliares dos
  `TenantsAcessoAutorizacaoTests`/`TenantsAcessoTelasTests` (cliente autenticado com role, 2FA falso,
  tenant cadastrado):
  - **Autorização:**
    - `[Theory]` sobre todas as rotas GET e POST: sem perfil → 403; `compras-admin` de setor → 403;
      `intranet-admin` sem 2FA → a tela de segundo fator;
    - SecureGate indisponível → indisponível.
  - **IDOR:**
    - `TenantForaDoCadastro_404` em todas as rotas;
    - `ClientIdForaDoFormato_404_SemChamarAPorta` (`/credenciais/nao-e-client`), com `Chamadas` vazio;
    - `TenantIdNoCorpo_Ignorado`: o POST com `tenantId` de outro tenant no form usa o da rota.
  - **Segredo:**
    - `Emitir_SecretSoNaRespostaComNoStore`: o HTML contém o secret, e `Cache-Control` contém `no-store`.
      Não há `Set-Cookie` de TempData, e o corpo não é redirect.
    - Nenhum registro da trilha capturada contém o secret, nem o log capturado (use o captor de log que
      os testes do script de provisionamento usam).
    - `Rotacionar_SecretSoNaResposta` (mesmas asserções).
  - **Fluxo:**
    - `Nova_Login_SecureGateDesligado_MostraAvisoSemFormulario`;
    - `Nova_ErroDePerfil_PreservaCampos`: com a porta falsa devolvendo `PerfilRecusado` (Review Focus 2),
      a resposta é a tela `Nova`, com a mensagem e o nome preenchido;
    - `Revogar_NomeErrado_Recusa`.
  - **Telas:** `Index`, `Nova` (dos dois tipos) e `Detalhe` renderizam nos dois temas, no padrão de
    `TenantsAcessoTelasTests`.
  - **Página do tenant:** tem o link para `/tenants/{id}/credenciais`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Secco.Intranet.Tests --filter FullyQualifiedName~TenantsCredenciaisTests`
Expected: falha de compilação.

- [ ] **Step 3: Implement** o controller, os view models, as views, o painel e a composição.

- [ ] **Step 4: Run to verify it passes**, depois a suíte inteira com UI:

Run: `SECCO_UI_TESTS=1 dotnet test tests/Secco.Intranet.Tests`
Expected: 0 falhas.

- [ ] **Step 5: Commit** — `feat(credenciais): telas de credenciais do tenant administrado`

---

### Task 5: Fumaça contra o SecureGate real e documentação

**Files:**
- Create: `tests/Secco.Intranet.Tests/Smoke/SecureGateGestaoDeCredenciaisFumacaTests.cs`
- Modify: `docs/roadmap.md` (item 4 dos subsistemas → `[x]`, com o link da spec)
- Modify: `docs/guia-de-deploy.md` (nota de operação do secret)
- Modify: `docs/plataforma.md` (a linha da #39 cita também os clients de tenant)
- Modify: `docs/specs/2026-10-18-area-administrativa-tenants-credenciais-design.md` (`**Estado:** implementado`)

- [ ] **Step 1: Write the smoke test** no molde de `SecureGateGestaoDeAcessoDoTenantFumacaTests`, com
  `[FumacaFact]`, a mesma configuração e um tenant novo criado pela `SecureGateGestaoDeTenants`. Passos,
  cada um com a sua asserção:
  1. emitir uma credencial de máquina com `logstream`, depois de ligar/provisionar o LogStream do tenant
     como a fumaça de tenants faz;
  2. pedir token em `{Authority}/connect/token` (`grant_type=client_credentials`, `scope=logstream`) e ver
     200, com o `tenant_id` do JWT (payload decodificado por base64url) igual ao tenant;
  3. rotacionar, pedir token com o secret antigo e ver 400/401;
  4. `ObterAsync` do mesmo `clientId` pelo id de **outro** tenant (o da Intranet) → `NaoEncontrada`;
  5. emitir uma de login com `https://compras.exemplo.test/signin-oidc` e ver o tipo `Login` no `ObterAsync`;
  6. revogar as duas e ver `NaoEncontrada` no `ObterAsync`.

- [ ] **Step 2: Run** — sem a variável, o teste é ignorado:

Run: `dotnet test tests/Secco.Intranet.Tests --filter FullyQualifiedName~SecureGateGestaoDeCredenciaisFumacaTests`
Expected: Skipped. Com `SECCO_SMOKE_SECUREGATE_URL` (roteiro em `docs/roteiro-fumaca-securegate.md`), o
esperado é PASS. Se o ambiente não estiver de pé, registre que não rodou.

- [ ] **Step 3: Update docs**
  - **Roadmap:** o item 4 passa a "Credencial do sistema administrado: emitir, alterar, rotacionar e revogar
    clients de máquina e de login do tenant ([spec](specs/2026-10-18-area-administrativa-tenants-credenciais-design.md)).
    Usa o client de máquina com `securegate:admin`: pela ADR-0044, clients de tenant são só do operador
    ([secco-platform#39](https://github.com/rafsecco/secco-platform/issues/39))".
  - **Guia de deploy:** uma seção curta "Credenciais dos sistemas administrados". O secret aparece uma vez,
    vai para o cofre do sistema, e rotacionar derruba o sistema até ele receber o novo. Não há configuração
    nova na Intranet.
  - **Plataforma:** na linha da #39, acrescentar "e emitir credencial (client) do tenant".

- [ ] **Step 4: Commit** — `test(credenciais): fumaca contra o SecureGate real; docs`
