# Cifragem de arquivo pelo SDK — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** todo arquivo gravado pela intranet passa a ser cifrado e decifrado pelo `ISeccoFileCipher` do `Secco.SDK.EntityFrameworkCore` 0.7.0, e o `EnvelopeCipher` próprio sai do repositório.

**Architecture:**
- **Fábrica:** uma fábrica estática do produto (`CifradorDeArquivos.Criar`) aplica a política da chave mestra de hoje e monta `new AesGcmFileCipher(AesGcmSecretCipher.FromBase64Keys(...))`. A política é a seção `Intranet:Documentos:Chave`, com a chave embutida só em Development e erros de configuração como `ChaveMestraException`.
- **Registro:** o resultado é registrado como singleton `ISeccoFileCipher`.
- **Armazenamento:** o `SistemaArquivosArquivoStore` passa a depender dele. A porta `IArquivoStore` e tudo acima dela não mudam.

**Tech Stack:** .NET 10, `Secco.SDK.EntityFrameworkCore` 0.7.0, xUnit, AwesomeAssertions.

**Spec:** [docs/specs/2026-11-25-cifragem-de-arquivo-pelo-sdk-design.md](../specs/2026-11-25-cifragem-de-arquivo-pelo-sdk-design.md)

## Global Constraints

- **Pacotes:** `Secco.SDK.EntityFrameworkCore` vai para **0.7.0** e `Secco.SharedKernel` para a estável mais recente (hoje **0.7.14**). As outras versões `Secco.*` de `Directory.Packages.props` são conferidas no feed na Tarefa 0 (`gh api users/rafsecco/packages/nuget/<pacote>/versions --jq '.[0].name'`) e sobem para a estável mais recente.
- **Configuração da chave mestra:** a seção `Intranet:Documentos:Chave` e as propriedades `ChaveAtiva` e `ChavesAposentadas` não mudam de nome. A chave embutida `ChaveMestraOptions.ChaveDesenvolvimento` só vale em Development.
- **Erro de configuração da chave:** ausente fora de Development, base64 inválido ou tamanho errado, na ativa ou numa aposentada. Lança `ChaveMestraException`, com mensagem que contém "Chave mestra" (exigido por `StartupChaveMestraTests`).
- **O que não muda:**
  - `IArquivoStore`, `ArquivoGravado`, os handlers, as entidades, as colunas, as migrations e as telas;
  - o caminho dos arquivos (`{tenantId}/[pasta/]{ab}/{guid}`).
- **Arquivo parcial:** qualquer exceção na gravação apaga o arquivo parcial, inclusive tamanho acima do limite e cancelamento.
- **Testes:** só com AwesomeAssertions. Build sem avisos (`TreatWarningsAsErrors`).
- **Commits:** direto na `main`, conferindo `git branch --show-current` antes de cada commit. Cada commit leva o trailer `Co-Authored-By` do modelo que o escreveu.
- **Documentos históricos:** specs, planos e releases antigas do CHANGELOG que citam `EnvelopeCipher` ficam como estão.

## Review Focus

1. **Arquivo gravado no formato antigo (`SCCOFILE`) lido depois da troca:** falha com `SeccoSecretCipherException`, sem entregar byte nenhum ao destino. Teste na Tarefa 2.
2. **Arquivo vazio e arquivo com exatamente 64 KiB, 64 KiB ± 1:** a ida e volta devolve o mesmo conteúdo, e `Tamanho` bate. Teste na Tarefa 2.
3. **Arquivo com exatamente `TamanhoMaximoBytes`:** é aceito. Um byte a mais lança `SeccoFileTooLargeException` e não deixa arquivo no disco. Teste na Tarefa 2.
4. **Chave aposentada malformada:** uma aposentada em base64 inválido, com a ativa válida, impede a subida com `ChaveMestraException`, em vez de falhar só no primeiro download antigo. Teste na Tarefa 1.
5. **Upload cancelado no meio:** lança `OperationCanceledException` e não sobra arquivo na raiz. Teste na Tarefa 2.

---

### Task 0: Pacotes da plataforma na versão nova

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `CHANGELOG.md` (seção `[Não lançado]`)

- [ ] **Step 1:** Conferir no feed a versão mais recente de cada pacote `Secco.*` de `Directory.Packages.props` e subir as que estiverem atrás, com `Secco.SDK.EntityFrameworkCore` em `0.7.0`.
- [ ] **Step 2:** Rodar `dotnet restore` e `dotnet build -warnaserror`. Esperado: restauração sem erro e 0 avisos. Se der 401, rodar `gh auth status`: a credencial do feed fica no NuGet.Config de usuário, nunca no repositório.
- [ ] **Step 3:** Rodar `dotnet test`. Esperado: tudo verde. O `EnvelopeCipher` ainda está em uso, e nada muda de comportamento.
- [ ] **Step 4:** Registrar as versões no CHANGELOG, numa linha por pacote, no padrão das subidas anteriores.
- [ ] **Step 5:** Commit `chore(deps): Secco.SDK.EntityFrameworkCore 0.7.0 e pacotes da plataforma`.

