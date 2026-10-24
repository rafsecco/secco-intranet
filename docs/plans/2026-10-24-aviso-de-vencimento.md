# Aviso de vencimento — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada setor mantém itens com data de vencimento, e a Intranet avisa o responsável e os admins do setor em marcos escalonados antes da data e no próprio dia.

**Architecture:** Domínio puro (`ItemVencimento` e `RegrasDeAviso`) decide estado e marcos; um handler de aplicação transforma a decisão em lotes para o `INotificadorDeMensagens` já existente; um `BackgroundService` em processo percorre o `ITenantCatalog` de hora em hora e chama esse handler por tenant. Telas sob a árvore do setor, num controller próprio.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core (SQL Server e Postgres), xUnit + AwesomeAssertions, `Secco.SDK.AspNetCore` (`ITenantCatalog`, `SetTenant`), `Secco.NotificationHub.Client` 0.6.0.

**Spec:** [`docs/specs/2026-10-24-aviso-de-vencimento-design.md`](../specs/2026-10-24-aviso-de-vencimento-design.md). Ler inteira antes da primeira task.

## Global Constraints

- Antecedências: de 1 a 6 valores distintos, cada um de 1 a 365; padrão `30, 7, 1`. O marco `0` (no dia) sempre existe e não entra na lista.
- Canais: faltando mais de 7 dias, `in_app`; 7 ou menos, e no dia, `in_app` + `email`. Nunca `teams`/`slack`.
- `Source`/`Type` da notificação: `vencimentos` / `aviso`.
- Configuração em `Intranet:Vencimentos`: `Habilitado` (`true`), `IntervaloEmMinutos` (`60`), `FusoHorario` (`America/Sao_Paulo`).
- Rotas: lista pelo caminho da árvore (`/{slug}/{slug-do-item}`); detalhe e ações em `/{slug}/vencimentos/...`.
- Leitura `setor-{slug}:read`; escrita `setor-{slug}:write`; recurso desligado → 404 na leitura e na escrita.
- Auditoria: `vencimento.criar`, `vencimento.editar`, `vencimento.renovar`, `vencimento.encerrar`, `vencimento.reativar`; recurso `vencimento`. O envio automático não vai à trilha.
- Banco: notação húngara (skill `secco-db-naming`), migration nos dois engines, mesmo nome nos dois.
- Código: tabs de 4 em `.cs`/`.cshtml` (`.editorconfig`), doc XML em português em todo tipo público, `ConfigureAwait(false)` como nos vizinhos.
- Telas nos dois temas, claro e escuro, sem markup estrutural próprio no core (skill `secco-intranet-temas`).
- Sem instalação em uso: nenhuma nota de upgrade nem compatibilidade com dado existente.

## Review Focus

1. **Duas instâncias da aplicação no mesmo instante** → um único aviso por marco. Teste de integração na Task 2 (`TentarRegistrarAsync` concorrente devolve `true` uma vez só).
2. **Hub fora do ar no meio de um tenant** → o marco volta a ficar pendente e os outros itens seguem. Teste na Task 5 (`HubIndisponivel_DesfazORegistro_ESegueParaOProximoItem`).
3. **Fuso horário: 23h30 em São Paulo já é amanhã em UTC** → "hoje" vem do fuso configurado, nunca do UTC. Teste na Task 4 (`Hoje_UsaOFusoConfigurado_NaViradaDoDiaUtc`).
4. **Responsável que saiu do setor** → deixa de receber e aparece como "sem acesso ao setor". Teste na Task 5 (`ResponsavelSemRoleDoSetor_NaoRecebe`).
5. **Item vencendo no dia do cadastro, ou cadastrado já vencido** → o primeiro sai na próxima execução como "vence hoje"; o segundo não dispara nada. Testes na Task 1 (`Decidir_CadastradoNoDia_EnviaMarcoZero`, `Decidir_CadastradoJaVencido_PulaTudo`).

---

### Task 1: Domínio do vencimento e regra dos marcos

**Files:**

- Create: `src/Secco.Intranet.Domain/Vencimentos/ItemVencimento.cs`
- Create: `src/Secco.Intranet.Domain/Vencimentos/RenovacaoDeVencimento.cs`
- Create: `src/Secco.Intranet.Domain/Vencimentos/AvisoDeVencimentoEnviado.cs`
- Create: `src/Secco.Intranet.Domain/Vencimentos/RegrasDeAviso.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/ItemVencimentoTests.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/RegrasDeAvisoTests.cs`

**Interfaces:**

