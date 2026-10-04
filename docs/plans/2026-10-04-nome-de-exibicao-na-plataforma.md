# Nome de exibição vindo da plataforma — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O nome de exibição do Diretório passa a ser o `displayName` do SecureGate — lido na listagem de usuários e gravado pela API administrativa —, e a coluna local `NomeExibicao` sai.

**Architecture:** A porta `IGestaoDeAcesso` ganha a escrita do nome e devolve o nome na listagem; a porta `IUsuariosParaDiretorio` passa a entregar o nome junto com o e-mail e ganha `Esquecer()` para invalidar o cache. `EditarContatoHandler` e `ImportarDiretorioHandler` gravam o nome no SecureGate **só quando ele muda**, antes de tocar o perfil local. `PerfilColaborador` perde o nome; migration nos dois engines.

**Tech Stack:** .NET 10, `Secco.SecureGate.Client` 0.14.0 (NSwag), EF Core (SQL Server + Postgres), xUnit + AwesomeAssertions.

**Spec:** [docs/specs/2026-10-04-nome-de-exibicao-na-plataforma-design.md](../specs/2026-10-04-nome-de-exibicao-na-plataforma-design.md)

## Global Constraints

- `Secco.SecureGate.Client` na versão estável mais recente: **0.14.0** (conferida no feed em 2026-10-04). A API usada: `SetUserDisplayNameAsync(Guid tenantId, Guid userId, SetDisplayNameRequest body, CancellationToken)`, `SetDisplayNameRequest.DisplayName`, `UserDto.DisplayName`.
- Limite do nome: **160** caracteres (o da plataforma).
- O nome só vai ao SecureGate quando **muda** (comparado, aparado, com o `displayName` atual).
- Falha ao gravar o nome: o perfil local **não** é gravado e o erro volta para a tela.
- Indentação: tabs em `.cs`; arquivos com BOM continuam com BOM.
- Commits direto na `main`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Sem push.
- Testes de integração precisam do SQL de DEV (`docker compose up -d`). Comando: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~<Classe>"`.

## Review Focus

1. **Formulário pré-preenchido com o e-mail** — pessoa com perfil local mas sem `displayName` abre "Meu perfil", muda só o ramal e salva: o nome **não** pode ir ao SecureGate como o e-mail dela. Hoje o form é preenchido com `PessoaDto.Nome`, que cai no e-mail. Teste na Task 2.
2. **Nome só com espaços / vazio** em quem já tem nome: limpa o `displayName` (volta a mostrar o e-mail), e isso é uma mudança — chama a plataforma. Teste na Task 2.
3. **CSV com célula de nome vazia**: "não alterar" (regra do CSV), nunca limpa o nome. Teste na Task 2.
4. **SecureGate fora do ar ao editar só ramal/"sobre"**: salva normalmente (não depende da plataforma). Teste na Task 2.
5. **Nome com 160 caracteres exatos** é aceito; 161 é recusado antes de chamar a plataforma. Teste na Task 2.

---

### Task 1: Client 0.14.0 e escrita do nome na gestão de acesso

**Files:**
- Modify: `Directory.Packages.props` (versão do `Secco.SecureGate.Client`)
- Modify: `src/Secco.Intranet.Application/Acesso/AcessoDtos.cs` (`UsuarioDto`)
- Modify: `src/Secco.Intranet.Application/Acesso/IGestaoDeAcesso.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Access/SecureGateGestaoDeAcesso.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Access/GestaoDeAcessoIndisponivel.cs`
- Modify: `tests/Secco.Intranet.Tests/Support/DublesDeAcesso.cs` (`GestaoDeAcessoFalsa`)
- Modify: `tests/Secco.Intranet.Tests/Unit/UsuariosParaDiretorioTests.cs` (`ContadorDeListagens`)
- Test: `tests/Secco.Intranet.Tests/Unit/SecureGateGestaoDeAcessoTests.cs`, `tests/Secco.Intranet.Tests/Smoke/SecureGateGestaoDeAcessoFumacaTests.cs`

**Interfaces:**
- Produces: `UsuarioDto(Guid Id, string Email, SituacaoDoUsuario Situacao, IReadOnlyList<string> Perfis, string? Nome = null)`; `IGestaoDeAcesso.DefinirNomeDeExibicaoAsync(Guid usuarioId, string? nome, CancellationToken cancellationToken = default) : Task<Result>` — nulo/vazio limpa; o nome vai aparado. Dublê: `GestaoDeAcessoFalsa.Nomes : Dictionary<Guid, string?>`, chamada registrada como `usuario-nome:{id}:{nome}`.

- [ ] **Step 1: Subir o pacote**

Em `Directory.Packages.props`: `<PackageVersion Include="Secco.SecureGate.Client" Version="0.14.0" />`.

