# Exportação Excel/PDF para RCLs — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** uma RCL externa exporta Excel/PDF pelo `Secco.SDK.Documents` só injetando serviços, com trilha fail-closed antes do envio e limite por pessoa, sem mudar a trilha atual.

**Architecture:** o `Secco.Intranet.Web.Extensibility` ganha a porta `ITrilhaDeExportacao` e o nome da política. A Web a implementa (`TrilhaDeExportacao`), montando o `RegistroDeAuditoria` e chamando uma porta nova da Application, `ITrilhaObrigatoria`, que devolve `Result`. A Infrastructure escolhe um de três adapters pela URL do LogStream **e** pelo nome do ambiente. A Web registra o SDK e a política de rate limiting, as duas na seção `Intranet:Exportacao`.

**Tech Stack:** .NET 10, ASP.NET Core MVC, `Secco.SDK.Documents`, `Microsoft.AspNetCore.RateLimiting` (framework), xUnit + AwesomeAssertions.

**Spec:** [docs/specs/2026-11-04-exportacao-de-documentos-design.md](../specs/2026-11-04-exportacao-de-documentos-design.md)

## Global Constraints

- `Secco.Intranet.Web.Extensibility` continua só com `<FrameworkReference Include="Microsoft.AspNetCore.App" />`: sem projeto do produto e sem pacote.
- A `ITrilhaDeAuditoria` e os três adapters dela não mudam de comportamento. Os testes atuais de `TrilhaDeAuditoriaTests` passam sem ajuste.
- Seção de configuração: `Intranet:Exportacao`, com as chaves do SDK (`MaxXlsxRows`, `MaxPdfRows`, `MaxConcurrentExports`, `TempDirectory`) e `PorPessoaPorMinuto` (padrão `10`, menor que 1 derruba a subida).
- Nome da política: `"exportacao"`. Janela fixa de 1 minuto, partição por `SeccoClaims.Subject`, `QueueLimit = 0`, `429`.
- Trilha: verbo `exportacao.gerar`, recurso `exportacao`, `RecursoId` = recurso informado pela RCL, `Metadata` = JSON `{"formato","linhas","filtros"}`, com cada valor de filtro cortado em 200 caracteres.
- Recurso: `\A[a-z0-9-]{1,64}\z`. Formato: `xlsx` ou `pdf`. Fora disso, `ArgumentException`.
- DEV = `IHostEnvironment.IsDevelopment()` **ou** `IsEnvironment("Testing")`. Nunca só a URL vazia.
- `Secco.SDK.Documents` na versão estável mais recente do feed, conferida ao vivo (hoje `0.1.0`).
- Nada da origem do fork (nomes de tema, tela, sistema) em código, doc, ADR ou commit.
- Indentação com tabs em `.cs`/`.cshtml`. Commits em Conventional Commits, português sem acento, com `Co-Authored-By`.

## Review Focus

1. **HML com a URL do LogStream esquecida:** a exportação é recusada, e não aceita como em DEV. Teste `Escolha_semLogStream_foraDeDev_recusa` na Task 2.
2. **LogStream configurado em DEV e fora do ar:** recusa. A leniência de DEV vale só sem URL. Teste `LogStream_falhaDeRede_recusa_mesmoEmDevelopment` na Task 2.
3. **Cancelamento pelo navegador no meio do registro:** propaga `OperationCanceledException`, não vira "recusado" com log de falha. Teste `LogStream_cancelamentoDoChamador_propaga` na Task 2.
4. **Filtro com texto enorme** (o usuário cola 5 mil caracteres no campo): o registro sai com o valor cortado em 200 e a exportação segue. Teste `Filtro_longo_eCortadoEm200` na Task 3.
5. **Duas pessoas exportando ao mesmo tempo:** o limite de uma não bloqueia a outra. Teste `Limite_ePorPessoa` na Task 5.

---

### Task 1: Contrato no `Extensibility`

**Files:**

- Create: `src/Secco.Intranet.Web.Extensibility/ITrilhaDeExportacao.cs`, `src/Secco.Intranet.Web.Extensibility/PoliticaDeExportacao.cs`
- Modify: `src/Secco.Intranet.Web.Extensibility/Secco.Intranet.Web.Extensibility.csproj` (só o comentário: o contrato deixa de ser só do menu)

**Interfaces:**

