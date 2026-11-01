# Imagem na publicação do Mural — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** quem escreve no setor anexa uma imagem ao aviso, que aparece inteira no fim do card, com a mesma regra de quem vê da publicação; só o título passa a ser obrigatório.

**Architecture:** a `Publicacao` ganha a referência a um arquivo cifrado (`IArquivoStore`, pasta `mural/imagens`), processado antes pela `Secco.SDK.Imaging` (WebP, maior lado 2048). Publicar e editar recebem o upload pelo comando; um caso de uso de leitura serve a imagem em `/publicacoes/{id}/imagem` com a regra da página da publicação mais a exceção do autor. O `CardModel` do contrato de tema ganha `Imagem`, desenhada depois do corpo.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core (SQL Server e Postgres), `Secco.SDK.Imaging`, xUnit + AwesomeAssertions, Razor nos temas `Vertical` e `Horizontal` (Sass compilado e versionado).

**Spec:** `docs/specs/2026-10-31-imagem-na-publicacao-design.md`

## Global Constraints

- Só o **título** é obrigatório; texto e imagem opcionais. Limite do texto: 4000 caracteres.
- Entrada aceita: JPEG, PNG ou WebP, até **10 MB** (limite já configurado da SDK, `Intranet:Imaging`). Saída: **WebP**, maior lado **2048 px**, sem ampliar, sem recorte.
- Armazenamento: `IArquivoStore`, pasta lógica **`mural/imagens`**. Descrição da imagem até **300** caracteres.
- Endpoint: **`GET /publicacoes/{id}/imagem`** — `image/webp`, `private, no-cache`, `ETag`, `nosniff`, `304` com `If-None-Match`. Inexistente, sem imagem ou fora do alcance: **404**.
- Leitura da imagem: regra do `ObterPublicacaoHandler` (no ar; `Empresa` ou setor do leitor) **ou** quem escreve no setor dono, para publicação ativa mesmo fora do ar.
- Mensagens: "A imagem passa de 10 MB." / "Envie a imagem em PNG, JPG ou WebP." / "Não foi possível ler esta imagem." / "A descrição da imagem passa de 300 caracteres."
- Ajuda do campo: "PNG, JPG ou WebP, até 10 MB. Aparece no fim do aviso, na largura do card." Descrição: "Para quem usa leitor de tela. Se a imagem é o aviso inteiro, escreva o que ela diz."
- Auditoria: `mural.publicar` ganha `imagem` (bool); `mural.editar` ganha `imagem` (bool) e `imagemAcao` (`manter|trocar|remover`).
- Banco: skill `secco-db-naming`; colunas pela convention; migration **`PublicacaoImagem`** nos dois engines, mesmo nome.
- Tema: contrato ADR-0004; `Vertical` e `Horizontal`, claro e escuro (skill `secco-intranet-temas`); CSS recompilado com `npm run build:css` em cada tema.
- Código: tabs em `.cs`, doc XML em português em todo público, `ConfigureAwait(false)` como nos vizinhos, erros de negócio por `Result`. Commits direto na `main`. Sem instalação em uso: nada de migração de dados.

## Review Focus

1. **Upload que falha depois do processamento** (o armazenamento lança): nada de linha gravada apontando para arquivo inexistente. → teste na Task 2.
2. **Editar sem mandar arquivo novo** (só muda o texto): a imagem existente continua, o arquivo não é apagado. → teste na Task 2.
3. **Apagar o arquivo antigo falha** (disco): a edição grava mesmo assim e só registra aviso no log. → teste na Task 2.
4. **Pedido da imagem de publicação agendada por quem só lê**: 404, sem vazar que existe; pelo autor do setor: 200. → teste na Task 3.
5. **Aviso sem texto e sem imagem no Mural**: card sem bloco de corpo vazio. → teste na Task 4.

---

### Task 1: Domínio, persistência e texto opcional

**Files:**
- Modify: `src/Secco.Intranet.Domain/Publicacoes/Publicacao.cs`
- Modify: `src/Secco.Intranet.Application/Publicacoes/ValidacaoDePublicacao.cs`, `PublicacaoDto.cs`, `Projecao.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs` (remove `Publicacoes.CorpoRequired`; acrescenta os erros de imagem da Task 2 aqui mesmo)
- Modify: `src/Secco.Intranet.Infrastructure/Mappings/PublicacaoConfiguration.cs`, `Repositories/PublicacaoRepository.cs` (as duas projeções para `PublicacaoDto`)
- Create: migration `PublicacaoImagem` em `src/Secco.Intranet.Migrations.SqlServer` e `src/Secco.Intranet.Migrations.Postgres` (task `ef: nova migration (ambos os engines)` do VS Code ou o comando equivalente)
- Test: `tests/Secco.Intranet.Tests/Unit/PublicacaoTests.cs`

