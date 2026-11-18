# Atributos do Entra no Diretório — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** o Diretório e o Organograma da intranet usam cargo, departamento (como setor), telefone e gestor vindos do Entra pela API `directory:read` do SecureGate, com precedência por campo sobre a edição manual e o CSV.

**Architecture:**
- **Leitura:** o adaptador `UsuariosParaDiretorioDoSecureGate` passa a ler `ListDirectoryPeople`, com um token `directory:read` próprio. Em qualquer falha, cai para a leitura de hoje (`securegate:tenant-manage`).
- **Precedência:** os atributos do Entra viajam em `UsuarioParaDiretorio`, e o `MontadorDePessoas` (Application, puro) aplica a precedência e o casamento departamento → setor.
- **Edição e CSV:** consultam os marcadores "do Entra" do `PessoaDto` para travar os campos.
- **Banco:** nada do Entra é gravado no banco da intranet.

**Tech Stack:** .NET 10, ASP.NET Core MVC, `Secco.SecureGate.Client` 0.25.0, `Secco.SDK.ClientCredentials`, xUnit + FluentAssertions 7 + NSubstitute.

**Spec:** [docs/specs/2026-11-17-atributos-do-entra-no-diretorio-design.md](../specs/2026-11-17-atributos-do-entra-no-diretorio-design.md)

## Global Constraints

- **Precedência:** o atributo só vale como "do Entra" com `AttributesSource == "directory"` **e** o campo preenchido (não nulo nem em branco). Senão, vale o `PerfilColaborador`.
- **O `PerfilColaborador` nunca é escrito com valor do Entra.** Campo travado mantém o valor guardado.
- **Departamento → setor:** casa com um setor **ativo** cujo nome normalizado é igual. A normalização é `Trim`, depois `Normalize(FormD)` sem `UnicodeCategory.NonSpacingMark`, depois `ToLowerInvariant`, com comparação ordinal. Precisa casar com **exatamente um** setor; nenhum ou mais de um → não casa.
- **Telefone:** campo novo, só leitura, ao lado do Ramal. O Ramal não muda.
- **Selo na tela:** "vem do Entra".
- **Fonte das pessoas:**
  - `ListDirectoryPeople` paginado (`size` 100) até `page * size >= total`;
  - só `IsActive == true`;
  - credencial de `Secco:SecureGate:TenantManagement`;
  - escopo `directory:read`, com `SeccoAccessTokenStore` próprio (singleton, separado do de `tenant-manage`).
- **Fallback:** qualquer falha da leitura do diretório (exceção HTTP, token recusado, resposta inválida) → leitura de hoje pela `IGestaoDeAcesso`, sem atributos, com um `LogWarning` **por processo** (sem conteúdo, só o tipo da exceção). Só vira "indisponível" se a leitura de hoje também falhar.
- **Pacotes** em `Directory.Packages.props`:
  - `Secco.SecureGate.Client` 0.25.0;
  - `Secco.SharedKernel` 0.7.12;
  - `Secco.SDK.AspNetCore` 0.10.2;
  - `Secco.SDK.ClientCredentials` 0.1.22.
- **Views:** texto vindo do Entra sempre com o encoding padrão do Razor (nunca `Html.Raw`).
- **Commits** direto na `main`, Conventional Commits (`feat(diretorio): ...`), terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Não tocar em `docs/plans/2026-11-16-reserva-de-sala.md`, que é outro trabalho, não versionado.
- **Verificação final:** `dotnet build Secco.Intranet.slnx -c Release` sem warnings (warnings = erros) e `dotnet test Secco.Intranet.slnx`.

## Review Focus

1. **POST forjado na edição** com cargo diferente do Entra → o cargo guardado não muda. Pinado na Task 3 (`Editar_CampoDoEntra_PostDiferente_MantemValorGuardado`).
2. **Gestor manual que fecha ciclo com arestas do Entra** → recusado. Pinado na Task 3 (`Editar_GestorManualFechaCicloComGestorDoEntra_Recusa`) e na Task 4 para o CSV (`Importar_GestorQueFechaCicloComEntra_Rejeita`).
3. **Departamento que casa com dois setores** ("Finanças" e "Financas") → nenhum casa, o setor manual vale, e o departamento aparece como ambíguo. Pinado na Task 2 (`Casar_DoisSetoresComMesmoNomeNormalizado_NaoCasa`) e na Task 5 (`DepartamentosSemSetor_Ambiguo_MarcadoAParte`).
4. **Gestor do Entra que não está entre os ativos** → `GestorInativo = true`. A pessoa vai para "sem posição" e o organograma não quebra. Pinado na Task 2 (`Montar_GestorDoEntraForaDosAtivos_GestorInativo`).
5. **Diretório do SecureGate falhando no meio da paginação** → fallback inteiro para a leitura de hoje, nunca uma lista parcial. Pinado na Task 1 (`Listar_FalhaNaSegundaPagina_CaiParaGestaoSemListaParcial`).