- Produces:
  - `enum EstadoDoVencimento { EmDia, AVencer, VenceHoje, Vencido, Encerrado }`
  - `enum ResultadoDoAviso { Enviado = 0, Pulado = 1, SemDestinatario = 2 }`
  - `sealed class ItemVencimento : BaseEntity`
    - ctor `(Guid setorId, string titulo, string? descricao, DateOnly dataVencimento, Guid? responsavelId, IReadOnlyList<int>? antecedencias)`; `null` em `antecedencias` = `AntecedenciasPadrao`.
    - `static readonly IReadOnlyList<int> AntecedenciasPadrao = [30, 7, 1]`
    - propriedades `SetorId`, `Titulo`, `Descricao`, `DataVencimento`, `ResponsavelId`, `IReadOnlyList<int> Antecedencias` (ordenada decrescente), `int Ciclo` (nasce 1), `DateTimeOffset? EncerradoEm`, `string? EncerradoPor`, `DateTimeOffset CreatedAt`
    - `bool Editar(string titulo, string? descricao, DateOnly dataVencimento, Guid? responsavelId, IReadOnlyList<int> antecedencias)`: devolve `true` quando a data mudou, e então `Ciclo++`
    - `RenovacaoDeVencimento Renovar(DateOnly novaData, DateOnly hoje, string renovadoPor, string? observacao)`
    - `void Encerrar(string encerradoPor, DateTimeOffset agora)`
    - `void Reativar(DateOnly novaData, DateOnly hoje)`: limpa o encerramento e faz `Ciclo++`
  - `sealed class RenovacaoDeVencimento : BaseEntity` com `ItemVencimentoId`, `CicloAnterior`, `DataAnterior`, `DataNova`, `RenovadoPor`, `RenovadoEm`, `Observacao`
  - `sealed class AvisoDeVencimentoEnviado : BaseEntity`, ctor `(Guid itemVencimentoId, int ciclo, int marco, ResultadoDoAviso resultado, int quantidadeDeDestinos, DateTimeOffset tratadoEm)`
  - `sealed record DecisaoDeAviso(int? MarcoAEnviar, IReadOnlyList<int> MarcosAPular, int DiasRestantes)`
  - `static class RegrasDeAviso`:
    - `EstadoDoVencimento Estado(ItemVencimento item, DateOnly hoje)`
    - `DecisaoDeAviso Decidir(ItemVencimento item, DateOnly hoje, IReadOnlySet<int> marcosTratadosNoCiclo)`
    - `IReadOnlyList<(int Marco, DateOnly Data)> ProximosAvisos(ItemVencimento item, DateOnly hoje, IReadOnlySet<int> marcosTratadosNoCiclo)`, para a tela de detalhe

Toda violação de invariante lança `DomainInvariantException` (padrão de `ItemInventario`). `Titulo` vazio, antecedências fora de 1–365, repetidas, mais de 6 ou lista vazia → exceção. `Renovar` exige `novaData > DataVencimento` **e** `novaData > hoje`; `Reativar` exige `novaData > hoje`; `Editar`/`Renovar` sobre item encerrado → exceção; `Encerrar` repetido é idempotente (não lança, não muda nada).

**Algoritmo de `Decidir`** (é o coração do recurso; os testes abaixo o fixam):

```text
devidos   = { N em Antecedencias | hoje >= DataVencimento - N }  ∪  { 0 se hoje >= DataVencimento }
pendentes = devidos − marcosTratadosNoCiclo
se item encerrado ou pendentes vazio      → (null, [], dias)
candidato = min(pendentes)
se candidato == 0 e hoje != DataVencimento → (null, pendentes, dias)       // marco 0 só no dia
se existe tratado t com t < candidato     → (null, pendentes, dias)       // mais distante que um já tratado
senão                                      → (candidato, pendentes − {candidato}, dias)
dias = DataVencimento.DayNumber - hoje.DayNumber
```

- [ ] **Step 1: Write the failing tests**

`ItemVencimentoTests`: `Criar_SemAntecedencias_UsaPadrao30_7_1`, `Criar_AntecedenciaForaDaFaixa_Lanca` (`[Theory]` com 0, 366), `Criar_AntecedenciasRepetidas_Lanca`, `Criar_MaisDeSeisAntecedencias_Lanca`, `Criar_DataNoPassado_Aceita`, `Editar_MudandoAData_AbreCicloNovo` (`Ciclo` 1 → 2, devolve `true`), `Editar_SemMudarAData_MantemOCiclo`, `Renovar_DataNaoPosteriorAAtual_Lanca`, `Renovar_DataNaoPosteriorAHoje_Lanca`, `Renovar_RegistraHistoricoEAbreCiclo` (`CicloAnterior == 1`, `DataAnterior` = data antiga, `Ciclo == 2`), `Encerrado_NaoEditaNemRenova`, `Encerrar_DuasVezes_NaoMudaOPrimeiroRegistro`, `Reativar_ExigeDataFutura_EAbreCiclo`.

`RegrasDeAvisoTests`, com vencimento em `2026-12-31` e antecedências padrão salvo indicação:

```csharp
[Theory]
[InlineData("2026-11-30", EstadoDoVencimento.EmDia)]    // véspera da janela de 30
[InlineData("2026-12-01", EstadoDoVencimento.AVencer)]  // primeiro dia da janela
[InlineData("2026-12-31", EstadoDoVencimento.VenceHoje)]
[InlineData("2027-01-01", EstadoDoVencimento.Vencido)]
public void Estado_NasFronteiras(string hoje, EstadoDoVencimento esperado) { ... }
```

