# Troca da chave mestra — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a cada `migrate`, toda chave de arquivo embrulhada por uma chave mestra aposentada é reembrulhada pela ativa, nas quatro colunas de chave de todos os tenants. Uma chave que nenhuma chave configurada abre faz o tenant falhar.

**Architecture:**
- **A rotina:** uma classe de Infrastructure, `ReembrulhoDeChavesDeArquivo`, recebe o `IntranetDbContext` de um tenant e percorre as quatro colunas. Para cada linha cuja chave não está na ativa, grava `RewrapKey` com `ExecuteUpdateAsync` condicionado à chave lida.
- **Onde ela roda:** o `IntranetTenantMigrator` a chama depois das migrations de cada tenant, registra as contagens e põe o tenant na lista de falhas quando sobra chave sem chave conhecida.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server e PostgreSQL), `Secco.SDK.EntityFrameworkCore` 0.7.0 (`ISeccoFileCipher`), xUnit, AwesomeAssertions, Testcontainers.

**Spec:** [docs/specs/2026-11-25-troca-da-chave-mestra-design.md](../specs/2026-11-25-troca-da-chave-mestra-design.md)

## Global Constraints

- **As quatro colunas:**

  | Entidade | Propriedade | Coluna |
  |---|---|---|
  | `Documento` | `ChaveEmbrulhada` | `ds_chave_embrulhada` |
  | `Publicacao` | `ImagemChaveEmbrulhada` | `ds_imagem_chave_embrulhada` |
  | `ImagemNoTexto` | `ChaveEmbrulhada` | `ds_chave_embrulhada` |
  | `PerfilColaborador` | `FotoChaveEmbrulhada` | `ds_foto_chave_embrulhada` |

- **Linhas consideradas:** só as de chave preenchida (não nula nem vazia). As entidades não têm filtro de consulta, então documentos arquivados e publicações inativas entram.
- **Gravação:** só por `ExecuteUpdateAsync`, condicionado a `Id == id` **e** chave igual à lida. Sem SQL cru, e nenhuma outra coluna é tocada.
- **Escopo:** nenhum arquivo é lido nem regravado. Não há migration de banco nesta mudança.
- **Chave sem chave conhecida** (`SeccoSecretCipherException` em `RewrapKey`): a linha é contada e a passada continua. No migrator, o tenant entra na lista de falhas.
- **Log e exceções:** nunca levam chave (embrulhada ou não), connection string nem caminho de arquivo.
- **Testes:** só com AwesomeAssertions. Build sem avisos (`TreatWarningsAsErrors`). Os testes de integração usam Testcontainers e precisam do Docker.
- **Commits:** cada commit leva o trailer `Co-Authored-By` do modelo que o escreveu.

## Review Focus

1. **Documento arquivado e documento-grupo:** o arquivado tem chave e entra no reembrulho. O grupo tem `ChaveEmbrulhada` nula e é ignorado sem erro. Teste na Tarefa 1.
2. **Imagem no texto com chave vazia** (o construtor do EF a inicia com `string.Empty`): é ignorada, não conta como "sem chave conhecida". Teste na Tarefa 1.
3. **Chave trocada entre a leitura e a escrita:** a atualização condicionada não sobrescreve, e a próxima passada converge a linha. Teste na Tarefa 1.
4. **Tenant com migration falhando:** o reembrulho não roda nele, e o tenant aparece uma vez só na lista de falhas. Teste na Tarefa 2 (unitário, tenant inalcançável).
5. **Segundo `migrate` sem nada a fazer:** nenhuma linha muda e nenhum log de reembrulho é emitido. Teste na Tarefa 2.

---