---

### Task 1: Pacotes e leitura pela API de diretório com fallback

**Files:**
- Modify: `Directory.Packages.props` (as quatro versões das Global Constraints)
- Create: `src/Secco.Intranet.Application/Diretorio/AtributosDoEntra.cs`
- Modify: `src/Secco.Intranet.Application/Diretorio/IUsuariosParaDiretorio.cs` (`UsuarioParaDiretorio` ganha `AtributosDoEntra? Entra = null` no fim)
- Create: `src/Secco.Intranet.Infrastructure/Diretorio/LeitorDoDiretorioDoSecureGate.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Diretorio/UsuariosParaDiretorioDoSecureGate.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (registro do `HttpClient` nomeado e do leitor)
- Test: `tests/Secco.Intranet.Tests/Unit/UsuariosParaDiretorioTests.cs` e `tests/Secco.Intranet.Tests/Unit/LeitorDoDiretorioDoSecureGateTests.cs`

**Interfaces:**
- **Produces:**
  - `public sealed record AtributosDoEntra(string? Cargo, string? Departamento, string? Telefone, Guid? GestorUsuarioId)`. Os textos vêm já aparados, e branco vira `null`. A instância só existe quando `AttributesSource == "directory"`.
  - `UsuarioParaDiretorio(Guid Id, string Email, string? Nome = null, AtributosDoEntra? Entra = null)`.
  - Infrastructure, `internal sealed class LeitorDoDiretorioDoSecureGate`:
    - `Task<IReadOnlyList<UsuarioParaDiretorio>> ListarAtivosAsync(CancellationToken)`;
    - lança em qualquer falha, e quem decide o fallback é o adaptador.
- **Consumes:** `ISecureGateClient`/`SecureGateClient` gerado (0.25.0), operação `ListDirectoryPeople(page, size)` → `PagedResult` de `DirectoryPersonDto`. Confirmar o nome exato do método gerado; deve ser `ListDirectoryPeopleAsync`.

- [ ] **Step 1: Testes que falham:**
  - **`LeitorDoDiretorioDoSecureGateTests`**, com `HttpMessageHandler` falso respondendo o token e o JSON da API:
    - `Listar_PercorreTodasAsPaginas`: 2 páginas com `total` 150.
    - `Listar_SoAtivos`.
    - `Listar_AtributosSoComOrigemDirectory`: `attributesSource` nulo → `Entra == null`, mesmo com campos.
    - `Listar_BrancoViraNulo`.
    - `Listar_PedeEscopoDirectoryRead`: o corpo do `/connect/token` tem `scope=directory%3Aread`.
  - **`UsuariosParaDiretorioTests`**:
    - `Listar_LeitorFunciona_UsaDiretorioEAtributos`.
    - `Listar_LeitorLanca_CaiParaGestaoSemAtributos`: atributos nulos e aviso logado uma vez.
    - `Listar_FalhaNaSegundaPagina_CaiParaGestaoSemListaParcial`.
    - `Listar_LeitorEGestaoFalham_Indisponivel`.
    - O cache de 60 s continua valendo para as duas fontes.
- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests -c Release --filter "FullyQualifiedName~UsuariosParaDiretorioTests|FullyQualifiedName~LeitorDoDiretorioDoSecureGateTests"` → falha.
- [ ] **Step 3: Implementar.**
  - **Registro:** `HttpClient` nomeado `secco-intranet-directory` com `SeccoClientCredentialsHandler(baseUrl, clientId, clientSecret, "directory:read", store)`. A URL vem de `Secco:SecureGate:BaseUrl` e as credenciais de `Secco:SecureGate:TenantManagement`, as mesmas que `SecureGateTenantManagement` já lê. O `store` é um singleton próprio, e o handler é adicionado só quando `SecureGateTenantManagement.EstaDisponivel`.
  - **Leitor:** cria `new SecureGateClient(httpClient)`; se o construtor gerado não aceitar só o `HttpClient`, use o que aceitar `BaseUrl` + `HttpClient`.
  - **Adaptador:** tenta o leitor e, em exceção (`HttpRequestException`, `ApiException`, `TaskCanceledException` que não seja cancelamento do chamador, `JsonException`), usa o caminho atual. O aviso usa `[LoggerMessage]` e sai uma vez por processo (`static int` com `Interlocked`).