Run: `dotnet build` — Expected: compila (0.12–0.14 só acrescentam). Se algum fake que implementa `ISecureGateClient` à mão quebrar, é por método novo da interface: implemente delegando/lançando `NotSupportedException`, como os vizinhos.

- [ ] **Step 2: Testes do adaptador (falham)**

Em `SecureGateGestaoDeAcessoTests.ClientFalso`, acrescente:

```csharp
		public SetDisplayNameRequest? NomeEnviado { get; private set; }

		public override async Task SetUserDisplayNameAsync(
			Guid tenantId, Guid userId, SetDisplayNameRequest body, CancellationToken cancellationToken)
		{
			NomeEnviado = body;
			await Registrar($"SetUserDisplayName:{tenantId}:{userId}");
		}
```

E os testes:

```csharp
	[Fact]
	public async Task ListarUsuarios_TrazONomeDeExibicao()
	{
		var id = Guid.NewGuid();
		var client = new ClientFalso { Usuarios = [new UserDto { Id = id, Email = "ana@x.com", Status = "active", DisplayName = "Ana Ribeiro" }] };

		var lista = await Criar(client).ListarUsuariosAsync();

		lista.Value.Single().Nome.Should().Be("Ana Ribeiro");
	}

	[Fact]
	public async Task DefinirNome_EnviaAparadoParaOTenantEUsuarioCertos()
	{
		var client = new ClientFalso();
		var usuario = Guid.NewGuid();

		var resultado = await Criar(client).DefinirNomeDeExibicaoAsync(usuario, "  Ana Ribeiro ");

		resultado.IsSuccess.Should().BeTrue();
		client.Chamadas.Should().Equal($"SetUserDisplayName:{Tenant}:{usuario}");
		client.NomeEnviado!.DisplayName.Should().Be("Ana Ribeiro");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("   ")]
	public async Task DefinirNome_VazioLimpa(string? nome)
	{
		var client = new ClientFalso();

		(await Criar(client).DefinirNomeDeExibicaoAsync(Guid.NewGuid(), nome)).IsSuccess.Should().BeTrue();

		client.NomeEnviado!.DisplayName.Should().BeNull("nulo é o que a plataforma entende como limpar");
	}

	[Fact]
	public async Task DefinirNome_404_ViraUsuarioNaoEncontrado()
	{
		var client = new ClientFalso { Falha = new ApiException("not found", 404, null, new Dictionary<string, IEnumerable<string>>(), null) };

		var resultado = await Criar(client).DefinirNomeDeExibicaoAsync(Guid.NewGuid(), "Ana");

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}
```

> `Criar(client)` é o helper que o arquivo já usa para montar o adaptador com `TenantContextFalso(Tenant)`; se tiver outro nome, use o existente. O construtor de `ApiException` é o mesmo dos testes de 404 que já existem no arquivo — copie de lá.

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~SecureGateGestaoDeAcessoTests"` — Expected: não compila (`Nome`, `DefinirNomeDeExibicaoAsync`).

- [ ] **Step 3: Implementar**

`UsuarioDto` (AcessoDtos.cs), com o `<param>` novo:

```csharp
/// <param name="Nome">Nome de exibição (o <c>displayName</c> do SecureGate); nulo quando não definido.</param>
public sealed record UsuarioDto(Guid Id, string Email, SituacaoDoUsuario Situacao, IReadOnlyList<string> Perfis, string? Nome = null);
```

`IGestaoDeAcesso`, depois de `EncerrarSessoesAsync`:

```csharp
	/// <summary>Define o nome de exibição do usuário no SecureGate; nulo ou vazio limpa.</summary>
	/// <param name="usuarioId">Usuário.</param>
	/// <param name="nome">Nome novo (é aparado).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> DefinirNomeDeExibicaoAsync(Guid usuarioId, string? nome, CancellationToken cancellationToken = default);
```

`SecureGateGestaoDeAcesso` — no `ListarUsuariosAsync`, o `new UsuarioDto(...)` ganha `u.DisplayName` como último argumento; e o método novo:

```csharp
	/// <inheritdoc />
	public Task<Result> DefinirNomeDeExibicaoAsync(Guid usuarioId, string? nome, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"definir nome de exibicao",
			(tenantId, token) => client.SetUserDisplayNameAsync(
				tenantId,
				usuarioId,
				new SetDisplayNameRequest { DisplayName = string.IsNullOrWhiteSpace(nome) ? null : nome.Trim() },
				token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			null,
			cancellationToken);
```

`GestaoDeAcessoIndisponivel`:

```csharp
	/// <inheritdoc />
	public Task<Result> DefinirNomeDeExibicaoAsync(Guid usuarioId, string? nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));
```

`GestaoDeAcessoFalsa` (DublesDeAcesso.cs): propriedade `public Dictionary<Guid, string?> Nomes { get; } = [];`; no `ListarUsuariosAsync` do dublê, passe `Nomes.GetValueOrDefault(u.Id)` como `Nome` do `UsuarioDto`; e