### Task 1: A rotina de reembrulho

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Armazenamento/ReembrulhoDeChavesDeArquivo.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/ReembrulhoDeChavesDeArquivoTests.cs`

**Interfaces:**
- Produces:
  - `internal sealed class ReembrulhoDeChavesDeArquivo(ISeccoFileCipher cipher)`;
  - `public Task<ResultadoDoReembrulho> ReembrulharAsync(IntranetDbContext context, CancellationToken cancellationToken = default)`;
  - `internal sealed record ResultadoDoReembrulho(IReadOnlyList<ContagemDaColuna> Colunas)`, com `public int Reembrulhadas` (soma) e `public int SemChaveConhecida` (soma);
  - `internal sealed record ContagemDaColuna(string Tabela, int Reembrulhadas, int SemChaveConhecida)`. `Tabela` é o nome da tabela no banco, e só serve ao log;
  - a gravação condicionada, exposta para o teste de corrida: `internal static Task<bool> TrocarSeAindaForAsync<TEntidade>(IntranetDbContext context, string propriedade, Guid id, string chaveLida, string chaveNova, CancellationToken cancellationToken) where TEntidade : BaseEntity`, que devolve `true` quando uma linha foi atualizada. Usa `EF.Property<string>(e, propriedade)` no `Where` e no `SetProperty`.

- [ ] **Step 1: Escrever os testes que falham** em `ReembrulhoDeChavesDeArquivoTests`.
  - **Fixture e banco:** classe com `IClassFixture<IntranetWebFactory>` própria e `InitializeAsync => factory.EnsureDatabaseMigratedAsync()`. O contexto vem de `factory.Services.CreateScope()`, `SetTenant(factory.TenantAlfa)` e `GetRequiredService<IntranetDbContext>()`.
  - **Cifradores:** montar com `CifradorDeArquivos.Criar(new ChaveMestraOptions { ChaveAtiva = ..., ChavesAposentadas = [...] }, ambiente)`, com as chaves A, B e C sorteadas (32 bytes em base64) e um `IHostEnvironment` falso em `"Production"`.
  - **Arquivo de verdade:** `cipherA.EncryptAsync` de um conteúdo conhecido para um `MemoryStream` dá `chaveA` e os bytes cifrados.
  - **Linhas:** criar uma linha em cada tabela com `chaveA`, usando os construtores e métodos de domínio que os testes de persistência já usam (`ImagemNoTextoRepositoryTests`, `PerfilColaboradorPersistenciaTests` e o fluxo de documento, para `Documento` com dono válido). O `CaminhoRelativo` pode ser fictício.
  - `ReembrulharAsync_ChavesDaAposentada_ReembrulhaAsQuatroColunas`:
    - rodar a rotina com `cipherB = Criar(ativa B, aposentadas [A])`;
    - o resultado tem `Reembrulhadas == 4` e `SemChaveConhecida == 0`;
    - relidas sem rastreamento, as quatro chaves passam em `cipherB.IsKeyWrappedWithActiveKey`;
    - `cipherSoB = Criar(ativa B, sem aposentadas)` decifra os bytes com a chave nova de cada linha e devolve o conteúdo original.
  - `ReembrulharAsync_SegundaPassada_NaoMudaNada`: depois da primeira passada, uma segunda devolve `Reembrulhadas == 0`, e as chaves ficam idênticas às da primeira.
  - `ReembrulharAsync_ChaveSemChaveConhecida_ContaESegue`:
    - uma linha com chave embrulhada por C, que não está configurada, e outra com `chaveA`;
    - com `cipherB`, o resultado tem `SemChaveConhecida == 1` e `Reembrulhadas >= 1`;
    - a linha de C fica inalterada, e a de A é convertida.
  - `ReembrulharAsync_DocumentoArquivadoEGrupo` (Review Focus 1): o documento arquivado com `chaveA` é convertido, e o documento-grupo (chave nula) não entra em contagem nenhuma.
  - `ReembrulharAsync_ImagemNoTextoComChaveVazia_Ignora` (Review Focus 2): a chave vazia gera contagem zero, e a linha fica intocada.
  - `TrocarSeAindaForAsync_ChaveMudouDepoisDaLeitura_NaoSobrescreve` (Review Focus 3):
    - ler a chave de um `PerfilColaborador` e, antes de chamar o método, mudar a coluna para `chaveX`;
    - chamar com `chaveLida` igual à antiga deve devolver `false`, e a coluna continua `chaveX`;
    - chamar com `chaveLida = chaveX` devolve `true`.
- [ ] **Step 2:** Rodar `dotnet test --filter FullyQualifiedName~ReembrulhoDeChavesDeArquivoTests`. Esperado: falha de compilação, porque o tipo não existe.
- [ ] **Step 3: Implementar a rotina.**
  - Para cada coluna da tabela das Global Constraints, ler `(Id, chave)` com `AsNoTracking`, só das linhas de chave preenchida.
  - Pular as chaves que `IsKeyWrappedWithActiveKey` dá como em dia.
  - Para as outras, chamar `RewrapKey` e depois `TrocarSeAindaForAsync`.
  - Capturar só `SeccoSecretCipherException`, que conta como `SemChaveConhecida`.
  - Uma atualização que devolve `false` não conta em nada.
  - A forma (laço sobre descritores ou quatro chamadas) é escolha de quem implementa.
- [ ] **Step 4:** Rodar o mesmo filtro. Esperado: tudo verde.
- [ ] **Step 5:** Commit `feat(armazenamento): reembrulho das chaves de arquivo para a chave mestra ativa`.

### Task 2: Reembrulho no `migrate`

**Files:**
- Modify: `src/Secco.Intranet.Infrastructure/IntranetTenantMigrator.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (registro)
- Modify: `tests/Secco.Intranet.Tests/Unit/IntranetTenantMigratorTests.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/TrocaDaChaveMestraTests.cs`