- `Decidir_NoPrimeiroDiaDaJanela_EnviaMarco30` → `MarcoAEnviar == 30`, `DiasRestantes == 30`.
- `Decidir_VariosMarcosDevidos_EnviaSoOMaisProximo_EPulaOsOutros`: cadastrado em `2026-12-26` (5 dias) → `MarcoAEnviar == 7`, `MarcosAPular == [30]`, `DiasRestantes == 5`.
- `Decidir_MarcoJaTratado_NaoRepete`: tratados `{30}`, hoje `2026-12-10` → `MarcoAEnviar == null`.
- `Decidir_CadastradoNoDia_EnviaMarcoZero`: hoje `2026-12-31`, nenhum tratado → `MarcoAEnviar == 0`, `MarcosAPular == [30, 7, 1]` (em qualquer ordem).
- `Decidir_CadastradoJaVencido_PulaTudo`: hoje `2027-01-15` → `MarcoAEnviar == null`, `MarcosAPular` contém `0, 1, 7, 30`.
- `Decidir_MarcoZeroDepoisDoDia_Pula`: tratados `{30, 7, 1}`, hoje `2027-01-01` → `MarcoAEnviar == null`, `MarcosAPular == [0]`.
- `Decidir_MarcoMaisDistanteQueUmJaTratado_Pula`: antecedências `[30, 7]`, tratados `{7}`, hoje `2026-12-26` → `MarcoAEnviar == null`, `MarcosAPular == [30]`.
- `Decidir_MarcoNovoSemNadaTratado_Envia`: antecedências `[30]`, nada tratado, hoje `2026-12-21` → `MarcoAEnviar == 30`, `DiasRestantes == 10`.
- `Decidir_Encerrado_NaoEnviaNemPula`.
- `ProximosAvisos_ListaOsNaoTratadosFuturos_EmOrdemDeData`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~ItemVencimentoTests|FullyQualifiedName~RegrasDeAvisoTests"`
Expected: falha de compilação (tipos inexistentes).

- [ ] **Step 3: Implement the four domain files** with the signatures above. `Antecedencias` é guardada ordenada decrescente; o ctor de rehidratação do EF segue o padrão de `ItemInventario`.

- [ ] **Step 4: Run tests to verify they pass** (mesmo comando). Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Domain/Vencimentos tests/Secco.Intranet.Tests/Unit/ItemVencimentoTests.cs tests/Secco.Intranet.Tests/Unit/RegrasDeAvisoTests.cs
git commit -m "feat(vencimentos): dominio do item e regra dos marcos de aviso"
```

---

### Task 2: Persistência e migrations

**Files:**

- Create: `src/Secco.Intranet.Infrastructure/Mappings/ItemVencimentoConfiguration.cs` (inclui as configurações de `RenovacaoDeVencimento` e `AvisoDeVencimentoEnviado`, uma classe por entidade no mesmo padrão dos vizinhos; arquivos separados se o vizinho fizer assim)
- Create: `src/Secco.Intranet.Application/Vencimentos/IItemVencimentoRepository.cs`
- Create: `src/Secco.Intranet.Infrastructure/Repositories/ItemVencimentoRepository.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs` (três `DbSet`)
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (registro do repositório)
- Create: migration `Vencimentos` em `src/Secco.Intranet.Migrations.SqlServer` e `src/Secco.Intranet.Migrations.Postgres`
- Test: `tests/Secco.Intranet.Tests/Integration/VencimentoPersistenciaTests.cs`

**Interfaces:**

- Consumes: entidades da Task 1.
- Produces `IItemVencimentoRepository`:
  - `Task AddAsync(ItemVencimento item, CancellationToken ct = default)`
  - `Task<ItemVencimento?> GetParaEdicaoAsync(Guid id, CancellationToken ct = default)` (rastreado)
  - `Task<IReadOnlyList<ItemVencimento>> ListarDoSetorAsync(Guid setorId, bool incluirEncerrados, CancellationToken ct = default)`, ordenado por `DataVencimento`
  - `Task<IReadOnlyList<RenovacaoDeVencimento>> ListarRenovacoesAsync(Guid itemId, CancellationToken ct = default)`, mais recente primeiro
  - `Task<IReadOnlyList<AvisoDeVencimentoEnviado>> ListarAvisosAsync(Guid itemId, CancellationToken ct = default)`
  - `Task<IReadOnlyList<ItemParaAviso>> ListarAtivosParaAvisoAsync(CancellationToken ct = default)`: itens não encerrados de setor ativo com o item de menu `Vencimentos` ativo (e ancestrais ativos), cada um com `SetorSlug` e o conjunto de marcos já tratados no ciclo atual. `sealed record ItemParaAviso(ItemVencimento Item, string SetorSlug, IReadOnlySet<int> MarcosTratados)`
  - `Task<bool> TentarRegistrarAvisoAsync(AvisoDeVencimentoEnviado aviso, CancellationToken ct = default)`: `false` quando o índice único recusa (padrão `PerfilColaboradorRepository.TentarAdicionarAsync`: captura `DbUpdateException`, destaca, confere a existência, relança se for outra causa)
  - `Task RemoverAvisoAsync(Guid avisoId, CancellationToken ct = default)`
  - `Task AdicionarRenovacaoAsync(RenovacaoDeVencimento renovacao, CancellationToken ct = default)`
  - `Task SaveChangesAsync(CancellationToken ct = default)`

Mapeamento: FK para `Setor` sem navegação (como `ItemInventarioConfiguration`); `Titulo` 256, `Descricao` 4 096, `EncerradoPor`/`RenovadoPor` 256, `Observacao` 4 096; `Antecedencias` como coluna texto (`"30,7,1"`) com `ValueConverter` e `ValueComparer`; índice em (`SetorId`, `DataVencimento`); **índice único em (`ItemVencimentoId`, `Ciclo`, `Marco`)**; FK de `RenovacaoDeVencimento` e `AvisoDeVencimentoEnviado` para `ItemVencimento` com `Cascade` não é usada (o item nunca é excluído): `Restrict`. Os filtros de "ancestrais ativos" em `ListarAtivosParaAvisoAsync` podem ser feitos em memória sobre `ItensMenu` do setor, reaproveitando a lógica de `ResolverCaminhoDeMenuHandler.CaminhoDoTipoAsync`; a quantidade de setores é pequena.

- [ ] **Step 1: Write the failing tests** em `VencimentoPersistenciaTests` (mesmo fixture de banco de `InventarioPersistenciaTests`, nos dois engines se aquele cobrir os dois): `GravaERelêComAntecedencias`, `TentarRegistrarAviso_Duplicado_DevolveFalse`, `TentarRegistrarAviso_Concorrente_SoUmVence` (dois contextos, `Task.WhenAll`, exatamente um `true`), `ListarAtivosParaAviso_IgnoraEncerrado_SetorInativo_ERecursoDesligado`, `ListarAtivosParaAviso_TrazSoOsMarcosDoCicloAtual`.
- [ ] **Step 2: Run them** (`dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~VencimentoPersistenciaTests"`). Expected: falha de compilação.
- [ ] **Step 3: Implement mapping, DbSets, repository and DI registration.** O nome das tabelas e colunas sai da convention da ADR-0017; conferir com a skill `secco-db-naming` o que a convention não decide.
- [ ] **Step 4: Create the migrations**

