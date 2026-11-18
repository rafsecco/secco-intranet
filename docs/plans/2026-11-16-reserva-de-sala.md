# Reserva de sala — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a intranet guarda as reservas das salas de reunião, impede dois usos da mesma sala no mesmo horário e, quando configurada, espelha cada reserva na agenda do Microsoft 365 por uma caixa de serviço.

**Architecture:** fatia nova `Salas` nas quatro camadas, no molde de Vencimentos. A gravação de reserva roda numa transação que trava a linha da sala antes de conferir a sobreposição. O espelhamento é um processo em segundo plano que lê o estado gravado na própria reserva e fala com a agenda por uma interface (`IAgendaExterna`); a implementação do Microsoft usa `HttpClient` direto, sem biblioteca nova.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 (SQL Server e PostgreSQL), xUnit, AwesomeAssertions, Testcontainers, Playwright (opt-in com `SECCO_UI_TESTS=1`).

**Spec:** [docs/specs/2026-11-16-reserva-de-sala-design.md](../specs/2026-11-16-reserva-de-sala-design.md)

## Global Constraints

- Migration sempre em par, mesmo nome nos dois projetos (`Migrations.SqlServer` e `Migrations.Postgres`), gerada com `dotnet ef` (ferramenta local: `dotnet tool restore`). Nome de tabela e coluna vem da convenção da ADR-0017; não escrever `HasColumnName`.
- Todo POST com `[ValidateAntiForgeryToken]`. Sem `style=""`, `<script>` inline nem `onclick` nas views (CSP em Enforce).
- Quem não pode alterar uma reserva recebe 404, nunca 403. As telas de `/salas/gerenciar` usam `[SomenteIntranetAdmin]`, como `SetoresController`.
- Testes com AwesomeAssertions, nunca FluentAssertions. Nomes, e-mails e salas de teste fictícios (domínio `exemplo.test`).
- As views ficam em `src/Secco.Intranet.Web/Views/Salas`; CSS e script de tela mudam igual em `Themes.Vertical` e `Themes.Horizontal`, e o `theme.css` versionado é reconstruído com `npm ci && npm run build` nos dois.
- Nenhum pacote NuGet novo. Se algum se mostrar necessário, a versão é a estável mais recente conferida ao vivo.
- Horário guardado em UTC (`DateTimeOffset`); o fuso da instalação só entra na borda (formulário e tela), por `RelogioDeSalas`.
- Limites da spec: título 120, descrição 1000, 50 convidados, passos de 15 minutos, início e fim no mesmo dia local.
- Commits direto na `main`; conferir `git branch --show-current` antes de cada commit.
- Texto de tela e mensagens em português, sem citar sistema de terceiros além do "Microsoft 365" onde a spec cita.
- Segredo (`ClientSecret`) nunca em log, em mensagem de erro nem em arquivo versionado.

## Review Focus

1. **Reservas encostadas** (10:00–11:00 e 11:00–12:00 na mesma sala): as duas entram. Testes nas Tarefas 1 e 2.
2. **Alterar a própria reserva sem mudar o horário** (só o título): não acusa conflito consigo mesma. Teste na Tarefa 4.
3. **Hora local que não existe ou existe duas vezes** no fuso configurado (virada de horário de verão, por exemplo `America/New_York`): recusa com `Salas.HorarioInvalido`, sem exceção. Teste na Tarefa 4.
4. **Criar o evento deu certo, mas gravar o identificador falhou:** o ciclo seguinte não cria um segundo evento, porque a criação leva uma chave de idempotência estável. Testes nas Tarefas 7 e 8.
5. **Reserva alterada enquanto o envio anterior ainda está pendente ou falhou:** volta para "aguardando" com as tentativas zeradas, e o que vai para a agenda é a versão mais nova. Teste na Tarefa 7.
6. **Formulário adulterado** (sala inativa, convidado que não está no Diretório, o próprio dono entre os convidados, convidado repetido): recusa ou normaliza conforme a tabela da Tarefa 4, nada gravado pela metade. Teste na Tarefa 4.

---

### Task 1: domínio — `Sala`, `Reserva` e as regras de horário

**Files:**
- Create: `src/Secco.Intranet.Domain/Salas/Sala.cs`, `Reserva.cs`, `ConvidadoDaReserva.cs`, `EstadoDoEspelhamento.cs`, `ExclusaoDeEventoPendente.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/SalaTests.cs`, `ReservaTests.cs`

**Interfaces (Produces):**

```csharp
public sealed class Sala : BaseEntity
{
    public const int NomeMaximo = 120, LocalizacaoMaxima = 200, RecursosMaximo = 500, EmailMaximo = 256;
    public Sala(string nome, string? localizacao, int? capacidade, string? recursos, string? emailNaAgenda, int ordem);
    public string Nome; public string? Localizacao; public int? Capacidade; public string? Recursos;
    public string? EmailNaAgenda; public int Ordem; public bool Ativa;            // nasce ativa
    public void Editar(string nome, string? localizacao, int? capacidade, string? recursos, string? emailNaAgenda, int ordem);
    public void Ativar(); public void Desativar();
}

public enum EstadoDoEspelhamento { NaoSeAplica = 0, Aguardando = 1, Espelhada = 2, Falhou = 3 }

public sealed class Reserva : BaseEntity
{
    public const int TituloMaximo = 120, DescricaoMaxima = 1000, MaximoDeConvidados = 50, PassoEmMinutos = 15;
    public Reserva(Guid salaId, DateTimeOffset inicio, DateTimeOffset fim, string titulo, string? descricao,
        Guid reservadoPorId, string reservadoPorNome, string? reservadoPorEmail,
        IEnumerable<ConvidadoDaReserva> convidados, bool espelhar);
    public Guid SalaId; public DateTimeOffset Inicio; public DateTimeOffset Fim; public string Titulo; public string? Descricao;
    public Guid ReservadoPorId; public string ReservadoPorNome; public string? ReservadoPorEmail;
    public IReadOnlyList<ConvidadoDaReserva> Convidados;
    public int Versao;                                   // 1 ao nascer, +1 a cada Alterar
    public EstadoDoEspelhamento Espelhamento; public string? EventoExternoId;
    public int TentativasDeEspelhamento; public DateTimeOffset? ProximaTentativaEm; public string? MotivoDaFalha;
    public void Alterar(Guid salaId, DateTimeOffset inicio, DateTimeOffset fim, string titulo, string? descricao,
        IEnumerable<ConvidadoDaReserva> convidados, bool espelhar);
    public bool Sobrepoe(DateTimeOffset inicio, DateTimeOffset fim);   // inicio < Fim && fim > Inicio
    public bool JaComecou(DateTimeOffset agora);
    public void MarcarEspelhada(string eventoExternoId);
    public void RegistrarFalhaPassageira(string motivo, DateTimeOffset agora);
    public void MarcarFalhaDefinitiva(string motivo);
    public void TentarDeNovo();                          // Falhou -> Aguardando, tentativas 0
}

public sealed class ConvidadoDaReserva : BaseEntity   // (Guid usuarioId, string nome, string? email)
public sealed class ExclusaoDeEventoPendente : BaseEntity   // (string eventoExternoId); mesmas Tentativas/ProximaTentativaEm/MotivoDaFalha/Falhou
```