```csharp
	/// <inheritdoc />
	public Task<Result> DefinirNomeDeExibicaoAsync(Guid usuarioId, string? nome, CancellationToken cancellationToken = default) =>
		Escrever($"usuario-nome:{usuarioId}:{nome}", () => Nomes[usuarioId] = string.IsNullOrWhiteSpace(nome) ? null : nome.Trim());
```

`ContadorDeListagens` (UsuariosParaDiretorioTests.cs): delega como os demais —
`public Task<Secco.SharedKernel.Results.Result> DefinirNomeDeExibicaoAsync(Guid usuarioId, string? nome, CancellationToken cancellationToken = default) => _interno.DefinirNomeDeExibicaoAsync(usuarioId, nome, cancellationToken);`

- [ ] **Step 4: Fumaça contra o SecureGate real**

Em `SecureGateGestaoDeAcessoFumacaTests`:

```csharp
	[FumacaFact]
	public async Task Usuario_NomeDeExibicao_GravaLeELimpa()
	{
		var usuario = await f.CriarUsuarioAsync("nome");

		(await f.Gestao.DefinirNomeDeExibicaoAsync(usuario, "Fumaça da Silva")).IsSuccess.Should().BeTrue();
		(await f.Gestao.ListarUsuariosAsync()).Value.Single(u => u.Id == usuario).Nome.Should().Be("Fumaça da Silva");

		(await f.Gestao.DefinirNomeDeExibicaoAsync(usuario, null)).IsSuccess.Should().BeTrue();
		(await f.Gestao.ListarUsuariosAsync()).Value.Single(u => u.Id == usuario).Nome.Should().BeNull();
	}
```

- [ ] **Step 5: Rodar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~SecureGateGestaoDeAcesso|FullyQualifiedName~UsuariosParaDiretorioTests|FullyQualifiedName~Acesso"`
Expected: PASS (a fumaça fica ignorada sem `SECCO_FUMACA_*`).

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props src tests
git commit -m "feat(acesso): SecureGate.Client 0.14.0; nome de exibicao na listagem e na escrita

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Diretório lê e grava o nome na plataforma; coluna local sai

**Files:**
- Modify: `src/Secco.Intranet.Application/Diretorio/IUsuariosParaDiretorio.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Diretorio/UsuariosParaDiretorioDoSecureGate.cs`, `UsuariosParaDiretorioDeDesenvolvimento.cs`, `UsuariosParaDiretorioIndisponivel.cs`
- Modify: `src/Secco.Intranet.Application/Diretorio/MontadorDePessoas.cs`, `PessoaDto.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/NomeDeExibicao.cs`
- Modify: `src/Secco.Intranet.Application/Diretorio/EditarContatoHandler.cs`, `ImportarDiretorioHandler.cs`
- Modify: `src/Secco.Intranet.Domain/Diretorio/PerfilColaborador.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Mappings/PerfilColaboradorConfiguration.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Seeding/DiretorioDesenvolvimentoSeeder.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/DiretorioController.cs` (pré-preenchimento), `src/Secco.Intranet.Web/Models/Diretorio/FormulariosDoDiretorio.cs`
- Create: migrations `RemoverNomeExibicaoDoPerfil` em `src/Secco.Intranet.Migrations.SqlServer` e `src/Secco.Intranet.Migrations.Postgres`
- Modify (tests): `Support/DublesDeDiretorio.cs`, `Unit/UsuariosParaDiretorioTests.cs`, `Unit/LeituraDoDiretorioHandlersTests.cs`, `Unit/EscritaDoDiretorioHandlersTests.cs`, `Unit/ImportarDiretorioHandlerTests.cs`, `Unit/PerfilColaboradorTests.cs`, `Integration/DiretorioEdicaoTests.cs`, `Integration/PerfilColaboradorPersistenciaTests.cs`, `Integration/DiretorioDesenvolvimentoSeederTests.cs`, e qualquer outro que o build apontar
- Modify (docs): `docs/roadmap.md`, `docs/plataforma.md`, `docs/specs/2026-09-24-diretorio-organizacional-design.md`

**Interfaces:**
- Consumes (Task 1): `UsuarioDto.Nome`, `IGestaoDeAcesso.DefinirNomeDeExibicaoAsync`, `GestaoDeAcessoFalsa.Nomes`/`Chamadas`.
- Produces: `UsuarioParaDiretorio(Guid Id, string Email, string? Nome = null)`; `IUsuariosParaDiretorio.Esquecer() : void`; `PessoaDto` ganha `string? NomeDeExibicao` (último parâmetro); `NomeDeExibicao.MaxLength = 160`; `EditarContatoHandler(IUsuariosParaDiretorio, IPerfilColaboradorRepository, IGestaoDeAcesso, ITrilhaDeAuditoria)`; `ImportarDiretorioHandler(IUsuariosParaDiretorio, IPerfilColaboradorRepository, ISetorRepository, IGestaoDeAcesso, ITrilhaDeAuditoria)`; `PerfilColaborador.EditarContato(string? ramal, string? sobre)`.