```bash
dotnet ef migrations add Vencimentos --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add Vencimentos --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```

Conferir no arquivo gerado: três tabelas, o índice único com as três colunas, nenhuma alteração em tabela existente.

- [ ] **Step 5: Run the tests** (mesmo filtro, mais `MigracoesDosTenantsTests`). Expected: PASS.
- [ ] **Step 6: Commit**

```bash
git add src/Secco.Intranet.Infrastructure src/Secco.Intranet.Application/Vencimentos/IItemVencimentoRepository.cs src/Secco.Intranet.Migrations.SqlServer src/Secco.Intranet.Migrations.Postgres tests/Secco.Intranet.Tests/Integration/VencimentoPersistenciaTests.cs
git commit -m "feat(vencimentos): persistencia, indice unico por marco e migrations"
```

---

### Task 3: Tipo `Vencimentos` na árvore de menu

**Files:**

- Modify: `src/Secco.Intranet.Domain/Menu/ItemMenu.cs` (`Vencimentos = 4` em `TipoDeItemMenu`)
- Modify: `src/Secco.Intranet.Application/Menu/CriarItemMenuHandler.cs:90-110` (folha e no máximo um por setor, como Documentos/Avisos)
- Modify: `src/Secco.Intranet.Application/Setores/CreateSetorHandler.cs` (`bool HabilitarVencimentos = false` no comando; ordem alfabética entre os filhos: Avisos, Documentos, Vencimentos)
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs:140` (`Vencimentos` abre no caminho da árvore)
- Modify: `src/Secco.Intranet.Web/Views/Setores/Menu.cshtml:106-113` (opção Vencimentos quando o setor ainda não tem)
- Modify: formulário de criação de setor em `src/Secco.Intranet.Web/Views/Setores/` e o view model correspondente (checkbox "Habilitar Vencimentos", desmarcado)
- Modify: `src/Secco.Intranet.Application/Acesso/IntranetPermissoes.cs` (descrição da família de escrita: `"Alterar um setor específico: publicações, documentos, vencimentos e menu"`)
- Modify: `src/Secco.Intranet.Application/Menu/ResolverCaminhoDeMenuHandler.cs` (doc de `CaminhoDoTipoAsync` cita Vencimentos)
- Test: `tests/Secco.Intranet.Tests/Unit/CriarItemMenuHandlerTests.cs`, `CreateSetorHandlerTests.cs`, `ReconciliarItensDeMenuHandlerTests.cs`

`ReconciliarItensDeMenuHandler` **não muda**: o teste novo prova que ele não cria Vencimentos.

- [ ] **Step 1: Write the failing tests**: `CriarItemMenu_SegundoVencimentosNoSetor_Recusa`, `CriarItemMenu_FilhoDeVencimentos_Recusa`, `CreateSetor_ComHabilitarVencimentos_CriaOItem_DepoisDeDocumentos`, `CreateSetor_PorPadrao_NaoCriaVencimentos`, `Reconciliar_NaoCriaVencimentos`.
- [ ] **Step 2: Run** (`--filter "FullyQualifiedName~CriarItemMenuHandlerTests|FullyQualifiedName~CreateSetorHandlerTests|FullyQualifiedName~ReconciliarItensDeMenuHandlerTests"`). Expected: FAIL.
- [ ] **Step 3: Implement** the modifications listed.
- [ ] **Step 4: Run** the same filter plus `SetorMenuAdministracaoTests`. Expected: PASS.
- [ ] **Step 5: Commit** — `feat(vencimentos): tipo Vencimentos na arvore de menu do setor`.

---

### Task 4: Casos de uso de escrita e leitura

**Files:**

- Create: `src/Secco.Intranet.Application/Vencimentos/VencimentosOptions.cs` (`SectionKey = "Intranet:Vencimentos"`, `Habilitado = true`, `IntervaloEmMinutos = 60`, `FusoHorario = "America/Sao_Paulo"`)
- Create: `src/Secco.Intranet.Application/Vencimentos/RelogioDeVencimentos.cs`: `sealed class RelogioDeVencimentos(TimeProvider relogio, VencimentosOptions opcoes)` com `DateOnly Hoje()` e `DateTimeOffset Agora()`; o fuso é resolvido por `TimeZoneInfo.FindSystemTimeZoneById` (IANA funciona no Windows com ICU)
- Create: `src/Secco.Intranet.Application/Vencimentos/VencimentoDtos.cs`
- Create: `CriarVencimentoHandler.cs`, `EditarVencimentoHandler.cs`, `RenovarVencimentoHandler.cs`, `EncerrarVencimentoHandler.cs`, `ReativarVencimentoHandler.cs`, `ListarVencimentosDoSetorHandler.cs`, `ObterVencimentoHandler.cs`, em `src/Secco.Intranet.Application/Vencimentos/`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs` (classe `Vencimentos`: `NotFound`, `TituloRequired`, `TituloTooLong(int)`, `AntecedenciasInvalidas`, `DataDeRenovacaoInvalida`, `Encerrado`, `ResponsavelSemAcesso`)
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs` (cinco verbos e `RecursosDeAuditoria.Vencimento = "vencimento"`; remover o bloco de linhas em branco e `RecursosDeAuditoria.Tenant`, que sobraram da ADR-0010, depois de confirmar com `grep` que `Tenant` não tem uso)
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs` (handlers, `RelogioDeVencimentos`, `TimeProvider.System` se ainda não registrado)
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (bind de `VencimentosOptions`, mesmo padrão de `NotificacaoOptions`)
- Test: `tests/Secco.Intranet.Tests/Unit/VencimentoHandlersTests.cs`, `RelogioDeVencimentosTests.cs`