Regras que o domínio garante (exceção `DomainInvariantException`): nome e título obrigatórios e dentro do limite; capacidade, se informada, maior que zero; `fim > inicio`; minutos de início e fim múltiplos de 15 com segundos zerados; convidados sem repetir `UsuarioId`, sem o dono (retirado em silêncio) e no máximo 50. "Mesmo dia local" e "não no passado" dependem do fuso e do relógio e ficam na Tarefa 4.

Espera entre tentativas, em minutos, pelo número da falha: 1, 2, 4, 8, 16, 32, 60, 60. Na oitava falha passageira o estado vira `Falhou` com o último motivo. `MotivoDaFalha` é cortado em 500 caracteres. `Alterar` com `espelhar: true` põe `Aguardando`, zera tentativas e mantém `EventoExternoId`; com `espelhar: false` não toca no estado.

- [ ] **Step 1: Testes que falham.** `SalaTests`: `Construtor_NomeVazio_Lanca`, `Construtor_CapacidadeZero_Lanca`, `Construtor_NasceAtiva`, `Editar_TrocaOsCampos`. `ReservaTests`:

```csharp
[Fact] public void Construtor_FimAntesDoInicio_Lanca()
[Fact] public void Construtor_MinutoForaDoPasso_Lanca()                 // 10:07
[Fact] public void Construtor_TituloCom121_Lanca()
[Fact] public void Construtor_ConvidadoRepetidoEODono_FicaUmSemODono()
[Fact] public void Construtor_51Convidados_Lanca()
[Fact] public void Construtor_SemEspelhar_NaoSeAplica_ComEspelhar_Aguardando()
[Theory] // (10:00-11:00) x (11:00-12:00)=false, (10:30-11:30)=true, (09:00-10:00)=false, (09:00-12:00)=true
public void Sobrepoe_ConfereOsLimites(...)
[Fact] public void Alterar_ComEspelhar_VoltaParaAguardando_ZeraTentativas_MantemEvento_SomaVersao()
[Fact] public void RegistrarFalhaPassageira_AgendaAEspera_1_2_4_8_16_32_60()
[Fact] public void RegistrarFalhaPassageira_NaOitava_ViraFalhou()
[Fact] public void TentarDeNovo_DeFalhou_VoltaParaAguardando()
```

- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~SalaTests|FullyQualifiedName~ReservaTests"` → falha de compilação.
- [ ] **Step 3: Implementar** as cinco classes, com construtor privado de rehidratação como em `ItemVencimento`.
- [ ] **Step 4:** rodar de novo → passam.
- [ ] **Step 5: Commit** `feat(salas): dominio de sala e reserva`.

### Task 2: persistência, migration e a trava da sala

**Files:**
- Create: `src/Secco.Intranet.Application/Salas/ISalaRepository.cs`, `IReservaRepository.cs`
- Create: `src/Secco.Intranet.Infrastructure/Mappings/SalaConfiguration.cs`, `ReservaConfiguration.cs`; `Repositories/SalaRepository.cs`, `ReservaRepository.cs`; `Salas/TravaDeSala.cs`
- Modify: `Infrastructure/Contexts/IntranetDbContext.cs` (DbSets), `IntranetInfrastructureExtensions.cs` (registro)
- Create: migration `SalasEReservas` nos dois projetos de migrations
- Test: `tests/Secco.Intranet.Tests/Integration/ReservaPersistenciaTests.cs`, `ReservaConcorrenciaTests.cs`

**Interfaces:**
- Consumes: Tarefa 1.
- Produces:

```csharp
public interface ISalaRepository
{
    Task AddAsync(Sala sala, CancellationToken ct = default);
    Task<Sala?> GetAsync(Guid id, CancellationToken ct = default);                 // rastreada
    Task<IReadOnlyList<Sala>> ListarAsync(bool somenteAtivas, CancellationToken ct = default);   // por Ordem, depois Nome
    Task<bool> NomeEmUsoAsync(string nome, Guid? ignorarId, CancellationToken ct = default);
    Task<bool> TemReservaFuturaAsync(Guid salaId, DateTimeOffset agora, CancellationToken ct = default);
    void Remover(Sala sala);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IReservaRepository
{
    /// Abre transação, trava a linha da sala, roda a ação e confirma. Reverte se a ação lançar.
    Task<T> ComSalaTravadaAsync<T>(Guid salaId, Func<CancellationToken, Task<T>> acao, CancellationToken ct = default);
    Task<Reserva?> PrimeiraSobrepostaAsync(Guid salaId, DateTimeOffset inicio, DateTimeOffset fim, Guid? ignorarId, CancellationToken ct = default);
    Task AddAsync(Reserva reserva, CancellationToken ct = default);
    Task<Reserva?> GetAsync(Guid id, CancellationToken ct = default);              // rastreada, com convidados
    Task<IReadOnlyList<Reserva>> ListarNoIntervaloAsync(DateTimeOffset de, DateTimeOffset ate, CancellationToken ct = default);
    Task<IReadOnlyList<Reserva>> ListarFuturasDaPessoaAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken ct = default); // dono ou convidado
    Task<IReadOnlyList<Reserva>> ListarParaEspelharAsync(DateTimeOffset agora, int limite, CancellationToken ct = default);       // Aguardando e ProximaTentativaEm nula ou vencida
    void Remover(Reserva reserva);
    Task AdicionarExclusaoPendenteAsync(ExclusaoDeEventoPendente pendencia, CancellationToken ct = default);
    Task<IReadOnlyList<ExclusaoDeEventoPendente>> ListarExclusoesPendentesAsync(DateTimeOffset agora, int limite, CancellationToken ct = default);
    void RemoverExclusaoPendente(ExclusaoDeEventoPendente pendencia);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

Mapeamento: índice único no nome da sala; índice em `(SalaId, Inicio)` e em `(Espelhamento, ProximaTentativaEm)` na reserva; reserva → sala com `Restrict`; convidado → reserva com `Cascade`; `EventoExternoId` até 512; `MotivoDaFalha` até 500.

`TravaDeSala` é a única peça com SQL escrito à mão. O nome da tabela e da coluna de chave saem do modelo do EF (`FindEntityType(typeof(Sala))`), nunca de texto fixo, e o id vai como parâmetro:

```sql
-- PostgreSQL
SELECT 1 FROM "<tabela>" WHERE "<coluna id>" = @id FOR UPDATE
-- SQL Server
SELECT 1 FROM [<tabela>] WITH (UPDLOCK, ROWLOCK) WHERE [<coluna id>] = @id
```

`ComSalaTravadaAsync` usa `Database.CreateExecutionStrategy()` em volta da transação, para conviver com nova tentativa automática do provedor.

- [ ] **Step 1: Mapeamentos, DbSets e migration em par:**

```bash
dotnet ef migrations add SalasEReservas --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add SalasEReservas --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```

Conferir nos dois arquivos que os nomes seguem a convenção (`tb_`, `id_`, `ds_`, `dt_`, `fl_`, `nr_`) e que só entraram as quatro tabelas novas.

- [ ] **Step 2: Testes que falham.** `ReservaPersistenciaTests` (na `IntranetWebFactory`): `Salvar_ReservaComConvidados_LeDeVolta`, `ExcluirReserva_LevaOsConvidados`, `ListarFuturasDaPessoa_TrazDonoEConvidado_SemPassadas`, `ListarParaEspelhar_SoAguardandoComEsperaVencida`. `ReservaConcorrenciaTests` usa `BancosDeMigracaoFixture` com `[Theory]` nos dois engines:

```csharp
// 8 tarefas em paralelo, cada uma com contexto próprio, tentando 10:00-11:00 na mesma sala
// dentro de ComSalaTravadaAsync (confere PrimeiraSobrepostaAsync, grava se livre).
public async Task MesmaSala_GravacoesSimultaneas_SoUmaEntra(IntranetDatabaseProvider provider)   // contagem final == 1
public async Task SalasDiferentes_GravacoesSimultaneas_TodasEntram(IntranetDatabaseProvider provider)
public async Task AcaoQueLanca_NadaFicaGravado(IntranetDatabaseProvider provider)
public async Task Encostadas_AsDuasEntram(IntranetDatabaseProvider provider)
```

- [ ] **Step 3:** rodar → falham.
- [ ] **Step 4: Implementar** repositórios e `TravaDeSala`; registrar no DI.
- [ ] **Step 5:** rodar as duas classes e `MigracoesPorEngineTests` → passam.
- [ ] **Step 6: Commit** `feat(salas): persistencia e trava da sala para o conflito de horario`.

### Task 3: cadastro de salas (casos de uso)

**Files:**
- Create: `src/Secco.Intranet.Application/Salas/SalaDtos.cs`, `CriarSalaHandler.cs`, `EditarSalaHandler.cs`, `ExcluirSalaHandler.cs`, `ListarSalasHandler.cs`, `ObterSalaHandler.cs`
- Modify: `Application/IntranetErrors.cs` (classe `Salas`), `Auditoria/VerbosDeAuditoria.cs` e `RecursosDeAuditoria`, `IntranetApplicationExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/SalasHandlersTests.cs`, `Support/SalaRepositorioFalso.cs`

**Interfaces (Produces):**

```csharp
public sealed record SalaDto(Guid Id, string Nome, string? Localizacao, int? Capacidade, string? Recursos, string? EmailNaAgenda, int Ordem, bool Ativa);
public sealed record SalvarSalaCommand(Guid? Id, string? Nome, string? Localizacao, int? Capacidade, string? Recursos, string? EmailNaAgenda, int Ordem, bool Ativa);
// CriarSalaHandler/EditarSalaHandler.HandleAsync(SalvarSalaCommand) -> Result<Guid>
// ExcluirSalaHandler.HandleAsync(Guid id) -> Result
// ListarSalasHandler.HandleAsync(bool somenteAtivas) -> IReadOnlyList<SalaDto>;  ObterSalaHandler.HandleAsync(Guid) -> Result<SalaDto>
```

| Caso | Erro (`IntranetErrors.Salas`) |
|---|---|
| Nome vazio / acima de 120 | `NomeRequired` / `NomeTooLong` |
| Nome já usado por outra sala (sem diferenciar maiúsculas) | `NomeEmUso` |
| Capacidade ≤ 0 | `CapacidadeInvalida` |
| E-mail da agenda preenchido e malformado | `EmailInvalido` |
| Sala inexistente | `NotFound` |
| Excluir com reserva futura | `TemReservaFutura` ("Esta sala tem reservas futuras. Desative-a em vez de excluir.") |

Excluir uma sala sem reserva futura apaga também as reservas passadas dela. Verbos de auditoria: `sala.criar`, `sala.editar`, `sala.excluir`.

- [ ] **Step 1: Testes que falham**, um por linha da tabela, mais `Criar_Valida_Grava`, `Editar_Desativa`, `Excluir_SoComPassadas_ApagaTudo`.
- [ ] **Step 2:** rodar → falham. **Step 3: Implementar.** **Step 4:** rodar → passam.
- [ ] **Step 5: Commit** `feat(salas): casos de uso do cadastro de salas`.

### Task 4: reservar, alterar e cancelar (casos de uso)

**Files:**
- Create: `src/Secco.Intranet.Application/Salas/SalasOptions.cs`, `RelogioDeSalas.cs`, `ReservaDtos.cs`, `ReservarSalaHandler.cs`, `AlterarReservaHandler.cs`, `CancelarReservaHandler.cs`, `ObterReservaHandler.cs`, `ListarOcupacaoDoDiaHandler.cs`, `ListarMinhasReservasHandler.cs`, `ListarConvidadosPossiveisHandler.cs`, `IEspelhamentoLigado.cs`
- Modify: `IntranetErrors.cs`, `VerbosDeAuditoria.cs`, `IntranetApplicationExtensions.cs`, `IntranetInfrastructureExtensions.cs` (bind de `SalasOptions`)
- Test: `tests/Secco.Intranet.Tests/Unit/RelogioDeSalasTests.cs`, `ReservasHandlersTests.cs`, `Support/ReservaRepositorioFalso.cs`

**Interfaces:**
- Consumes: Tarefas 1 e 2; `IUsuariosParaDiretorio.ListarAtivosAsync` (`UsuarioParaDiretorio(Id, Email, Nome)`).
- Produces:

```csharp
public sealed class SalasOptions { public const string SectionKey = "Intranet:Salas"; public string FusoHorario { get; set; } = "America/Sao_Paulo"; }