- [ ] **Step 1: Dublês**

`UsuariosParaDiretorioFalso`: `Com(Guid id, string email, string? nome = null)` grava `new UsuarioParaDiretorio(id, email, nome)`; propriedade `public int Esquecimentos { get; private set; }` e `public void Esquecer() => Esquecimentos++;`.

- [ ] **Step 2: Testes de leitura (falham)**

Em `UsuariosParaDiretorioTests`:

```csharp
	[Fact]
	public async Task SecureGate_TrazONome()
	{
		var id = Guid.NewGuid();
		var gestao = new ContadorDeListagens().ComUsuario(id, "ana@x.com");
		// ComUsuario do contador delega ao GestaoDeAcessoFalsa interno; o nome entra pelo dublê.
		await gestao.DefinirNomeDeExibicaoAsync(id, "Ana Ribeiro");

		var lidos = await Criar(gestao).ListarAtivosAsync();

		lidos.Value.Single().Nome.Should().Be("Ana Ribeiro");
	}

	[Fact]
	public async Task Esquecer_FazAProximaListagemIrAPlataforma()
	{
		var gestao = new ContadorDeListagens().ComUsuario(Guid.NewGuid(), "ana@x.com");
		var adaptador = Criar(gestao);
		await adaptador.ListarAtivosAsync();

		adaptador.Esquecer();
		await adaptador.ListarAtivosAsync();

		gestao.Listagens.Should().Be(2, "sem esquecer, a segunda viria do cache");
	}

	[Fact]
	public async Task Desenvolvimento_TrazOsNomesFicticios()
	{
		var lidos = await new UsuariosParaDiretorioDeDesenvolvimento().ListarAtivosAsync();

		lidos.Value.Single(u => u.Id == PessoasDeDesenvolvimento.AnaId).Nome.Should().Be("Ana Ribeiro");
	}
```

> `Criar(gestao)` = o helper do arquivo que monta `UsuariosParaDiretorioDoSecureGate` com cache e tenant (use o existente).

Em `LeituraDoDiretorioHandlersTests`: o helper `Perfil(usuario, nome, ...)` perde `nome`. **Regra de reescrita para cada teste do arquivo:** onde o nome era dado ao perfil (`Perfil(Ana, "Ana Ribeiro")`), ele passa para o usuário (`.Com(Ana, "ana@x.com", "Ana Ribeiro")`) e o perfil só leva cargo/setor/gestor. Acrescente:

```csharp
	[Fact]
	public async Task NomeDeExibicao_ENuloSemDisplayName_MesmoComPerfil()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com");
		var perfis = new PerfisColaboradorFalso().Com(Perfil(Ana, cargo: "Analista"));
		var (_, obter, _) = Montar(usuarios, perfis);

		var pessoa = (await obter.HandleAsync(Ana)).Value.Pessoa;

		pessoa.Nome.Should().Be("ana@x.com", "a tela cai no e-mail");
		pessoa.NomeDeExibicao.Should().BeNull("o formulário não pode ser preenchido com o e-mail");
	}
```

- [ ] **Step 3: Testes de escrita (falham)**

Em `EscritaDoDiretorioHandlersTests`, o handler de contato passa a receber a gestão. Monte com `new EditarContatoHandler(usuarios, perfis, gestao, trilha)` onde `gestao = new GestaoDeAcessoFalsa()`. Reescreva os testes que verificavam `NomeExibicao` local para verificar `gestao.Chamadas`/`gestao.Nomes`, e acrescente:

```csharp
	[Fact]
	public async Task Contato_NomeIgualAoAtual_NaoChamaAPlataforma()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com", "Ana Ribeiro");
		var gestao = new GestaoDeAcessoFalsa();
		var handler = new EditarContatoHandler(usuarios, new PerfisColaboradorFalso(), gestao, new TrilhaDeAcessoFalsa());

		(await handler.HandleAsync(new EditarContatoCommand(Ana, " Ana Ribeiro ", "2100", null))).IsSuccess.Should().BeTrue();

		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Contato_NomeMudou_GravaNaPlataformaEEsqueceOCache()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com");
		var gestao = new GestaoDeAcessoFalsa();
		var trilha = new TrilhaDeAcessoFalsa();
		var handler = new EditarContatoHandler(usuarios, new PerfisColaboradorFalso(), gestao, trilha);

		(await handler.HandleAsync(new EditarContatoCommand(Ana, "Ana Ribeiro", null, null))).IsSuccess.Should().BeTrue();

		gestao.Chamadas.Should().Equal($"usuario-nome:{Ana}:Ana Ribeiro");
		usuarios.Esquecimentos.Should().Be(1);
		trilha.Registros.Should().ContainSingle().Which.Metadata.Should().Contain("\"nome\"");
	}

	[Fact]
	public async Task Contato_NomeVazioEmQuemTinhaNome_LimpaNaPlataforma()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com", "Ana Ribeiro");
		var gestao = new GestaoDeAcessoFalsa();
		var handler = new EditarContatoHandler(usuarios, new PerfisColaboradorFalso(), gestao, new TrilhaDeAcessoFalsa());

		(await handler.HandleAsync(new EditarContatoCommand(Ana, "   ", null, null))).IsSuccess.Should().BeTrue();

		gestao.Chamadas.Should().Equal($"usuario-nome:{Ana}:   ");
		gestao.Nomes[Ana].Should().BeNull();
	}

	[Fact]
	public async Task Contato_PlataformaRecusaONome_NaoGravaOPerfilLocal()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com");
		var perfis = new PerfisColaboradorFalso();
		var gestao = new GestaoDeAcessoFalsa { FalharCom = IntranetErrors.Acesso.Indisponivel };
		var handler = new EditarContatoHandler(usuarios, perfis, gestao, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(new EditarContatoCommand(Ana, "Ana", "2100", "sobre"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		perfis.Perfis.Should().BeEmpty("o ramal não é gravado sozinho quando o nome falhou");
	}

	[Fact]
	public async Task Contato_SoRamal_FuncionaComAPlataformaForaDoAr()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com", "Ana");
		var perfis = new PerfisColaboradorFalso();
		var gestao = new GestaoDeAcessoFalsa { FalharCom = IntranetErrors.Acesso.Indisponivel };
		var handler = new EditarContatoHandler(usuarios, perfis, gestao, new TrilhaDeAcessoFalsa());

		(await handler.HandleAsync(new EditarContatoCommand(Ana, "Ana", "2100", null))).IsSuccess.Should().BeTrue();

		perfis.Perfis.Single().Ramal.Should().Be("2100");
	}

	[Theory]
	[InlineData(160, true)]
	[InlineData(161, false)]
	public async Task Contato_LimiteDoNome(int tamanho, bool aceito)
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com");
		var gestao = new GestaoDeAcessoFalsa();
		var handler = new EditarContatoHandler(usuarios, new PerfisColaboradorFalso(), gestao, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(new EditarContatoCommand(Ana, new string('n', tamanho), null, null));

		resultado.IsSuccess.Should().Be(aceito);
		gestao.Chamadas.Should().HaveCount(aceito ? 1 : 0);
	}
```

> O teste existente parametrizado com `PerfilColaborador.NomeMaxLength + 1` (linha ~88) sai: o caso 161 está acima. Os demais que davam nome ao perfil seguem a mesma regra da leitura (nome vai para o usuário).

Em `ImportarDiretorioHandlerTests` (o handler passa a receber `gestao`):

```csharp
	[Fact]
	public async Task Importar_NomeQueMuda_VaiParaAPlataforma_ENomeIgualNao()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(AnaId, "ana@x.com", "Ana").Com(BrunoId, "bruno@x.com", "Bruno");
		var gestao = new GestaoDeAcessoFalsa();
		var handler = Criar(usuarios, gestao);

		var relatorio = await handler.AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana Ribeiro;;;;\nbruno@x.com;Bruno;;;;\n"));

		gestao.Chamadas.Should().Equal($"usuario-nome:{AnaId}:Ana Ribeiro");
		relatorio.Value.Atualizados.Should().Be(1);
		relatorio.Value.SemAlteracao.Should().Be(1);
		usuarios.Esquecimentos.Should().Be(1, "uma vez no fim do lote");
	}

	[Fact]
	public async Task Importar_NomeVazioNoCsv_NaoMexeNoNome()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(AnaId, "ana@x.com", "Ana");
		var gestao = new GestaoDeAcessoFalsa();

		await Criar(usuarios, gestao).AplicarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;;Analista;;;\n"));

		gestao.Chamadas.Should().BeEmpty("célula vazia no CSV é 'não alterar'");
	}

	[Fact]
	public async Task Importar_PlataformaRecusaUmNome_LinhaViraErroEOLoteContinua()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(AnaId, "ana@x.com").Com(BrunoId, "bruno@x.com");
		var gestao = new GestaoDeAcessoFalsa { FalharCom = IntranetErrors.Acesso.Indisponivel };
		var perfis = new PerfisColaboradorFalso();

		var relatorio = await Criar(usuarios, gestao, perfis).AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana;;;;\nbruno@x.com;;Analista;;;\n"));

		relatorio.Value.Linhas.Single(l => l.Email == "ana@x.com").Status.Should().Be(StatusDaLinha.Erro);
		relatorio.Value.Linhas.Single(l => l.Email == "bruno@x.com").Status.Should().Be(StatusDaLinha.Criar);
		perfis.Perfis.Should().ContainSingle(p => p.UsuarioId == BrunoId && p.Cargo == "Analista");
	}
```