**Interfaces:**

- Consumes: `IItemVencimentoRepository` (Task 2), `ITrilhaDeAuditoria`, `IAtorAtual`, `IDiretorioDeUsuarios`.
- Produces:
  - `sealed record VencimentoResumoDto(Guid Id, string Titulo, DateOnly DataVencimento, EstadoDoVencimento Estado, Guid? ResponsavelId)`
  - `sealed record VencimentoDetalheDto(Guid Id, string Titulo, string? Descricao, DateOnly DataVencimento, EstadoDoVencimento Estado, Guid? ResponsavelId, IReadOnlyList<int> Antecedencias, int Ciclo, IReadOnlyList<(int Marco, DateOnly Data)> ProximosAvisos, IReadOnlyList<RenovacaoDeVencimento> Renovacoes, IReadOnlyList<AvisoDeVencimentoEnviado> Avisos)`
  - Comandos, todos com `Guid SetorId, string SetorSlug` para conferir que o item é do setor da rota (item de outro setor → `NotFound`):
    - `CriarVencimentoCommand(SetorId, SetorSlug, Titulo, Descricao, DataVencimento, ResponsavelId, IReadOnlyList<int>? Antecedencias)` → `Result<Guid>`
    - `EditarVencimentoCommand(Id, SetorId, SetorSlug, Titulo, Descricao, DataVencimento, ResponsavelId, Antecedencias)` → `Result<Guid>`
    - `RenovarVencimentoCommand(Id, SetorId, SetorSlug, NovaData, Observacao)` → `Result<Guid>`
    - `EncerrarVencimentoCommand(Id, SetorId, SetorSlug)` e `ReativarVencimentoCommand(Id, SetorId, SetorSlug, NovaData)` → `Result<Guid>`
  - `ListarVencimentosDoSetorHandler.HandleAsync(Guid setorId, bool incluirEncerrados, ct)` → `Result<IReadOnlyList<VencimentoResumoDto>>`
  - `ObterVencimentoHandler.HandleAsync(Guid id, Guid setorId, ct)` → `Result<VencimentoDetalheDto>`