public sealed class RelogioDeSalas(TimeProvider relogio, SalasOptions opcoes)   // fuso resolvido no construtor, como RelogioDeVencimentos
{
    public DateTimeOffset Agora();
    public DateOnly Hoje();
    public DateTimeOffset? ParaUtc(DateOnly dia, TimeOnly hora);     // null quando a hora local não existe ou é ambígua
    public (DateOnly Dia, TimeOnly Hora) ParaLocal(DateTimeOffset instante);
    public (DateTimeOffset De, DateTimeOffset Ate) LimitesDoDia(DateOnly dia);
}

public interface IEspelhamentoLigado { bool Ligado { get; } }      // Tarefa 7 registra a implementação real; aqui, uma que devolve false

public sealed record QuemPede(Guid UsuarioId, bool EhAdministrador);
public sealed record ReservarSalaCommand(Guid SalaId, DateOnly Dia, TimeOnly Inicio, TimeOnly Fim, string? Titulo, string? Descricao, IReadOnlyList<Guid> Convidados, QuemPede Quem);
public sealed record AlterarReservaCommand(Guid Id, Guid SalaId, DateOnly Dia, TimeOnly Inicio, TimeOnly Fim, string? Titulo, string? Descricao, IReadOnlyList<Guid> Convidados, QuemPede Quem);
public sealed record PessoaDaReservaDto(Guid UsuarioId, string Nome);
public sealed record ReservaDto(Guid Id, Guid SalaId, string SalaNome, DateOnly Dia, TimeOnly Inicio, TimeOnly Fim, string Titulo, string? Descricao,
    PessoaDaReservaDto ReservadoPor, IReadOnlyList<PessoaDaReservaDto> Convidados, bool PodeAlterar,
    EstadoDoEspelhamento Espelhamento, string? MotivoDaFalha);