> `AnaId`, `BrunoId`, `Cabecalho` e o helper de criação já existem no arquivo com outros nomes, talvez; adapte o `Criar(usuarios, gestao, perfis = null)` para montar o handler com setores falsos e trilha como os testes vizinhos fazem. A ordem das colunas do `Cabecalho` é a do arquivo (email;nome;cargo;ramal;setor;gestor) — confira no topo do arquivo.

Em `PerfilColaboradorTests`: os testes de `EditarContato` passam a `EditarContato(ramal, sobre)`; os que só testavam o nome saem (o nome não é mais do domínio).

Run: `dotnet build tests/Secco.Intranet.Tests` — Expected: erros de compilação nos tipos novos (`Nome`, `Esquecer`, `NomeDeExibicao`, construtores).

- [ ] **Step 4: Porta e adaptadores**

`IUsuariosParaDiretorio.cs`:

```csharp
/// <summary>Usuário ativo do tenant, como o diretório precisa dele — só identidade.</summary>
/// <param name="Id">Id no SecureGate.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição (o <c>displayName</c> do SecureGate); nulo quando não definido.</param>
public sealed record UsuarioParaDiretorio(Guid Id, string Email, string? Nome = null);
```

e na interface:

```csharp
	/// <summary>
	/// Descarta a lista guardada em cache, para a próxima leitura ir à fonte — chamado depois de
	/// gravar um nome, para a pessoa ver a mudança na hora. Adaptadores sem cache não fazem nada.
	/// </summary>
	void Esquecer();
```

`UsuariosParaDiretorioDoSecureGate`: o `Select` vira `new UsuarioParaDiretorio(usuario.Id, usuario.Email ?? string.Empty, usuario.Nome)`; extraia a chave para `private string Chave => $"diretorio:usuarios:{tenantContext.TenantId}";` e

```csharp
	/// <inheritdoc />
	public void Esquecer()
	{
		if (tenantContext.IsResolved)
		{
			cache.Remove(Chave);
		}
	}
```

`UsuariosParaDiretorioDeDesenvolvimento`: `new UsuarioParaDiretorio(pessoa.Id, pessoa.Email, pessoa.Nome)` e `public void Esquecer() { }` (sem cache). `UsuariosParaDiretorioIndisponivel`: `public void Esquecer() { }`.

- [ ] **Step 5: Leitura**

`NomeDeExibicao.cs`:

```csharp
namespace Secco.Intranet.Application.Diretorio;

/// <summary>Regras do nome de exibição, que mora no SecureGate (secco-platform#30).</summary>
public static class NomeDeExibicao
{
	/// <summary>Tamanho máximo — o mesmo que a plataforma aceita.</summary>
	public const int MaxLength = 160;

	/// <summary>Forma comparável: aparado, e vazio vira nulo (é o que a plataforma grava).</summary>
	public static string? Normalizar(string? nome) => string.IsNullOrWhiteSpace(nome) ? null : nome.Trim();
}
```

`PessoaDto`: acrescente por último `string? NomeDeExibicao` (com `<param>`: "O `displayName` cru, nulo quando não definido — para pré-preencher formulário; para mostrar, use <see cref="Nome"/>").

`MontadorDePessoas.NomeDe`:

```csharp
		static string NomeDe(UsuarioParaDiretorio usuario) =>
			!string.IsNullOrWhiteSpace(usuario.Nome)
				? usuario.Nome
				: string.IsNullOrWhiteSpace(usuario.Email) ? usuario.Id.ToString() : usuario.Email;
```

(o dicionário `perfilPorUsuario` continua para os outros campos), e o `new PessoaDto(...)` ganha `NomeDeExibicao.Normalizar(usuario.Nome)` como último argumento.

`DiretorioController`: nos dois pré-preenchimentos (`Nome = pessoa.TemPerfil ? pessoa.Nome : null`), troque por `Nome = pessoa.NomeDeExibicao`. `FormulariosDoDiretorio.cs`: `[StringLength(NomeDeExibicao.MaxLength)]` no `Nome` (com `using Secco.Intranet.Application.Diretorio;`).

- [ ] **Step 6: Escrita no handler de contato**

`EditarContatoHandler` — construtor `(IUsuariosParaDiretorio usuarios, IPerfilColaboradorRepository perfis, IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)` (+ `<param name="gestao">Gestão de acesso — dona do nome de exibição.</param>` e `using Secco.Intranet.Application.Acesso;`). Validação do nome contra `NomeDeExibicao.MaxLength`. Depois de achar o usuário entre os ativos:

```csharp
		var usuario = ativos.Value.FirstOrDefault(candidato => candidato.Id == command.UsuarioId);

		if (usuario is null)
		{
			return Result.Failure(IntranetErrors.Diretorio.PessoaNaoEncontrada);
		}

		var campos = new List<string>();
		var nomeNovo = NomeDeExibicao.Normalizar(command.Nome);

		// O nome mora no SecureGate. Só vai para lá quando muda — editar ramal ou "sobre" não
		// depende da plataforma estar no ar — e vai primeiro: se ela recusar, nada local é gravado.
		if (!string.Equals(nomeNovo, NomeDeExibicao.Normalizar(usuario.Nome), StringComparison.Ordinal))
		{
			var gravado = await gestao.DefinirNomeDeExibicaoAsync(command.UsuarioId, command.Nome, cancellationToken).ConfigureAwait(false);

			if (gravado.IsFailure)
			{
				return gravado;
			}

			usuarios.Esquecer();
			campos.Add(PerfilColaborador.CampoNome);
		}

		campos.AddRange(await EdicaoDePerfil
			.AplicarAsync(perfis, command.UsuarioId, perfil => perfil.EditarContato(command.Ramal, command.Sobre), cancellationToken)
			.ConfigureAwait(false));
```

e o `if (campos.Count > 0)` da auditoria segue igual.

> Repare que `Contato_NomeVazioEmQuemTinhaNome_LimpaNaPlataforma` espera a chamada com o texto como veio (`"   "`): o handler repassa `command.Nome` e quem apara é o adaptador. É o mesmo contrato do dublê.

- [ ] **Step 7: Escrita na importação**

`ImportarDiretorioHandler` — construtor ganha `IGestaoDeAcesso gestao` (antes da trilha). `LinhaPlanejada` troca `string? Nome` por `string? NomeNovo` (nulo = não mexer). Em `Planejar`:

```csharp
		var nomeNovo = linha.Nome is not null
			&& !string.Equals(NomeDeExibicao.Normalizar(linha.Nome), NomeDeExibicao.Normalizar(usuario.Nome), StringComparison.Ordinal)
			? linha.Nome
			: null;

		var mudouLocal = Mudou(atual?.Cargo, linha.Cargo)
			|| Mudou(atual?.Ramal, linha.Ramal)
			|| (setorId is not null && setorId != atual?.SetorId)
			|| (gestorId is not null && gestorId != atual?.GestorUsuarioId);

		var status = !mudouLocal && nomeNovo is null
			? StatusDaLinha.SemAlteracao
			: atual is null && mudouLocal ? StatusDaLinha.Criar : StatusDaLinha.Atualizar;
```

(o limite de tamanho do nome passa a `NomeDeExibicao.MaxLength`). Em `AplicarAsync`, troque o laço por:

```csharp
		var relatorioPorNumero = plano.Value.ToDictionary(linha => linha.Relatorio.Numero, linha => linha.Relatorio);
		var algumNome = false;

		foreach (var linha in plano.Value.Where(linha => linha.Relatorio.Status is StatusDaLinha.Criar or StatusDaLinha.Atualizar))
		{
			// Mesma regra da edição: o nome vai primeiro; recusado, a linha inteira vira erro.
			if (linha.NomeNovo is not null)
			{
				var gravado = await gestao.DefinirNomeDeExibicaoAsync(linha.UsuarioId, linha.NomeNovo, cancellationToken).ConfigureAwait(false);

				if (gravado.IsFailure)
				{
					relatorioPorNumero[linha.Relatorio.Numero] = linha.Relatorio with
					{
						Status = StatusDaLinha.Erro,
						Erro = $"o SecureGate recusou o nome: {gravado.Error.Description}",
					};

					continue;
				}

				algumNome = true;
			}

			await EdicaoDePerfil.AplicarAsync(
				perfis,
				linha.UsuarioId,
				perfil => [
					.. perfil.EditarContato(linha.Ramal ?? perfil.Ramal, perfil.Sobre),
					.. perfil.EditarDadosFuncionais(linha.Cargo ?? perfil.Cargo, linha.SetorId ?? perfil.SetorId, linha.GestorId ?? perfil.GestorUsuarioId),
				],
				cancellationToken).ConfigureAwait(false);
		}

		if (algumNome)
		{
			usuarios.Esquecer();
		}

		var relatorio = new RelatorioDeImportacao([.. relatorioPorNumero.OrderBy(par => par.Key).Select(par => par.Value)], Aplicado: true);
```

(`Rejeitada` passa `null` no lugar do nome, como hoje.)

- [ ] **Step 8: Domínio, mapeamento, seeder e migration**

`PerfilColaborador`: remova `NomeMaxLength`, a propriedade `NomeExibicao`, e o nome de `EditarContato`, que vira `EditarContato(string? ramal, string? sobre)`. Mantenha `CampoNome` (a auditoria do handler usa) e ajuste o XML doc do construtor/classe se citar o nome.