Regras dos handlers: `DomainInvariantException` vira o `Error` de validação correspondente (padrão de `EditarItemInventarioHandler`); o responsável, quando informado, precisa ter a Role `{slug}-admin` ou `{slug}-user` em `IDiretorioDeUsuarios` → senão `ResponsavelSemAcesso`; quem registra renovação ou encerramento é `IAtorAtual.Atual()?.Nome`, e `"sistema"` no modo aberto de DEV; trilha com `titulo`, `setor` e as datas no JSON, como em `ArquivarDocumentoHandler`.

- [ ] **Step 1: Write the failing tests**: `Hoje_UsaOFusoConfigurado_NaViradaDoDiaUtc` (`FakeTimeProvider` em `2026-12-31T02:30Z` → `Hoje() == 2026-12-30` em São Paulo), `FusoInvalido_Lanca`; e, nos handlers, `Criar_RegistraNaTrilha`, `Criar_ResponsavelSemRoleDoSetor_Recusa`, `Editar_ItemDeOutroSetor_NotFound`, `Renovar_DataInvalida_DevolveErroDeValidacao`, `Renovar_GravaHistoricoERegistraNaTrilha`, `Encerrar_Repetido_NaoRegistraDuasVezes`, `Listar_OrdenaPorData_ESemEncerradosPorPadrao`, `Obter_CalculaEstadoEProximosAvisos`.
- [ ] **Step 2: Run** (`--filter "FullyQualifiedName~VencimentoHandlersTests|FullyQualifiedName~RelogioDeVencimentosTests"`). Expected: FAIL.
- [ ] **Step 3: Implement** the files listed. Se o pacote de `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) não estiver em `Directory.Packages.props`, acrescentá-lo na versão estável mais recente, consultada no NuGet na hora.
- [ ] **Step 4: Run** the same filter plus `TrilhaDeAuditoriaTests`. Expected: PASS.
- [ ] **Step 5: Commit** — `feat(vencimentos): casos de uso, relogio no fuso do produto e trilha`.

---

### Task 5: Decisão e envio dos avisos

**Files:**

- Create: `src/Secco.Intranet.Application/Vencimentos/AvisoDeVencimento.cs` (estática, pura, no molde de `AvisoDePublicacao`)
- Create: `src/Secco.Intranet.Application/Vencimentos/ProcessarAvisosDeVencimentoHandler.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/AvisoDeVencimentoTests.cs`, `ProcessarAvisosDeVencimentoHandlerTests.cs`

**Interfaces:**

- Consumes: `RegrasDeAviso.Decidir` (Task 1), `IItemVencimentoRepository.ListarAtivosParaAvisoAsync`/`TentarRegistrarAvisoAsync`/`RemoverAvisoAsync` (Task 2), `RelogioDeVencimentos` (Task 4), `IDiretorioDeUsuarios`, `INotificadorDeMensagens`, `NotificacaoOptions.UrlBase`.
- Produces:
  - `AvisoDeVencimento.Origem = "vencimentos"`, `AvisoDeVencimento.Tipo = "aviso"`
  - `IReadOnlyList<string> AvisoDeVencimento.Canais(int diasRestantes)`
  - `IReadOnlyList<UsuarioDoTenant> AvisoDeVencimento.Destinatarios(IReadOnlyList<UsuarioDoTenant> usuarios, string setorSlug, Guid? responsavelId)`
  - `IReadOnlyList<(IReadOnlyList<string> Canais, IReadOnlyList<DestinoDaMensagem> Destinos)> AvisoDeVencimento.Lotes(IReadOnlyList<UsuarioDoTenant> destinatarios, IReadOnlyList<string> canais)`: com `email` nos canais, um lote `in_app`+`email` para quem tem e-mail e outro só `in_app` para quem não tem; lotes vazios não saem; cada lote fatiado em 500 (`AvisoDePublicacao.MaxDestinosPorLote`)
  - `string AvisoDeVencimento.Titulo(string titulo, int diasRestantes)`: `"{titulo} vence hoje"` para 0, `"{titulo} vence em 1 dia"`, `"{titulo} vence em {n} dias"`
  - `ProcessarAvisosDeVencimentoHandler.HandleAsync(CancellationToken ct)` → `Task<int>` (quantos avisos enviados)

Fluxo do handler, por item de `ListarAtivosParaAvisoAsync`:

1. `Decidir(item, hoje, marcosTratados)`.
2. Cada marco em `MarcosAPular` → `TentarRegistrarAvisoAsync(... Pulado, 0 ...)`.
3. Com `MarcoAEnviar`: destinatários (lista do tenant lida **uma vez** por execução); vazio → registra `SemDestinatario` e loga warning. Senão registra `Enviado` com a quantidade; `false` no registro → outra instância já tratou, segue. Depois envia os lotes; `NotificacaoIndisponivelException` → `RemoverAvisoAsync` e segue para o próximo item.
4. Resumo da mensagem: nome do setor, data por extenso (`pt-BR`, `"31 de dezembro de 2026"`) e o responsável, se houver; link `{UrlBase}/{slug}/vencimentos/{id}`.

- [ ] **Step 1: Write the failing tests**
  - `AvisoDeVencimentoTests`: `Canais_MaisDeSeteDias_SoSino`, `Canais_SeteDiasOuMenos_SinoEEmail` (`[Theory]` 7, 1, 0), `Destinatarios_ResponsavelMaisAdmins_SemRepeticao`, `ResponsavelSemRoleDoSetor_NaoRecebe`, `UsuarioComRoleUser_SemSerResponsavel_NaoRecebe`, `Lotes_SemEmail_RecebeSoNoSino`, `Titulo_UsaOsDiasReais` (`"Certificado A1 vence em 5 dias"`, `"... vence em 1 dia"`, `"... vence hoje"`).
  - `ProcessarAvisosDeVencimentoHandlerTests` (dublês de repositório, diretório e notificador): `EnviaUmLotePorCanal_ERegistraEnviado`, `RegistroRecusado_NaoEnvia`, `HubIndisponivel_DesfazORegistro_ESegueParaOProximoItem`, `SemDestinatario_RegistraELogaSemEnviar`, `MarcosPulados_RegistradosSemEnvio`, `ListaDeUsuariosLidaUmaVezPorExecucao`.
- [ ] **Step 2: Run** (`--filter "FullyQualifiedName~AvisoDeVencimentoTests|FullyQualifiedName~ProcessarAvisosDeVencimentoHandlerTests"`). Expected: FAIL.
- [ ] **Step 3: Implement** both files; register the handler in `IntranetApplicationExtensions`.
- [ ] **Step 4: Run** the same filter plus `AvisoDePublicacaoTests`. Expected: PASS.
- [ ] **Step 5: Commit** — `feat(vencimentos): decisao e envio dos avisos pelo NotificationHub`.

---

### Task 6: Worker por tenant

**Files:**

- Create: `src/Secco.Intranet.Infrastructure/Vencimentos/VencimentosWorker.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (`AddHostedService<VencimentosWorker>()` **só quando** `NotificacaoOptions.HubUrl` estiver preenchida; sem Hub, um log de informação no startup explicando que os avisos de vencimento estão desligados)
- Test: `tests/Secco.Intranet.Tests/Integration/VencimentosWorkerTests.cs`