- [ ] **Step 4:** mesmo filtro → verde. `dotnet build Secco.Intranet.slnx -c Release` → sem warnings.
- [ ] **Step 5:** commit `feat(diretorio): ler pessoas pela API de diretorio do SecureGate com atributos do Entra`.

---

### Task 2: Precedência e departamento → setor no montador

**Files:**
- Create: `src/Secco.Intranet.Application/Diretorio/CasadorDeDepartamento.cs`
- Modify: `src/Secco.Intranet.Application/Diretorio/PessoaDto.cs`
- Modify: `src/Secco.Intranet.Application/Diretorio/MontadorDePessoas.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/CasadorDeDepartamentoTests.cs`, `tests/Secco.Intranet.Tests/Unit/MontadorDePessoasTests.cs` (criar se não existir) e `ConstrutorDeOrganogramaTests.cs`

**Interfaces:**
- **Consumes:** `AtributosDoEntra`, `UsuarioParaDiretorio.Entra` (Task 1).
- **Produces:**
  - `public static class CasadorDeDepartamento`:
    - `static string Normalizar(string nome)`;
    - `static SetorDto? Casar(string? departamento, IReadOnlyList<SetorDto> setores)`, só ativos e exatamente um;
    - `static bool EhAmbiguo(string departamento, IReadOnlyList<SetorDto> setores)`, mais de um ativo casa.
  - `PessoaDto` ganha, **no fim e com padrão**: `string? Telefone = null, bool CargoDoEntra = false, bool SetorDoEntra = false, bool GestorDoEntra = false`. Cargo, setor e gestor do DTO passam a ser os **efetivos**.

- [ ] **Step 1: Testes que falham:**
  - **`CasadorDeDepartamentoTests`:**
    - `Casar_IgnoraMaiusculasAcentosEEspacos`: `"  Finanças "` casa com `"FINANCAS"`.
    - `Casar_SetorInativo_NaoCasa`.
    - `Casar_Nenhum_Nulo`.
    - `Casar_DoisSetoresComMesmoNomeNormalizado_NaoCasa`.
    - `Casar_DepartamentoNuloOuBranco_Nulo`.
  - **`MontadorDePessoasTests`:**
    - `Montar_EntraPreenchido_VenceEMarca`, um teste para cada um de cargo, setor e gestor.
    - `Montar_EntraVazio_UsaPerfil`.
    - `Montar_SemPerfilSoEntra_PreencheTudo`.
    - `Montar_DepartamentoSemSetor_SetorDoPerfilEditavel`.
    - `Montar_Telefone_SoDoEntra`.
    - `Montar_GestorDoEntraForaDosAtivos_GestorInativo`.
  - **`ConstrutorDeOrganogramaTests`:** `Construir_SoComGestoresDoEntra_MontaArvore` (nenhum `PerfilColaborador`).
- [ ] **Step 2:** `--filter "FullyQualifiedName~CasadorDeDepartamentoTests|FullyQualifiedName~MontadorDePessoasTests|FullyQualifiedName~ConstrutorDeOrganogramaTests"` → falha.
- [ ] **Step 3:** implementar. O montador continua puro e já recebe os setores.
- [ ] **Step 4:** mesmo filtro, mais `~LeituraDoDiretorioHandlersTests` (regressão) → verde.
- [ ] **Step 5:** commit `feat(diretorio): precedencia do Entra e departamento como setor pelo nome`.

---

### Task 3: Edição da pessoa travada pelo Entra

**Files:**
- Modify: `src/Secco.Intranet.Application/Diretorio/EditarDadosFuncionaisHandler.cs`
- Modify: `src/Secco.Intranet.Web/Views/Diretorio/Editar.cshtml`
- Test: `tests/Secco.Intranet.Tests/Unit/EscritaDoDiretorioHandlersTests.cs` e `ObterPessoaParaEdicaoHandlerTests.cs`