`PerfilColaboradorConfiguration`: remova a linha do `NomeExibicao`.

`DiretorioDesenvolvimentoSeeder`: `perfil.EditarContato(pessoa.Ramal, null);` — e em `DiretorioDesenvolvimentoSeederTests`, a asserção do nome (linha ~52) vira a do ramal da Ana (`PessoasDeDesenvolvimento` dá o valor). `PerfilColaboradorPersistenciaTests`: `EditarContato("2100", "Sobre a Ana")`, sem a asserção do nome.

Migrations:

```bash
dotnet ef migrations add RemoverNomeExibicaoDoPerfil --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add RemoverNomeExibicaoDoPerfil --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```

Expected: cada `Up` tem só um `DropColumn` da coluna do nome em `tb_perfis_colaboradores` (o nome exato da coluna está no snapshot); o `Down` a recria. Se aparecer qualquer outra operação, pare: o modelo divergiu do snapshot por outro motivo.

- [ ] **Step 9: Integração**

`DiretorioEdicaoTests`: o `CriarCliente` passa a também definir `factory.GestaoDeAcesso = gestao` (uma `GestaoDeAcessoFalsa` criada no teste), e o `DisposeAsync` zera `factory.GestaoDeAcesso`. Os testes que conferiam `lida!.Nome` depois de salvar passam a conferir `gestao.Chamadas` (ex.: `Contain($"usuario-nome:{eu}:Eva Souza")`). Acrescente:

```csharp
	[Fact]
	public async Task Meu_perfil_ComPerfilESemNome_SalvarSoORamal_NaoGravaOEmailComoNome()
	{
		var eu = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com");
		var gestao = new GestaoDeAcessoFalsa();
		var client = CriarCliente(usuarios, gestao, eu, "diretorio-user");

		// Primeiro salvamento cria o perfil local (só ramal).
		var token = await TokenAsync(client, "/diretorio/perfil");
		await client.PostAsync("/diretorio/perfil", Form(("Ramal", "1111"), ("__RequestVerificationToken", token)));

		// Reabre o formulário: o campo Nome não pode vir com o e-mail.
		var html = Decodificar(await client.GetStringAsync("/diretorio/perfil"));
		html.Should().NotContain("value=\"eu@x.com\"");

		// Reenvia o que o formulário trouxe, mudando só o ramal.
		token = await TokenAsync(client, "/diretorio/perfil");
		await client.PostAsync("/diretorio/perfil", Form(("Nome", ""), ("Ramal", "2222"), ("__RequestVerificationToken", token)));

		gestao.Chamadas.Should().BeEmpty("o nome não mudou — continua sem nome");
	}
```

(`CriarCliente` ganha o parâmetro `GestaoDeAcessoFalsa gestao` — atualize os call sites do arquivo.)

- [ ] **Step 10: Rodar**

Run: `dotnet test tests/Secco.Intranet.Tests` — Expected: PASS, suíte inteira (os ignorados continuam ignorados).

Run: `grep -rn "NomeExibicao\|NomeMaxLength" src tests --include=*.cs --include=*.cshtml | grep -v "/obj/\|Migrations"` — Expected: nenhuma linha.

- [ ] **Step 11: Documentação**

- `docs/roadmap.md`: o item do Diretório organizacional vira `[x]`, com "nome de exibição vindo do SecureGate (#30) — [spec](specs/2026-10-04-nome-de-exibicao-na-plataforma-design.md)"; o trecho da foto sai dele e vira item novo `[ ]` na Fase 3: "Foto do colaborador no Diretório — upload, recorte e redução pela SDK de imagem; bloqueado por [secco-platform#29](https://github.com/rafsecco/secco-platform/issues/29). Até lá, avatar por iniciais".
- `docs/plataforma.md`: na linha da #30, troque "trocar de adaptador e migrar os dados é trabalho próprio, ainda não feito" por "consumido pelo Diretório desde 2026-10-04 (nome lido da listagem, gravado por `SetUserDisplayName`; a coluna local saiu)".
- `docs/specs/2026-09-24-diretorio-organizacional-design.md`: abaixo do cabeçalho, `**Revisada por:** [2026-10-04-nome-de-exibicao-na-plataforma-design.md](2026-10-04-nome-de-exibicao-na-plataforma-design.md) — o nome de exibição passou para a plataforma; a etapa 6 (foto) saiu do MVP e espera a #29.`

- [ ] **Step 12: Commit**

```bash
git add src tests docs
git commit -m "feat(diretorio): nome de exibicao vem do SecureGate; coluna local sai

O nome e lido da listagem de usuarios e gravado pela API so quando muda,
antes do perfil local. Formulario pre-preenchido com o displayName cru,
nunca com o e-mail. Importacao CSV segue a mesma regra; recusa da
plataforma vira erro na linha. Migration remove a coluna nos dois engines.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