public sealed record OcupacaoDoDiaDto(DateOnly Dia, IReadOnlyList<SalaDto> Salas, IReadOnlyList<ReservaDto> Reservas);
// ReservarSalaHandler -> Result<Guid>; AlterarReservaHandler -> Result; CancelarReservaHandler.HandleAsync(Guid id, QuemPede) -> Result
// ObterReservaHandler.HandleAsync(Guid id, QuemPede) -> Result<ReservaDto>
// ListarOcupacaoDoDiaHandler.HandleAsync(DateOnly dia, QuemPede) -> OcupacaoDoDiaDto      (só salas ativas)
// ListarMinhasReservasHandler.HandleAsync(QuemPede) -> IReadOnlyList<ReservaDto>
// ListarConvidadosPossiveisHandler.HandleAsync(Guid quemPede) -> IReadOnlyList<PessoaDaReservaDto>   (ativos do Diretório, por nome, sem quem pede)
```

`PodeAlterar` = (dono ou administrador) e a reserva ainda não começou. O nome de uma pessoa é `Nome` do Diretório ou, sem ele, o e-mail. Nome e e-mail de dono e convidados são copiados para a reserva no momento de gravar.

| Caso | Resultado |
|---|---|
| Título vazio / acima de 120; descrição acima de 1000 | `Salas.TituloRequired` / `TituloTooLong` / `DescricaoTooLong` |
| Fim ≤ início, ou minuto fora do passo de 15 | `Salas.HorarioInvalido` |
| `ParaUtc` devolve nulo | `Salas.HorarioInvalido` |
| Início já passou | `Salas.NoPassado` |
| Sala inexistente ou inativa | `Salas.NotFound` |
| Convidado que não é usuário ativo do Diretório | `Salas.ConvidadoInvalido` |
| Mais de 50 convidados | `Salas.ConvidadosDemais` |
| Dono entre os convidados, ou convidado repetido | aceito, normalizado |
| Sobreposição | `Salas.Conflito(nomeDeQuemReservou, inicio, fim)`: "Sala ocupada das 10:00 às 11:00 por Ana Souza." |
| Alterar ou cancelar sem ser dono nem admin, ou reserva inexistente | `Salas.ReservaNotFound` |
| Alterar ou cancelar reserva que já começou | `Salas.JaComecou` |
| Quem pede não está entre os usuários ativos do Diretório (modo aberto de desenvolvimento) | grava com o nome "Visitante" e sem e-mail |

Toda a validação que não depende do banco roda antes de `ComSalaTravadaAsync`; dentro da trava ficam só `PrimeiraSobrepostaAsync` (com `ignorarId` na alteração), a gravação e o `SaveChangesAsync`. A alteração trava a sala de destino. Cancelar remove a reserva e, quando ela tem `EventoExternoId`, grava uma `ExclusaoDeEventoPendente` na mesma gravação. Verbos de auditoria: `reserva.criar`, `reserva.alterar`, `reserva.cancelar`.

- [ ] **Step 1: Testes que falham.** `RelogioDeSalasTests`: `ParaUtc_SaoPaulo_Converte`, `ParaUtc_HoraQueNaoExiste_Nulo` e `ParaUtc_HoraAmbigua_Nulo` (fuso `America/New_York`, 2026-03-08 02:30 e 2026-11-01 01:30), `LimitesDoDia_CobreODiaLocal`. `ReservasHandlersTests`: um teste por linha da tabela, mais `Alterar_SoOTitulo_NaoConflitaConsigoMesma`, `Alterar_TrocaDeSala_ConfereConflitoNaNova`, `Reservar_Encostada_Aceita`, `Cancelar_ComEvento_DeixaExclusaoPendente`, `Cancelar_SemEvento_NaoDeixaPendencia`, `Reservar_ComEspelhamentoLigado_NasceAguardando`, `Admin_CancelaReservaDeOutro`.
- [ ] **Step 2:** rodar → falham. **Step 3: Implementar.** **Step 4:** rodar → passam.
- [ ] **Step 5: Commit** `feat(salas): reservar, alterar e cancelar com conflito de horario`.

### Task 5: item "Salas" no menu e telas do cadastro

**Files:**
- Modify: `src/Secco.Intranet.Web/Navigation/CatalogoDeItensDoMenuPrincipal.cs`, `IntranetNavigation.cs`, `ViewComponents/NavigationViewComponent.cs`
- Create: `src/Secco.Intranet.Web/Controllers/SalasController.cs` (parte de gestão), `Models/Salas/SalaFormViewModel.cs`, `Views/Salas/Gerenciar.cshtml`, `Views/Salas/SalaForm.cshtml`
- Test: `tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs`, `Integration/SalasCadastroTests.cs`, `Integration/SlugsReservadosTests.cs`

**Interfaces:**
- Consumes: Tarefa 3.
- Produces: `CatalogoDeItensDoMenuPrincipal.ChaveSalas = "salas"` (reservada, como Mural e Diretório); `NavigationRequest(..., bool MostrarSalas = false)`; `PrincipalNaOrdemPadrao(bool incluirDiretorio, extras, bibliotecas = null, bool incluirSalas = false)` com a entrada `new(ChaveSalas, "Salas", "bi-door-open", "/salas")` logo depois do Diretório. `MostrarSalas` é verdadeiro no modo aberto de desenvolvimento ou com usuário autenticado.

Rotas de gestão, todas com `[SomenteIntranetAdmin]`: `GET /salas/gerenciar`, `GET|POST /salas/gerenciar/nova`, `GET|POST /salas/gerenciar/{id:guid}/editar`, `POST /salas/gerenciar/{id:guid}/excluir`. O item fica ativo no menu para qualquer caminho que comece com `/salas`. A entrada "Salas" obedece à ordenação do menu que o admin já configura (`PosicaoMenu`), como os outros itens fixos.

- [ ] **Step 1: Testes que falham.** `NavegacaoETemaTests`: `Salas_ComMostrarSalas_VemDepoisDoDiretorio`, `Salas_SemMostrarSalas_NaoAparece`, `Salas_CaminhoDeGestao_ItemAtivo`. `SlugsReservadosTests`: `Salas_EhSlugReservado` (criar setor com slug `salas` é recusado). `SalasCadastroTests`: `Gerenciar_SemSerAdmin_403`, `Nova_Valida_ApareceNaLista`, `Nova_NomeRepetido_MostraErro`, `Editar_Desativa_MostraInativa`, `Excluir_ComReservaFutura_MostraOrientacao`, `ItemExtra_ComChaveSalas_EhIgnorado`.
- [ ] **Step 2:** rodar → falham. **Step 3: Implementar.** **Step 4:** rodar → passam, junto com `MenuOrdemAdministracaoTests` e `MenuItemExtraTests`.
- [ ] **Step 5: Commit** `feat(salas): item Salas no menu e cadastro de salas`.

### Task 6: grade do dia, formulário, detalhe e "Minhas reservas"

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/SalasController.cs`
- Create: `src/Secco.Intranet.Web/Models/Salas/GradeDoDiaViewModel.cs`, `ReservaFormViewModel.cs`; `Views/Salas/Index.cshtml`, `ReservaForm.cshtml`, `Reserva.cshtml`, `Minhas.cshtml`
- Create: `wwwroot/js/salas.js` nos dois temas; estilos `sc-salas` nos dois `_components.scss`, com `theme.css` reconstruído
- Test: `tests/Secco.Intranet.Tests/Unit/GradeDoDiaTests.cs`, `Integration/ReservaDeSalaTelasTests.cs`

**Interfaces:**
- Consumes: Tarefa 4.
- Produces:

```csharp
public sealed record CelulaDaGrade(TimeOnly Hora, ReservaDto? Reserva, int Linhas, bool Livre);   // Linhas = duração / 15 quando abre uma reserva
public sealed record ColunaDaGrade(SalaDto Sala, IReadOnlyList<CelulaDaGrade> Celulas);            // células cobertas por uma reserva não entram
public sealed class GradeDoDiaViewModel
{
    public static GradeDoDiaViewModel Montar(OcupacaoDoDiaDto ocupacao, DateOnly hoje, TimeOnly agora, Guid? salaEmFoco);
    public DateOnly Dia; public TimeOnly Abre; public TimeOnly Fecha; public IReadOnlyList<ColunaDaGrade> Colunas; public Guid? SalaEmFoco;
}
```

Rotas (usuário autenticado ou modo aberto de desenvolvimento, senão desafio de login): `GET /salas?data=yyyy-MM-dd&sala={id}`, `GET|POST /salas/reservar` (GET aceita `sala`, `data`, `inicio=HH:mm`), `GET /salas/reservas/{id:guid}`, `GET|POST /salas/reservas/{id:guid}/alterar`, `POST /salas/reservas/{id:guid}/cancelar`, `GET /salas/minhas`. `QuemPede` vem de `AcessoAoDiretorio.UsuarioId(User)` e de `AcessoAdministrativo.SomenteIntranetAdmin(User)`; no modo aberto sem usuário, um id fixo de desenvolvimento e administrador verdadeiro.

A grade é uma `<table>` com uma linha por passo de 15 minutos e uma coluna por sala; a reserva ocupa as linhas dela com `rowspan`, o que dispensa posicionamento por estilo embutido. Faixa mostrada: 07:00 às 20:00, alargada até a hora cheia que contenha a reserva mais cedo e a mais tarde do dia. Célula livre no futuro é um link para `/salas/reservar` com sala, data e início; célula livre no passado não é link. O rótulo da hora aparece só nas linhas de hora cheia. A reserva mostra título, horário e quem reservou, e é um link para o detalhe. Sem sala cadastrada, a tela mostra um estado vazio, com atalho para o cadastro quando quem vê é admin.

Em telas estreitas (abaixo de 768px) só a coluna da sala em foco aparece, e uma faixa de abas (links `?sala=`) troca a sala; sem `sala` na URL, a primeira. Em telas largas as abas somem e todas as colunas aparecem. A página nunca rola na horizontal.

Formulário: sala (só ativas), data, início e fim (`<input type="time" step="900">`), título, descrição e convidados como lista de caixas de marcar com nome. `salas.js` acrescenta um campo de busca que filtra essa lista; sem JavaScript a lista aparece inteira e o formulário funciona. Erro de validação reapresenta o formulário com o que foi digitado. Depois de gravar, volta para a grade do dia da reserva com a mensagem de sucesso do padrão `FeedbackDeAcao`. O detalhe mostra o estado do espelhamento só para dono e admin e só quando ele não é `NaoSeAplica` (texto: "Aguardando envio para a agenda", "Enviada para a agenda", "Falha ao enviar para a agenda: {motivo}").

- [ ] **Step 1: Testes que falham.** `GradeDoDiaTests`: `SemReservas_Das7As20_SoCelulasLivres`, `ReservaDe90Min_AbreComSeisLinhas_ECobreAsSeguintes`, `ReservaAs6_AlargaAFaixa`, `Hoje_CelulasPassadasNaoSaoLivres`, `SalaEmFoco_Padrao_PrimeiraColuna`. `ReservaDeSalaTelasTests`:

```csharp
public async Task Grade_MostraSalasAtivasEAReserva()
public async Task Grade_SemSalas_EstadoVazio()
public async Task Grade_DataInvalidaNaUrl_CaiEmHoje()
public async Task Reservar_Valida_ApareceNaGrade()
public async Task Reservar_Conflito_MostraQuemEstaComASala_ENaoGrava()
public async Task Reservar_SemAntiforgery_400()
public async Task Detalhe_Dono_VeAlterarECancelar_Outro_NaoVe()
public async Task Alterar_DeOutraPessoa_404()
public async Task Cancelar_DeOutraPessoa_404_Admin_Cancela()
public async Task Cancelar_Dono_SomeDaGrade()
public async Task Minhas_TrazAsQueFizEAsQueFuiConvidado()
public async Task Telas_SemEstiloNemScriptEmbutido()        // regex no HTML das quatro telas, nos dois temas
```

- [ ] **Step 2:** rodar → falham. **Step 3: Implementar** controller, views, script e estilos; `npm ci && npm run build` nos dois temas. **Step 4:** rodar → passam.
- [ ] **Step 5: Commit** `feat(salas): grade do dia, reserva e minhas reservas`.

### Task 7: espelhamento — configuração, processo em segundo plano e estado na tela

**Files:**
- Create: `src/Secco.Intranet.Application/Salas/Agenda/IAgendaExterna.cs`, `AgendaOptions.cs`, `MontadorDeEvento.cs`, `ProcessarEspelhamentoHandler.cs`, `TentarEspelharDeNovoHandler.cs`
- Create: `src/Secco.Intranet.Infrastructure/Salas/EspelhamentoWorker.cs`, `AgendaServiceCollectionExtensions.cs`
- Modify: `IntranetInfrastructureExtensions.cs`, `SalasController.cs` (`POST /salas/reservas/{id:guid}/espelhar`, admin), `Views/Salas/Reserva.cshtml` (botão "Tentar de novo" quando `Falhou`)
- Test: `tests/Secco.Intranet.Tests/Unit/MontadorDeEventoTests.cs`, `ProcessarEspelhamentoTests.cs`, `Integration/AgendaConfiguracaoTests.cs`, `EspelhamentoWorkerTests.cs`, `Support/AgendaFalsa.cs`

**Interfaces:**
- Consumes: Tarefas 1, 2 e 4.
- Produces:

```csharp
public sealed record ParticipanteDeAgenda(string Nome, string Email);
public sealed record EventoDeAgenda(string ChaveDeIdempotencia, string Assunto, string Corpo, string Local,
    DateTimeOffset Inicio, DateTimeOffset Fim, IReadOnlyList<ParticipanteDeAgenda> Participantes, string? EmailDaSala);
public enum DesfechoDaAgenda { Feito, FalhaPassageira, FalhaDefinitiva }
public sealed record ResultadoDaAgenda(DesfechoDaAgenda Desfecho, string? EventoId = null, string? Motivo = null);
public interface IAgendaExterna
{
    Task<ResultadoDaAgenda> CriarAsync(EventoDeAgenda evento, CancellationToken ct = default);
    Task<ResultadoDaAgenda> AtualizarAsync(string eventoId, EventoDeAgenda evento, CancellationToken ct = default);
    Task<ResultadoDaAgenda> ApagarAsync(string eventoId, CancellationToken ct = default);
}
public sealed class AgendaOptions   // "Intranet:Agenda"
{
    public string? Provedor { get; set; }                 // vazio ou "Microsoft" (sem diferenciar maiúsculas)
    public int IntervaloEmSegundos { get; set; } = 30;
    public MicrosoftAgendaOptions Microsoft { get; set; } = new();   // TenantId, ClientId, ClientSecret, CaixaOrganizadora
}
// ProcessarEspelhamentoHandler.HandleAsync(ct) -> int (quantas pendências tratou); lote de 20 reservas e 20 exclusões por ciclo
// TentarEspelharDeNovoHandler.HandleAsync(Guid id, QuemPede) -> Result      (só admin, só quando Falhou)
```

`MontadorDeEvento.Montar(Reserva, Sala)`: chave de idempotência = id da reserva (a mesma em toda tentativa de criação); assunto = título; corpo = "Reservado por {nome}" e, se houver, uma linha em branco e a descrição; local = nome da sala; participantes = dono e convidados que têm e-mail, sem repetir e-mail; `EmailDaSala` = o da sala.

Ciclo: para cada reserva pendente, cria (sem `EventoExternoId`) ou atualiza (com ele). `Feito` → `MarcarEspelhada`; `FalhaPassageira` → `RegistrarFalhaPassageira`; `FalhaDefinitiva` → `MarcarFalhaDefinitiva`. Cada reserva é gravada logo depois de tratada, e a falha de uma não interrompe as outras. Para cada exclusão pendente: `Feito` remove a pendência; as falhas seguem a mesma política de espera, e na definitiva a pendência fica marcada como falha e sai da fila. Se a reserva foi alterada entre a leitura e a gravação (a `Versao` mudou), o resultado da tentativa não muda o estado, que continua `Aguardando`.

Configuração: `Provedor` vazio → sem `EspelhamentoWorker`, `IEspelhamentoLigado.Ligado == false`, nada aparece nas telas. `Provedor` desconhecido derruba a subida. Com `Microsoft`, derrubam a subida: `TenantId` ou `ClientId` que não são GUID, `ClientSecret` vazio, `CaixaOrganizadora` que não é e-mail, `IntervaloEmSegundos` menor que 5. A mensagem do erro cita a chave, nunca o valor do segredo. O worker percorre os tenants do catálogo com um escopo por tenant, como `VencimentosWorker`, e expõe `ExecutarUmCicloAsync` para os testes. Nesta tarefa `Microsoft` registra uma `IAgendaExterna` provisória que devolve `FalhaDefinitiva("Provedor ainda não disponível.")`; a Tarefa 8 a substitui.

- [ ] **Step 1: Testes que falham.** `MontadorDeEventoTests`: `Monta_AssuntoCorpoLocal`, `ConvidadoSemEmail_FicaFora`, `EmailRepetido_EntraUmaVez`, `SalaSemEmail_EmailDaSalaNulo`, `Chave_EOIdDaReserva`. `ProcessarEspelhamentoTests` (com `AgendaFalsa` roteirizada):

```csharp
public async Task SemEvento_Cria_EMarcaEspelhada()
public async Task ComEvento_Atualiza()
public async Task FalhaPassageira_AgendaNovaTentativa_ENaoApareceAntesDaHora()
public async Task FalhaDefinitiva_MarcaFalhouComMotivo()
public async Task UmaFalha_NaoInterrompeAsOutras()
public async Task CriouMasNaoGravou_ProximoCicloUsaAMesmaChave()
public async Task AlteradaDuranteOEnvio_ContinuaAguardando()
public async Task ExclusaoPendente_Feita_SaiDaFila()
public async Task TentarDeNovo_DeFalhou_VoltaParaAFila_NaoAdmin_NotFound()
```

`AgendaConfiguracaoTests`: `SemProvedor_SobeSemWorker`, `ProvedorDesconhecido_NaoSobe`, e um `[Theory]` com cada chave do Microsoft inválida. `EspelhamentoWorkerTests`: `Ciclo_PercorreOsDoisTenants`, `FalhaNumTenant_NaoParaOOutro`, e pela tela: `Detalhe_Falhou_AdminVeBotao_DonoVeSoOMotivo`, `Detalhe_EspelhamentoDesligado_NaoMostraEstado`.

- [ ] **Step 2:** rodar → falham. **Step 3: Implementar.** **Step 4:** rodar → passam.
- [ ] **Step 5: Commit** `feat(salas): espelhamento das reservas na agenda externa`.