- Produces:
  - `public sealed record FiltroDeExportacao(string Nome, string Valor);`
  - `public sealed record ExportacaoParaAuditar(string Recurso, string Formato, int Linhas, IReadOnlyList<FiltroDeExportacao> Filtros);`
  - `public interface ITrilhaDeExportacao { Task<bool> RegistrarAsync(ExportacaoParaAuditar exportacao, CancellationToken cancellationToken); }` O XML doc diz: `true` = registrada, pode enviar; `false` = descarte o documento e não envie; argumento inválido lança `ArgumentException`; cancelamento do chamador propaga.
  - `public static class PoliticaDeExportacao { public const string Nome = "exportacao"; }`

- [ ] **Step 1:** crie os tipos acima, com XML doc em português no tom dos existentes (`IItemDeMenuPrincipal.cs`).
- [ ] **Step 2:** `dotnet build Secco.Intranet.slnx` sem erro nem aviso novo.
- [ ] **Step 3: Commit** `feat(exportacao): contrato de trilha e politica no extensibility (issue #5)`.

### Task 2: Trilha obrigatória — Application e Infrastructure

**Files:**

- Create: `src/Secco.Intranet.Application/Auditoria/ITrilhaObrigatoria.cs`, `src/Secco.Intranet.Infrastructure/Auditoria/LogStreamTrilhaObrigatoria.cs`, `src/Secco.Intranet.Infrastructure/Auditoria/TrilhaObrigatoriaDeDesenvolvimento.cs`, `src/Secco.Intranet.Infrastructure/Auditoria/TrilhaObrigatoriaIndisponivel.cs`, `src/Secco.Intranet.Infrastructure/Auditoria/EscolhaDaTrilhaObrigatoria.cs`, `src/Secco.Intranet.Infrastructure/Auditoria/RegistroParaLogStream.cs`
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs` (verbo e recurso), `src/Secco.Intranet.Application/IntranetErrors.cs` (classe `Auditoria`), `src/Secco.Intranet.Infrastructure/Auditoria/LogStreamTrilhaDeAuditoria.cs` (passa a usar `RegistroParaLogStream`), `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (registro)
- Test: `tests/Secco.Intranet.Tests/Unit/TrilhaObrigatoriaTests.cs` (novo)

**Interfaces:**

- Produces:
  - `public interface ITrilhaObrigatoria { Task<Result> RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default); }` Registro nulo lança `ArgumentNullException`.
  - `VerbosDeAuditoria.ExportacaoGerar = "exportacao.gerar"`; `RecursosDeAuditoria.Exportacao = "exportacao"`.
  - `IntranetErrors.Auditoria.TrilhaIndisponivel` = `Error.Failure("Intranet.Auditoria.TrilhaIndisponivel", "Não foi possível exportar agora. Tente de novo em alguns minutos.")`.
  - `public enum ModoDaTrilhaObrigatoria { LogStream, Desenvolvimento, Indisponivel }` e `public static class EscolhaDaTrilhaObrigatoria` com `static ModoDaTrilhaObrigatoria Para(string? logStreamUrl, IHostEnvironment ambiente)` e `static bool EhDesenvolvimento(IHostEnvironment ambiente)` (Development ou Testing).
  - `internal static class RegistroParaLogStream { static CreateAuditEntryRequest Montar(RegistroDeAuditoria registro, AtorDaAcao ator); }`, extraído do adapter atual (ator, `ActorType.User`, correlação do `SeccoAmbientContext`, `OccurredAt = UtcNow`).
  - Adapters (todos `public sealed`, implementando `ITrilhaObrigatoria`):
    - `LogStreamTrilhaObrigatoria(ILogStreamClient, IAtorAtual, IHostEnvironment, ILogger<...>)`. Sem ator: `Success` com `Warning` em DEV, `TrilhaIndisponivel` com `Warning` fora. `ApiException`, `HttpRequestException` e timeout (`OperationCanceledException` sem cancelamento pedido): `TrilhaIndisponivel` com `Warning`. Cancelamento pedido: propaga.
    - `TrilhaObrigatoriaDeDesenvolvimento(ILogger<...>)`: `Success` com `Warning` "exportação de {Recurso} não auditada: LogStream não configurado", onde `{Recurso}` é o `RecursoId`.
    - `TrilhaObrigatoriaIndisponivel(ILogger<...>)`: `TrilhaIndisponivel` com `Warning` "exportação recusada: LogStream não configurado fora de Development".
  - Registro em `AddIntranetInfrastructure`: `services.AddScoped<ITrilhaObrigatoria>(sp => ...)`, com `EscolhaDaTrilhaObrigatoria.Para(sp.GetRequiredService<AuditoriaOptions>().LogStreamUrl, sp.GetRequiredService<IHostEnvironment>())` e `ActivatorUtilities.CreateInstance` do adapter correspondente.

