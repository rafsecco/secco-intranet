# Foto do colaborador no Diretório — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O colaborador envia, recorta e remove a própria foto no Diretório, e o admin do diretório a de qualquer um; a foto substitui as iniciais onde houver avatar.

**Architecture:** O upload passa pelo `Secco.SDK.Imaging` (ADR-0042 da plataforma) atrás de uma porta da Application, que devolve uma única variante WebP já recortada e sem metadados. O resultado é cifrado pelo `IArquivoStore` de sempre, numa pasta lógica própria, e referenciado no `PerfilColaborador`. A entrega é uma rota autenticada com `ETag`, e o avatar vira um partial do contrato de tema nos dois temas.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core (SQL Server e PostgreSQL), `Secco.SDK.Imaging` 0.1.0, Cropper.js 1.6.2 (MIT, empacotado no tema), xUnit + AwesomeAssertions.

**Spec:** [`docs/specs/2026-09-24-diretorio-organizacional-design.md`](../specs/2026-09-24-diretorio-organizacional-design.md), seção "Foto (última etapa)". Desbloqueada por [secco-platform#29](https://github.com/rafsecco/secco-platform/issues/29).

## Global Constraints

- **Armazenamento:** a foto **não** é um Documento; ela vai pelo mesmo `IArquivoStore` (mesma cifragem e isolamento por tenant), com a pasta lógica `diretorio/fotos`.
- **Reenvio substitui:** grava o novo arquivo, troca a referência no perfil e só então remove o antigo. Se a remoção falhar, fica um arquivo órfão e um aviso no log, nunca uma referência quebrada.
- **Entrega:** `/diretorio/{id}/foto`, **só para autenticados**, com `Cache-Control: private`, `ETag` por versão da foto, `Content-Type` fixo e `X-Content-Type-Options: nosniff`.
- **Recorte e redução:** a tela de recorte usa uma biblioteca MIT embutida no projeto, **sem CDN**. O servidor recebe **só as coordenadas** e recorta e reduz pela SDK; o original nunca é guardado (README do `Secco.SDK.Imaging`).
- **Quem envia:** o colaborador (a própria foto) e o admin do diretório (a de qualquer um). Remover a foto volta às iniciais.
- **Tema:** o contrato de tema ganha o partial `_Avatar` (foto ou iniciais), presente **nos dois temas**.
- Commits direto na `main`; build com `-warnaserror` sem avisos; tabs em `.cs`; nomes de coluna pela convenção do projeto (notação húngara, skill `secco-db-naming`).

## Review Focus

- **Foto de outra pessoa pela rota de colaborador:** `POST /diretorio/perfil/foto` só altera a foto de quem está logado, nunca a de um id vindo do formulário. Teste na Task 5.
- **HEIC do iPhone ou arquivo que não é imagem:** volta à tela com "envie JPEG, PNG ou WebP", nunca 500. Teste na Task 3.
- **Recorte com coordenadas fora da imagem ou negativas** (navegador sem JavaScript manda vazio; formulário forjado manda lixo): vazio vira o quadrado central; inválido vira mensagem. Teste na Task 3.
- **Pessoa sem perfil local enviando a primeira foto:** o perfil nasce e a foto fica nele. Teste na Task 4.
- **Pedir a foto de quem não tem foto:** `404`, e a tela mostra as iniciais sem buscar a rota. Teste na Task 5.

---

### Task 1: Referência da foto no perfil, com migration nos dois engines

**Files:**
- Modify: `src/Secco.Intranet.Domain/Diretorio/PerfilColaborador.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Mappings/PerfilColaboradorConfiguration.cs`
- Create: migration `FotoDoColaborador` em `src/Secco.Intranet.Migrations.SqlServer` e `src/Secco.Intranet.Migrations.Postgres` (task VS Code `ef: nova migration (ambos os engines)`, ou `dotnet ef migrations add FotoDoColaborador --project <cada um> --output-dir Migrations`)
- Test: `tests/Secco.Intranet.Tests/Unit/PerfilColaboradorTests.cs` e `tests/Secco.Intranet.Tests/Integration/PerfilColaboradorPersistenciaTests.cs`

**Interfaces:**
- Produces, em `PerfilColaborador`:
  - `string? FotoCaminhoRelativo { get; private set; }` e `string? FotoChaveEmbrulhada { get; private set; }`, com `HasMaxLength(256)` nos dois, como em `DocumentoConfiguration`;
  - `bool TemFoto => FotoCaminhoRelativo is not null`;
  - `string? DefinirFoto(string caminhoRelativo, string chaveEmbrulhada)`, que devolve o caminho anterior (ou `null`) para o chamador remover;
  - `string? RemoverFoto()`, que limpa os dois campos e devolve o caminho anterior.

- [ ] **Step 1: Testes de domínio**
  - `DefinirFoto_PrimeiraVez_DevolveNull_ETemFoto`;
  - `DefinirFoto_Reenvio_DevolveOCaminhoAnterior`;
  - `RemoverFoto_SemFoto_DevolveNull_ENaoQuebra`;
  - `DefinirFoto_CaminhoOuChaveVazios_LancaArgumentException`.
- [ ] **Step 2: Teste de persistência** `Foto_GravaERelêOCaminhoEAChave`, no padrão de `PerfilColaboradorPersistenciaTests`, contra o banco de teste.
- [ ] **Step 3: Rodar e ver falhar**: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~PerfilColaborador"` → erro de compilação.
- [ ] **Step 4: Implementar** domínio e mapeamento; gerar a migration nos dois projetos e conferir no `.cs` gerado que as colunas saem com o nome da convenção.
- [ ] **Step 5: Rodar e ver passar** (mesmo filtro).
- [ ] **Step 6: Commit** `feat(diretorio): referencia da foto no perfil do colaborador`.

---

### Task 2: Pasta lógica no `IArquivoStore`

**Files:**
- Modify: `src/Secco.Intranet.Application/Documentos/IArquivoStore.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Armazenamento/SistemaArquivosArquivoStore.cs`
- Test: o teste existente do armazenamento em `tests/Secco.Intranet.Tests` (procure `SistemaArquivosArquivoStore`)

**Interfaces:**
- Produces:
  - `Task<ArquivoGravado> GravarAsync(Stream conteudo, string pastaLogica, CancellationToken cancellationToken = default)`, ao lado da sobrecarga atual, que segue igual;
  - `public static class PastasDeArquivo { public const string FotosDoDiretorio = "diretorio/fotos"; }`, no mesmo namespace da porta.
- O caminho passa a ser `{tenantId}/{pastaLogica}/{2 primeiros}/{identificador}`. A pasta é aceita só se estiver em `PastasDeArquivo`; fora dela, `ArgumentException`, para que um valor nunca venha do usuário.

- [ ] **Step 1: Testes**
  - `Gravar_ComPastaLogica_CaminhoTemTenantPastaEIdentificador`;
  - `Gravar_PastaForaDaLista_Lanca`;
  - `Gravar_ComPasta_LeDeVoltaOMesmoConteudo` (ida e volta com `EscreverEmAsync`);
  - `Remover_DaPastaLogica_ApagaOArquivo`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** a sobrecarga, reaproveitando a cifragem e a resolução de caminho que já existem; nenhuma lógica de cifra duplicada.
- [ ] **Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** `feat(armazenamento): pasta logica para arquivos que nao sao documento`.

---

### Task 3: Porta de processamento de imagem sobre o `Secco.SDK.Imaging`

**Files:**
- Modify: `Directory.Packages.props` (`Secco.SDK.Imaging` 0.1.0) e `src/Secco.Intranet.Infrastructure/Secco.Intranet.Infrastructure.csproj`
- Create: `src/Secco.Intranet.Application/Diretorio/IProcessadorDeImagem.cs`
- Create: `src/Secco.Intranet.Infrastructure/Diretorio/ProcessadorDeImagemDaSdk.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (`services.AddSeccoImaging("Intranet:Imaging")` e o registro da porta)
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs` (classe `Diretorio`)
- Test: `tests/Secco.Intranet.Tests/Unit/ProcessadorDeImagemDaSdkTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record RecorteDaFoto(int X, int Y, int Largura, int Altura);`
  - `public sealed record FotoProcessada(byte[] Conteudo, string ContentType);`
  - `public interface IProcessadorDeImagem { Task<Result<FotoProcessada>> ProcessarFotoAsync(Stream upload, RecorteDaFoto? recorte, CancellationToken cancellationToken = default); }`
  - Erros em `IntranetErrors.Diretorio`:
    - `FotoGrandeDemais`: "A foto passa de 10 MB.";
    - `FotoFormatoNaoSuportado`: "Envie a foto em JPEG, PNG ou WebP.";
    - `FotoInvalida`: "Não foi possível ler esta imagem.";
    - `FotoRecorteInvalido`: "O recorte saiu dos limites da foto. Escolha de novo.".
- A implementação pede **uma** variante, `ImageVariant("foto", 256)`, em `ImageOutputFormat.WebP`. Com `recorte` nulo, não passa `Crop` (vale o quadrado central da SDK). Com `recorte`, traduz para `CropRectangle`.
- Mapeia os erros da SDK para os quatro acima:
  - `InputTooLarge` → `FotoGrandeDemais`;
  - `UnsupportedFormat` → `FotoFormatoNaoSuportado`;
  - `InvalidImage` e `TooManyPixels` → `FotoInvalida`;
  - `InvalidCrop` → `FotoRecorteInvalido`.
- Recorte com largura ou altura ≤ 0, ou X/Y negativos, vira `FotoRecorteInvalido` antes de chamar a SDK. A SDK lançaria `ArgumentException` só para pedido mal montado, e coordenada vinda de formulário não pode virar 500.

- [ ] **Step 1: Testes** contra a SDK real, com imagens geradas em memória no próprio teste (um PNG de 800x600 criado com o encoder da SDK ou bytes fixos de um PNG pequeno):
  - `Processar_PngSemRecorte_DevolveWebp256OuMenor`;
  - `Processar_ComRecorteValido_DevolveQuadradoDoRecorte` (largura igual à altura, ≤ 256);
  - `Processar_RecorteForaDaImagem_FotoRecorteInvalido`;
  - `Processar_RecorteNegativo_FotoRecorteInvalido_SemExcecao`;
  - `Processar_TextoQualquer_FotoInvalida`;
  - `Processar_CabecalhoHeic_FotoFormatoNaoSuportado` (bytes com a marca `ftyp` `heic`).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** porta, adaptador, erros e registro.
- [ ] **Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** `feat(diretorio): processamento de foto pelo Secco.SDK.Imaging`.

---

### Task 4: Casos de uso da foto

**Files:**
- Create: `src/Secco.Intranet.Application/Diretorio/FotoDoColaboradorHandler.cs`
- Modify: `src/Secco.Intranet.Application/Diretorio/PessoaDto.cs` (último parâmetro `bool TemFoto`) e `MontadorDePessoas.cs` (preenche a partir de `perfil?.TemFoto ?? false`)
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs` e `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/FotoDoColaboradorHandlerTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces, em `FotoDoColaboradorHandler(IPerfilColaboradorRepository perfis, IProcessadorDeImagem imagens, IArquivoStore arquivos, ITrilhaDeAuditoria trilha, ILogger<FotoDoColaboradorHandler> logger)`:
  - `Task<Result> DefinirAsync(Guid usuarioId, Stream upload, RecorteDaFoto? recorte, CancellationToken)`;
  - `Task<Result> RemoverAsync(Guid usuarioId, CancellationToken)`;
  - `Task<Result<FotoGuardada>> ObterAsync(Guid usuarioId, CancellationToken)`, com `public sealed record FotoGuardada(string CaminhoRelativo, string ChaveEmbrulhada, string ETag, string ContentType)`;
    - o `ETag` é derivado do caminho (cada upload gera um caminho novo, então muda a cada versão), entre aspas, sem expor o caminho: hash SHA-256 truncado em 16 caracteres hexadecimais;
    - o `ContentType` é `image/webp`;
  - Verbos `DiretorioFotoDefinir = "diretorio.foto-definir"` e `DiretorioFotoRemover = "diretorio.foto-remover"`, com alvo `RecursosDeAuditoria.Diretorio` e o `usuarioId`.
- Regras:
  - **Definir** processa → grava em `PastasDeArquivo.FotosDoDiretorio` → `DefinirFoto` → salva → remove o anterior. Falha ao remover o anterior gera um `LogWarning` sem o caminho completo, e o resultado continua sucesso.
  - **Sem perfil local:** o perfil nasce (`new PerfilColaborador(usuarioId)` + `TentarAdicionarAsync`), como a edição de contato já faz.
  - **Falha no processamento:** não grava nada.
  - **Remover sem foto:** sucesso sem trilha.
  - **Obter sem perfil ou sem foto:** `IntranetErrors.Diretorio.FotoNaoEncontrada` (`Error.NotFound`).

- [ ] **Step 1: Testes com dublês** (porta de imagem falsa, `IArquivoStore` em memória que registra gravações e remoções, repositório falso; reaproveitar dublês existentes de `Support/` quando houver):
  - `Definir_PrimeiraFoto_GravaNaPastaDoDiretorio_ESalva`;
  - `Definir_Reenvio_RemoveOAnteriorDepoisDeSalvar`;
  - `Definir_FalhaAoRemoverOAnterior_SucessoMesmoAssim`;
  - `Definir_ImagemInvalida_NaoGravaNada`;
  - `Definir_SemPerfil_CriaOPerfil`;
  - `Remover_ApagaOArquivo_EVoltaAsIniciais`;
  - `Obter_SemFoto_NaoEncontrada`;
  - `Obter_ETagMudaQuandoAFotoMuda`;
  - cada escrita grava o verbo esperado na trilha.
- [ ] **Step 2: Rodar e ver falhar. Step 3: Implementar. Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** `feat(diretorio): definir, remover e obter a foto do colaborador`.

---

### Task 5: Rotas de envio, remoção e entrega

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/DiretorioController.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/DiretorioFotoTests.cs`

**Interfaces:**
- Consumes: `FotoDoColaboradorHandler`, `IArquivoStore.EscreverEmAsync`, `AcessoAoDiretorio.UsuarioId(User)` (já usado no controller).
- Produces, com o `[ExigeNivelNoDiretorio(Usuario)]` que já vale na classe:

| Verbo | Rota | Quem | Parâmetros |
|---|---|---|---|
| POST | `/diretorio/perfil/foto` | o próprio, pelo `sub` | `IFormFile? foto`, `int? x, y, largura, altura` |
| POST | `/diretorio/perfil/foto/remover` | o próprio | — |
| POST | `/diretorio/{id}/foto` | `[ExigeNivelNoDiretorio(Administrador)]` | os mesmos do envio |
| POST | `/diretorio/{id}/foto/remover` | `[ExigeNivelNoDiretorio(Administrador)]` | — |
| GET | `/diretorio/{id}/foto` | qualquer autenticado com acesso ao diretório | `If-None-Match` |

- Coordenadas incompletas (qualquer uma nula) → `recorte = null`, quadrado central. Sem arquivo → feedback "Escolha uma foto.".
- Os POSTs usam `[ValidateAntiForgeryToken]`, `[RequestSizeLimit(10 * 1024 * 1024 + 64 * 1024)]` e redirecionam de volta (`/diretorio/perfil` ou `/diretorio/{id}/editar`) com o feedback de sucesso ou a mensagem do erro em `TempData`, como as outras ações do controller.
- O GET responde:
  - `304` quando `If-None-Match` bate com o `ETag`;
  - `404` sem foto;
  - senão, `200` com `Content-Type: image/webp`, `ETag`, `Cache-Control: private, no-cache` e `X-Content-Type-Options: nosniff`, escrevendo o corpo com `EscreverEmAsync` direto na resposta.

- [ ] **Step 1: Testes de integração** (fábrica existente, `IProcessadorDeImagem` trocado por dublê determinístico via `WithWebHostBuilder`/`ConfigureTestServices`):
  - `EnviarPropria_GravaParaQuemEstaLogado_IgnorandoIdNoFormulario`;
  - `EnviarDeOutro_SemNivelAdministrador_403`;
  - `EnviarDeOutro_Administrador_Grava`;
  - `Entregar_ComFoto_200ComCabecalhos` (confere os cinco cabeçalhos);
  - `Entregar_IfNoneMatchIgual_304`;
  - `Entregar_SemFoto_404`;
  - `Entregar_SemAcessoAoDiretorio_403`;
  - `Enviar_SemToken_400`;
  - `Enviar_ImagemRecusada_VoltaComAMensagem`;
  - `Remover_VoltaAsIniciais`.
- [ ] **Step 2: Rodar e ver falhar. Step 3: Implementar. Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** `feat(diretorio): rotas da foto do colaborador`.

---

### Task 6: Partial `_Avatar` no contrato de tema, nos dois temas

**Files:**
- Create: `src/Secco.Intranet.Web.Theming/Contracts/AvatarModel.cs`
- Create: `src/Secco.Intranet.Themes.Vertical/Themes/Vertical/Views/Shared/_Avatar.cshtml` e o equivalente em `Themes.Horizontal`
- Modify: CSS dos dois temas (`wwwroot/scss/...`, onde está `.sc-avatar`): `img.sc-avatar` com `object-fit: cover` e o mesmo raio do avatar de iniciais
- Modify: `src/Secco.Intranet.Web/Views/Diretorio/{Index,Pessoa,Organograma,_NoOrganograma}.cshtml`, trocando os seis `<span class="sc-avatar…">@Iniciais.De(…)</span>` pelo partial
- Modify: `docs/temas.md` (linha do `_Avatar` na tabela de partials)
- Test: `tests/Secco.Intranet.Tests/Integration/DiretorioFotoTests.cs` (telas)

**Interfaces:**
- Produces:
  - `public enum AvatarTamanho { Padrao, Grande }`;
  - `public sealed record AvatarModel(string Iniciais, string? FotoUrl, AvatarTamanho Tamanho = AvatarTamanho.Padrao);`.
- Com `FotoUrl` nulo, o partial renderiza o `<span>` de iniciais de hoje, com `sc-avatar--lg` no Grande.
- Com `FotoUrl`, renderiza `<img class="sc-avatar …" src="…" alt="" loading="lazy">`, mantendo o `aria-hidden` atual.
- As views montam o modelo com `new AvatarModel(Iniciais.De(p.Nome), p.TemFoto ? $"/diretorio/{p.UsuarioId}/foto" : null, …)`.

- [ ] **Step 1: Testes de tela:**
  - `Diretorio_PessoaComFoto_MostraImgDaRota`;
  - `Diretorio_PessoaSemFoto_MostraIniciais_SemBuscarARota`;
  - um teste que renderiza `/diretorio` nos dois temas (padrão dos testes de tema existentes; procure `Intranet:Theme:Nome`) e confere o `img`.
- [ ] **Step 2: Rodar e ver falhar. Step 3: Implementar. Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** `feat(tema): partial _Avatar com foto ou iniciais nos dois temas`.

---

### Task 7: Tela de envio com recorte

**Files:**
- Modify: `src/Secco.Intranet.Themes.Vertical/package.json` (devDependency `cropperjs` **1.6.2**) e `scripts/copy-assets.mjs` (copiar `cropper.min.js` e `cropper.min.css` para `wwwroot/vendor/js` e `wwwroot/vendor/css`, como o Toast UI); rodar `npm install` e `npm run build` no tema e commitar os arquivos gerados em `wwwroot/vendor`
- Create: `src/Secco.Intranet.Web/Views/Diretorio/_EnviarFoto.cshtml` (formulário reaproveitado em `Perfil.cshtml` e `Editar.cshtml`) e o script do recorte (pasta onde ficam os scripts próprios da Web)
- Modify: `src/Secco.Intranet.Web/Views/Diretorio/Perfil.cshtml` e `Editar.cshtml`
- Test: `tests/Secco.Intranet.Tests/Integration/DiretorioFotoTests.cs`

**Interfaces:**
- Consumes: as rotas da Task 5. O partial recebe a `action` do envio e da remoção e se há foto.
- O formulário é `multipart/form-data`, com `@Html.AntiForgeryToken()`, `<input type="file" name="foto" accept="image/jpeg,image/png,image/webp">` e os hidden `x`, `y`, `largura`, `altura`.
- O script abre o Cropper com `aspectRatio: 1`, `viewMode: 1`, `checkOrientation: true` e, no submit, preenche os hidden com `cropper.getData(true)`. Esses valores já estão em coordenadas da imagem orientada, como a SDK espera.
- **Sem JavaScript:** os hidden ficam vazios e vale o quadrado central.
- Texto de ajuda: "JPEG, PNG ou WebP, até 10 MB. A foto é reduzida e guardada sem os dados do arquivo original (como a localização)."
- Botão "Remover foto" só quando há foto, com `data-confirmar`.

- [ ] **Step 1: Testes de tela:**
  - `Perfil_MostraOFormularioDeFoto_ComOsCamposDeRecorte`;
  - `Editar_AdminVeOFormularioDeFotoDaPessoa`;
  - `Perfil_SemFoto_NaoOfereceRemover`.
- [ ] **Step 2: Rodar e ver falhar. Step 3: Implementar. Step 4: Rodar e ver passar.**
- [ ] **Step 5: Conferir no navegador:** com a aplicação em Development e o tenant de DEV, envie uma foto JPEG de celular (com EXIF de rotação), recorte e confira que sai de pé e quadrada, nos temas Vertical e Horizontal, claro e escuro.
- [ ] **Step 6: Commit** `feat(diretorio): envio da foto com recorte`.

---

### Task 8: Documentação

**Files:**
- Modify: `docs/roadmap.md` (o item "Foto do colaborador no Diretório" vira `[x]`, com o link deste plano)
- Modify: `docs/specs/2026-09-24-diretorio-organizacional-design.md` (a seção da foto passa a dizer que foi implementada, com a SDK, a variante única de 256 px em WebP e a pasta lógica)
- Modify: `docs/guia-de-deploy.md` (seção opcional `Intranet__Imaging__*`, com `MaxInputBytes`, `MaxPixels` e `MaxConcurrentOperations` e os padrões da SDK; aviso de que a imagem precisa ser glibc (a base `aspnet:10.0` já é) e nunca Alpine)

- [ ] **Step 1: Editar os três documentos.**
- [ ] **Step 2: Rodar a suíte inteira com os testes de tela:** `SECCO_UI_TESTS=1 dotnet test tests/Secco.Intranet.Tests` → tudo verde.
- [ ] **Step 3: Commit** `docs(diretorio): foto do colaborador entregue`.