**Interfaces:**
- Consumes: `ReembrulhoDeChavesDeArquivo.ReembrulharAsync(IntranetDbContext, CancellationToken)` e `ResultadoDoReembrulho`, da Tarefa 1.
- Produces: o construtor `IntranetTenantMigrator(ITenantCatalog catalog, IntranetDatabaseOptions databaseOptions, ReembrulhoDeChavesDeArquivo reembrulho, ILogger<IntranetTenantMigrator> logger)`.

- [ ] **Step 1: Ajustar a construção e escrever os testes que falham.**
  - **`IntranetTenantMigratorTests`:** os testes atuais passam a construir o migrator com um `ReembrulhoDeChavesDeArquivo` real, montado com `CifradorDeArquivos.Criar` e chave sorteada.
  - `MigrateAsync_TenantComMigrationFalhando_ApareceUmaVez` (Review Focus 4): com um tenant inalcançável, `falhas` contém o id dele **exatamente uma vez**.
  - **`TrocaDaChaveMestraTests`:** integração, com `IClassFixture<IntranetWebFactory>` própria, para os bancos dos tenants terem só as linhas desta classe.
    - O host é montado com `factory.WithWebHostBuilder`. A configuração `Intranet:Documentos:Chave:ChaveAtiva = B` e `Intranet:Documentos:Chave:ChavesAposentadas:0 = A` entra por `ConfigureAppConfiguration` com `AddInMemoryCollection`, como em `StartupChaveMestraTests`.
    - O log é capturado por um `ILoggerProvider` de teste, registrado com `ConfigureLogging`.
  - `Migrate_ComChaveAposentada_ReembrulhaNosDoisTenants`:
    - uma linha de `PerfilColaborador` com chave embrulhada por A em `TenantAlfa` e outra em `TenantBeta`;
    - `RunSeccoMigrationsAsync()` devolve `true`;
    - as duas chaves passam em `IsKeyWrappedWithActiveKey` do cifrador de B;
    - nenhuma mensagem capturada contém a chave antiga, a nova, nem o texto `"secco-enc:"`.
  - `Migrate_SegundaPassada_NaoMudaNadaNemLoga` (Review Focus 5): depois da primeira passada, a segunda devolve `true`, as chaves ficam iguais e não aparece nenhuma mensagem nova de reembrulho.
  - `Migrate_ChaveSemChaveConhecida_FalhaOTenantEConvergeOOutro`:
    - em `TenantAlfa`, uma linha embrulhada por C; em `TenantBeta`, uma por A;
    - `RunSeccoMigrationsAsync()` devolve `false`;
    - a linha de `TenantBeta` está convertida;
    - existe uma mensagem capturada com o id de `TenantAlfa` e sem nenhuma chave.