- [ ] **Step 1: Testes que falham** em `TrilhaObrigatoriaTests`. Reaproveite o padrão de `ClientFalso`/`ClientQueFalha` de `TrilhaDeAuditoriaTests` (copie para classes privadas do arquivo novo, ou extraia para `tests/Secco.Intranet.Tests/Support` se preferir) e um `IHostEnvironment` falso com `EnvironmentName` configurável. Casos:
  - `Escolha_comLogStream_usaLogStream_emQualquerAmbiente`: `[Theory]` com `"Development"`, `"Testing"`, `"Staging"`, `"Production"` e URL `"https://logstream"` → `LogStream`.
  - `Escolha_semLogStream_emDev_aceita`: `"Development"` e `"Testing"`, URL `""` e `null` → `Desenvolvimento`.
  - `Escolha_semLogStream_foraDeDev_recusa`: `"Staging"`, `"Production"`, `"Homologacao"` → `Indisponivel`.
  - `LogStream_aceito_devolveSucesso_eEnviaOVerbo`: o `ClientFalso` recebe um request com `Action == "exportacao.gerar"`.
  - `LogStream_falhaDeRede_recusa_mesmoEmDevelopment`: `ClientQueFalha` com ambiente `"Development"` → `IsFailure`, `Error == IntranetErrors.Auditoria.TrilhaIndisponivel`.
  - `LogStream_recusaDoServico_recusa`: client que lança `ApiException` (status 500) → falha.
  - `LogStream_timeout_recusa`: client que lança `TaskCanceledException` com token não cancelado → falha.
  - `LogStream_cancelamentoDoChamador_propaga`: token já cancelado e client que respeita o token → `await act.Should().ThrowAsync<OperationCanceledException>()`.
  - `LogStream_semAtor_emTesting_aceita` e `LogStream_semAtor_emProduction_recusa`: `IAtorAtual` que devolve `null`.
  - `Desenvolvimento_aceita` e `Indisponivel_recusa`.
- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TrilhaObrigatoriaTests"` → falha de compilação.
- [ ] **Step 3:** implemente as interfaces acima. No XML doc de `ITrilhaObrigatoria`, diga por que ela coexiste com a `ITrilhaDeAuditoria` (fail-closed × fail-open). Na escolha, diga que os critérios das duas **não devem ser unificados**.
- [ ] **Step 4:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TrilhaObrigatoriaTests|FullyQualifiedName~TrilhaDeAuditoriaTests"` → tudo passa, sem mudar `TrilhaDeAuditoriaTests`.
- [ ] **Step 5: Commit** `feat(auditoria): trilha obrigatoria fail-closed para exportacao (issue #5)`.

### Task 3: Web — `TrilhaDeExportacao` e registro do SDK

**Files:**

- Create: `src/Secco.Intranet.Web/Exportacao/TrilhaDeExportacao.cs`, `src/Secco.Intranet.Web/Exportacao/ExportacaoServiceCollectionExtensions.cs`
- Modify: `Directory.Packages.props` (`Secco.SDK.Documents`), `src/Secco.Intranet.Web/Secco.Intranet.Web.csproj` (`PackageReference`), `src/Secco.Intranet.Web/Program.cs` (uma chamada `builder.Services.AddIntranetExportacao();` logo depois de `AddIntranetInfrastructure`)
- Test: `tests/Secco.Intranet.Tests/Unit/TrilhaDeExportacaoTests.cs` (novo)

**Interfaces:**