**Interfaces:**
- Produces (domínio, `Publicacao`):
  - `string? ImagemCaminhoRelativo { get; }`, `string? ImagemChaveEmbrulhada { get; }`, `string? ImagemDescricao { get; }`, `bool TemImagem { get; }`
  - `const int LimiteDaDescricaoDaImagem = 300`
  - `string? DefinirImagem(string caminhoRelativo, string chaveEmbrulhada, string? descricao)` — devolve o caminho anterior (ou `null`)
  - `string? RemoverImagem()` — devolve o caminho anterior
  - `void DescreverImagem(string? descricao)` — só a descrição, exige `TemImagem` (lança `DomainInvariantException` sem imagem)
  - Corpo vazio ou nulo vira `string.Empty`; descrição em branco vira `null`; descrição acima de 300 lança `DomainInvariantException`.
- Produces (aplicação): `PublicacaoDto` ganha, no fim, `bool TemImagem` e `string? ImagemDescricao`.
- Produces (erros): `IntranetErrors.Publicacoes.ImagemGrandeDemais`, `ImagemFormatoNaoSuportado`, `ImagemInvalida`, `ImagemDescricaoTooLong` (textos das Global Constraints; códigos `Intranet.Publicacao.<Nome>`).

- [ ] **Step 1: Testes que falham** em `PublicacaoTests`:
  - `Criar_SemCorpo_Aceita` → `new Publicacao(..., corpo: "", ...)` e `"   "` ⇒ `Corpo.Should().BeEmpty()`.
  - `Criar_SemTitulo_ContinuaRecusando` → título `"  "` ⇒ `DomainInvariantException`.
  - `DefinirImagem_GuardaEDevolveOAnterior` → primeira chamada devolve `null`; segunda devolve o primeiro caminho; `TemImagem` true.
  - `RemoverImagem_LimpaOsTresCampos` → devolve o caminho; `ImagemDescricao` também nula.
  - `DefinirImagem_DescricaoAcimaDe300_Recusa` → 301 caracteres ⇒ `DomainInvariantException`.
  - `DescreverImagem_SemImagem_Recusa`.
- [ ] **Step 2:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~PublicacaoTests"` → falha de compilação.
- [ ] **Step 3: Implementar** o domínio; tirar `CorpoRequired` da `ValidacaoDePublicacao` e do `IntranetErrors` (e de quem o citar em teste); `HasMaxLength(256)` para caminho e chave, `HasMaxLength(300)` para a descrição; projeções do repositório e `Projecao.De` preenchem `TemImagem`/`ImagemDescricao`.
- [ ] **Step 4: Migration** `PublicacaoImagem` nos dois engines; conferir no `Up` as três colunas nulas `ds_imagem_caminho_relativo`, `ds_imagem_chave_embrulhada`, `ds_imagem_descricao`.
- [ ] **Step 5:** `dotnet build Secco.Intranet.slnx` (0 avisos) e `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~Publicacao"` → verde.
- [ ] **Step 6: Commit** `feat(mural): imagem na publicacao e texto opcional (dominio e banco)`.

### Task 2: Processar, guardar e trocar a imagem ao publicar e editar