**Interfaces:**
- **Consumes:** `PessoaDto` com os marcadores (Task 2) e `MontadorDePessoas.Montar`.
- **Produces:** nada novo. O comportamento muda dentro do handler.

**Regras:**
- O handler monta as pessoas (como o `ObterPessoaParaEdicaoHandler` já faz) para saber, da pessoa editada, quais campos são do Entra.
- Para cada campo travado, ele passa ao `EditarDadosFuncionais` o valor **guardado** do perfil (`perfil?.Cargo`, `perfil?.SetorId`, `perfil?.GestorUsuarioId`) no lugar do valor do comando.
- A validação de setor e de gestor só roda para campos não travados.
- A checagem de ciclo usa o mapa de **gestor efetivo** (`pessoas` → `GestorUsuarioId`), não o dos perfis.
- **View:** campo travado aparece `disabled` com o valor efetivo e o selo "vem do Entra". O Telefone aparece só leitura, quando houver.

- [ ] **Step 1: Testes que falham:**
  - `Editar_CampoDoEntra_PostDiferente_MantemValorGuardado`, para cargo, setor e gestor.
  - `Editar_CampoSemEntra_AplicaComoHoje`.
  - `Editar_GestorManualFechaCicloComGestorDoEntra_Recusa`.
  - `Editar_SoCampoTravadoNoPost_NaoGeraTrilha`, porque nenhum campo muda.
  - `ObterParaEdicao_ExpoeMarcadores`.
- [ ] **Step 2:** `--filter "FullyQualifiedName~EscritaDoDiretorioHandlersTests|FullyQualifiedName~ObterPessoaParaEdicaoHandlerTests"` → falha.
- [ ] **Step 3:** implementar o handler e a view.
- [ ] **Step 4:** mesmo filtro → verde. Se houver teste de UI ou smoke da tela de edição em `tests/.../Ui`, rodar.
- [ ] **Step 5:** commit `feat(diretorio): campos do Entra so leitura na edicao da pessoa`.

---

### Task 4: CSV respeita o Entra

**Files:**
- Modify: `src/Secco.Intranet.Application/Diretorio/ImportarDiretorioHandler.cs` (`LinhaDoRelatorio` ganha `string? Aviso = null` no fim)
- Modify: `src/Secco.Intranet.Web/Views/Diretorio/ImportarRelatorio.cshtml` (mostra o aviso)
- Test: `tests/Secco.Intranet.Tests/Unit/ImportarDiretorioHandlerTests.cs`

**Regras:**
- No planejamento, para a pessoa cujo campo é do Entra, a coluna correspondente (`cargo` → `CargoDoEntra`, `setor` → `SetorDoEntra`, `gestor` → `GestorDoEntra`) é tratada como **ausente**. A linha leva um aviso, com os campos em ordem fixa e separados por vírgula: `"cargo vem do Entra; coluna ignorada"` ou `"cargo, gestor vêm do Entra; colunas ignoradas"`.
- A checagem de ciclo do CSV usa o gestor efetivo, com as arestas do Entra, mais os gestores já planejados no próprio arquivo.
- Sem Entra, o relatório fica idêntico ao de hoje.

- [ ] **Step 1: Testes que falham:**
  - `Importar_ColunaDoEntra_IgnoradaComAviso`.
  - `Importar_SemEntra_RelatorioIgualAoDeHoje`, usando os casos existentes como regressão.
  - `Importar_GestorQueFechaCicloComEntra_Rejeita`.
  - `Importar_SoColunasTravadas_SemAlteracaoComAviso`.
- [ ] **Step 2:** `--filter FullyQualifiedName~ImportarDiretorioHandlerTests` → falha.
- [ ] **Step 3:** implementar o handler e a view.
- [ ] **Step 4:** mesmo filtro → verde.
- [ ] **Step 5:** commit `feat(diretorio): importacao por CSV ignora colunas que vem do Entra`.

---

### Task 5: Departamentos sem setor, telefone nas telas e dados de desenvolvimento