- Consumes: `ITrilhaDeExportacao`, `ExportacaoParaAuditar`, `FiltroDeExportacao` (Task 1); `ITrilhaObrigatoria`, `VerbosDeAuditoria.ExportacaoGerar`, `RecursosDeAuditoria.Exportacao` (Task 2).
- Produces:
  - `public sealed class TrilhaDeExportacao(ITrilhaObrigatoria trilha) : ITrilhaDeExportacao`, com `public const int TamanhoMaximoDoFiltro = 200;`. Valida recurso, formato (`xlsx`/`pdf`, sem diferenciar caixa, gravado em minúsculas), `Linhas >= 0` e `Filtros` não nulo, e lança `ArgumentException`. Monta `RegistroDeAuditoria(ExportacaoGerar, Exportacao, recurso, metadata)`, com `metadata` = `JsonSerializer.Serialize` de `{ formato, linhas, filtros: { nome: valorCortado } }`. Nome repetido: vale o último. Devolve `result.IsSuccess`.
  - `public static class ExportacaoServiceCollectionExtensions` com `const string SectionKey = "Intranet:Exportacao"` e `static IServiceCollection AddIntranetExportacao(this IServiceCollection services)`, que chama `AddSeccoDocuments(SectionKey)` e `AddScoped<ITrilhaDeExportacao, TrilhaDeExportacao>()`. A Task 5 acrescenta o rate limiting aqui.

- [ ] **Step 1:** adicione o pacote. Confira a versão ao vivo (`gh api` no feed `rafsecco`, `MSYS_NO_PATHCONV=1` no Git Bash) e use a estável mais recente.
- [ ] **Step 2: Testes que falham** em `TrilhaDeExportacaoTests`, com um `ITrilhaObrigatoria` falso que guarda o registro e devolve um `Result` configurável:
  - `Registro_levaVerboRecursoEId`: `Verbo == "exportacao.gerar"`, `Recurso == "exportacao"`, `RecursoId == "relatorio-de-teste"`.
  - `Metadata_temFormatoLinhasEFiltros`: com `("Período", "01/10 a 31/10")`, o JSON parseado tem `formato == "xlsx"`, `linhas == 42` e `filtros["Período"] == "01/10 a 31/10"`.
  - `Filtro_longo_eCortadoEm200`: valor de 5.000 caracteres → 200 no metadata, e a trilha é chamada.
  - `Formato_emMaiusculas_eGravadoEmMinusculas`: `"PDF"` → `"pdf"`.
  - `Trilha_recusada_devolveFalse` e `Trilha_aceita_devolveTrue`.
  - `[Theory] Recurso_invalido_lanca`: `""`, `"Com Maiuscula"`, `"acentuação"`, `new string('a', 65)` → `ArgumentException`, e a trilha não é chamada.
  - `Formato_desconhecido_lanca` (`"csv"`), `Linhas_negativas_lanca`, `Filtros_nulos_lanca`.
  - `Secao_invalida_derrubaAValidacao`: `ServiceCollection` com configuração em memória `Intranet:Exportacao:MaxPdfRows = 0` e `AddIntranetExportacao()`, e resolver `IOptions<SeccoDocumentsOptions>.Value` → `OptionsValidationException`. É o que o `ValidateOnStart` do SDK dispara na subida.
- [ ] **Step 3:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TrilhaDeExportacaoTests"` → falha de compilação.
- [ ] **Step 4:** implemente as interfaces acima e registre em `Program.cs`.
- [ ] **Step 5:** filtro da Step 3 passa, e `dotnet test Secco.Intranet.slnx` inteiro passa (o host sobe com o SDK registrado).
- [ ] **Step 6: Commit** `feat(exportacao): trilha de exportacao e registro do Secco.SDK.Documents (issue #5)`.

### Task 4: Integração — exportar de uma application part externa

**Files:**

- Create: `tests/Secco.Intranet.Tests/Integration/Exportacao/ExportacaoDeTesteController.cs`, `tests/Secco.Intranet.Tests/Integration/ExportacaoTests.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs` (propriedade e registro da trilha falsa)

**Interfaces:**