### Task 1: Fábrica do cifrador com a política da chave mestra

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Armazenamento/CifradorDeArquivos.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/CifradorDeArquivosTests.cs`

**Interfaces:**
- Produces: `internal static class CifradorDeArquivos` com `public static ISeccoFileCipher Criar(ChaveMestraOptions options, IHostEnvironment environment)`.

- [ ] **Step 1: Escrever os testes que falham** em `CifradorDeArquivosTests`. Usar um `IHostEnvironment` falso, como o `AmbienteFalso` de `SistemaArquivosArquivoStoreTests`, e chaves de 32 bytes sorteadas em base64.
  - `Criar_SemChaveForaDeDevelopment_Falha` (Theory com `"Production"`, `"Staging"` e `"Testing"`): `ChaveAtiva = null`. Deve lançar `ChaveMestraException`.
  - `Criar_ComChaveEmBrancoEmDevelopment_UsaAChaveDeDesenvolvimento`: `ChaveAtiva = "  "`. Cifrar e decifrar ida e volta funciona. Um cifrador montado à mão com `AesGcmSecretCipher.FromBase64Keys(ChaveMestraOptions.ChaveDesenvolvimento)` decifra o mesmo arquivo, o que prova que a chave usada é a embutida.
  - `Criar_ComChaveAtivaEmBase64Invalido_Falha`: `ChaveAtiva = "não é base64"`. Deve lançar `ChaveMestraException`, com `Message` contendo "Chave mestra" e `InnerException` do tipo `SeccoSecretCipherException`.
  - `Criar_ComChaveAtivaDeTamanhoErrado_Falha`: base64 de 16 bytes. Mesmas asserções do anterior.
  - `Criar_ComChaveAposentadaMalformada_Falha` (Review Focus 4): ativa válida e `ChavesAposentadas = ["xx"]`. Deve lançar `ChaveMestraException`.
  - `Decifrar_ArquivoDaChaveAposentada_Funciona`:
    - cifrar com o cifrador A (ativa `k1`);
    - montar o cifrador B (ativa `k2`, aposentadas `[k1]`);
    - B decifra o arquivo de A com o mesmo conteúdo;
    - `B.IsKeyWrappedWithActiveKey(chaveDeA)` deve ser `false`.
  - `Decifrar_ComChaveMestraDesconhecida_Falha`: o cifrador com ativa `k2` e sem aposentadas, decifrando um arquivo de `k1`, lança `SeccoSecretCipherException`.
- [ ] **Step 2:** Rodar `dotnet test --filter FullyQualifiedName~CifradorDeArquivosTests`. Esperado: falha de compilação, porque `CifradorDeArquivos` não existe.
- [ ] **Step 3: Implementar `CifradorDeArquivos.Criar`.** É a mesma escolha de chave ativa do construtor atual do `EnvelopeCipher`: chave em branco usa a embutida em Development e falha fora dele com `"Chave mestra de documentos ausente fora de Development."`. Depois disso:
  - monta `new AesGcmFileCipher(AesGcmSecretCipher.FromBase64Keys(ativa, options.ChavesAposentadas))`;
  - captura `SeccoSecretCipherException` e relança `new ChaveMestraException("Chave mestra de documentos inválida: base64 de 32 bytes esperado (AES-256).", excecao)`.
- [ ] **Step 4:** Rodar o mesmo filtro. Esperado: tudo verde.
- [ ] **Step 5:** Commit `feat(armazenamento): fabrica do cifrador de arquivos do SDK com a politica da chave mestra`.

### Task 2: Armazenamento no cifrador do SDK e fim do `EnvelopeCipher`

**Files:**
- Modify: `src/Secco.Intranet.Infrastructure/Armazenamento/SistemaArquivosArquivoStore.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs:77-78`
- Modify: `src/Secco.Intranet.Infrastructure/Armazenamento/ValidacaoChaveMestraHostedService.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Armazenamento/ChaveMestraOptions.cs` (só os comentários XML)
- Delete: `src/Secco.Intranet.Infrastructure/Armazenamento/EnvelopeCipher.cs`
- Delete: `tests/Secco.Intranet.Tests/Unit/EnvelopeCipherTests.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/SistemaArquivosArquivoStoreTests.cs`

**Interfaces:**
- Consumes: `CifradorDeArquivos.Criar(ChaveMestraOptions, IHostEnvironment)`, da Tarefa 1.
- Produces: o construtor `SistemaArquivosArquivoStore(ArquivoStoreOptions options, DocumentoOptions documentoOptions, ISeccoFileCipher cipher, ITenantContext tenantContext)`.

- [ ] **Step 1: Ajustar a montagem e escrever os testes que falham** em `SistemaArquivosArquivoStoreTests`.
  - **Montagem:** `CriarStore(long? limite = null)` monta o cifrador com `CifradorDeArquivos.Criar(new ChaveMestraOptions(), new AmbienteFalso())` e `DocumentoOptions { TamanhoMaximoBytes = limite ?? <default> }`. Os quatro testes existentes ficam.
  - `GravarELer_EmQualquerTamanho_DevolveOConteudo` (Theory com 0, 65535, 65536, 65537 e 3 × 65536 + 7; Review Focus 2): o conteúdo lido é igual ao gravado, `gravado.Tamanho == tamanho` e `gravado.ChaveEmbrulhada` começa com `"secco-enc:v1:"`.
  - `Gravar_ArquivoNoDisco_ComecaComOCabecalhoDoSdk`: os 9 primeiros bytes do arquivo em `Path.Combine(_raiz, gravado.CaminhoRelativo)` são `"SECCOENC"u8` seguido de `0x01`.
  - `Ler_ArquivoAdulterado_Falha`: inverter um byte depois do cabeçalho deve lançar `SeccoSecretCipherException` em `EscreverEmAsync`.
  - `Ler_ArquivoNoFormatoAntigo_Falha` (Review Focus 1): sobrescrever o arquivo gravado com `"SCCOFILE"u8` mais 1 KiB qualquer, mantendo a chave embrulhada válida. Deve lançar `SeccoSecretCipherException`, e o `MemoryStream` de destino fica com `Length == 0`.
  - `Ler_ComChaveDeOutraChaveMestra_Falha`: gravar com um store de chave ativa `k1` e ler o mesmo caminho e a mesma chave com um store de `k2` (ambiente `"Production"`). Deve lançar `SeccoSecretCipherException`.
  - `Gravar_NoLimiteExato_Aceita` e `Gravar_UmByteAcimaDoLimite_FalhaSemDeixarArquivo` (Review Focus 3), com `limite = 1000`: 1000 bytes são gravados. 1001 bytes lançam `SeccoFileTooLargeException`, e `Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories)` sai vazio.
  - `Gravar_Cancelado_FalhaSemDeixarArquivo` (Review Focus 5): um `CancellationToken` já cancelado lança `OperationCanceledException` (ou derivada), e a raiz fica sem arquivos.
- [ ] **Step 2:** Rodar `dotnet test --filter FullyQualifiedName~SistemaArquivosArquivoStoreTests`. Esperado: falha de compilação, porque o construtor ainda pede `EnvelopeCipher`.
- [ ] **Step 3: Trocar o cifrador no `SistemaArquivosArquivoStore`.**
  - Gravar: `var cifrado = await cipher.EncryptAsync(conteudo, destino, documentoOptions.TamanhoMaximoBytes, cancellationToken)`, com retorno `new ArquivoGravado(caminhoRelativo, cifrado.WrappedKey, cifrado.PlaintextLength)`.
  - Ler: `await cipher.DecryptAsync(origem, destino, chaveEmbrulhada, cancellationToken)`.
  - Sai todo o manuseio de `chaveDoArquivo` e `Array.Clear`. O `catch` que apaga o parcial fica.
- [ ] **Step 4: Atualizar o registro e a guarda de subida.**
  - Em `IntranetInfrastructureExtensions`, `AddSingleton<EnvelopeCipher>()` vira `AddSingleton<ISeccoFileCipher>(sp => CifradorDeArquivos.Criar(sp.GetRequiredService<ChaveMestraOptions>(), sp.GetRequiredService<IHostEnvironment>()))`.
  - `ValidacaoChaveMestraHostedService` resolve `ISeccoFileCipher`.
- [ ] **Step 5: Corrigir os comentários de `ChaveMestraOptions`.** A classe e `ChavesAposentadas` deixam de dizer que a rotação reembrulha os documentos. As aposentadas servem só para ler arquivos cifrados antes da troca, e ainda não há rotina que converta as chaves dos arquivos antigos, então uma aposentada não pode ser retirada. A propriedade do envelope (trocar a chave mestra só mexe na coluna, nunca no arquivo) pode ficar.
- [ ] **Step 6:** Apagar `EnvelopeCipher.cs` e `EnvelopeCipherTests.cs`. Rodar `grep -rn EnvelopeCipher src tests --include=*.cs`. Esperado: nenhuma linha.
- [ ] **Step 7:** Rodar `dotnet build -warnaserror` e `dotnet test`. Esperado:
  - 0 avisos;
  - tudo verde, inclusive `StartupChaveMestraTests`, `DocumentoFluxoTests` e os testes de Mural e Diretório que gravam imagem.
- [ ] **Step 8:** Commit `feat(armazenamento): arquivos cifrados pelo ISeccoFileCipher do SDK (secco-platform#54)`. No corpo, registrar a quebra: arquivos gravados antes não abrem mais.

### Task 3: Documentação

**Files:**
- Modify: `docs/seguranca.md`
- Modify: `docs/guia-de-deploy.md:276` (e a nota de rotação perto da linha 468)
- Modify: `docs/adr/secco-intranet-adrs.md` (ADR-0005)
- Modify: `docs/plataforma.md`
- Modify: `docs/roadmap.md`
- Modify: `CHANGELOG.md` (seção `[Não lançado]`)
- Modify: `docs/specs/2026-11-25-cifragem-de-arquivo-pelo-sdk-design.md` (Estado)

- [ ] **Step 1: `seguranca.md`, seção "Cifrados no servidor".**
  - Os blocos passam a 64 KiB.
  - Acrescentar que a cifragem vem do SDK da plataforma (`Secco.SDK.EntityFrameworkCore`, ADR-0061 da plataforma).
  - O resto da seção continua verdadeiro e fica como está.
- [ ] **Step 2: `seguranca.md`, item "Troca de chave".** Passa a dizer o que existe:
  - uma chave nova cifra os arquivos novos;
  - as antigas, nas aposentadas, continuam necessárias para ler os arquivos anteriores;
  - ainda não há ferramenta que converta as chaves dos arquivos antigos para a chave nova, então uma chave aposentada não pode ser descartada enquanto existir arquivo cifrado com ela;
  - quando a ferramenta existir, ela regrava só as chaves no banco, não os arquivos.
- [ ] **Step 3: `guia-de-deploy.md`.** A linha de `ChavesAposentadas` troca "até a chave ativa ter reembrulhado todos os documentos" pelo mesmo fato do Step 2: mantenha enquanto existir arquivo cifrado com ela, porque ainda não há reembrulho.
- [ ] **Step 4: ADR-0005.**
  - Trocar "blocos de 1 MiB" por "blocos de 64 KiB", na Decisão e nas Consequências.
  - Acrescentar ao fim da Decisão uma nota datada: "**Atualização (2026-11-25):** a cifragem passou a ser a do `Secco.SDK.EntityFrameworkCore` (`ISeccoFileCipher`, ADR-0061 da plataforma), formato `SECCOENC` v1, construção STREAM; o `EnvelopeCipher` próprio saiu."
  - A decisão em si não muda.
- [ ] **Step 5: `plataforma.md`.**
  - Tirar a linha da #54 de "Demandas abertas". A tabela fica vazia, no mesmo formato que o arquivo já usa quando não há demanda aberta (se não houver precedente, deixar uma linha "Nenhuma no momento.").
  - Acrescentar a #54 no topo de "Atendidas", no padrão das outras linhas: `Secco.SDK.EntityFrameworkCore` **0.7.0** (2026-11-25), ADR-0061, `ISeccoFileCipher`/`AesGcmFileCipher`, blocos de 64 KiB, `RewrapKey` para a rotação, e a versão do `SharedKernel` que veio junto. Registrar também que os arquivos no formato antigo foram descartados, sem recifragem.
- [ ] **Step 6: `roadmap.md`.**
  - Marcar `[x]` no item "Avaliar trocar o embrulho de chave do `EnvelopeCipher`...", com a data e uma linha dizendo que o SDK cobriu o embrulho e o conteúdo (#54).
  - Acrescentar logo abaixo `[ ]` "Rotina de troca da chave mestra". Ela percorre o banco de cada tenant, reembrulha com `RewrapKey` as chaves que `IsKeyWrappedWithActiveKey` aponta como antigas, nas quatro colunas de chave (documento, imagem de destaque, imagem no texto, foto), e é o que permite descartar uma chave aposentada.
- [ ] **Step 7: CHANGELOG, em `[Não lançado]`.**
  - Em Alterado: a cifragem dos arquivos passa a ser a do SDK.
  - Na seção de quebras do arquivo, ou em Alterado com o prefixo "**Quebra:**" se não houver seção: arquivos gravados antes não abrem mais. Para limpar, apague os arquivos e os registros que apontam para eles, ou zere o banco e a pasta de armazenamento de dev.
- [ ] **Step 8:** No topo da spec, `**Estado:** entregue em <data do commit>.`
- [ ] **Step 9:** Rodar `grep -n "1 MiB" docs/seguranca.md docs/adr/secco-intranet-adrs.md` e `grep -n "reembrulhado todos" docs/guia-de-deploy.md docs/seguranca.md src -r`. Esperado: nenhuma linha.
- [ ] **Step 10:** Commit `docs: cifragem de arquivo pelo SDK e troca de chave sem ferramenta (secco-platform#54)`.