**Files:**
- Create: `src/Secco.Intranet.Application/Diretorio/ListarDepartamentosSemSetorHandler.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/DiretorioController.cs` (ação GET de importar), `Views/Diretorio/Importar.cshtml`, `Views/Diretorio/Pessoa.cshtml` e `Views/Diretorio/Index.cshtml` (Telefone ao lado do Ramal), e o registro do handler onde os outros do Diretório são registrados
- Modify: `src/Secco.Intranet.Infrastructure/Diretorio/PessoasDeDesenvolvimento.cs` e `UsuariosParaDiretorioDeDesenvolvimento.cs` (algumas pessoas com `AtributosDoEntra`: um setor casado, um departamento sem setor, um telefone, gestores formando uma árvore)
- Test: `tests/Secco.Intranet.Tests/Unit/ListarDepartamentosSemSetorHandlerTests.cs`

**Interfaces:**
- **Produces:**
  - `public sealed record DepartamentoSemSetorDto(string Departamento, int Pessoas, bool Ambiguo)`;
  - `ListarDepartamentosSemSetorHandler.HandleAsync(CancellationToken) → Result<IReadOnlyList<DepartamentoSemSetorDto>>`, que usa `IUsuariosParaDiretorio` e `ISetorRepository`, agrupa pelo nome normalizado, exibe a primeira grafia e ordena pelo nome;
  - na página de importação, a seção "Departamentos do Entra sem setor" só aparece quando a lista não estiver vazia.

- [ ] **Step 1: Testes que falham:**
  - `DepartamentosSemSetor_ContaPessoasAtivas`.
  - `DepartamentosSemSetor_Casados_NaoAparecem`.
  - `DepartamentosSemSetor_Ambiguo_MarcadoAParte`.
  - `DepartamentosSemSetor_GrafiasDiferentes_AgrupaPeloNormalizado`.
  - `DepartamentosSemSetor_SemEntra_Vazio`.
- [ ] **Step 2:** `--filter FullyQualifiedName~ListarDepartamentosSemSetorHandlerTests` → falha.
- [ ] **Step 3:** implementar o handler, o controller, as views e os dados de desenvolvimento.
- [ ] **Step 4:** mesmo filtro, mais `~AcessoAoDiretorioTests|~LeituraDoDiretorioHandlersTests` → verde. Build da solution sem warnings.
- [ ] **Step 5:** commit `feat(diretorio): departamentos do Entra sem setor e telefone nas telas`.

---

### Task 6: Documentação e verificação final

**Files:**
- Modify: `docs/adr/secco-intranet-adrs.md`. Adicionar uma **ADR-0015** curta, "Atributos do Entra no Diretório", e atualizar a "Última atualização".
  - **Contexto:** dois cadastros do mesmo dado.
  - **Decisão:** precedência por campo, nada gravado localmente, departamento → setor pelo nome com unicidade, telefone ao lado, fallback.
  - **Alternativas:** mapeamento configurável e substituir o ramal, adiados e descartados como no spec.
  - **Consequências:** o admin alinha nomes de setor com o Entra, e a lista de sem setor mostra as divergências.
- Modify: `docs/guia-de-deploy.md`, na seção dos três clients. O `directory:read` do client de gestão passa a alimentar o Diretório. Sem ele, o Diretório funciona sem os atributos do Entra e avisa no log. Para ter os atributos, o SecureGate precisa da ADR-0059 (`User.Read.All` no Entra, ver o README do SecureGate).
- Modify: `docs/specs/2026-11-17-atributos-do-entra-no-diretorio-design.md`:
  - "Estado" passa a entregue;
  - a regra "Sem `directory:read` concedido" passa a dizer que **qualquer** falha da leitura do diretório cai para a leitura de hoje. Decisão do plano: a recusa do token chega como `HttpRequestException` genérica do `SeccoClientCredentialsHandler`, sem status distinguível.
- Modify: `CHANGELOG.md` e `docs/roadmap.md`, seguindo o formato das entradas existentes.
- [ ] **Step 1:** escrever a documentação.
- [ ] **Step 2: Verificação pelo comando documentado.** Esperado: build sem warning e testes verdes. Falha de suíte inteira por container: rodar a classe isolada antes de concluir que é regressão.

```bash
dotnet build Secco.Intranet.slnx -c Release
dotnet test Secco.Intranet.slnx
```

- [ ] **Step 3:** checklist da skill `secco-platform-standards` do repositório (camadas, Result, segurança ADR-0020).
- [ ] **Step 4:** commit `docs: ADR-0015 e guia dos atributos do Entra no diretorio`.