- Consumes: `ITrilhaDeExportacao`, `FiltroDeExportacao`, `ExportacaoParaAuditar`, `PoliticaDeExportacao.Nome` (Task 1); `ITrilhaObrigatoria`, `TrilhaObrigatoriaDeDesenvolvimento` (Task 2); `ISeccoDocumentExporter`, `ExportRequest<T>`, `ExportColumns<T>`, `ToHttpResult()` do SDK.
- Produces:
  - `IntranetWebFactory.TrilhaObrigatoria` (`ITrilhaObrigatoria?`, get/set), com `services.AddScoped<ITrilhaObrigatoria>(sp => TrilhaObrigatoria ?? ActivatorUtilities.CreateInstance<TrilhaObrigatoriaDeDesenvolvimento>(sp))` em `ConfigureTestServices`. É o mesmo adapter que o ambiente Testing escolheria.
  - `ExportacaoDeTesteController` (público, no assembly de testes, que já é application part):
    - `[HttpGet("/exportacao-de-teste")]` devolve `Content` com o `RequestToken` de `IAntiforgery.GetAndStoreTokens(HttpContext)`.
    - `[HttpPost("/exportacao-de-teste")]`, `[ValidateAntiForgeryToken]`, `[EnableRateLimiting(PoliticaDeExportacao.Nome)]`, parâmetro `string formato` do form. Gera 3 linhas em memória (`IAsyncEnumerable`) com uma coluna de texto e chama `ITrilhaDeExportacao` com recurso `"relatorio-de-teste"` e filtro `("Filtro", <campo "filtro" do form>)`. Recusado: `DisposeAsync` e `Redirect("/exportacao-de-teste")`. Aceito: `ToHttpResult()`. Segue a ordem da spec, com `try/catch` que descarta o documento.

- [ ] **Step 1: Testes que falham** em `ExportacaoTests` (`IClassFixture<IntranetWebFactory>`). O cliente leva o header de tenant e `RolesDeTesteMiddleware.HeaderUsuario` com um **Guid novo por teste**, para nenhum teste gastar o limite de outro (o contador é do host, compartilhado pela classe), e `AllowAutoRedirect = false`. O token vem do GET e vai no campo `__RequestVerificationToken`, como em `AcessoTelasTests`:
  - `Testing_semLogStream_exporta`: `POST` com `formato=xlsx` → `200`, `Content-Disposition` começa com `attachment`, `Content-Type` do `.xlsx`, corpo não vazio.
  - `Pdf_exporta`: `formato=pdf` → `200`, `application/pdf`.
  - `TrilhaQueFalha_naoEnvia`: `factory.TrilhaObrigatoria` = falso que devolve `TrilhaIndisponivel` → `302` para `/exportacao-de-teste` e sem `Content-Disposition`. Restaure `null` num `finally`.
  - `TrilhaRegistra_antesDoEnvio`: falso que aceita e guarda os registros → depois do `200`, um registro com `RecursoId == "relatorio-de-teste"` e metadata com `"linhas":3`.
  - `SemAntiforgery_recusa`: `POST` sem token → `400`, e o falso não recebe registro.
- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~ExportacaoTests"` → falha (controller inexistente).
- [ ] **Step 3:** implemente o controller e o registro na fábrica.
- [ ] **Step 4:** o filtro da Step 2 passa, e `SlugsReservadosTests` continua passando (a rota nova reserva `exportacao-de-teste`).
- [ ] **Step 5: Commit** `test(exportacao): exportacao por application part externa com trilha fail-closed (issue #5)`.

### Task 5: Limite por pessoa

**Files:**

- Create: `src/Secco.Intranet.Web/Exportacao/LimiteDeExportacao.cs`
- Modify: `src/Secco.Intranet.Web/Exportacao/ExportacaoServiceCollectionExtensions.cs` (chama o registro do limite), `src/Secco.Intranet.Web/Program.cs` (`app.UseRateLimiter();` logo depois do bloco do `UseAuthorization()` e antes de `MapSeccoHealthChecks()`, com um comentário dizendo que precisa vir depois da autenticação e da tenancy para a partição ver o `sub`)
- Test: `tests/Secco.Intranet.Tests/Integration/ExportacaoLimiteTests.cs` (novo), `tests/Secco.Intranet.Tests/Unit/LimiteDeExportacaoTests.cs` (novo)

**Interfaces:**

- Consumes: `PoliticaDeExportacao.Nome` (Task 1); `ExportacaoServiceCollectionExtensions.SectionKey` (Task 3); `ExportacaoDeTesteController` (Task 4).
- Produces: `internal static class LimiteDeExportacao` com `const string ChavePorPessoaPorMinuto = "PorPessoaPorMinuto"`, `const int PadraoPorMinuto = 10` e `static IServiceCollection AddLimiteDeExportacao(this IServiceCollection services)`. É o desenho do `ExportRateLimiting` do LogStream (`secco-platform/src/LogStream/Secco.LogStream.Api/Endpoints/ExportRateLimiting.cs`): leitura na montagem das options, `InvalidOperationException` com o nome completo da chave quando menor que 1, partição por `SeccoClaims.Subject` (`string.Empty` sem claim).