**Files:**
- Modify: `src/Secco.Intranet.Application/Diretorio/IProcessadorDeImagem.cs`, `src/Secco.Intranet.Infrastructure/Diretorio/ProcessadorDeImagemDaSdk.cs`
- Modify: `src/Secco.Intranet.Application/Publicacoes/PublicarPublicacaoHandler.cs`, `EditarPublicacaoHandler.cs`
- Create: `src/Secco.Intranet.Application/Publicacoes/ImagemDaPublicacao.cs` (o helper de gravar/apagar abaixo)
- Modify: `tests/Secco.Intranet.Tests/Support/DublesDeFoto.cs` (`ProcessadorDeImagemFalso` e `ArquivoStoreFalso` ganham o que for preciso)
- Test: `tests/Secco.Intranet.Tests/Unit/ImagemDaPublicacaoHandlersTests.cs`; ajustar construções em `AuditoriaDoMuralTests`, `AvisoDePublicacaoTests`, `PublicacaoAutorizacaoTests`; `tests/Secco.Intranet.Tests/Unit/ProcessadorDeImagemDaSdkTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `IProcessadorDeImagem.ProcessarImagemDePublicacaoAsync(Stream upload, CancellationToken) : Task<Result<FotoProcessada>>` — variante única de 2048 px, WebP; erros da SDK traduzidos para `IntranetErrors.Publicacoes.Imagem*` (`InputTooLarge`→`ImagemGrandeDemais`, `UnsupportedFormat`→`ImagemFormatoNaoSuportado`, demais→`ImagemInvalida`).
  - `sealed record ImagemEnviada(Stream Conteudo)`.
  - `PublicarPublicacaoCommand` ganha, no fim: `ImagemEnviada? Imagem = null`, `string? ImagemDescricao = null`.
  - `EditarPublicacaoCommand` ganha, no fim: `ImagemEnviada? Imagem = null`, `string? ImagemDescricao = null`, `bool RemoverImagem = false`. Sem arquivo e sem `RemoverImagem`: mantém a imagem e aplica a descrição informada (via `DescreverImagem`, só se `TemImagem`).
  - Os dois handlers recebem `IProcessadorDeImagem`, `IArquivoStore` e `ILogger<T>` pelo construtor.

Ordem nos dois handlers: validar campos → processar a imagem (falha ⇒ `Result` de erro, nada gravado) → `store.GravarAsync(conteudo, "mural/imagens")` → aplicar no domínio → salvar → apagar o arquivo anterior (falha ⇒ `LogWarning` só com o tipo da exceção, como `FotoDoColaboradorHandler.ApagarAsync`) → auditar → notificar (publicar). Exceção do `GravarAsync` **sobe**, como no `PublicarDocumentoHandler`: é falha de infraestrutura, não erro de negócio; a garantia é só que nenhuma linha fica gravada.

- [ ] **Step 1: Testes que falham** em `ImagemDaPublicacaoHandlersTests` (dublês `ProcessadorDeImagemFalso`, `ArquivoStoreFalso`, repositório em memória do teste):
  - `Publicar_ComImagem_ProcessaGravaNaPastaDoMuralEDefine` → `store` recebeu pasta `"mural/imagens"`; `dto.TemImagem` true.
  - `Publicar_ImagemRecusada_NaoGravaNada` → processador falha com `ImagemFormatoNaoSuportado`; resultado com esse erro; `store` sem gravação; repositório vazio.
  - `Publicar_SemTextoSemImagem_Aceita`.
  - `Publicar_Audita_ImagemTrueOuFalse`.
  - `Editar_SemArquivoNovo_MantemImagemENaoApaga` (Review Focus 2).
  - `Editar_ArquivoNovo_TrocaEApagaOAnteriorDepoisDeSalvar` → `store.Removidos` contém o caminho antigo; auditoria `imagemAcao = "trocar"`.
  - `Editar_RemoverImagem_ApagaEAudita` → `imagemAcao = "remover"`.
  - `Editar_ApagarAnteriorFalha_GravaELogaAviso` (Review Focus 3).
  - `Publicar_StoreFalha_NaoGravaLinha` (Review Focus 1) → `ArquivoStoreFalso` configurado para lançar; repositório vazio.
  - Em `ProcessadorDeImagemDaSdkTests`: `ImagemDePublicacao_Grande_Reduz2048` e `ImagemDePublicacao_Pequena_NaoAmplia` (gerar PNG em memória como os testes vizinhos fazem).
- [ ] **Step 2:** rodar o filtro `ImagemDaPublicacao|ProcessadorDeImagemDaSdk` → falha.
- [ ] **Step 3: Implementar.** Atualizar as construções dos handlers nos testes existentes. Registrar nada novo no DI além do que os construtores pedem (as portas já estão registradas).
- [ ] **Step 4:** `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~Unit"` → verde.
- [ ] **Step 5: Commit** `feat(mural): processar e guardar a imagem ao publicar e editar`.

### Task 3: Servir a imagem