**Interfaces:**

- Consumes: `ProcessarAvisosDeVencimentoHandler` (Task 5), `ITenantCatalog`, `IServiceScopeFactory`, `TenantScopeExtensions.SetTenant`, `VencimentosOptions`.
- Produces: `internal sealed partial class VencimentosWorker(IServiceScopeFactory scopes, ITenantCatalog catalogo, VencimentosOptions opcoes, ILogger<VencimentosWorker> logger) : BackgroundService`, com `internal Task ExecutarUmCicloAsync(CancellationToken ct)` exposto aos testes por `InternalsVisibleTo` (conferir se o projeto já o declara).

Molde: `LogRetentionWorker` do `secco-platform` (`src/LogStream/Secco.LogStream.Infrastructure/Retention/LogRetentionWorker.cs`): `Habilitado == false` ou `IntervaloEmMinutos < 1` ou fuso inválido → loga e retorna sem rodar; roda um ciclo ao subir e depois a cada `PeriodicTimer`; por tenant, `CreateAsyncScope()`, `SetTenant(tenant.TenantId)` e o handler; exceção de um tenant é logada com `[LoggerMessage]` e não para os demais; `OperationCanceledException` com o token de parada é relançada.

Ponto de atenção da spec, que esta task fecha: `SecureGateDiretorioDeUsuarios` lê o tenant por `ITenantContext.IsResolved`/`TenantId`. Confirmar que o `TenantContext` que `SetTenant` preenche é a mesma instância de escopo resolvida como `ITenantContext`; o teste `DiretorioDeUsuarios_ResolveOTenantDoEscopoDoWorker` prova isso.

- [ ] **Step 1: Write the failing tests**: `UmCiclo_PercorreTodosOsTenants` (dois tenants no catálogo de teste, um item devido em cada → dois envios no notificador dublê), `FalhaNumTenant_NaoParaOOutro`, `Desabilitado_NaoRoda`, `DiretorioDeUsuarios_ResolveOTenantDoEscopoDoWorker`, `SemHubConfigurado_WorkerNaoRegistrado` (via `IntranetWebFactory`, sem `Intranet:Notificacao:HubUrl` → nenhum `VencimentosWorker` entre os `IHostedService`).
- [ ] **Step 2: Run** (`--filter "FullyQualifiedName~VencimentosWorkerTests"`). Expected: FAIL.
- [ ] **Step 3: Implement** the worker and the conditional registration.
- [ ] **Step 4: Run** the same filter plus `WebSmokeTests` and `StartupSecureGateTests`. Expected: PASS.
- [ ] **Step 5: Commit** — `feat(vencimentos): worker em processo por tenant (camada 1 da ADR-0015 da plataforma)`.

---

### Task 7: Telas do setor

**Files:**