- [ ] **Step 2:** Rodar `dotnet test --filter "FullyQualifiedName~TrocaDaChaveMestraTests|FullyQualifiedName~IntranetTenantMigratorTests"`. Esperado: falha de compilação, porque o construtor ainda não recebe o reembrulho.
- [ ] **Step 3: Implementar no migrator.**
  - No laço por tenant, depois de `MigrateAsync` dar certo e com o mesmo contexto, chamar `reembrulho.ReembrulharAsync(context, ct)`.
  - **Log com `[LoggerMessage]`:**
    - para cada `ContagemDaColuna` com algo diferente de zero, `Information` com `TenantId`, `Tabela`, `Reembrulhadas` e `SemChaveConhecida`;
    - quando `SemChaveConhecida > 0`, `Error` dizendo que o tenant tem chave de arquivo que nenhuma chave mestra configurada abre, e que provavelmente uma aposentada foi retirada cedo demais.
  - **Falha:** com `SemChaveConhecida > 0`, acrescentar o id do tenant em `failures`. O `catch` existente continua cobrindo exceções, e um tenant nunca é acrescentado duas vezes.
- [ ] **Step 4: Registrar.** Em `IntranetInfrastructureExtensions`, `services.AddSingleton<ReembrulhoDeChavesDeArquivo>()`, perto do registro do `ISeccoFileCipher`.
- [ ] **Step 5:** Rodar o filtro do Step 2 e depois `dotnet build -warnaserror` e `dotnet test` completos. Esperado: tudo verde e 0 avisos, inclusive `MigracoesDosTenantsTests`.
- [ ] **Step 6:** Commit `feat(armazenamento): migrate reembrulha as chaves de arquivo em todos os tenants`.

### Task 3: Documentação

**Files:**
- Modify: `docs/seguranca.md`
- Modify: `docs/guia-de-deploy.md`
- Modify: `src/Secco.Intranet.Infrastructure/Armazenamento/ChaveMestraOptions.cs` (comentários XML)
- Modify: `docs/adr/secco-intranet-adrs.md` (ADR-0005)
- Modify: `docs/roadmap.md`
- Modify: `CHANGELOG.md` (seção `[Não lançado]`)
- Modify: `docs/specs/2026-11-25-troca-da-chave-mestra-design.md` (Estado)

- [ ] **Step 1: `seguranca.md`, item "Troca de chave".** Passa a dizer:
  - a troca existe: a chave nova vai para a ativa, a atual para as aposentadas, e o `migrate` de cada deploy reembrulha as chaves dos arquivos, sem regravar nenhum arquivo;
  - a aposentada sai da configuração quando um `migrate` não reembrulha mais nada;
  - o procedimento está no guia de deploy, seção "Troca da chave mestra".
- [ ] **Step 2: `guia-de-deploy.md`.**
  - Nova seção "Troca da chave mestra", perto de "Backup e restauração", com os cinco passos da spec ("Procedimento de troca"), incluindo o `migrate` de novo depois que todos os containers estiverem na configuração nova.
  - O código de saída 1 aponta "sem chave conhecida" no log.
  - A linha da variável `ChavesAposentadas` e as notas de rotação e restauração deixam de dizer que não há ferramenta, e passam a apontar a seção nova.
- [ ] **Step 3: `ChaveMestraOptions`.** Os comentários da classe e de `ChavesAposentadas` deixam de dizer que não há rotina: o `migrate` reembrulha, e a aposentada sai quando nada mais depende dela.
- [ ] **Step 4: ADR-0005, nas Consequências.** A frase "A rotina que faz isso ainda não existe (ver roadmap)." vira "O `migrate` faz esse reembrulho em todos os tenants (2026-11-25)."
- [ ] **Step 5: `roadmap.md`.** Marcar `[x]` no item "Rotina de troca da chave mestra", com a data e uma linha sobre o `migrate`.
- [ ] **Step 6: CHANGELOG.** Em `[Não lançado]`, nova seção "### Troca da chave mestra", no padrão das seções por funcionalidade do arquivo, com link para a spec. Ela registra:
  - o reembrulho no `migrate`;
  - a falha do tenant quando sobra chave sem chave conhecida;
  - o procedimento no guia de deploy.
- [ ] **Step 7:** No topo da spec, `**Estado:** entregue em 2026-11-25.`
- [ ] **Step 8:** Rodar `grep -rn -iE "ainda não (existe|há) (rotina|ferramenta)|não há ferramenta|não há rotina" docs/seguranca.md docs/guia-de-deploy.md docs/adr/secco-intranet-adrs.md src/Secco.Intranet.Infrastructure/Armazenamento/ChaveMestraOptions.cs`. Esperado: nenhuma linha.
- [ ] **Step 9:** Commit `docs: troca da chave mestra pelo migrate`.