**Files:**
- Create: `src/Secco.Intranet.Application/Publicacoes/ObterImagemDaPublicacaoHandler.cs`
- Create: `src/Secco.Intranet.Application/ETagDeArquivo.cs`; Modify: `src/Secco.Intranet.Application/Diretorio/FotoDoColaboradorHandler.cs` passa a usá-lo
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs` (registro)
- Modify: `src/Secco.Intranet.Web/Controllers/MuralController.cs` (ação `Imagem`)
- Test: `tests/Secco.Intranet.Tests/Integration/PublicacaoImagemTests.cs`

**Interfaces:**
- Produces:
  - `static class ETagDeArquivo { static string De(string caminhoRelativo) }` — o mesmo cálculo de hoje da foto (SHA-256 do caminho, 16 hex, entre aspas). A foto do Diretório passa a usá-lo, sem mudar o valor.
  - `sealed record ObterImagemDaPublicacaoQuery(Guid Id, IReadOnlyList<string> SetoresDoLeitor, IReadOnlySet<string> SetoresComEscrita, bool ExigirVisibilidade)`
  - `sealed record ImagemGuardada(string CaminhoRelativo, string ChaveEmbrulhada, string ETag, string ContentType)` com `ContentType = "image/webp"`.
  - `ObterImagemDaPublicacaoHandler.HandleAsync(query, ct) : Task<Result<ImagemGuardada>>` — `NotFound` quando: não existe; inativa; sem imagem; nem (no ar e visível pela regra do `ObterPublicacaoHandler`) nem (`SetoresComEscrita` contém o slug do setor dono). Com `ExigirVisibilidade: false` (modo aberto de DEV), só ativa e com imagem.
  - `MuralController.Imagem(Guid id, CancellationToken)` em `[HttpGet("/publicacoes/{id:guid}/imagem")]`, cabeçalhos e decifragem em memória **iguais** a `DiretorioController.Foto` (inclusive o 404 para arquivo ausente). `SetoresComEscrita` vem de `IPermissoesDeSetor` com `"write"`, como o `Detalhe` faz para `administraOSetor`.

- [ ] **Step 1: Testes que falham** em `PublicacaoImagemTests` (publicam pela tela, como `PublicacaoFluxoTests`, com arquivo multipart; `factory.ProcessadorDeImagem` e `factory.ArquivoStore` como em `DiretorioFotoTests`):
  - `Entregar_ComImagem_200ComCabecalhos` → `image/webp`, `private, no-cache`, `ETag` presente, `nosniff`.
  - `Entregar_IfNoneMatchIgual_304`.
  - `Entregar_SemImagem_404` e `Entregar_Inexistente_404`.
  - `Entregar_SomenteSetor_LeitorDeFora_404`.
  - `Entregar_Agendada_SoLeitor_404_AutorDoSetor_200` (Review Focus 4).
  - `Entregar_ArquivoSumiuDoDisco_404`.
- [ ] **Step 2:** rodar `PublicacaoImagemTests` → falha.
- [ ] **Step 3: Implementar.** `FotoDoColaboradorHandler` usa `ETagDeArquivo.De`; os testes de foto existentes continuam verdes.
- [ ] **Step 4:** rodar `PublicacaoImagemTests|DiretorioFoto|FotoDoColaborador` → verde.
- [ ] **Step 5: Commit** `feat(mural): servir a imagem da publicacao com a regra de leitura`.

### Task 4: Formulário, edição e exibição

**Files:**
- Modify: `src/Secco.Intranet.Web/Models/Publicacoes/SetorAvisosViewModel.cs` (`PublicacaoFormViewModel`: tira o `[Required]` do `Corpo`; ganha `IFormFile? Imagem`, `string? ImagemDescricao` com `[StringLength(300)]`, `bool RemoverImagem`, `bool TemImagem` só de exibição)
- Modify: `src/Secco.Intranet.Web/Controllers/SetorController.cs` (`SalvarAviso` repassa imagem; GET da aba de avisos aceita `?editar={id}` e carrega o form; mensagem "Escolha a imagem de novo." quando o form volta com erro e havia arquivo)
- Modify: `src/Secco.Intranet.Web/Views/Setor/Avisos.cshtml` (`enctype="multipart/form-data"`, campos, miniatura e "Remover a imagem" na edição, botão **Editar** por linha, miniatura na lista)
- Modify: `src/Secco.Intranet.Web.Theming/Contracts/CardModel.cs` (+ `CardImagemModel`), `src/Secco.Intranet.Web/Models/Mural/*.cs`, `Controllers/MuralController.cs`, `Views/Mural/Index.cshtml`, `Views/Mural/Detalhe.cshtml`
- Modify: `_Card.cshtml` e `wwwroot/scss/_components.scss` (+ `css/theme.css` recompilado) nos dois temas; `wwwroot/js/editor.js` nos dois temas
- Modify: `docs/temas.md`
- Test: `tests/Secco.Intranet.Tests/Integration/PublicacaoImagemTests.cs` (mais casos), `PublicacaoFluxoTests.cs` (corpo opcional)

**Interfaces:**
- Consumes: Tasks 2 e 3.
- Produces:
  - `sealed record CardImagemModel(string Url, string? Descricao)`; `CardModel` ganha, no fim, `CardImagemModel? Imagem = null`. O tema desenha `<img class="sc-card__imagem" src alt loading="lazy">` **depois** do corpo; `alt` = descrição ou `""`.
  - `PublicacaoViewModel` ganha `string? ImagemUrl`, `string? ImagemDescricao`; `Corpo` passa a `IHtmlContent?`, nulo quando o texto está vazio (o card não desenha corpo vazio).
  - Edição: link "Editar" → `/{slug}/avisos?editar={id}`; só para quem pode publicar; publicação de outro setor ou inexistente ⇒ form vazio (sem vazar nada).

Decisão já tomada (ruling do plano): a aba de avisos **não tinha entrada para editar** — o caso de uso existia, a tela não. Esta task acrescenta o botão, porque trocar e remover a imagem (critério da spec) só existem na edição.

- [ ] **Step 1: Testes que falham** (integração):
  - `Publicar_PelaTela_ComImagem_CardDoMuralMostraImgDepoisDoTexto` → no HTML do Mural, a posição de `src="/publicacoes/{id}/imagem"` vem depois do texto do corpo; `alt` = descrição.
  - `Publicar_SoTitulo_Aceita` e `Publicar_SemTitulo_Recusa`.
  - `Mural_AvisoSemTexto_NaoDesenhaCorpoVazio` (Review Focus 5) → sem `<div class="sc-card__body"></div>`.
  - `Detalhe_ComImagem_MostraImg`.
  - `Avisos_Editar_CarregaOFormComMiniaturaERemover` e `Editar_RemoverImagem_TiraDoCard`.
  - `Publicar_ImagemRecusada_VoltaComAMensagemEPedeOArquivoDeNovo`.
  - `Mural_NosDoisTemas_MostraImgDaPublicacao` (`[InlineData("Vertical")] [InlineData("Horizontal")]`, como `Diretorio_NosDoisTemas_MostraImgDaFoto`).
- [ ] **Step 2:** rodar → falha.
- [ ] **Step 3: Implementar.** No `editor.js` dos dois temas: tirar `image` da barra (`toolbarItems` sem o item de imagem) e impedir colar imagem (`hooks.addImageBlobHook` que não insere nada). CSS: `.sc-card__imagem { display:block; max-width:100%; height:auto; margin-top:.75rem; border-radius: var(--bs-border-radius) }`, mais a miniatura da lista (`.sc-list__miniatura`, até 48 px de altura); `npm run build:css` nos dois temas.
- [ ] **Step 4:** `dotnet build Secco.Intranet.slnx` (0 avisos) e a suíte inteira `dotnet test Secco.Intranet.slnx` → verde (rodar sem outra execução do Testcontainers em paralelo).
- [ ] **Step 5: Conferência visual** (fica com o controller): publicar arte só-imagem, texto+imagem e só título; Mural, Detalhe e aba do setor nos dois temas, claro e escuro, e a 390 px.
- [ ] **Step 6:** `docs/temas.md` (campo `Imagem` do `CardModel`, classe `sc-card__imagem`); spec com estado "aprovado, implementado".
- [ ] **Step 7: Commit** `feat(mural): imagem no formulario do aviso e no card`.

### Task 5: Adotar o modo sem recorte da SDK (aguarda secco-platform#52)

Acrescentada durante a execução: a `Secco.SDK.Imaging` 0.1.0 só recorta o quadrado central. Até a entrega da #52, a imagem do Mural sai cortada; esta task fecha o recurso.

**Files:** `Directory.Packages.props` (versão da SDK que trouxer o modo), `src/Secco.Intranet.Infrastructure/Diretorio/ProcessadorDeImagemDaSdk.cs`, `tests/Secco.Intranet.Tests/Unit/ProcessadorDeImagemDaSdkTests.cs`, `docs/plataforma.md` (demanda para "Atendidas").

- [ ] **Step 1:** conferir no feed a versão publicada da SDK com o modo (issue fechada não é pacote publicado).
- [ ] **Step 2:** atualizar a versão e usar o modo sem recorte em `ProcessarImagemDePublicacaoAsync`.
- [ ] **Step 3:** tirar o `Skip` de `ImagemDePublicacao_Grande_Reduz2048` e `ImagemDePublicacao_Pequena_NaoAmplia`; rodar → verde.
- [ ] **Step 4:** conferência visual de uma arte retangular (retrato e paisagem) no Mural.
- [ ] **Step 5: Commit** `feat(mural): imagem inteira, sem recorte (secco-platform#52)`.