- Create: `src/Secco.Intranet.Web/Controllers/VencimentosController.cs`, rota de classe `[Route("{slug:" + SlugDeSetorRouteConstraint.Nome + "}/vencimentos")]`
- Modify: `src/Secco.Intranet.Web/Controllers/SetorController.cs:87-99` (`TipoDeItemMenu.Vencimentos =>` renderiza a lista, no mesmo molde de Documentos/Avisos; o handler de listagem entra no construtor)
- Create: `src/Secco.Intranet.Web/Models/Vencimentos/` (view models da lista, do formulário e do detalhe)
- Create: `src/Secco.Intranet.Web/Views/Setor/Vencimentos.cshtml` (lista) e `src/Secco.Intranet.Web/Views/Vencimentos/Detalhe.cshtml`, `Formulario.cshtml`
- Test: `tests/Secco.Intranet.Tests/Integration/VencimentoFluxoTests.cs`

Rotas do `VencimentosController`: `GET novo`, `POST novo`, `GET {id:guid}`, `GET {id:guid}/editar`, `POST {id:guid}/editar`, `POST {id:guid}/renovar`, `POST {id:guid}/encerrar`, `POST {id:guid}/reativar`. Todo `POST` com `[ValidateAntiForgeryToken]`. Leitura com `setor-{slug}:read`, escrita com `setor-{slug}:write`, recurso desligado → 404, tudo pelo mesmo caminho de `SetorController` (`PodeLerAsync`, `PodePublicarAsync`, `ResolverTipoAsync`). Para não duplicar essas três checagens, extraí-las para um serviço da Web (`AcessoAoRecursoDoSetor`, registrado com escopo) usado pelos dois controllers; `SetorController` passa a delegar para ele sem mudar comportamento.

As views compõem só parciais do contrato (`_PageHeader`, `_Card`, `_Badge`, `_EmptyState`, `_Avatar`): o estado vira `_Badge`; encerrar usa `<form data-confirmar="Encerrar este vencimento? Os avisos param.">`; o responsável é um `<select>` com quem tem Role `{slug}-admin`/`{slug}-user`, com nome vindo da mesma fonte que o Diretório usa (`UsuariosParaDiretorio`). No detalhe, responsável que perdeu a Role do setor aparece com o texto "sem acesso ao setor" (a lista de usuários do tenant diz isso; não é gravado). Feedback das ações pelo `TempData[FeedbackViewComponent.ChaveDaMensagem]`. Se faltar algum parcial no contrato para o que a tela precisa, parar e perguntar: acrescentar ao contrato exige os dois temas.

- [ ] **Step 1: Write the failing tests**: `Lista_PeloCaminhoDaArvore_MostraOsItensOrdenadosPorData`, `RecursoDesligado_404NaLeituraENaEscrita` (GET da lista, GET do detalhe, POST de novo e de renovar), `Criar_Renovar_Encerrar_FluxoCompleto` (feedback e estado depois de cada passo), `ItemDeOutroSetor_404`, `PostSemAntiforgery_Recusado`, `Detalhe_ResponsavelSemRoleDoSetor_MostraSemAcesso`.
- [ ] **Step 2: Run** (`--filter "FullyQualifiedName~VencimentoFluxoTests"`). Expected: FAIL.
- [ ] **Step 3: Implement** controller, service extraction, view models and views.
- [ ] **Step 4: Run** the same filter plus `SetorMenuRotaTests`, `DocumentoFluxoTests` and `PublicacaoFluxoTests` (a extração não pode mudar Documentos/Avisos). Expected: PASS.
- [ ] **Step 5: Commit** — `feat(vencimentos): telas do setor (lista, detalhe, criar, renovar, encerrar)`.

---

### Task 8: Fechamento

**Files:**

- Modify: `docs/roadmap.md` (item da Fase 2 marcado `[x]`, com link para a spec e este plano)
- Modify: `docs/guia-de-deploy.md` (seção `Intranet:Vencimentos`, com os três valores e o aviso de que sem `Intranet:Notificacao:HubUrl` não há aviso de vencimento)
- Modify: `src/Secco.Intranet.Infrastructure/Seeding/SetoresDesenvolvimentoSeeder.cs` (um setor de desenvolvimento com Vencimentos ligado e dois itens: um a vencer em 5 dias, um vencido)
- Modify: `docs/specs/2026-10-24-aviso-de-vencimento-design.md` (estado: "aprovado, implementado")

- [ ] **Step 1: Build the solution** — `dotnet build Secco.Intranet.slnx -nologo`. Expected: 0 avisos, 0 erros.
- [ ] **Step 2: Run the full suite** — `dotnet test Secco.Intranet.slnx --no-build -nologo`. Expected: tudo aprovado; os ignorados são só fumaça e UI.
- [ ] **Step 3: Run the app** em Development (skill `run` do projeto ou `scripts/dev.ps1`) e conferir à mão, nos dois temas e nos dois modos: a lista do setor de desenvolvimento, o detalhe com os próximos avisos, renovar e encerrar.
- [ ] **Step 4: Commit** — `docs(vencimentos): roadmap, guia de deploy e seed de desenvolvimento`.