### Task 8: provedor Microsoft 365

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Salas/Microsoft/AgendaDoMicrosoft.cs`, `TokenDoMicrosoft.cs`
- Modify: `Infrastructure/Salas/AgendaServiceCollectionExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/AgendaDoMicrosoftTests.cs`, `TokenDoMicrosoftTests.cs`

**Interfaces:**
- Consumes: `IAgendaExterna`, `AgendaOptions` (Tarefa 7).
- Produces: `internal sealed class AgendaDoMicrosoft(HttpClient http, TokenDoMicrosoft token, AgendaOptions opcoes) : IAgendaExterna`, registrada com `AddHttpClient` e tempo limite de 30 segundos.

- [ ] **Step 1: Conferir na documentação atual da Microsoft** (páginas de criar, atualizar e apagar evento, do recurso `event` e do fluxo de credenciais de cliente) e anotar na descrição do commit a data da conferência: o formato do corpo do evento; a propriedade de idempotência da criação (`transactionId`) e o limite de tamanho dela; como a sala entra (participante do tipo `resource`); o endereço e os campos do pedido de token. Onde a documentação divergir do que está abaixo, vale a documentação, e a divergência é relatada.

Chamadas previstas (`{caixa}` = `CaixaOrganizadora`, escapada):

| Operação | Chamada |
|---|---|
| Token | `POST https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token`, `grant_type=client_credentials`, `scope=https://graph.microsoft.com/.default` |
| Criar | `POST https://graph.microsoft.com/v1.0/users/{caixa}/events` |
| Atualizar | `PATCH …/users/{caixa}/events/{id}` |
| Apagar | `DELETE …/users/{caixa}/events/{id}` |

Corpo do evento: assunto, corpo em texto simples, início e fim em UTC (`timeZone: "UTC"`), local com o nome da sala, participantes `required` e a sala como `resource`, e a chave de idempotência. O token fica em memória até um minuto antes de vencer; uma resposta 401 descarta o token e repete a chamada uma vez.

| Resposta | Desfecho |
|---|---|
| 2xx | `Feito` (na criação, com o `id` do evento) |
| 404 ao apagar | `Feito` |
| 408, 429, 5xx, tempo esgotado, erro de rede | `FalhaPassageira` |
| 400, 403, 404 (criar ou atualizar), 401 depois da repetição, falha no pedido de token por credencial | `FalhaDefinitiva` |

O motivo guardado é curto e legível ("A agenda recusou a permissão (403).", "A agenda não respondeu."), sem corpo de resposta bruto, sem token e sem segredo.

- [ ] **Step 2: Testes que falham**, com um `HttpMessageHandler` de teste que grava os pedidos e devolve respostas roteirizadas: `Criar_MontaOPedido` (verbo, endereço com a caixa escapada, cabeçalho `Authorization`, JSON com assunto, horários em UTC, participantes, sala como recurso e a chave), `Criar_DevolveOIdDoEvento`, `Atualizar_UsaPatchNoEvento`, `Apagar_404_EhFeito`, um `[Theory]` com a tabela de respostas, `Resposta401_RenovaOTokenERepeteUmaVez`, `Token_ReusaAteVencer`, `Token_CredencialRecusada_FalhaDefinitiva`, `Motivo_NaoCarregaSegredoNemToken`.
- [ ] **Step 3:** rodar → falham. **Step 4: Implementar** e trocar o registro provisório. **Step 5:** rodar → passam, junto com `AgendaConfiguracaoTests`.
- [ ] **Step 6: Commit** `feat(salas): provedor Microsoft 365 do espelhamento`.

### Task 9: testes de tela, documentação e fechamento

**Files:**
- Create: `tests/Secco.Intranet.Tests/Ui/SalasTelaTests.cs`
- Modify: `tests/Secco.Intranet.Tests/Ui/MenuDaArvoreTelaTests.cs` (`TelaFixture`: duas salas e uma reserva fictícias)
- Modify: `docs/guia-de-deploy.md`, `CHANGELOG.md`, `docs/roadmap.md`, `docs/specs/2026-11-16-reserva-de-sala-design.md` (estado), `.env.example` se as chaves do chat estiverem lá

- [ ] **Step 1: Testes de tela** (`[UiTheory]` nos dois temas, usando `Expect(...).ToHaveURLAsync` e nunca `WaitForURLAsync`): `Desktop_TodasAsSalasLadoALado_SemRolagemHorizontal`, `Celular_UmaSalaPorVez_AbaTrocaASala_SemRolagemHorizontal`, `CliqueNoEspacoLivre_AbreOFormularioPreenchido`, `Reservar_ApareceComoBlocoNaGrade`, `BuscaDeConvidados_FiltraALista`, `SemJavaScript_FormularioGrava`, `Telas_SemViolacaoDeCsp`.
- [ ] **Step 2:** `SECCO_UI_TESTS=1 dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~SalasTelaTests"` → passam.
- [ ] **Step 3: Guia de deploy.** Linhas novas na tabela de configuração para `Intranet__Salas__FusoHorario` e as chaves de `Intranet__Agenda__*`. Seção "Espelhar as reservas no Microsoft 365" com o passo a passo do administrador: registrar o aplicativo; criar a caixa de serviço; conceder o papel `Application Calendars.ReadWrite` com escopo só nessa caixa (RBAC for Applications do Exchange Online), sem a permissão ampla no Entra ID; configurar cada sala para aceitar pedidos só da caixa de serviço; roteiro de conferência manual (reservar, alterar, cancelar e ver o convite chegar). Os comandos são conferidos na documentação atual da Microsoft antes de escrever, com a data da conferência no texto. Avisar que reservas feitas antes de ligar o espelhamento não são enviadas.
- [ ] **Step 4: CHANGELOG** em `[Não lançado]`: a funcionalidade e a seção "Quebras — Reserva de sala" (migration `SalasEReservas`; slug `salas` passa a ser reservado, e setor com esse slug sai do menu até ser renomeado; chave `salas` reservada no menu principal; classes `sc-salas` novas no CSS do tema; `PrincipalNaOrdemPadrao` e `NavigationRequest` com parâmetro novo). Roadmap e estado da spec atualizados.
- [ ] **Step 5: Suíte completa** com e sem os testes de tela → tudo passa. Conferir `git status` limpo depois de `npm run build` nos dois temas.
- [ ] **Step 6: Commit** `docs(salas): guia de deploy, changelog e testes de tela da reserva de sala`.