- [ ] **Step 1: Testes que falham.**
  - `LimiteDeExportacaoTests.PorMinuto_menorQueUm_derrubaAMontagem`: `ServiceCollection` com configuração em memória `Intranet:Exportacao:PorPessoaPorMinuto = 0`, e resolver `IOptions<RateLimiterOptions>.Value` → `InvalidOperationException` cuja mensagem contém `Intranet:Exportacao:PorPessoaPorMinuto`.
  - `ExportacaoLimiteTests`, com fábrica própria (`factory.WithWebHostBuilder` + `UseSetting("Intranet:Exportacao:PorPessoaPorMinuto", "2")`):
    - `AcimaDoLimite_429`: três `POST` do mesmo usuário → `200`, `200`, `429`.
    - `Limite_ePorPessoa`: dois `POST` do usuário A, e então um do usuário B → `200`.
- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~LimiteDeExportacao|FullyQualifiedName~ExportacaoLimiteTests"` → falha.
- [ ] **Step 3:** implemente e ligue o middleware.
- [ ] **Step 4:** o filtro da Step 2 passa, e `dotnet test Secco.Intranet.slnx` inteiro passa (o `UseRateLimiter` não afeta rota sem política).
- [ ] **Step 5: Commit** `feat(exportacao): limite de exportacao por pessoa (issue #5)`.

### Task 6: Documentação, ADR-0013 e roadmap

**Files:**

- Modify: `docs/extensao.md`, `docs/guia-de-deploy.md`, `docs/adr/secco-intranet-adrs.md`, `docs/roadmap.md`, `docs/specs/2026-11-04-exportacao-de-documentos-design.md` (estado "aprovado, implementado")

- [ ] **Step 1:** `docs/extensao.md`: o título e a abertura passam a falar dos pontos de extensão (menu e exportação). Uma seção nova, "Exportando Excel e PDF", com:
  - as referências (`Extensibility` + `Secco.SDK.Documents`);
  - o formulário (`<form method="post">` com os filtros em `<input type="hidden">`; o tag helper põe o antiforgery);
  - o controller completo (`[HttpPost]`, `[ValidateAntiForgeryToken]`, `[EnableRateLimiting(PoliticaDeExportacao.Nome)]`, a política de permissão da própria RCL, gerar → `ITrilhaDeExportacao` → `ToHttpResult()`, descarte na recusa e na exceção, `TooManyRows` e a mensagem genérica pelo `TempData`);
  - as regras do recurso e dos filtros (200 caracteres; nunca conteúdo nem dado que não seja filtro);
  - o que muda por ambiente (sem LogStream: exporta em DEV/Testing, recusa fora).

  Baseie o exemplo no `ExportacaoDeTesteController` da Task 4, trocando a resposta de recusa pelo `TempData`.
- [ ] **Step 2:** `docs/guia-de-deploy.md`: linhas novas na tabela de variáveis (`Intranet__Exportacao__MaxXlsxRows`, `__MaxPdfRows`, `__MaxConcurrentExports`, `__TempDirectory`, `__PorPessoaPorMinuto`, com os padrões e a validação), e na linha de `Intranet__Auditoria__LogStreamUrl` o aviso de que, fora de Development, exportação exige a URL.
- [ ] **Step 3:** ADR-0013 em `docs/adr/secco-intranet-adrs.md`, no formato da ADR-0012: "Exportação: trilha fail-closed ao lado da fail-open, e o `Extensibility` além do menu". Contexto (ADR-0058 da plataforma, a trilha atual fail-open). Decisão (porta obrigatória separada, escolha por URL e ambiente, contrato no `Extensibility` com tipos próprios, política nomeada, seção `Intranet:Exportacao`, parcial de tema adiado). Consequências (critérios diferentes de propósito, LogStream obrigatório para exportar fora de DEV, mudança no contrato = incompatível como na ADR-0011). Atualize o índice no topo do arquivo, se houver.
- [ ] **Step 4:** `docs/roadmap.md`: item da issue #5 marcado como entregue, na fase em que os itens de plataforma adotada ficam.
- [ ] **Step 5:** `dotnet build Secco.Intranet.slnx` e `dotnet test Secco.Intranet.slnx` inteiro passam. Confira que nenhum doc cita nome da origem do fork.
- [ ] **Step 6: Commit** `docs(exportacao): adr-0013, guia de extensao e deploy (issue #5)`.
