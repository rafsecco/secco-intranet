# Diretório organizacional — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trocar a demonstração do diretório por um recurso real: busca de pessoas, perfil complementar de cada colaborador, organograma por gestor e importação CSV, com acesso só de `intranet-admin`, `diretorio-admin` e `diretorio-user`.

**Architecture:** Um `PerfilColaborador` local (um por usuário do SecureGate, sem espelho de identidade) é juntado em memória à lista de usuários ativos, obtida pela porta `IUsuariosParaDiretorio` (sobre `IGestaoDeAcesso`, com cache curta). O acesso é um nível calculado por uma função única (`Nenhum`/`Usuario`/`Administrador`) e aplicado por um filtro de autorização declarativo, como o `[SomenteIntranetAdmin]`. Editar o próprio contato e editar dados funcionais são dois comandos separados.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 (SqlServer + Postgres), `Secco.SharedKernel.Results`/`Pagination`, xUnit + AwesomeAssertions.

**Spec:** [`docs/specs/2026-09-24-diretorio-organizacional-design.md`](../specs/2026-09-24-diretorio-organizacional-design.md). **Fora deste plano:** a etapa 6 da spec (foto), bloqueada por [secco-platform#29](https://github.com/rafsecco/secco-platform/issues/29) — ganha plano próprio quando a SDK existir.

## Global Constraints

- **Commits vão direto na `main`**, por caminho explícito — nunca `git add -A`. Trailer: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- **Só três perfis acessam o Diretório:** `intranet-admin` e `diretorio-admin` (nível `Administrador`) e `diretorio-user` (nível `Usuario`). Quem não tem nenhum — inclusive `{slug}-admin`, `{slug}-user` e `inventario-admin` — recebe **403 em toda rota**, e não vê o item de menu nem o link "Meu perfil".
- **Bypass de "modo aberto" só em `IWebHostEnvironment.IsDevelopment() && !IntranetAuthenticationExtensions.IsConfigured(configuration)`** — nunca em `Testing`. Use `AcessoAdministrativo.ModoAbertoDeDev`.
- **O filtro de acesso roda antes do antifalsificação** (`Order = int.MinValue`): um `POST` sem permissão dá 403, não 400.
- **Colaborador edita só o próprio contato** (nome, ramal, sobre). **Cargo, setor (lotação) e gestor só o admin do diretório.** São **dois comandos e dois handlers**; o formulário do colaborador não tem os campos funcionais, e o `usuarioId` do "próprio" vem **sempre** do claim `sub`, nunca do formulário.
- **Identidade não é espelhada:** `UsuarioId` e `GestorUsuarioId` são Guids do SecureGate, **sem FK**. Só `SetorId` é FK (tabela local de setores).
- **Regras de gestor:** ninguém é gestor de si mesmo; sem ciclo (A→B→A ou mais longo); gestor precisa ser usuário ativo; gestor desativado depois **não apaga a equipe** (vira raiz, marca "gestor inativo"). Lotação só aceita setor **existente e ativo** para valores novos.
- **Limites de campo:** nome 120, cargo 120, ramal 20, sobre 500 (constantes em `PerfilColaborador`, reusadas nas validações e nos formulários).
- **Falha do SecureGate vira `Result`, nunca lista vazia silenciosa:** a lista de usuários **não é cacheada quando falha**; as telas respondem 200 com a explicação.
- **Auditoria** (recurso `diretorio`): `diretorio.perfil-editar`, `diretorio.dados-funcionais-editar`, `diretorio.importar`. Metadata com **nomes de campo, nunca valores**. Leitura não é auditada.
- **Importação CSV:** cabeçalho `email;nome;cargo;ramal;setor;gestor`, aceita `;` ou `,`, UTF-8 com/sem BOM, 1 MB e 5 mil linhas no máximo; `setor` é o slug, `gestor` é o e-mail; **não cria usuário**; célula vazia = **não alterar**; pré-visualização não grava; confirmação revalida tudo.
- **Views só com os partials do contrato de tema** (`_PageHeader`, `_Badge`, `_EmptyState`, `_Pagination`); nenhum arquivo em `Themes/*` muda neste plano.
- **Nada no repositório, na documentação ou nos commits faz referência a sistema de terceiro analisado.**
- **Estilo:** tabs (4) em `.cs`; 4 espaços em `.cshtml`; XML doc `<summary>` em todo membro público; build com **0 avisos**; suíte inteira verde antes de cada commit. **Não use heredoc do shell para escrever arquivo**; use Write/Edit.

## Review Focus

1. **Campo funcional forjado.** Um `POST` de "Meu perfil" com `Cargo`, `SetorId` ou `GestorUsuarioId` no corpo **não altera** esses campos (o colaborador não tem como promover a si mesmo). Task 8.
2. **Gestor.** Autogestor, ciclo direto, ciclo de três pessoas, gestor desativado e **dado já corrompido no banco** (ciclo gravado à mão) não travam nem somem com ninguém do organograma. Tasks 7 e 9.
3. **Perfil sem usuário / usuário sem perfil.** Perfil local de usuário desativado **não aparece**; usuário sem perfil aparece pelo e-mail; e-mail vazio cai no id sem quebrar a view. Task 5.
4. **CSV hostil ou desajeitado.** BOM, `;` e `,`, aspas com o delimitador dentro, linha em branco, coluna desconhecida, e-mail repetido no arquivo, gestor que só existe mais abaixo no mesmo arquivo, ciclo dentro do lote, arquivo acima do limite. Task 10.
5. **SecureGate ausente ou fora do ar / sem `sub`.** Toda tela do Diretório responde 200 explicando (nunca 500), e "Meu perfil" sem usuário identificado (modo aberto de DEV) explica em vez de quebrar. Tasks 6 e 8.

---

## Task 1: Domínio — `PerfilColaborador`

**Files:**
- Create: `src/Secco.Intranet.Domain/Diretorio/PerfilColaborador.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/PerfilColaboradorTests.cs`

**Interfaces:**
- Produces: `PerfilColaborador : BaseEntity` com `UsuarioId`, `NomeExibicao`, `Cargo`, `Ramal`, `Sobre`, `SetorId`, `GestorUsuarioId`, `CreatedAt`, `UpdatedAt`; `new PerfilColaborador(Guid usuarioId)`; `IReadOnlyList<string> EditarContato(string? nome, string? ramal, string? sobre)` e `IReadOnlyList<string> EditarDadosFuncionais(string? cargo, Guid? setorId, Guid? gestorUsuarioId)` — ambos devolvem os **nomes dos campos que mudaram** (`CampoNome`, `CampoRamal`, `CampoSobre`, `CampoCargo`, `CampoSetor`, `CampoGestor`); constantes `NomeMaxLength`, `CargoMaxLength`, `RamalMaxLength`, `SobreMaxLength`.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
// tests/Secco.Intranet.Tests/Unit/PerfilColaboradorTests.cs
using AwesomeAssertions;
using Secco.Intranet.Domain.Diretorio;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class PerfilColaboradorTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();

	[Fact]
	public void Novo_ComUsuarioVazio_Recusa()
	{
		var criar = () => new PerfilColaborador(Guid.Empty);

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Novo_NasceSemNadaPreenchido()
	{
		var perfil = new PerfilColaborador(Ana);

		perfil.UsuarioId.Should().Be(Ana);
		perfil.NomeExibicao.Should().BeNull();
		perfil.Cargo.Should().BeNull();
		perfil.UpdatedAt.Should().BeNull();
	}

	[Fact]
	public void EditarContato_AparaEDevolveOsCamposAlterados()
	{
		var perfil = new PerfilColaborador(Ana);

		var alterados = perfil.EditarContato("  Ana Ribeiro ", " 2100 ", null);

		alterados.Should().Equal(PerfilColaborador.CampoNome, PerfilColaborador.CampoRamal);
		perfil.NomeExibicao.Should().Be("Ana Ribeiro");
		perfil.Ramal.Should().Be("2100");
		perfil.UpdatedAt.Should().NotBeNull();
	}

	[Fact]
	public void EditarContato_TextoEmBranco_ViraNulo()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.EditarContato("Ana", "1", "sobre");

		var alterados = perfil.EditarContato("   ", "", null);

		alterados.Should().Equal(PerfilColaborador.CampoNome, PerfilColaborador.CampoRamal, PerfilColaborador.CampoSobre);
		perfil.NomeExibicao.Should().BeNull();
		perfil.Ramal.Should().BeNull();
		perfil.Sobre.Should().BeNull();
	}

	[Fact]
	public void EditarContato_SemMudanca_NaoDevolveCampoNemAtualizaData()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.EditarContato("Ana", null, null);
		var atualizado = perfil.UpdatedAt;

		var alterados = perfil.EditarContato("Ana", null, null);

		alterados.Should().BeEmpty();
		perfil.UpdatedAt.Should().Be(atualizado);
	}

	[Theory]
	[InlineData(PerfilColaborador.NomeMaxLength + 1, 0, 0)]
	[InlineData(0, PerfilColaborador.RamalMaxLength + 1, 0)]
	[InlineData(0, 0, PerfilColaborador.SobreMaxLength + 1)]
	public void EditarContato_AcimaDoLimite_Recusa(int nome, int ramal, int sobre)
	{
		var perfil = new PerfilColaborador(Ana);

		var editar = () => perfil.EditarContato(new string('a', nome), new string('1', ramal), new string('s', sobre));

		editar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void EditarContato_Recusado_NaoAlteraNada()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.EditarContato("Ana", "1", null);

		var editar = () => perfil.EditarContato("Outro", "2", new string('s', PerfilColaborador.SobreMaxLength + 1));

		editar.Should().Throw<DomainInvariantException>();
		perfil.NomeExibicao.Should().Be("Ana", "a validação acontece antes de qualquer atribuição");
		perfil.Ramal.Should().Be("1");
	}

	[Fact]
	public void EditarDadosFuncionais_DefineEDevolveOsCampos()
	{
		var perfil = new PerfilColaborador(Ana);
		var setor = Guid.NewGuid();

		var alterados = perfil.EditarDadosFuncionais(" Analista ", setor, Bruno);

		alterados.Should().Equal(PerfilColaborador.CampoCargo, PerfilColaborador.CampoSetor, PerfilColaborador.CampoGestor);
		perfil.Cargo.Should().Be("Analista");
		perfil.SetorId.Should().Be(setor);
		perfil.GestorUsuarioId.Should().Be(Bruno);
	}

	[Fact]
	public void EditarDadosFuncionais_NulosLimpamOsCampos()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.EditarDadosFuncionais("Analista", Guid.NewGuid(), Bruno);

		var alterados = perfil.EditarDadosFuncionais(null, null, null);

		alterados.Should().HaveCount(3);
		perfil.SetorId.Should().BeNull();
		perfil.GestorUsuarioId.Should().BeNull();
	}

	[Fact]
	public void EditarDadosFuncionais_GuidVazio_TratadoComoNulo()
	{
		var perfil = new PerfilColaborador(Ana);

		var alterados = perfil.EditarDadosFuncionais(null, Guid.Empty, Guid.Empty);

		alterados.Should().BeEmpty();
		perfil.SetorId.Should().BeNull();
	}

	[Fact]
	public void EditarDadosFuncionais_GestorEhOProprio_Recusa()
	{
		var perfil = new PerfilColaborador(Ana);

		var editar = () => perfil.EditarDadosFuncionais(null, null, Ana);

		editar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void EditarDadosFuncionais_CargoAcimaDoLimite_Recusa()
	{
		var perfil = new PerfilColaborador(Ana);

		var editar = () => perfil.EditarDadosFuncionais(new string('c', PerfilColaborador.CargoMaxLength + 1), null, null);

		editar.Should().Throw<DomainInvariantException>();
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter PerfilColaboradorTests`
Expected: falha de compilação (`PerfilColaborador` não existe).

- [ ] **Step 3: Implementar a entidade**

```csharp
// src/Secco.Intranet.Domain/Diretorio/PerfilColaborador.cs
using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Diretorio;

/// <summary>
/// Perfil complementar de um colaborador: só o que o SecureGate não guarda. Identidade (id,
/// e-mail, situação da conta) vive na plataforma; <see cref="UsuarioId"/> é um Guid dela, sem FK
/// (ADR-0006). Nasce na primeira edição — quem não tem perfil aparece pelo e-mail.
/// </summary>
public sealed class PerfilColaborador : BaseEntity
{
	/// <summary>Tamanho máximo do nome de exibição.</summary>
	public const int NomeMaxLength = 120;

	/// <summary>Tamanho máximo do cargo.</summary>
	public const int CargoMaxLength = 120;

	/// <summary>Tamanho máximo do ramal.</summary>
	public const int RamalMaxLength = 20;

	/// <summary>Tamanho máximo do texto "sobre".</summary>
	public const int SobreMaxLength = 500;

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoNome = "nome";

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoRamal = "ramal";

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoSobre = "sobre";

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoCargo = "cargo";

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoSetor = "setor";

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoGestor = "gestor";

	private PerfilColaborador()
	{
		// Construtor de rehidratação do EF Core
	}

	/// <summary>Cria um perfil vazio para o usuário do SecureGate informado.</summary>
	/// <param name="usuarioId">Id do usuário no SecureGate.</param>
	/// <exception cref="DomainInvariantException">Se o id for vazio.</exception>
	public PerfilColaborador(Guid usuarioId)
	{
		if (usuarioId == Guid.Empty)
		{
			throw new DomainInvariantException("Um perfil de colaborador exige um usuário válido.");
		}

		UsuarioId = usuarioId;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Id do usuário no SecureGate. Único, sem FK.</summary>
	public Guid UsuarioId { get; private set; }

	/// <summary>Nome de exibição; nulo cai no e-mail na tela.</summary>
	public string? NomeExibicao { get; private set; }

	/// <summary>Cargo. Só o admin do diretório edita.</summary>
	public string? Cargo { get; private set; }

	/// <summary>Ramal telefônico.</summary>
	public string? Ramal { get; private set; }

	/// <summary>Texto livre sobre a pessoa.</summary>
	public string? Sobre { get; private set; }

	/// <summary>Setor de lotação (FK para o setor local). Só o admin do diretório edita.</summary>
	public Guid? SetorId { get; private set; }

	/// <summary>Id, no SecureGate, do gestor a quem a pessoa reporta. Sem FK. Só o admin edita.</summary>
	public Guid? GestorUsuarioId { get; private set; }

	/// <summary>Momento da criação.</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Momento da última alteração real (nulo se nunca alterado).</summary>
	public DateTimeOffset? UpdatedAt { get; private set; }

	/// <summary>Altera o contato. Devolve os nomes dos campos que <b>de fato</b> mudaram.</summary>
	/// <exception cref="DomainInvariantException">Algum campo acima do limite; nada é alterado.</exception>
	public IReadOnlyList<string> EditarContato(string? nome, string? ramal, string? sobre)
	{
		var novoNome = Normalizar(nome, NomeMaxLength, "nome");
		var novoRamal = Normalizar(ramal, RamalMaxLength, "ramal");
		var novoSobre = Normalizar(sobre, SobreMaxLength, "sobre");

		var alterados = new List<string>();

		Atribuir(NomeExibicao, novoNome, valor => NomeExibicao = valor, CampoNome, alterados);
		Atribuir(Ramal, novoRamal, valor => Ramal = valor, CampoRamal, alterados);
		Atribuir(Sobre, novoSobre, valor => Sobre = valor, CampoSobre, alterados);

		if (alterados.Count > 0)
		{
			UpdatedAt = DateTimeOffset.UtcNow;
		}

		return alterados;
	}

	/// <summary>
	/// Altera os dados funcionais (valores nulos limpam o campo). Devolve os nomes dos campos que
	/// de fato mudaram.
	/// </summary>
	/// <exception cref="DomainInvariantException">Cargo acima do limite, ou gestor igual ao próprio usuário.</exception>
	public IReadOnlyList<string> EditarDadosFuncionais(string? cargo, Guid? setorId, Guid? gestorUsuarioId)
	{
		var novoCargo = Normalizar(cargo, CargoMaxLength, "cargo");
		var novoSetor = setorId == Guid.Empty ? null : setorId;
		var novoGestor = gestorUsuarioId == Guid.Empty ? null : gestorUsuarioId;

		if (novoGestor == UsuarioId)
		{
			throw new DomainInvariantException("Ninguém pode ser gestor de si mesmo.");
		}

		var alterados = new List<string>();

		Atribuir(Cargo, novoCargo, valor => Cargo = valor, CampoCargo, alterados);

		if (SetorId != novoSetor)
		{
			SetorId = novoSetor;
			alterados.Add(CampoSetor);
		}

		if (GestorUsuarioId != novoGestor)
		{
			GestorUsuarioId = novoGestor;
			alterados.Add(CampoGestor);
		}

		if (alterados.Count > 0)
		{
			UpdatedAt = DateTimeOffset.UtcNow;
		}

		return alterados;
	}

	private static void Atribuir(string? atual, string? novo, Action<string?> definir, string campo, List<string> alterados)
	{
		if (string.Equals(atual, novo, StringComparison.Ordinal))
		{
			return;
		}

		definir(novo);
		alterados.Add(campo);
	}

	private static string? Normalizar(string? valor, int limite, string rotulo)
	{
		var aparado = valor?.Trim();

		if (string.IsNullOrEmpty(aparado))
		{
			return null;
		}

		if (aparado.Length > limite)
		{
			throw new DomainInvariantException($"O campo {rotulo} excede o limite de {limite} caracteres.");
		}

		return aparado;
	}
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter PerfilColaboradorTests`
Expected: PASS, 0 avisos.

- [ ] **Step 5: Commit**

```bash
git add src/Secco.Intranet.Domain/Diretorio tests/Secco.Intranet.Tests/Unit/PerfilColaboradorTests.cs
git commit -m "feat(diretorio): entidade PerfilColaborador

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 2: Persistência — mapeamento, repositório e migrations

**Files:**
- Modify: `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs`
- Create: `src/Secco.Intranet.Infrastructure/Mappings/PerfilColaboradorConfiguration.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/IPerfilColaboradorRepository.cs`
- Create: `src/Secco.Intranet.Infrastructure/Repositories/PerfilColaboradorRepository.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`
- Create: migrations nos dois provedores (geradas por `dotnet ef`)
- Create: `tests/Secco.Intranet.Tests/Integration/PerfilColaboradorPersistenciaTests.cs`

**Interfaces:**
- Consumes: `PerfilColaborador` (Task 1).
- Produces: `IntranetDbContext.PerfisColaboradores : DbSet<PerfilColaborador>`; `IPerfilColaboradorRepository` com `Task<PerfilColaborador?> GetByUsuarioIdAsync(Guid, CancellationToken)` (sem rastreamento), `Task<PerfilColaborador?> GetParaEdicaoAsync(Guid, CancellationToken)` (rastreado), `Task<IReadOnlyList<PerfilColaborador>> ListarTodosAsync(CancellationToken)` (sem rastreamento), `Task<bool> TentarAdicionarAsync(PerfilColaborador, CancellationToken)` (`false` se já existia um perfil desse usuário — corrida de dois primeiros salvamentos), `Task SaveChangesAsync(CancellationToken)`.

- [ ] **Step 1: A porta do repositório**

```csharp
// src/Secco.Intranet.Application/Diretorio/IPerfilColaboradorRepository.cs
using Secco.Intranet.Domain.Diretorio;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Porta de persistência dos perfis de colaborador — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IPerfilColaboradorRepository
{
	/// <summary>Busca o perfil de um usuário, <b>desrastreado</b> — caminho de leitura.</summary>
	/// <param name="usuarioId">Id do usuário no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PerfilColaborador?> GetByUsuarioIdAsync(Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Busca o perfil <b>rastreado</b>, para alteração. Alterar o resultado de
	/// <see cref="GetByUsuarioIdAsync"/> e chamar <see cref="SaveChangesAsync"/> não gravaria nada.
	/// </summary>
	/// <param name="usuarioId">Id do usuário no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PerfilColaborador?> GetParaEdicaoAsync(Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Todos os perfis do tenant, desrastreados. O diretório junta isto em memória com os usuários.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<PerfilColaborador>> ListarTodosAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Persiste um perfil novo. Devolve <c>false</c> — sem lançar — se já existe um perfil desse
	/// usuário (dois primeiros salvamentos simultâneos): o chamador recarrega e reaplica.
	/// </summary>
	/// <param name="perfil">Perfil novo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> TentarAdicionarAsync(PerfilColaborador perfil, CancellationToken cancellationToken = default);

	/// <summary>Grava alterações pendentes de um perfil já rastreado.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: `DbSet` e mapeamento**

Em `IntranetDbContext.cs`, acrescente `using Secco.Intranet.Domain.Diretorio;` e, junto dos outros `DbSet`:

```csharp
	/// <summary>Perfis complementares de colaborador (tabela <c>tb_perfis_colaboradores</c>).</summary>
	public DbSet<PerfilColaborador> PerfisColaboradores => Set<PerfilColaborador>();
```

```csharp
// src/Secco.Intranet.Infrastructure/Mappings/PerfilColaboradorConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="PerfilColaborador"/>. Nomes de tabela e colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class PerfilColaboradorConfiguration : IEntityTypeConfiguration<PerfilColaborador>
{
	public void Configure(EntityTypeBuilder<PerfilColaborador> builder)
	{
		// SetorId é uma FK de verdade (o setor é local), sem navegação — mesmo padrão de
		// ItemInventarioConfiguration. GestorUsuarioId e UsuarioId NÃO são FK: identidade vive só
		// no SecureGate (ADR-0006), então ficam como Guid simples, sem prefixo id_fk_.
		builder
			.HasOne<Setor>()
			.WithMany()
			.HasForeignKey(perfil => perfil.SetorId)
			.OnDelete(DeleteBehavior.Restrict)
			.IsRequired(false);

		builder.Property(perfil => perfil.NomeExibicao).HasMaxLength(PerfilColaborador.NomeMaxLength);
		builder.Property(perfil => perfil.Cargo).HasMaxLength(PerfilColaborador.CargoMaxLength);
		builder.Property(perfil => perfil.Ramal).HasMaxLength(PerfilColaborador.RamalMaxLength);
		builder.Property(perfil => perfil.Sobre).HasMaxLength(PerfilColaborador.SobreMaxLength);

		// Um perfil por usuário — é o que sustenta o "upsert" sob demanda.
		builder.HasIndex(perfil => perfil.UsuarioId).IsUnique();
		builder.HasIndex(perfil => perfil.SetorId);
		builder.HasIndex(perfil => perfil.GestorUsuarioId);
	}
}
```

- [ ] **Step 3: O repositório**

```csharp
// src/Secco.Intranet.Infrastructure/Repositories/PerfilColaboradorRepository.cs
using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência de perfis de colaborador no banco do tenant atual.</summary>
internal sealed class PerfilColaboradorRepository(IntranetDbContext context) : IPerfilColaboradorRepository
{
	public async Task<PerfilColaborador?> GetByUsuarioIdAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		await context.PerfisColaboradores
			.AsNoTracking()
			.FirstOrDefaultAsync(perfil => perfil.UsuarioId == usuarioId, cancellationToken)
			.ConfigureAwait(false);

	public async Task<PerfilColaborador?> GetParaEdicaoAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		await context.PerfisColaboradores
			.FirstOrDefaultAsync(perfil => perfil.UsuarioId == usuarioId, cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<PerfilColaborador>> ListarTodosAsync(CancellationToken cancellationToken = default) =>
		await context.PerfisColaboradores
			.AsNoTracking()
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<bool> TentarAdicionarAsync(PerfilColaborador perfil, CancellationToken cancellationToken = default)
	{
		context.PerfisColaboradores.Add(perfil);

		try
		{
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

			return true;
		}
		catch (DbUpdateException)
		{
			context.Entry(perfil).State = EntityState.Detached;

			var jaExiste = await context.PerfisColaboradores
				.AsNoTracking()
				.AnyAsync(outro => outro.UsuarioId == perfil.UsuarioId, cancellationToken)
				.ConfigureAwait(false);

			// Outro pedido criou o perfil um instante antes: não é falha, o chamador reaplica.
			// Qualquer outra causa de DbUpdateException continua subindo.
			if (jaExiste)
			{
				return false;
			}

			throw;
		}
	}

	public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
```

Em `IntranetInfrastructureExtensions.cs`, acrescente `using Secco.Intranet.Application.Diretorio;` e, junto dos outros repositórios:

```csharp
		services.AddScoped<IPerfilColaboradorRepository, PerfilColaboradorRepository>();
```

- [ ] **Step 4: Gerar as migrations nos dois provedores**

Run:
```bash
dotnet ef migrations add PerfisColaboradores --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add PerfisColaboradores --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```
Expected: dois arquivos novos por provedor (`*_PerfisColaboradores.cs` e `.Designer.cs`) e o snapshot de cada um atualizado.

- [ ] **Step 5: Conferir a migration gerada**

Abra os dois `*_PerfisColaboradores.cs` e confirme: tabela `tb_perfis_colaboradores`; PK `id_pk_perfil_colaborador`; `id_fk_setor` **nullable** com FK para `tb_setores` em `NO ACTION`; `UsuarioId` e `GestorUsuarioId` como colunas **sem** prefixo `id_fk_` (não são FKs reconhecidas); índice **único** em `UsuarioId`; índices em `id_fk_setor` e `GestorUsuarioId`; `MaxLength` 120/120/20/500. Se um nome destoar, ajuste `PerfilColaboradorConfiguration`, rode `dotnet ef migrations remove` nos dois e regenere (mesmo cuidado do Inventário).

- [ ] **Step 6: Teste de persistência real**

```csharp
// tests/Secco.Intranet.Tests/Integration/PerfilColaboradorPersistenciaTests.cs
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Diretorio;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Persistência de verdade, contra o banco do tenant: o teste do dublê de repositório não prova
/// que o mapeamento, o índice único e o rastreamento funcionam.
/// </summary>
public class PerfilColaboradorPersistenciaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private IServiceScope NovoEscopo()
	{
		var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		return escopo;
	}

	[Fact]
	public async Task Adicionar_EBuscar_GravaDeVerdade()
	{
		var usuario = Guid.NewGuid();
		var perfil = new PerfilColaborador(usuario);
		perfil.EditarContato("Ana Ribeiro", "2100", "Sobre a Ana");

		using (var escopo = NovoEscopo())
		{
			var criado = await escopo.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>().TentarAdicionarAsync(perfil);
			criado.Should().BeTrue();
		}

		// Escopo novo, DbContext novo: só sobrevive o que foi para o banco.
		using var leitura = NovoEscopo();
		var lido = await leitura.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>().GetByUsuarioIdAsync(usuario);

		lido.Should().NotBeNull();
		lido!.NomeExibicao.Should().Be("Ana Ribeiro");
		lido.Ramal.Should().Be("2100");
	}

	[Fact]
	public async Task SegundoPerfilDoMesmoUsuario_DevolveFalse_SemLancar()
	{
		var usuario = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			(await escopo.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>()
				.TentarAdicionarAsync(new PerfilColaborador(usuario))).Should().BeTrue();
		}

		using var outro = NovoEscopo();
		var repetido = await outro.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>()
			.TentarAdicionarAsync(new PerfilColaborador(usuario));

		repetido.Should().BeFalse("o índice único barra o segundo, e a corrida não vira 500");
	}

	[Fact]
	public async Task Editar_Rastreado_GravaDeVerdade()
	{
		var usuario = Guid.NewGuid();
		var gestor = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			await escopo.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>()
				.TentarAdicionarAsync(new PerfilColaborador(usuario));
		}

		using (var escopo = NovoEscopo())
		{
			var repositorio = escopo.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>();
			var perfil = await repositorio.GetParaEdicaoAsync(usuario);
			perfil!.EditarDadosFuncionais("Analista", null, gestor);
			await repositorio.SaveChangesAsync();
		}

		using var leitura = NovoEscopo();
		var lido = await leitura.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>().GetByUsuarioIdAsync(usuario);

		lido!.Cargo.Should().Be("Analista");
		lido.GestorUsuarioId.Should().Be(gestor);
	}

	[Fact]
	public async Task ListarTodos_TrazOsPerfisDoTenant()
	{
		var usuario = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			await escopo.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>()
				.TentarAdicionarAsync(new PerfilColaborador(usuario));
		}

		using var leitura = NovoEscopo();
		var todos = await leitura.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>().ListarTodosAsync();

		todos.Should().Contain(perfil => perfil.UsuarioId == usuario);
	}
}
```

- [ ] **Step 7: Rodar e commitar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter PerfilColaboradorPersistenciaTests` → PASS; `dotnet build` com 0 avisos.

```bash
git add src/Secco.Intranet.Application/Diretorio src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs src/Secco.Intranet.Infrastructure/Mappings/PerfilColaboradorConfiguration.cs src/Secco.Intranet.Infrastructure/Repositories/PerfilColaboradorRepository.cs src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs src/Secco.Intranet.Migrations.SqlServer src/Secco.Intranet.Migrations.Postgres tests/Secco.Intranet.Tests/Integration/PerfilColaboradorPersistenciaTests.cs
git commit -m "feat(diretorio): persistencia do PerfilColaborador nos dois provedores

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

## Task 3: Nível de acesso ao Diretório, gate e perfis do produto

**Files:**
- Create: `src/Secco.Intranet.Web/Navigation/AcessoAoDiretorio.cs`
- Create: `src/Secco.Intranet.Web/Authentication/ExigeNivelNoDiretorioAttribute.cs`
- Modify: `src/Secco.Intranet.Application/Acesso/ClassificacaoDePerfil.cs`
- Modify: `tests/Secco.Intranet.Tests/Integration/TestAuthentication/RolesDeTesteMiddleware.cs`
- Modify: `tests/Secco.Intranet.Tests/Unit/RolesDeTesteMiddlewareTests.cs`
- Modify: `tests/Secco.Intranet.Tests/Unit/LeituraDeAcessoHandlersTests.cs`
- Modify: `tests/Secco.Intranet.Tests/Unit/ClassificacaoDePerfilTests.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/AcessoAoDiretorioTests.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/ExigeNivelNoDiretorioAttributeTests.cs`

**Interfaces:**
- Produces:
  - `enum NivelDeAcessoAoDiretorio { Nenhum = 0, Usuario = 1, Administrador = 2 }` e `static class AcessoAoDiretorio` (`RoleAdmin = "diretorio-admin"`, `RoleUsuario = "diretorio-user"`, `Nivel(ClaimsPrincipal?)`, `TemNivel(ClaimsPrincipal?, NivelDeAcessoAoDiretorio minimo)`, `UsuarioId(ClaimsPrincipal?) : Guid?` lido do claim `sub`), em `Secco.Intranet.Web.Navigation`.
  - `[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio)]` (classe ou método): filtro de autorização, `Order = int.MinValue`, 403 se o nível for menor; libera no modo aberto de DEV.
  - `ClassificacaoDePerfil.DiretorioAdmin`, `ClassificacaoDePerfil.DiretorioUsuario` e ambos em `PerfisDoProduto`.
  - `RolesDeTesteMiddleware.HeaderUsuario` (`"X-Test-User"`): define o claim `sub`.

- [ ] **Step 1: Testes do nível de acesso (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/AcessoAoDiretorioTests.cs
using System.Security.Claims;
using AwesomeAssertions;
using Secco.Intranet.Web.Navigation;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class AcessoAoDiretorioTests
{
	private static ClaimsPrincipal Usuario(params string[] roles) =>
		new(new ClaimsIdentity(roles.Select(role => new Claim(SeccoClaims.Role, role)), authenticationType: "Teste"));

	[Theory]
	[InlineData("intranet-admin", NivelDeAcessoAoDiretorio.Administrador)]
	[InlineData("Intranet-Admin", NivelDeAcessoAoDiretorio.Administrador)]
	[InlineData("diretorio-admin", NivelDeAcessoAoDiretorio.Administrador)]
	[InlineData("diretorio-user", NivelDeAcessoAoDiretorio.Usuario)]
	[InlineData("inventario-admin", NivelDeAcessoAoDiretorio.Nenhum)]
	[InlineData("financeiro-admin", NivelDeAcessoAoDiretorio.Nenhum)]
	[InlineData("financeiro-user", NivelDeAcessoAoDiretorio.Nenhum)]
	[InlineData("diretorio-admin-falso", NivelDeAcessoAoDiretorio.Nenhum)]
	public void Nivel_ClassificaPelaRole(string role, NivelDeAcessoAoDiretorio esperado)
	{
		AcessoAoDiretorio.Nivel(Usuario(role)).Should().Be(esperado);
	}

	[Fact]
	public void Nivel_UsuarioNuloOuSemRole_Nenhum()
	{
		AcessoAoDiretorio.Nivel(null).Should().Be(NivelDeAcessoAoDiretorio.Nenhum);
		AcessoAoDiretorio.Nivel(Usuario()).Should().Be(NivelDeAcessoAoDiretorio.Nenhum);
	}

	[Fact]
	public void Nivel_ComVariasRoles_VenceAMaisAlta()
	{
		AcessoAoDiretorio.Nivel(Usuario("financeiro-admin", "diretorio-user", "diretorio-admin"))
			.Should().Be(NivelDeAcessoAoDiretorio.Administrador);
	}

	[Fact]
	public void TemNivel_ComparaComOMinimo()
	{
		AcessoAoDiretorio.TemNivel(Usuario("diretorio-user"), NivelDeAcessoAoDiretorio.Usuario).Should().BeTrue();
		AcessoAoDiretorio.TemNivel(Usuario("diretorio-user"), NivelDeAcessoAoDiretorio.Administrador).Should().BeFalse();
		AcessoAoDiretorio.TemNivel(Usuario("diretorio-admin"), NivelDeAcessoAoDiretorio.Usuario).Should().BeTrue();
		AcessoAoDiretorio.TemNivel(Usuario("inventario-admin"), NivelDeAcessoAoDiretorio.Usuario).Should().BeFalse();
	}

	[Fact]
	public void UsuarioId_LeDoClaimSub()
	{
		var id = Guid.NewGuid();
		var usuario = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SeccoClaims.Subject, id.ToString())], "Teste"));

		AcessoAoDiretorio.UsuarioId(usuario).Should().Be(id);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("nao-e-guid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public void UsuarioId_SemSubValido_Nulo(string? sub)
	{
		var claims = sub is null ? [] : new[] { new Claim(SeccoClaims.Subject, sub) };
		var usuario = new ClaimsPrincipal(new ClaimsIdentity(claims, "Teste"));

		AcessoAoDiretorio.UsuarioId(usuario).Should().BeNull();
		AcessoAoDiretorio.UsuarioId(null).Should().BeNull();
	}
}
```

- [ ] **Step 2: Implementar `AcessoAoDiretorio`**

```csharp
// src/Secco.Intranet.Web/Navigation/AcessoAoDiretorio.cs
using System.Security.Claims;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.Navigation;

/// <summary>Nível de acesso de um usuário ao Diretório organizacional.</summary>
public enum NivelDeAcessoAoDiretorio
{
	/// <summary>Sem acesso: 403 em toda rota, sem item de menu.</summary>
	Nenhum = 0,

	/// <summary>Vê o diretório e edita o próprio contato.</summary>
	Usuario = 1,

	/// <summary>Edita todos os campos de todos e importa CSV.</summary>
	Administrador = 2,
}

/// <summary>
/// Única função que decide o nível de acesso ao Diretório: gate das rotas, item de menu e link
/// "Meu perfil" passam por aqui. Quando o modelo de permissões chegar, é só esta classe que
/// troca "nome da Role" por "permissão" (<c>diretorio:read</c> / <c>diretorio:manage</c>).
/// </summary>
public static class AcessoAoDiretorio
{
	/// <summary>Role de administração do Diretório (Role fixa do produto).</summary>
	public const string RoleAdmin = "diretorio-admin";

	/// <summary>Role de uso do Diretório (Role fixa do produto).</summary>
	public const string RoleUsuario = "diretorio-user";

	/// <summary>
	/// Nível do usuário. <c>intranet-admin</c> e <c>diretorio-admin</c> dão
	/// <see cref="NivelDeAcessoAoDiretorio.Administrador"/>; <c>diretorio-user</c> dá
	/// <see cref="NivelDeAcessoAoDiretorio.Usuario"/>; qualquer outra Role — inclusive de setor e
	/// <c>inventario-admin</c> — dá <see cref="NivelDeAcessoAoDiretorio.Nenhum"/>.
	/// </summary>
	/// <param name="usuario">Usuário atual; <c>null</c> devolve <see cref="NivelDeAcessoAoDiretorio.Nenhum"/>.</param>
	public static NivelDeAcessoAoDiretorio Nivel(ClaimsPrincipal? usuario)
	{
		if (usuario is null)
		{
			return NivelDeAcessoAoDiretorio.Nenhum;
		}

		var nivel = NivelDeAcessoAoDiretorio.Nenhum;

		foreach (var claim in usuario.FindAll(SeccoClaims.Role))
		{
			if (Igual(claim.Value, AcessoAdministrativo.RoleIntranetAdmin) || Igual(claim.Value, RoleAdmin))
			{
				return NivelDeAcessoAoDiretorio.Administrador;
			}

			if (Igual(claim.Value, RoleUsuario))
			{
				nivel = NivelDeAcessoAoDiretorio.Usuario;
			}
		}

		return nivel;
	}

	/// <summary>Indica se o usuário tem pelo menos o nível informado.</summary>
	/// <param name="usuario">Usuário atual.</param>
	/// <param name="minimo">Nível mínimo exigido.</param>
	public static bool TemNivel(ClaimsPrincipal? usuario, NivelDeAcessoAoDiretorio minimo) =>
		Nivel(usuario) >= minimo && minimo != NivelDeAcessoAoDiretorio.Nenhum;

	/// <summary>
	/// Id do usuário logado no SecureGate, lido do claim <c>sub</c>. É a **única** fonte de "quem
	/// sou eu": nunca se aceita um id vindo do formulário para isso.
	/// </summary>
	/// <param name="usuario">Usuário atual.</param>
	public static Guid? UsuarioId(ClaimsPrincipal? usuario)
	{
		var sub = usuario?.FindFirst(SeccoClaims.Subject)?.Value;

		return Guid.TryParse(sub, out var id) && id != Guid.Empty ? id : null;
	}

	private static bool Igual(string valor, string esperado) =>
		string.Equals(valor, esperado, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: Testes e implementação do filtro**

```csharp
// tests/Secco.Intranet.Tests/Unit/ExigeNivelNoDiretorioAttributeTests.cs
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Navigation;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ExigeNivelNoDiretorioAttributeTests
{
	private sealed class AmbienteFalso(string nome) : IWebHostEnvironment
	{
		public string ApplicationName { get; set; } = "Teste";

		public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

		public string WebRootPath { get; set; } = string.Empty;

		public string EnvironmentName { get; set; } = nome;

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private static AuthorizationFilterContext Contexto(string ambiente, bool autenticacaoConfigurada, params string[] roles)
	{
		var configuracao = new ConfigurationBuilder()
			.AddInMemoryCollection(autenticacaoConfigurada
				? new Dictionary<string, string?> { ["Secco:SecureGate:Authority"] = "https://securegate.exemplo" }
				: [])
			.Build();

		var servicos = new ServiceCollection()
			.AddSingleton<IWebHostEnvironment>(new AmbienteFalso(ambiente))
			.AddSingleton<IConfiguration>(configuracao)
			.BuildServiceProvider();

		var http = new DefaultHttpContext { RequestServices = servicos };

		if (roles.Length > 0)
		{
			http.User = new ClaimsPrincipal(new ClaimsIdentity(
				roles.Select(role => new Claim(SeccoClaims.Role, role)), authenticationType: "Teste"));
		}

		return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
	}

	private static bool Bloqueou(AuthorizationFilterContext contexto) =>
		contexto.Result is StatusCodeResult { StatusCode: StatusCodes.Status403Forbidden };

	[Theory]
	[InlineData("diretorio-user", NivelDeAcessoAoDiretorio.Usuario, false)]
	[InlineData("diretorio-user", NivelDeAcessoAoDiretorio.Administrador, true)]
	[InlineData("diretorio-admin", NivelDeAcessoAoDiretorio.Administrador, false)]
	[InlineData("intranet-admin", NivelDeAcessoAoDiretorio.Administrador, false)]
	[InlineData("inventario-admin", NivelDeAcessoAoDiretorio.Usuario, true)]
	[InlineData("financeiro-admin", NivelDeAcessoAoDiretorio.Usuario, true)]
	[InlineData("financeiro-user", NivelDeAcessoAoDiretorio.Usuario, true)]
	public void Decide_PeloNivelMinimo(string role, NivelDeAcessoAoDiretorio minimo, bool deveBloquear)
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false, role);

		new ExigeNivelNoDiretorioAttribute(minimo).OnAuthorization(contexto);

		Bloqueou(contexto).Should().Be(deveBloquear);
	}

	[Fact]
	public void SemRole_Bloqueia()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false);

		new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Usuario).OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Fact]
	public void ModoAbertoDeDev_Libera()
	{
		var contexto = Contexto("Development", autenticacaoConfigurada: false);

		new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Administrador).OnAuthorization(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public void Testing_NuncaTemBypass()
	{
		var contexto = Contexto("Testing", autenticacaoConfigurada: false);

		new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Usuario).OnAuthorization(contexto);

		Bloqueou(contexto).Should().BeTrue();
	}

	[Fact]
	public void Order_RodaAntesDoFiltroAntifalsificacao()
	{
		new ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio.Usuario).Order.Should().BeLessThan(1000);
	}
}
```

```csharp
// src/Secco.Intranet.Web/Authentication/ExigeNivelNoDiretorioAttribute.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Secco.Intranet.Web.Navigation;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Exige um nível mínimo de acesso ao Diretório (<see cref="AcessoAoDiretorio.Nivel"/>). Aplicado
/// na classe do controller (nível <c>Usuario</c>) e nas actions administrativas (nível
/// <c>Administrador</c>): nenhuma rota nova nasce aberta. Mesmo desenho do
/// <see cref="SomenteIntranetAdminAttribute"/> — <c>Order</c> menor que o do antifalsificação, para
/// o <c>POST</c> sem permissão dar 403 e não 400.
/// </summary>
/// <param name="minimo">Nível mínimo exigido.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ExigeNivelNoDiretorioAttribute(NivelDeAcessoAoDiretorio minimo) : Attribute, IAuthorizationFilter, IOrderedFilter
{
	/// <inheritdoc />
	public int Order => int.MinValue;

	/// <inheritdoc />
	public void OnAuthorization(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var servicos = context.HttpContext.RequestServices;
		var ambiente = (IWebHostEnvironment)servicos.GetService(typeof(IWebHostEnvironment))!;
		var configuracao = (IConfiguration)servicos.GetService(typeof(IConfiguration))!;

		if (AcessoAdministrativo.ModoAbertoDeDev(ambiente, configuracao)
			|| AcessoAoDiretorio.TemNivel(context.HttpContext.User, minimo))
		{
			return;
		}

		context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
	}
}
```

- [ ] **Step 4: Perfis do produto**

Em `ClassificacaoDePerfil.cs`, acrescente as constantes e amplie a lista:

```csharp
	/// <summary>Administrador do Diretório organizacional.</summary>
	public const string DiretorioAdmin = "diretorio-admin";

	/// <summary>Usuário do Diretório organizacional (ver e editar o próprio contato).</summary>
	public const string DiretorioUsuario = "diretorio-user";
```

e troque a lista por:

```csharp
	public static readonly IReadOnlyList<string> PerfisDoProduto =
		[IntranetAdmin, InventarioAdmin, DiretorioAdmin, DiretorioUsuario];
```

Em `ClassificacaoDePerfilTests.cs`, acrescente `[InlineData("diretorio-admin", TipoDePerfil.Produto)]` e `[InlineData("diretorio-user", TipoDePerfil.Produto)]` à teoria de `Tipo`, e `[InlineData("diretorio-user")]` à de `DoSetor_NaoEDeSetor_Nulo`.

Em `LeituraDeAcessoHandlersTests.cs`, dois testes assumiam só dois perfis do produto — ajuste:

- `ListarPerfis_OrdenaPorNome_EApontaOsPerfisDoProdutoQueFaltam`: a última asserção passa a ser `resultado.Value.PerfisDoProdutoFaltando.Should().BeEquivalentTo("inventario-admin", "diretorio-admin", "diretorio-user");`
- `ListarPerfis_ProdutoFaltandoIgnoraCaixa`: crie também `.ComPerfil("Diretorio-Admin").ComPerfil("DIRETORIO-USER")` e mantenha a asserção de `BeEmpty`.

- [ ] **Step 5: `X-Test-User` no middleware de teste**

Substitua o corpo de `RolesDeTesteMiddleware.cs` por:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Tests.Integration.TestAuthentication;

/// <summary>
/// Simula um usuário autenticado, lendo os headers <see cref="Header"/> (roles separadas por
/// vírgula) e <see cref="HeaderUsuario"/> (o claim <c>sub</c>, um Guid). Existe só nos testes: o
/// pipeline real nunca registra autenticação no ambiente <c>Testing</c> (ver
/// <c>IntranetWebFactory</c>), então esta é a única forma de exercitar autorização por role — e
/// "quem sou eu" — através do host HTTP real. Sem nenhum dos dois headers não faz nada.
/// </summary>
/// <param name="next">Próximo middleware do pipeline.</param>
public sealed class RolesDeTesteMiddleware(RequestDelegate next)
{
	/// <summary>Header lido: roles separadas por vírgula.</summary>
	public const string Header = "X-Test-Roles";

	/// <summary>Header lido: id do usuário (claim <c>sub</c>).</summary>
	public const string HeaderUsuario = "X-Test-User";

	/// <summary>Processa a requisição.</summary>
	/// <param name="context">Contexto HTTP da requisição atual.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		var valorRoles = context.Request.Headers[Header].ToString();
		var valorUsuario = context.Request.Headers[HeaderUsuario].ToString();

		if (!string.IsNullOrWhiteSpace(valorRoles) || !string.IsNullOrWhiteSpace(valorUsuario))
		{
			var claims = new List<Claim>();

			foreach (var role in valorRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				claims.Add(new Claim(SeccoClaims.Role, role));
			}

			if (!string.IsNullOrWhiteSpace(valorUsuario))
			{
				claims.Add(new Claim(SeccoClaims.Subject, valorUsuario.Trim()));
			}

			context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Teste"));
		}

		await next(context).ConfigureAwait(false);
	}
}
```

Acrescente a `RolesDeTesteMiddlewareTests`:

```csharp
	[Fact]
	public async Task ComHeaderDeUsuario_DefineOClaimSub()
	{
		var id = Guid.NewGuid();
		var context = new DefaultHttpContext();
		context.Request.Headers[RolesDeTesteMiddleware.HeaderUsuario] = id.ToString();
		var middleware = new RolesDeTesteMiddleware(_ => Task.CompletedTask);

		await middleware.InvokeAsync(context);

		context.User.FindFirst(SeccoClaims.Subject)!.Value.Should().Be(id.ToString());
		context.User.FindAll(SeccoClaims.Role).Should().BeEmpty();
	}
```

- [ ] **Step 6: Rodar tudo e commitar**

Run: `dotnet test` → PASS, 0 avisos.

```bash
git add src/Secco.Intranet.Web/Navigation/AcessoAoDiretorio.cs src/Secco.Intranet.Web/Authentication/ExigeNivelNoDiretorioAttribute.cs src/Secco.Intranet.Application/Acesso/ClassificacaoDePerfil.cs tests/Secco.Intranet.Tests/Integration/TestAuthentication/RolesDeTesteMiddleware.cs tests/Secco.Intranet.Tests/Unit/RolesDeTesteMiddlewareTests.cs tests/Secco.Intranet.Tests/Unit/LeituraDeAcessoHandlersTests.cs tests/Secco.Intranet.Tests/Unit/ClassificacaoDePerfilTests.cs tests/Secco.Intranet.Tests/Unit/AcessoAoDiretorioTests.cs tests/Secco.Intranet.Tests/Unit/ExigeNivelNoDiretorioAttributeTests.cs
git commit -m "feat(diretorio): nivel de acesso, gate declarativo e perfis diretorio-admin/diretorio-user

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 4: Usuários para o diretório, adaptadores e seeder de DEV

`IUsuariosParaDiretorio` devolve os usuários **ativos** do tenant como `Result` (falha não vira lista vazia) e é a única porta de identidade do diretório. Não mexe em `IDiretorioDeUsuarios` nem em `UsuarioDoTenant` (Inventário e notificação seguem como estão).

**Files:**
- Create: `src/Secco.Intranet.Application/Diretorio/IUsuariosParaDiretorio.cs`
- Create: `src/Secco.Intranet.Infrastructure/Diretorio/UsuariosParaDiretorioDoSecureGate.cs`
- Create: `src/Secco.Intranet.Infrastructure/Diretorio/UsuariosParaDiretorioIndisponivel.cs`
- Create: `src/Secco.Intranet.Infrastructure/Diretorio/PessoasDeDesenvolvimento.cs`
- Create: `src/Secco.Intranet.Infrastructure/Diretorio/UsuariosParaDiretorioDeDesenvolvimento.cs`
- Create: `src/Secco.Intranet.Infrastructure/Seeding/DiretorioDesenvolvimentoSeeder.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`
- Create: `tests/Secco.Intranet.Tests/Support/DublesDeDiretorio.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/UsuariosParaDiretorioTests.cs`
- Create: `tests/Secco.Intranet.Tests/Integration/DiretorioDesenvolvimentoSeederTests.cs`

**Interfaces:**
- Consumes: `IGestaoDeAcesso.ListarUsuariosAsync()` e `UsuarioDto`/`SituacaoDoUsuario` (área de acesso); `IPerfilColaboradorRepository`/`PerfilColaborador` (Tasks 1–2).
- Produces:
  - `record UsuarioParaDiretorio(Guid Id, string Email)`; `IUsuariosParaDiretorio.ListarAtivosAsync(CancellationToken) : Task<Result<IReadOnlyList<UsuarioParaDiretorio>>>`.
  - `UsuariosParaDiretorioDoSecureGate(IGestaoDeAcesso, IMemoryCache, ITenantContext)`: filtra `Situacao != Desativado`, cache de 60 s por tenant **só em sucesso**; `UsuariosParaDiretorioIndisponivel` (público; devolve `IntranetErrors.Acesso.NaoConfigurado`); `UsuariosParaDiretorioDeDesenvolvimento` (devolve `PessoasDeDesenvolvimento.Todas`).
  - Dublês em `Secco.Intranet.Tests.Support`: `UsuariosParaDiretorioFalso`, `PerfisColaboradorFalso`, `SetoresFalsos`.

- [ ] **Step 1: A porta**

```csharp
// src/Secco.Intranet.Application/Diretorio/IUsuariosParaDiretorio.cs
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Usuário ativo do tenant, como o diretório precisa dele — só identidade.</summary>
/// <param name="Id">Id no SecureGate.</param>
/// <param name="Email">E-mail (o SecureGate não guarda nome).</param>
public sealed record UsuarioParaDiretorio(Guid Id, string Email);

/// <summary>
/// Fonte de identidade do diretório: os usuários <b>ativos</b> do tenant atual. Falha de
/// infraestrutura volta como <see cref="Result"/> — uma lista vazia silenciosa faria o diretório
/// parecer vazio quando o SecureGate só está fora do ar.
/// </summary>
public interface IUsuariosParaDiretorio
{
	/// <summary>Lista os usuários ativos do tenant atual.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Dublês de teste compartilhados**

```csharp
// tests/Secco.Intranet.Tests/Support/DublesDeDiretorio.cs
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Tests.Support;

/// <summary>Usuários ativos em memória.</summary>
public sealed class UsuariosParaDiretorioFalso : IUsuariosParaDiretorio
{
	/// <summary>Usuários ativos devolvidos.</summary>
	public List<UsuarioParaDiretorio> Usuarios { get; } = [];

	/// <summary>Quando não nulo, a listagem falha com este erro.</summary>
	public Error? FalharCom { get; set; }

	/// <summary>Quantas vezes a lista foi pedida.</summary>
	public int Chamadas { get; private set; }

	/// <summary>Acrescenta um usuário ativo.</summary>
	public UsuariosParaDiretorioFalso Com(Guid id, string email)
	{
		Usuarios.Add(new UsuarioParaDiretorio(id, email));

		return this;
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(CancellationToken cancellationToken = default)
	{
		Chamadas++;

		IReadOnlyList<UsuarioParaDiretorio> lista = [.. Usuarios];

		return Task.FromResult(FalharCom is null
			? Result.Success(lista)
			: Result.Failure<IReadOnlyList<UsuarioParaDiretorio>>(FalharCom));
	}
}

/// <summary>Repositório de perfis em memória.</summary>
public sealed class PerfisColaboradorFalso : IPerfilColaboradorRepository
{
	/// <summary>Perfis existentes.</summary>
	public List<PerfilColaborador> Perfis { get; } = [];

	/// <summary>Quantas vezes <see cref="SaveChangesAsync"/> foi chamado.</summary>
	public int Salvou { get; private set; }

	/// <summary>Acrescenta um perfil já pronto.</summary>
	public PerfisColaboradorFalso Com(PerfilColaborador perfil)
	{
		Perfis.Add(perfil);

		return this;
	}

	/// <inheritdoc />
	public Task<PerfilColaborador?> GetByUsuarioIdAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Perfis.FirstOrDefault(perfil => perfil.UsuarioId == usuarioId));

	/// <inheritdoc />
	public Task<PerfilColaborador?> GetParaEdicaoAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		GetByUsuarioIdAsync(usuarioId, cancellationToken);

	/// <inheritdoc />
	public Task<IReadOnlyList<PerfilColaborador>> ListarTodosAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<PerfilColaborador>>([.. Perfis]);

	/// <inheritdoc />
	public Task<bool> TentarAdicionarAsync(PerfilColaborador perfil, CancellationToken cancellationToken = default)
	{
		if (Perfis.Any(existente => existente.UsuarioId == perfil.UsuarioId))
		{
			return Task.FromResult(false);
		}

		Perfis.Add(perfil);
		Salvou++;

		return Task.FromResult(true);
	}

	/// <inheritdoc />
	public Task SaveChangesAsync(CancellationToken cancellationToken = default)
	{
		Salvou++;

		return Task.CompletedTask;
	}
}

/// <summary>Setores em memória; só a leitura que o diretório usa.</summary>
public sealed class SetoresFalsos : ISetorRepository
{
	/// <summary>Setores existentes.</summary>
	public List<Setor> Setores { get; } = [];

	/// <summary>Acrescenta um setor; <paramref name="ativo"/> falso o desativa.</summary>
	public Setor Com(string nome, string slug, bool ativo = true)
	{
		var setor = new Setor(nome, slug);

		if (!ativo)
		{
			setor.Desativar();
		}

		Setores.Add(setor);

		return setor;
	}

	/// <inheritdoc />
	public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		Task.FromResult(Setores.FirstOrDefault(setor => setor.Id == id));

	/// <inheritdoc />
	public Task<PagedResult<Setor>> SearchAsync(SetorSearchCriteria criteria, CancellationToken cancellationToken = default)
	{
		IReadOnlyList<Setor> itens = [.. Setores.Where(setor => !criteria.ApenasAtivos || setor.Ativo)];

		return Task.FromResult(PagedResult.Create(itens, new PageRequest(1, 200), itens.Count));
	}

	/// <inheritdoc />
	public Task AddAsync(Setor setor, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	/// <inheritdoc />
	public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
		Task.FromResult(Setores.FirstOrDefault(setor => string.Equals(setor.Slug, slug, StringComparison.OrdinalIgnoreCase)));

	/// <inheritdoc />
	public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	/// <inheritdoc />
	public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	/// <inheritdoc />
	public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
```

Se `ISetorRepository` tiver algum membro além destes oito, implemente-o também lançando `NotSupportedException` — a interface é a que está em `src/Secco.Intranet.Application/Setores/ISetorRepository.cs`.

- [ ] **Step 3: Testes dos adaptadores (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/UsuariosParaDiretorioTests.cs
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Infrastructure.Diretorio;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class UsuariosParaDiretorioTests
{
	private static readonly Guid Tenant = Guid.NewGuid();
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();

	private sealed class TenantContextFalso(Guid? tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => tenantId is not null;
	}

	private static UsuariosParaDiretorioDoSecureGate Montar(IGestaoDeAcesso gestao, IMemoryCache? cache = null, Guid? tenant = null) =>
		new(gestao, cache ?? new MemoryCache(new MemoryCacheOptions()), new TenantContextFalso(tenant ?? Tenant));

	[Fact]
	public async Task ExcluiOsDesativados_EMantemBloqueados()
	{
		var gestao = new GestaoDeAcessoFalsa()
			.ComUsuario(Ana, "ana@x.com")
			.ComUsuario(Bruno, "bruno@x.com", SituacaoDoUsuario.Desativado)
			.ComUsuario(Carla, "carla@x.com", SituacaoDoUsuario.Bloqueado);

		var resultado = await Montar(gestao).ListarAtivosAsync();

		resultado.Value.Select(u => u.Id).Should().BeEquivalentTo([Ana, Carla],
			"bloqueio por tentativas é temporário; só desativado sai do diretório");
	}

	[Fact]
	public async Task GuardaEmCache_ESoConsultaAPlataformaUmaVez()
	{
		var gestao = new ContadorDeListagens().ComUsuario(Ana, "ana@x.com");
		var adaptador = Montar(gestao);

		await adaptador.ListarAtivosAsync();
		await adaptador.ListarAtivosAsync();

		gestao.Listagens.Should().Be(1);
	}

	[Fact]
	public async Task NaoGuardaFalhaEmCache()
	{
		var gestao = new ContadorDeListagens { ErroDeListagem = IntranetErrors.Acesso.Indisponivel };
		var adaptador = Montar(gestao);

		var primeira = await adaptador.ListarAtivosAsync();
		gestao.ErroDeListagem = null;
		gestao.ComUsuario(Ana, "ana@x.com");
		var segunda = await adaptador.ListarAtivosAsync();

		primeira.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		segunda.IsSuccess.Should().BeTrue("uma falha não pode ficar 60 s escondendo um diretório que voltou");
		segunda.Value.Should().ContainSingle();
	}

	[Fact]
	public async Task CacheEPorTenant()
	{
		var gestao = new ContadorDeListagens().ComUsuario(Ana, "ana@x.com");
		var cache = new MemoryCache(new MemoryCacheOptions());

		await Montar(gestao, cache, Guid.NewGuid()).ListarAtivosAsync();
		await Montar(gestao, cache, Guid.NewGuid()).ListarAtivosAsync();

		gestao.Listagens.Should().Be(2);
	}

	[Fact]
	public async Task TenantNaoResolvido_Falha_SemChamarAPlataforma()
	{
		var gestao = new ContadorDeListagens();
		var adaptador = new UsuariosParaDiretorioDoSecureGate(gestao, new MemoryCache(new MemoryCacheOptions()), new TenantContextFalso(null));

		var resultado = await adaptador.ListarAtivosAsync();

		resultado.IsFailure.Should().BeTrue();
		gestao.Listagens.Should().Be(0);
	}

	[Fact]
	public async Task Indisponivel_ResponderNaoConfigurado()
	{
		var resultado = await new UsuariosParaDiretorioIndisponivel().ListarAtivosAsync();

		resultado.Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
	}

	[Fact]
	public async Task DeDesenvolvimento_DevolveAsPessoasFicticias()
	{
		var resultado = await new UsuariosParaDiretorioDeDesenvolvimento().ListarAtivosAsync();

		resultado.Value.Should().HaveCount(PessoasDeDesenvolvimento.Todas.Count);
		resultado.Value.Select(u => u.Email).Should().OnlyContain(email => email.EndsWith("@exemplo.local"));
	}

	/// <summary>Conta quantas vezes a plataforma foi consultada e permite forçar falha na listagem.</summary>
	private sealed class ContadorDeListagens : IGestaoDeAcesso
	{
		private readonly GestaoDeAcessoFalsa _interno = new();

		public int Listagens { get; private set; }

		public Secco.SharedKernel.Results.Error? ErroDeListagem { get; set; }

		public ContadorDeListagens ComUsuario(Guid id, string email)
		{
			_interno.ComUsuario(id, email);

			return this;
		}

		public Task<Secco.SharedKernel.Results.Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(CancellationToken cancellationToken = default)
		{
			Listagens++;

			return ErroDeListagem is null
				? _interno.ListarUsuariosAsync(cancellationToken)
				: Task.FromResult(Secco.SharedKernel.Results.Result.Failure<IReadOnlyList<UsuarioDto>>(ErroDeListagem));
		}

		public Task<Secco.SharedKernel.Results.Result<IReadOnlyList<PerfilDto>>> ListarPerfisAsync(CancellationToken cancellationToken = default) => _interno.ListarPerfisAsync(cancellationToken);

		public Task<Secco.SharedKernel.Results.Result<PerfilDetalheDto>> ObterPerfilAsync(string nome, CancellationToken cancellationToken = default) => _interno.ObterPerfilAsync(nome, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result<PaginaDeMembros>> ListarMembrosAsync(string nome, int pagina, CancellationToken cancellationToken = default) => _interno.ListarMembrosAsync(nome, pagina, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result<UsuarioDetalheDto>> ObterUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) => _interno.ObterUsuarioAsync(usuarioId, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> CriarPerfilAsync(string nome, CancellationToken cancellationToken = default) => _interno.CriarPerfilAsync(nome, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> ExcluirPerfilAsync(string nome, CancellationToken cancellationToken = default) => _interno.ExcluirPerfilAsync(nome, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> AtribuirPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) => _interno.AtribuirPerfilAsync(usuarioId, perfil, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> RetirarPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) => _interno.RetirarPerfilAsync(usuarioId, perfil, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> DesativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) => _interno.DesativarUsuarioAsync(usuarioId, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> ReativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) => _interno.ReativarUsuarioAsync(usuarioId, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> EncerrarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken = default) => _interno.EncerrarSessoesAsync(usuarioId, cancellationToken);
	}
}
```

(`Montar` recebe `IGestaoDeAcesso`, então serve tanto à `GestaoDeAcessoFalsa` quanto ao `ContadorDeListagens`.)

- [ ] **Step 4: Implementar os adaptadores**

```csharp
// src/Secco.Intranet.Infrastructure/Diretorio/UsuariosParaDiretorioDoSecureGate.cs
using Microsoft.Extensions.Caching.Memory;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Diretorio;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>
/// Usuários ativos do tenant, vindos do SecureGate pela mesma porta da gestão de acesso (que já
/// devolve a situação da conta e falha como <see cref="Result"/>). <c>ListUsers</c> devolve o
/// tenant inteiro e não pagina, então o resultado fica em cache por 60 s por tenant —
/// <b>só quando dá certo</b>: uma falha cacheada esconderia por um minuto um diretório que voltou.
/// </summary>
/// <param name="gestao">Porta da gestão de acesso.</param>
/// <param name="cache">Cache em memória.</param>
/// <param name="tenantContext">Tenant da requisição atual (ADR-0005).</param>
public sealed class UsuariosParaDiretorioDoSecureGate(
	IGestaoDeAcesso gestao,
	IMemoryCache cache,
	ITenantContext tenantContext) : IUsuariosParaDiretorio
{
	private static readonly TimeSpan Validade = TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	public async Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(
		CancellationToken cancellationToken = default)
	{
		if (!tenantContext.IsResolved)
		{
			return Result.Failure<IReadOnlyList<UsuarioParaDiretorio>>(IntranetErrors.Acesso.Indisponivel);
		}

		var chave = $"diretorio:usuarios:{tenantContext.TenantId}";

		if (cache.TryGetValue(chave, out IReadOnlyList<UsuarioParaDiretorio>? guardada) && guardada is not null)
		{
			return Result.Success(guardada);
		}

		var lida = await gestao.ListarUsuariosAsync(cancellationToken).ConfigureAwait(false);

		if (lida.IsFailure)
		{
			return Result.Failure<IReadOnlyList<UsuarioParaDiretorio>>(lida.Error);
		}

		IReadOnlyList<UsuarioParaDiretorio> ativos =
		[
			.. lida.Value
				.Where(usuario => usuario.Situacao != SituacaoDoUsuario.Desativado)
				.Select(usuario => new UsuarioParaDiretorio(usuario.Id, usuario.Email ?? string.Empty)),
		];

		cache.Set(chave, ativos, Validade);

		return Result.Success(ativos);
	}
}
```

```csharp
// src/Secco.Intranet.Infrastructure/Diretorio/UsuariosParaDiretorioIndisponivel.cs
using Secco.Intranet.Application;
using Secco.Intranet.Application.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>
/// No-op para ambientes sem SecureGate e sem modo de desenvolvimento (Testing, produção sem a
/// seção). Responde "não configurado": as telas explicam, em vez de mostrar um diretório vazio.
/// </summary>
public sealed class UsuariosParaDiretorioIndisponivel : IUsuariosParaDiretorio
{
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<UsuarioParaDiretorio>>(IntranetErrors.Acesso.NaoConfigurado));
}
```

```csharp
// src/Secco.Intranet.Infrastructure/Diretorio/PessoasDeDesenvolvimento.cs
namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>
/// Colaboradores fictícios de desenvolvimento (ADR-0019). Ids fixos e determinísticos: o seeder
/// grava os perfis com estes ids, e o adaptador de DEV os devolve como usuários do tenant.
/// Substitui a demonstração estática: só existe em <c>Development</c> sem SecureGate.
/// </summary>
internal static class PessoasDeDesenvolvimento
{
	public sealed record Pessoa(
		Guid Id, string Email, string Nome, string Cargo, string Ramal, string SetorSlug, Guid? GestorId);

	public static readonly Guid AnaId = Guid.Parse("0dee0000-0000-7000-8000-000000000001");
	public static readonly Guid HenriqueId = Guid.Parse("0dee0000-0000-7000-8000-000000000008");
	public static readonly Guid CamilaId = Guid.Parse("0dee0000-0000-7000-8000-000000000003");
	public static readonly Guid GabrielaId = Guid.Parse("0dee0000-0000-7000-8000-000000000007");

	public static readonly IReadOnlyList<Pessoa> Todas =
	[
		new(AnaId, "ana.ribeiro@exemplo.local", "Ana Ribeiro", "Diretora de Operações", "2100", "diretoria", null),
		new(Guid.Parse("0dee0000-0000-7000-8000-000000000002"), "bruno.tavares@exemplo.local", "Bruno Tavares", "Analista de Infraestrutura", "2210", "infraestrutura", HenriqueId),
		new(CamilaId, "camila.nunes@exemplo.local", "Camila Nunes", "Coordenadora de Pessoas", "2305", "recursos-humanos", AnaId),
		new(Guid.Parse("0dee0000-0000-7000-8000-000000000004"), "diego.prado@exemplo.local", "Diego Prado", "Analista Financeiro", "2412", "financeiro", GabrielaId),
		new(Guid.Parse("0dee0000-0000-7000-8000-000000000005"), "elisa.moraes@exemplo.local", "Elisa Moraes", "Especialista em Segurança", "2215", "infraestrutura", HenriqueId),
		new(Guid.Parse("0dee0000-0000-7000-8000-000000000006"), "felipe.andrade@exemplo.local", "Felipe Andrade", "Analista de Benefícios", "2310", "recursos-humanos", CamilaId),
		new(GabrielaId, "gabriela.lopes@exemplo.local", "Gabriela Lopes", "Controller", "2405", "financeiro", AnaId),
		new(HenriqueId, "henrique.salles@exemplo.local", "Henrique Salles", "Gerente de Tecnologia", "2201", "infraestrutura", AnaId),
	];
}
```

```csharp
// src/Secco.Intranet.Infrastructure/Diretorio/UsuariosParaDiretorioDeDesenvolvimento.cs
using Secco.Intranet.Application.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>
/// Adaptador de DEV: expõe as pessoas fictícias como usuários do tenant. Só é escolhido em
/// <c>Development</c> <b>e</b> sem SecureGate configurado — nunca serve gente inventada numa
/// intranet em uso, que era o risco que o flag de demonstração existia para conter.
/// </summary>
public sealed class UsuariosParaDiretorioDeDesenvolvimento : IUsuariosParaDiretorio
{
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<UsuarioParaDiretorio> usuarios =
			[.. PessoasDeDesenvolvimento.Todas.Select(pessoa => new UsuarioParaDiretorio(pessoa.Id, pessoa.Email))];

		return Task.FromResult(Result.Success(usuarios));
	}
}
```

`PessoasDeDesenvolvimento` é `internal`; o teste a alcança por `InternalsVisibleTo` (já declarado na Infrastructure). Se `UsuariosParaDiretorioDeDesenvolvimento` (público) compilar reclamando de acessibilidade, torne-a `internal`, e o teste continua alcançando-a.

- [ ] **Step 5: Seeder de desenvolvimento**

```csharp
// src/Secco.Intranet.Infrastructure/Seeding/DiretorioDesenvolvimentoSeeder.cs
using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.Intranet.Infrastructure.Diretorio;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Seeding;

namespace Secco.Intranet.Infrastructure.Seeding;

/// <summary>
/// Perfis de colaborador de amostra para navegar em desenvolvimento (ADR-0019). Idempotente:
/// grava só os usuários fictícios que ainda não têm perfil. Roda depois do seeder de setores
/// (a lotação aponta para o setor pelo slug).
/// </summary>
/// <param name="catalog">Catálogo de tenants.</param>
/// <param name="databaseOptions">Engine dos bancos de tenant.</param>
internal sealed class DiretorioDesenvolvimentoSeeder(
	ITenantCatalog catalog,
	IntranetDatabaseOptions databaseOptions) : IDevelopmentDataSeeder
{
	/// <summary>Depois dos setores (Order 0) e antes das publicações (Order 10).</summary>
	public int Order => 5;

	/// <summary>Aplica o seed em cada tenant do catálogo.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			var options = IntranetDatabaseProviderConfigurator.CreateOptions(
				databaseOptions.Provider, tenant.ConnectionString);

			await using var context = new IntranetDbContext(options);

			var existentes = await context.PerfisColaboradores
				.Select(perfil => perfil.UsuarioId)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			var setores = await context.Setores
				.ToDictionaryAsync(setor => setor.Slug, setor => setor.Id, StringComparer.OrdinalIgnoreCase, cancellationToken)
				.ConfigureAwait(false);

			var novos = PessoasDeDesenvolvimento.Todas.Where(pessoa => !existentes.Contains(pessoa.Id)).ToList();

			if (novos.Count == 0)
			{
				continue;
			}

			foreach (var pessoa in novos)
			{
				var perfil = new PerfilColaborador(pessoa.Id);
				perfil.EditarContato(pessoa.Nome, pessoa.Ramal, null);
				perfil.EditarDadosFuncionais(
					pessoa.Cargo,
					setores.TryGetValue(pessoa.SetorSlug, out var setorId) ? setorId : null,
					pessoa.GestorId);

				context.PerfisColaboradores.Add(perfil);
			}

			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
```

- [ ] **Step 6: Composição por ambiente**

Em `IntranetInfrastructureExtensions.cs`, acrescente `using Secco.Intranet.Infrastructure.Diretorio;` e `using Microsoft.Extensions.Caching.Memory;` se faltarem, e:

```csharp
		services.AddMemoryCache();
		services.AddScoped<IDevelopmentDataSeeder, DiretorioDesenvolvimentoSeeder>();
		services.AddScoped<IUsuariosParaDiretorio>(CriarUsuariosParaDiretorio);
```

e o método, junto de `CriarGestaoDeAcesso`:

```csharp
	/// <summary>
	/// Escolhe a fonte de identidade do diretório: SecureGate configurado → o real; senão, em
	/// <c>Development</c>, as pessoas fictícias; senão, "não configurado".
	/// </summary>
	private static IUsuariosParaDiretorio CriarUsuariosParaDiretorio(IServiceProvider serviceProvider)
	{
		var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

		if (credenciais.IsConfigured)
		{
			return ActivatorUtilities.CreateInstance<UsuariosParaDiretorioDoSecureGate>(serviceProvider);
		}

		return serviceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment()
			? new UsuariosParaDiretorioDeDesenvolvimento()
			: new UsuariosParaDiretorioIndisponivel();
	}
```

Se o analisador reclamar da complexidade de `AddIntranetInfrastructure`, mova esses três registros para um método privado `AdicionarDiretorio(IServiceCollection)` chamado dali.

- [ ] **Step 7: Teste do seeder (integração)**

```csharp
// tests/Secco.Intranet.Tests/Integration/DiretorioDesenvolvimentoSeederTests.cs
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Infrastructure;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.Intranet.Infrastructure.Diretorio;
using Secco.Intranet.Infrastructure.Seeding;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

public class DiretorioDesenvolvimentoSeederTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public async Task DisposeAsync() => await LimparAsync();

	private async Task<IntranetDbContext> AbrirAsync()
	{
		using var escopo = factory.Services.CreateScope();
		var catalogo = escopo.ServiceProvider.GetRequiredService<ITenantCatalog>();
		var opcoes = escopo.ServiceProvider.GetRequiredService<IntranetDatabaseOptions>();
		var tenant = (await catalogo.ListAsync()).First(t => t.TenantId == factory.TenantAlfa);

		return new IntranetDbContext(IntranetDatabaseProviderConfigurator.CreateOptions(opcoes.Provider, tenant.ConnectionString));
	}

	private async Task LimparAsync()
	{
		await using var contexto = await AbrirAsync();
		var ids = PessoasDeDesenvolvimento.Todas.Select(p => p.Id).ToList();
		await contexto.PerfisColaboradores.Where(perfil => ids.Contains(perfil.UsuarioId)).ExecuteDeleteAsync();
	}

	[Fact]
	public async Task Seed_GravaAsPessoasEEIdempotente()
	{
		using var escopo = factory.Services.CreateScope();
		var seeder = new DiretorioDesenvolvimentoSeeder(
			escopo.ServiceProvider.GetRequiredService<ITenantCatalog>(),
			escopo.ServiceProvider.GetRequiredService<IntranetDatabaseOptions>());

		await seeder.SeedAsync();
		await seeder.SeedAsync();

		await using var contexto = await AbrirAsync();
		var ids = PessoasDeDesenvolvimento.Todas.Select(p => p.Id).ToList();
		var gravados = await contexto.PerfisColaboradores.Where(perfil => ids.Contains(perfil.UsuarioId)).ToListAsync();

		gravados.Should().HaveCount(PessoasDeDesenvolvimento.Todas.Count, "rodar duas vezes não duplica");
		gravados.Single(perfil => perfil.UsuarioId == PessoasDeDesenvolvimento.AnaId).NomeExibicao.Should().Be("Ana Ribeiro");
		gravados.Single(perfil => perfil.UsuarioId == PessoasDeDesenvolvimento.CamilaId).GestorUsuarioId
			.Should().Be(PessoasDeDesenvolvimento.AnaId);
	}
}
```

Se `IntranetDatabaseOptions`/`IntranetDatabaseProviderConfigurator` forem `internal` e o compilador reclamar, o `InternalsVisibleTo` da Infrastructure já cobre o projeto de testes (é como `SetoresDesenvolvimentoSeeder` é alcançado hoje); confira o namespace exato desses tipos com `grep -rn "class IntranetDatabaseOptions" src`.

- [ ] **Step 8: Rodar e commitar**

Run: `dotnet test --filter "UsuariosParaDiretorioTests|DiretorioDesenvolvimentoSeederTests"` → PASS; `dotnet build` 0 avisos; `dotnet test` (suíte toda) verde.

```bash
git add src/Secco.Intranet.Application/Diretorio src/Secco.Intranet.Infrastructure/Diretorio src/Secco.Intranet.Infrastructure/Seeding/DiretorioDesenvolvimentoSeeder.cs src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs tests/Secco.Intranet.Tests/Support/DublesDeDiretorio.cs tests/Secco.Intranet.Tests/Unit/UsuariosParaDiretorioTests.cs tests/Secco.Intranet.Tests/Integration/DiretorioDesenvolvimentoSeederTests.cs
git commit -m "feat(diretorio): fonte de usuarios ativos com cache, adaptadores e seeder de DEV

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

## Task 5: Application — montagem de pessoas e handlers de leitura

**Files:**
- Create: `src/Secco.Intranet.Application/Diretorio/PessoaDto.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/MontadorDePessoas.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/ListarPessoasHandler.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/ObterPessoaHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/LeituraDoDiretorioHandlersTests.cs`

**Interfaces:**
- Consumes: `IUsuariosParaDiretorio`, `IPerfilColaboradorRepository`, `ISetorRepository` (`SearchAsync(SetorSearchCriteria)`), `SetorDto`.
- Produces:
  - `PessoaDto(Guid UsuarioId, string Email, string Nome, string? Cargo, string? Ramal, string? Sobre, Guid? SetorId, string? SetorNome, string? SetorSlug, bool SetorAtivo, Guid? GestorUsuarioId, string? GestorNome, bool GestorInativo, bool TemPerfil)`. `Nome` cai no e-mail e, se este for vazio, no id.
  - `MontadorDePessoas.Montar(IReadOnlyList<UsuarioParaDiretorio>, IReadOnlyList<PerfilColaborador>, IReadOnlyList<SetorDto>) : IReadOnlyList<PessoaDto>` (só usuários ativos; perfil de quem não está na lista é ignorado).
  - `ListarPessoasHandler.HandleAsync(ListarPessoasQuery(string? Busca, string? SetorSlug, int Pagina), ct) : Task<Result<PessoasDaTelaDto>>` com `PessoasDaTelaDto(PagedResult<PessoaDto> Pagina, IReadOnlyList<SetorDto> Setores)` (24 por página, ordenado por nome).
  - `ObterPessoaHandler.HandleAsync(Guid usuarioId, ct) : Task<Result<PessoaDetalheDto>>` com `PessoaDetalheDto(PessoaDto Pessoa, PessoaDto? Gestor, IReadOnlyList<PessoaDto> Equipe)`; `NotFound` (`IntranetErrors.Diretorio.PessoaNaoEncontrada`) se o usuário não está entre os ativos.
  - `IntranetErrors.Diretorio.PessoaNaoEncontrada` (criado aqui; a Task 7 acrescenta os demais).

- [ ] **Step 1: Erro e DTO**

Em `IntranetErrors.cs`, acrescente antes do `}` final da classe:

```csharp
	/// <summary>Erros do Diretório organizacional.</summary>
	public static class Diretorio
	{
		/// <summary>Usuário inexistente ou desativado — não aparece no diretório.</summary>
		public static readonly Error PessoaNaoEncontrada =
			Error.NotFound("Intranet.Diretorio.PessoaNaoEncontrada", "Pessoa não encontrada.");
	}
```

```csharp
// src/Secco.Intranet.Application/Diretorio/PessoaDto.cs
namespace Secco.Intranet.Application.Diretorio;

/// <summary>Uma pessoa do diretório: identidade do SecureGate mais o perfil complementar local.</summary>
/// <param name="UsuarioId">Id no SecureGate.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição; sem perfil, o e-mail (e, sem e-mail, o id).</param>
/// <param name="Cargo">Cargo.</param>
/// <param name="Ramal">Ramal.</param>
/// <param name="Sobre">Texto livre.</param>
/// <param name="SetorId">Setor de lotação.</param>
/// <param name="SetorNome">Nome do setor.</param>
/// <param name="SetorSlug">Slug do setor (define a cor).</param>
/// <param name="SetorAtivo">Se o setor de lotação está ativo (esmaece o badge quando não).</param>
/// <param name="GestorUsuarioId">Id do gestor, quando definido.</param>
/// <param name="GestorNome">Nome do gestor, quando ele é um usuário ativo.</param>
/// <param name="GestorInativo">Verdadeiro se há gestor definido mas ele não é mais um usuário ativo.</param>
/// <param name="TemPerfil">Se existe perfil local para a pessoa.</param>
public sealed record PessoaDto(
	Guid UsuarioId,
	string Email,
	string Nome,
	string? Cargo,
	string? Ramal,
	string? Sobre,
	Guid? SetorId,
	string? SetorNome,
	string? SetorSlug,
	bool SetorAtivo,
	Guid? GestorUsuarioId,
	string? GestorNome,
	bool GestorInativo,
	bool TemPerfil);
```

- [ ] **Step 2: Testes de leitura (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/LeituraDoDiretorioHandlersTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LeituraDoDiretorioHandlersTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();
	private static readonly Guid Desativado = Guid.NewGuid();

	private static PerfilColaborador Perfil(Guid usuario, string? nome, string? cargo = null, Guid? setor = null, Guid? gestor = null)
	{
		var perfil = new PerfilColaborador(usuario);
		perfil.EditarContato(nome, null, null);
		perfil.EditarDadosFuncionais(cargo, setor, gestor);

		return perfil;
	}

	private static (ListarPessoasHandler Listar, ObterPessoaHandler Obter, SetoresFalsos Setores) Montar(
		UsuariosParaDiretorioFalso usuarios, PerfisColaboradorFalso perfis)
	{
		var setores = new SetoresFalsos();

		return (new ListarPessoasHandler(usuarios, perfis, setores), new ObterPessoaHandler(usuarios, perfis, setores), setores);
	}

	[Fact]
	public async Task UsuarioSemPerfil_AparecePeloEmail()
	{
		var (listar, _, _) = Montar(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), new PerfisColaboradorFalso());

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 1));

		var pessoa = resultado.Value.Pagina.Items.Should().ContainSingle().Subject;
		pessoa.Nome.Should().Be("ana@x.com");
		pessoa.TemPerfil.Should().BeFalse();
	}

	[Fact]
	public async Task UsuarioSemEmail_CaiNoId_SemQuebrar()
	{
		var (listar, _, _) = Montar(new UsuariosParaDiretorioFalso().Com(Ana, string.Empty), new PerfisColaboradorFalso());

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 1));

		resultado.Value.Pagina.Items.Single().Nome.Should().Be(Ana.ToString());
	}

	[Fact]
	public async Task PerfilDeUsuarioDesativado_NaoAparece()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com");
		var perfis = new PerfisColaboradorFalso().Com(Perfil(Ana, "Ana Ribeiro")).Com(Perfil(Desativado, "Fantasma"));
		var (listar, _, _) = Montar(usuarios, perfis);

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 1));

		resultado.Value.Pagina.Items.Select(p => p.Nome).Should().Equal("Ana Ribeiro");
	}

	[Fact]
	public async Task OrdenaPorNome_SemDiferenciarCaixa()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "a@x.com").Com(Bruno, "b@x.com").Com(Carla, "c@x.com");
		var perfis = new PerfisColaboradorFalso().Com(Perfil(Ana, "zélia")).Com(Perfil(Bruno, "Bruno")).Com(Perfil(Carla, "ana"));
		var (listar, _, _) = Montar(usuarios, perfis);

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 1));

		resultado.Value.Pagina.Items.Select(p => p.Nome).Should().Equal("ana", "Bruno", "zélia");
	}

	[Fact]
	public async Task Busca_AchaPorNomeCargoSetorEEmail()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@y.com").Com(Carla, "carla@z.com");
		var (listar, _, setores) = Montar(usuarios, new PerfisColaboradorFalso());
		var financeiro = setores.Com("Financeiro", "financeiro");
		var perfis = new PerfisColaboradorFalso()
			.Com(Perfil(Ana, "Ana Ribeiro", "Controller"))
			.Com(Perfil(Bruno, "Bruno", "Analista", financeiro.Id))
			.Com(Perfil(Carla, "Carla"));
		var handler = new ListarPessoasHandler(usuarios, perfis, setores);

		(await handler.HandleAsync(new ListarPessoasQuery("ribeiro", null, 1))).Value.Pagina.Items.Should().ContainSingle();
		(await handler.HandleAsync(new ListarPessoasQuery("controller", null, 1))).Value.Pagina.Items.Should().ContainSingle();
		(await handler.HandleAsync(new ListarPessoasQuery("FINANCEIRO", null, 1))).Value.Pagina.Items.Should().ContainSingle(p => p.UsuarioId == Bruno);
		(await handler.HandleAsync(new ListarPessoasQuery("@z.com", null, 1))).Value.Pagina.Items.Should().ContainSingle(p => p.UsuarioId == Carla);
	}

	[Fact]
	public async Task FiltroPorSetor_RestringePeloSlug()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com");
		var setores = new SetoresFalsos();
		var rh = setores.Com("RH", "rh");
		var perfis = new PerfisColaboradorFalso().Com(Perfil(Ana, "Ana", setor: rh.Id)).Com(Perfil(Bruno, "Bruno"));
		var handler = new ListarPessoasHandler(usuarios, perfis, setores);

		var resultado = await handler.HandleAsync(new ListarPessoasQuery(null, "RH", 1));

		resultado.Value.Pagina.Items.Select(p => p.UsuarioId).Should().Equal(Ana);
		resultado.Value.Setores.Should().ContainSingle(s => s.Slug == "rh");
	}

	[Fact]
	public async Task SetorDesativado_AindaMostraNaLotacao_MasNaoNoFiltro()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com");
		var setores = new SetoresFalsos();
		var antigo = setores.Com("Antigo", "antigo", ativo: false);
		var perfis = new PerfisColaboradorFalso().Com(Perfil(Ana, "Ana", setor: antigo.Id));
		var handler = new ListarPessoasHandler(usuarios, perfis, setores);

		var resultado = await handler.HandleAsync(new ListarPessoasQuery(null, null, 1));

		var pessoa = resultado.Value.Pagina.Items.Single();
		pessoa.SetorNome.Should().Be("Antigo");
		pessoa.SetorAtivo.Should().BeFalse();
		resultado.Value.Setores.Should().BeEmpty("o filtro só oferece setores ativos");
	}

	[Fact]
	public async Task PaginaAlemDoFim_DevolveVazioSemQuebrar()
	{
		var (listar, _, _) = Montar(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), new PerfisColaboradorFalso());

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 99));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Pagina.Items.Should().BeEmpty();
		resultado.Value.Pagina.TotalCount.Should().Be(1);
	}

	[Fact]
	public async Task PaginaTem24()
	{
		var usuarios = new UsuariosParaDiretorioFalso();

		for (var i = 0; i < 30; i++)
		{
			usuarios.Com(Guid.NewGuid(), $"u{i:00}@x.com");
		}

		var (listar, _, _) = Montar(usuarios, new PerfisColaboradorFalso());

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 1));

		resultado.Value.Pagina.Items.Should().HaveCount(24);
		resultado.Value.Pagina.TotalCount.Should().Be(30);
	}

	[Fact]
	public async Task SecureGateForaDoAr_PropagaOErro_NaoEListaVazia()
	{
		var usuarios = new UsuariosParaDiretorioFalso { FalharCom = IntranetErrors.Acesso.Indisponivel };
		var (listar, obter, _) = Montar(usuarios, new PerfisColaboradorFalso());

		(await listar.HandleAsync(new ListarPessoasQuery(null, null, 1))).Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		(await obter.HandleAsync(Ana)).Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task Obter_TrazGestorEEquipe_ComGestorInativoMarcado()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com").Com(Carla, "carla@x.com");
		var perfis = new PerfisColaboradorFalso()
			.Com(Perfil(Ana, "Ana"))
			.Com(Perfil(Bruno, "Bruno", gestor: Ana))
			.Com(Perfil(Carla, "Carla", gestor: Desativado));
		var (_, obter, _) = Montar(usuarios, perfis);

		var deAna = (await obter.HandleAsync(Ana)).Value;
		var deBruno = (await obter.HandleAsync(Bruno)).Value;
		var deCarla = (await obter.HandleAsync(Carla)).Value;

		deAna.Equipe.Select(p => p.UsuarioId).Should().Equal(Bruno);
		deBruno.Gestor!.UsuarioId.Should().Be(Ana);
		deBruno.Pessoa.GestorNome.Should().Be("Ana");
		deCarla.Gestor.Should().BeNull();
		deCarla.Pessoa.GestorInativo.Should().BeTrue("o gestor definido não é mais um usuário ativo");
	}

	[Fact]
	public async Task Obter_UsuarioDesativadoOuInexistente_NotFound()
	{
		var (_, obter, _) = Montar(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), new PerfisColaboradorFalso());

		(await obter.HandleAsync(Desativado)).Error.Should().Be(IntranetErrors.Diretorio.PessoaNaoEncontrada);
	}
}
```

- [ ] **Step 3: Implementar o montador**

```csharp
// src/Secco.Intranet.Application/Diretorio/MontadorDePessoas.cs
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Diretorio;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>
/// Junta identidade (usuários ativos do SecureGate) com o perfil complementar local e o setor.
/// Função pura, sem I/O — o organograma e as telas partem do mesmo resultado.
/// </summary>
public static class MontadorDePessoas
{
	/// <summary>
	/// Monta uma <see cref="PessoaDto"/> por usuário ativo. Perfil de quem não está na lista (usuário
	/// desativado ou removido) é ignorado.
	/// </summary>
	/// <param name="usuarios">Usuários ativos.</param>
	/// <param name="perfis">Todos os perfis locais.</param>
	/// <param name="setores">Todos os setores, ativos ou não (para nomear a lotação).</param>
	public static IReadOnlyList<PessoaDto> Montar(
		IReadOnlyList<UsuarioParaDiretorio> usuarios,
		IReadOnlyList<PerfilColaborador> perfis,
		IReadOnlyList<SetorDto> setores)
	{
		ArgumentNullException.ThrowIfNull(usuarios);
		ArgumentNullException.ThrowIfNull(perfis);
		ArgumentNullException.ThrowIfNull(setores);

		var usuarioPorId = usuarios.GroupBy(usuario => usuario.Id).ToDictionary(grupo => grupo.Key, grupo => grupo.First());
		var perfilPorUsuario = perfis.GroupBy(perfil => perfil.UsuarioId).ToDictionary(grupo => grupo.Key, grupo => grupo.First());
		var setorPorId = setores.GroupBy(setor => setor.Id).ToDictionary(grupo => grupo.Key, grupo => grupo.First());

		string NomeDe(UsuarioParaDiretorio usuario) =>
			perfilPorUsuario.TryGetValue(usuario.Id, out var perfil) && !string.IsNullOrWhiteSpace(perfil.NomeExibicao)
				? perfil.NomeExibicao
				: string.IsNullOrWhiteSpace(usuario.Email) ? usuario.Id.ToString() : usuario.Email;

		return
		[
			.. usuarioPorId.Values.Select(usuario =>
			{
				perfilPorUsuario.TryGetValue(usuario.Id, out var perfil);

				SetorDto? setor = perfil?.SetorId is { } setorId && setorPorId.TryGetValue(setorId, out var encontrado)
					? encontrado
					: null;

				string? gestorNome = null;
				var gestorInativo = false;

				if (perfil?.GestorUsuarioId is { } gestorId)
				{
					if (usuarioPorId.TryGetValue(gestorId, out var gestor))
					{
						gestorNome = NomeDe(gestor);
					}
					else
					{
						gestorInativo = true;
					}
				}

				return new PessoaDto(
					usuario.Id,
					usuario.Email,
					NomeDe(usuario),
					perfil?.Cargo,
					perfil?.Ramal,
					perfil?.Sobre,
					setor?.Id,
					setor?.Nome,
					setor?.Slug,
					setor?.Ativo ?? false,
					perfil?.GestorUsuarioId,
					gestorNome,
					gestorInativo,
					perfil is not null);
			}),
		];
	}
}
```

- [ ] **Step 4: Implementar os handlers**

```csharp
// src/Secco.Intranet.Application/Diretorio/ListarPessoasHandler.cs
using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Pedido da grade de pessoas.</summary>
/// <param name="Busca">Trecho de nome, cargo, setor ou e-mail.</param>
/// <param name="SetorSlug">Restringe à lotação neste setor.</param>
/// <param name="Pagina">Página (1-based), 24 por página.</param>
public sealed record ListarPessoasQuery(string? Busca, string? SetorSlug, int Pagina);

/// <summary>Grade de pessoas e os setores ativos para o filtro.</summary>
/// <param name="Pagina">Página de pessoas.</param>
/// <param name="Setores">Setores ativos, para o filtro.</param>
public sealed record PessoasDaTelaDto(PagedResult<PessoaDto> Pagina, IReadOnlyList<SetorDto> Setores);

/// <summary>Lista pessoas: usuários ativos do SecureGate + perfil local, filtrados e paginados em memória.</summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
public sealed class ListarPessoasHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores)
{
	private const int TamanhoDaPagina = 24;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Filtros e página.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PessoasDaTelaDto>> HandleAsync(ListarPessoasQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<PessoasDaTelaDto>(ativos.Error);
		}

		var todosOsSetores = await CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);
		var pessoas = MontadorDePessoas.Montar(ativos.Value, todosOsPerfis, todosOsSetores);

		var busca = query.Busca?.Trim();
		var slug = query.SetorSlug?.Trim();

		var filtradas = pessoas
			.Where(pessoa => string.IsNullOrEmpty(slug)
				|| string.Equals(pessoa.SetorSlug, slug, StringComparison.OrdinalIgnoreCase))
			.Where(pessoa => string.IsNullOrEmpty(busca) || Corresponde(pessoa, busca))
			.OrderBy(pessoa => pessoa.Nome, StringComparer.OrdinalIgnoreCase)
			.ThenBy(pessoa => pessoa.UsuarioId)
			.ToList();

		var pagina = new PageRequest(Math.Max(1, query.Pagina), TamanhoDaPagina);
		IReadOnlyList<PessoaDto> itens = [.. filtradas.Skip(pagina.Skip).Take(pagina.Size)];
		IReadOnlyList<SetorDto> ativosParaFiltro = [.. todosOsSetores.Where(setor => setor.Ativo).OrderBy(setor => setor.Nome, StringComparer.OrdinalIgnoreCase)];

		return new PessoasDaTelaDto(PagedResult.Create(itens, pagina, filtradas.Count), ativosParaFiltro);
	}

	internal static async Task<IReadOnlyList<SetorDto>> CarregarSetoresAsync(ISetorRepository setores, CancellationToken cancellationToken)
	{
		// O repositório limita a página a 200; um tenant com mais setores que isso não é o caso de uso.
		var pagina = await setores
			.SearchAsync(new SetorSearchCriteria(ApenasAtivos: false, Page: new PageRequest(1, 200)), cancellationToken)
			.ConfigureAwait(false);

		return [.. pagina.Items.Select(SetorDto.FromEntity)];
	}

	private static bool Corresponde(PessoaDto pessoa, string busca) =>
		Contem(pessoa.Nome, busca) || Contem(pessoa.Cargo, busca) || Contem(pessoa.SetorNome, busca) || Contem(pessoa.Email, busca);

	private static bool Contem(string? texto, string trecho) =>
		texto is not null && texto.Contains(trecho, StringComparison.OrdinalIgnoreCase);
}
```

```csharp
// src/Secco.Intranet.Application/Diretorio/ObterPessoaHandler.cs
using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Perfil de uma pessoa com o gestor e a equipe direta.</summary>
/// <param name="Pessoa">A pessoa.</param>
/// <param name="Gestor">O gestor, quando é um usuário ativo.</param>
/// <param name="Equipe">Quem reporta diretamente a ela, ordenado por nome.</param>
public sealed record PessoaDetalheDto(PessoaDto Pessoa, PessoaDto? Gestor, IReadOnlyList<PessoaDto> Equipe);

/// <summary>Detalhe de uma pessoa do diretório.</summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
public sealed class ObterPessoaHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Id da pessoa no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PessoaDetalheDto>> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<PessoaDetalheDto>(ativos.Error);
		}

		var todosOsSetores = await ListarPessoasHandler.CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);
		var pessoas = MontadorDePessoas.Montar(ativos.Value, todosOsPerfis, todosOsSetores);

		var pessoa = pessoas.FirstOrDefault(candidata => candidata.UsuarioId == usuarioId);

		if (pessoa is null)
		{
			return Result.Failure<PessoaDetalheDto>(IntranetErrors.Diretorio.PessoaNaoEncontrada);
		}

		var gestor = pessoa.GestorUsuarioId is { } gestorId
			? pessoas.FirstOrDefault(candidata => candidata.UsuarioId == gestorId)
			: null;

		IReadOnlyList<PessoaDto> equipe =
			[.. pessoas.Where(candidata => candidata.GestorUsuarioId == usuarioId).OrderBy(candidata => candidata.Nome, StringComparer.OrdinalIgnoreCase)];

		return new PessoaDetalheDto(pessoa, gestor, equipe);
	}
}
```

Registre no DI (`IntranetApplicationExtensions.cs`, com `using Secco.Intranet.Application.Diretorio;`):

```csharp
		services.AddScoped<ListarPessoasHandler>();
		services.AddScoped<ObterPessoaHandler>();
```

- [ ] **Step 5: Rodar e commitar**

Run: `dotnet test --filter LeituraDoDiretorioHandlersTests` → PASS; `dotnet build` 0 avisos.

```bash
git add src/Secco.Intranet.Application/Diretorio src/Secco.Intranet.Application/IntranetErrors.cs src/Secco.Intranet.Application/IntranetApplicationExtensions.cs tests/Secco.Intranet.Tests/Unit/LeituraDoDiretorioHandlersTests.cs
git commit -m "feat(diretorio): montagem de pessoas e handlers de leitura

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 6: Web — telas de leitura, menu, fim da demonstração e matriz de acesso

**Files:**
- Modify: `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs`
- Create: `tests/Secco.Intranet.Tests/Support/AuxiliaresDeHttp.cs`
- Modify: `src/Secco.Intranet.Web/Models/Diretorio/DiretorioViewModel.cs`
- Create: `src/Secco.Intranet.Web/Models/Diretorio/Iniciais.cs`
- Modify (reescrita): `src/Secco.Intranet.Web/Controllers/DiretorioController.cs`
- Modify (reescrita): `src/Secco.Intranet.Web/Views/Diretorio/Index.cshtml`
- Create: `src/Secco.Intranet.Web/Views/Diretorio/Pessoa.cshtml`, `Views/Diretorio/SemUsuario.cshtml`, `Views/Diretorio/Indisponivel.cshtml`
- Delete: `src/Secco.Intranet.Web/Views/Diretorio/Perfil.cshtml`, `Demonstracao/DiretorioDemonstracao.cs`, `DemoOptions.cs`
- Modify: `src/Secco.Intranet.Web/Program.cs`, `appsettings.Development.json`
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs`, `ViewComponents/NavigationViewComponent.cs`, `ViewComponents/UserMenuViewComponent.cs`
- Modify: `tests/Secco.Intranet.Tests/Unit/NavegacaoETemaTests.cs`, `tests/Secco.Intranet.Tests/Integration/DemonstracaoTests.cs`
- Create: `tests/Secco.Intranet.Tests/Integration/DiretorioLeituraTests.cs`
- Modify: `docs/roadmap.md` (só a frase da demonstração — ver Task 12; **não** nesta task)

**Interfaces:**
- Consumes: `ListarPessoasHandler`, `ObterPessoaHandler` (Task 5); `[ExigeNivelNoDiretorio]`, `AcessoAoDiretorio` (Task 3); `IUsuariosParaDiretorio` (Task 4).
- Produces: `IntranetWebFactory.UsuariosDoDiretorio` (`IUsuariosParaDiretorio?`, atribuível); `AuxiliaresDeHttp.TokenAsync/Form/Decodificar`; `NavigationRequest.MostrarDiretorio` (substitui `DemoHabilitado`); rotas `GET /diretorio`, `GET /diretorio/{id:guid}`, `GET /diretorio/perfil` (esta redireciona para a própria pessoa; a Task 8 a transforma no formulário); `Iniciais.De(string)`.

- [ ] **Step 1: Fábrica de teste com usuários substituíveis + auxiliares HTTP**

Em `IntranetWebFactory.cs`, acrescente `using Secco.Intranet.Application.Diretorio;`, `using Secco.Intranet.Infrastructure.Diretorio;`, a propriedade e o registro:

```csharp
	/// <summary>
	/// Fonte de usuários do diretório que o host devolve. Nula, vale o comportamento real do ambiente
	/// Testing (<see cref="UsuariosParaDiretorioIndisponivel"/>).
	/// </summary>
	public IUsuariosParaDiretorio? UsuariosDoDiretorio { get; set; }
```

e, dentro de `ConfigureTestServices`:

```csharp
		services.AddScoped<IUsuariosParaDiretorio>(_ => UsuariosDoDiretorio ?? new UsuariosParaDiretorioIndisponivel());
```

```csharp
// tests/Secco.Intranet.Tests/Support/AuxiliaresDeHttp.cs
using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Secco.Intranet.Tests.Support;

/// <summary>Auxiliares dos testes de tela: token antifalsificação, formulário e HTML legível.</summary>
public static class AuxiliaresDeHttp
{
	/// <summary>Abre a página e devolve o token antifalsificação do formulário dela.</summary>
	public static async Task<string> TokenAsync(HttpClient client, string url)
	{
		var html = await client.GetStringAsync(url);
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

		token.Success.Should().BeTrue($"a página {url} precisa trazer o token antifalsificação");

		return token.Groups[1].Value;
	}

	/// <summary>Monta um corpo de formulário.</summary>
	public static FormUrlEncodedContent Form(params (string Chave, string Valor)[] campos) =>
		new(campos.Select(campo => new KeyValuePair<string, string>(campo.Chave, campo.Valor)));

	/// <summary>O Razor codifica acentos em HTML (<c>&#xE3;</c>); comparar texto exige decodificar.</summary>
	public static string Decodificar(string html) => WebUtility.HtmlDecode(html);
}
```

- [ ] **Step 2: Testes de leitura e matriz de acesso (falham)**

```csharp
// tests/Secco.Intranet.Tests/Integration/DiretorioLeituraTests.cs
using System.Net;
using AwesomeAssertions;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Leitura do Diretório: quem entra (só os três perfis) e o que a tela mostra. O SecureGate real
/// não existe no ambiente Testing — a fonte de usuários é um dublê.
/// </summary>
public class DiretorioLeituraTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(UsuariosParaDiretorioFalso? usuarios, Guid? usuarioId, params string[] roles)
	{
		factory.UsuariosDoDiretorio = usuarios;
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		if (usuarioId is not null)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, usuarioId.ToString());
		}

		return client;
	}

	public static TheoryData<string[]> UsuariosSemAcesso => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin" },
		new[] { "financeiro-user" },
		new[] { "inventario-admin" },
	};

	public static TheoryData<string[]> UsuariosComAcesso => new()
	{
		new[] { "diretorio-user" },
		new[] { "diretorio-admin" },
		new[] { "intranet-admin" },
	};

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task SemNenhumDosTresPerfis_ToDaRotaDaLeitura_403(string[] roles)
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), Ana, roles);

		foreach (var rota in new[] { "/diretorio", $"/diretorio/{Ana}", "/diretorio/perfil" })
		{
			var resposta = await client.GetAsync(rota);

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota} exige um dos três perfis");
		}
	}

	[Theory]
	[MemberData(nameof(UsuariosComAcesso))]
	public async Task ComAlgumDosTresPerfis_Abre(string[] roles)
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com");
		var client = CriarCliente(usuarios, Ana, roles);

		(await client.GetAsync("/diretorio")).StatusCode.Should().Be(HttpStatusCode.OK);
		(await client.GetAsync($"/diretorio/{Ana}")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Index_ListaAsPessoasComBuscaEBadgeDeEmail()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@y.com");
		var client = CriarCliente(usuarios, Ana, "diretorio-user");

		var html = Decodificar(await client.GetStringAsync("/diretorio?busca=bruno"));

		html.Should().Contain("bruno@y.com").And.NotContain("ana@x.com");
	}

	[Fact]
	public async Task Index_PaginaAlemDoFim_NaoQuebra()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), Ana, "diretorio-user");

		var resposta = await client.GetAsync("/diretorio?page=99");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task PessoaInexistente_404()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), Ana, "diretorio-user");

		var resposta = await client.GetAsync($"/diretorio/{Guid.NewGuid()}");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task SemSecureGateConfigurado_AbreEExplica_SemQuebrar()
	{
		var client = CriarCliente(usuarios: null, Ana, "diretorio-user");

		foreach (var rota in new[] { "/diretorio", $"/diretorio/{Ana}" })
		{
			var resposta = await client.GetAsync(rota);

			resposta.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {rota} explica em vez de dar 500");
			Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
		}
	}

	[Fact]
	public async Task SecureGateForaDoAr_ExplicaSemDizerQueEstaVazio()
	{
		var usuarios = new UsuariosParaDiretorioFalso { FalharCom = Secco.Intranet.Application.IntranetErrors.Acesso.Indisponivel };
		var client = CriarCliente(usuarios, Ana, "diretorio-user");

		var html = Decodificar(await client.GetStringAsync("/diretorio"));

		html.Should().Contain("Não foi possível falar com o SecureGate");
	}

	[Fact]
	public async Task MeuPerfil_AbreOFormularioDeContatoDaPropriaPessoa()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), Ana, "diretorio-user");

		var resposta = await client.GetAsync("/diretorio/perfil");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/diretorio/perfil");
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("ana@x.com");
	}

	[Fact]
	public async Task MeuPerfil_SemUsuarioIdentificado_ExplicaSemQuebrar()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), usuarioId: null, "diretorio-user");

		var resposta = await client.GetAsync("/diretorio/perfil");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Não há usuário identificado");
	}

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task Menu_SemAcesso_NaoVeODiretorioNemOMeuPerfil(string[] roles)
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), Ana, roles);

		var html = await client.GetStringAsync("/");

		html.Should().NotContain("href=\"/diretorio\"");
		html.Should().NotContain("href=\"/diretorio/perfil\"");
	}

	[Theory]
	[MemberData(nameof(UsuariosComAcesso))]
	public async Task Menu_ComAcesso_VeODiretorio(string[] roles)
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), Ana, roles);

		var html = await client.GetStringAsync("/");

		html.Should().Contain("href=\"/diretorio\"");
	}
}
```

Ao rodar, `Menu_SemAcesso_...` deve conferir o markup real do link "Meu perfil" no `UserMenu/Default.cshtml` do tema Vertical (`grep -n "href" src/Secco.Intranet.Themes.Vertical/Themes/Vertical/Views/Shared/Components/UserMenu/Default.cshtml`) e ajustar a string `href="/diretorio/perfil"` se o link for montado de outro jeito.

- [ ] **Step 3: Remover a demonstração e ligar o novo menu**

1. Apague `src/Secco.Intranet.Web/DemoOptions.cs`, `src/Secco.Intranet.Web/Demonstracao/DiretorioDemonstracao.cs` (e a pasta `Demonstracao`, se ficar vazia) e `src/Secco.Intranet.Web/Views/Diretorio/Perfil.cshtml`.
2. Em `Program.cs`, apague o bloco `// Paginas de demonstracao ...` inteiro (o `builder.Services.AddSingleton(serviceProvider => { var demoOptions ...})`) e o `using` de `DemoOptions` se ficar sem uso.
3. Em `appsettings.Development.json`, apague a seção `"Demo": { "Habilitado": true },` (deixe `Development.TenantId` e `Secco.Seed`).
4. Em `DemonstracaoTests.cs`, apague os dois testes `Diretorio_ComDemonstracaoDesligada_Retorna404` e `Diretorio_ComDemonstracaoLigada_Retorna200`, o helper `ComDemonstracao`, e os `using` que sobrarem sem uso; mantenha `Mural_SemPublicacoes_RespondeComEstadoVazio` e `Home_SempreRenderizaComOLayoutDoTema` e ajuste o `<summary>` da classe para "Estado vazio do mural e resolução do tema".
5. Em `IntranetNavigation.cs`: no record `NavigationRequest`, troque `bool DemoHabilitado` por `bool MostrarDiretorio` (e o `<param>`: "Se o item Diretório deve aparecer (nível de acesso ≥ Usuário)"), e no `Build` troque `if (request.DemoHabilitado)` por `if (request.MostrarDiretorio)`.
6. Em `NavigationViewComponent.cs`: remova o parâmetro `DemoOptions demoOptions` do construtor e o `<param>` dele; no `InvokeAsync`, troque `demoOptions.Habilitado` por `MostrarDiretorio: modoAberto || AcessoAoDiretorio.Nivel(HttpContext.User) != NivelDeAcessoAoDiretorio.Nenhum`. O argumento fica nomeado, então a chamada do record passa a ser:

```csharp
		var request = new NavigationRequest(
			await CarregarSetoresAsync(HttpContext.User, autenticacaoAtiva).ConfigureAwait(false),
			HttpContext.Request.Path.Value ?? "/",
			MostrarAdministracao: modoAberto || AcessoAdministrativo.SomenteIntranetAdmin(HttpContext.User),
			MostrarDiretorio: modoAberto || AcessoAoDiretorio.Nivel(HttpContext.User) != NivelDeAcessoAoDiretorio.Nenhum,
			MostrarInventario: modoAberto || AcessoAdministrativo.TemAcesso(HttpContext.User, AcessoAdministrativo.RoleInventarioAdmin));
```

   (a ordem dos parâmetros posicionais do record é `Setores, CaminhoAtual, MostrarAdministracao, MostrarDiretorio, MostrarInventario` — mantenha a mesma ordem na declaração.)
7. Em `UserMenuViewComponent.cs`: troque o construtor por `(IConfiguration configuration, IWebHostEnvironment environment)` (com `using Microsoft.AspNetCore.Hosting;` e `using Secco.Intranet.Web.Navigation;`), apague o `<param name="demoOptions">` e troque as linhas do perfil por:

```csharp
		// "Meu perfil" só existe para quem tem acesso ao Diretório; sem acesso, não há para onde apontar.
		var temAcesso = AcessoAdministrativo.ModoAbertoDeDev(environment, configuration)
			|| AcessoAoDiretorio.Nivel(usuario) != NivelDeAcessoAoDiretorio.Nenhum;
		var urlPerfil = temAcesso ? "/diretorio/perfil" : null;
```

8. Em `NavegacaoETemaTests.cs`: troque toda ocorrência de `DemoHabilitado: false` por `MostrarDiretorio: false`, renomeie `Build_ForaDaDemonstracao_NaoOfereceOsItensDeDemonstracao` para `Build_SemAcessoAoDiretorio_SoTemOMural` (a asserção "Mural é o único item fixo" continua) e acrescente:

```csharp
	[Fact]
	public void Build_ComAcessoAoDiretorio_OfereceODiretorio()
	{
		var menu = IntranetNavigation.Build(
			new NavigationRequest([], "/diretorio/organograma", MostrarAdministracao: false, MostrarDiretorio: true, MostrarInventario: false));

		menu.Grupos.SelectMany(grupo => grupo.Itens)
			.Should().Contain(item => item.Texto == "Diretório" && item.Ativo);
	}
```

- [ ] **Step 4: ViewModels, controller e views**

```csharp
// src/Secco.Intranet.Web/Models/Diretorio/Iniciais.cs
namespace Secco.Intranet.Web.Models.Diretorio;

/// <summary>Iniciais para o avatar (substituídas pela foto na etapa 6).</summary>
public static class Iniciais
{
	/// <summary>Primeira e última iniciais do nome, em maiúsculas; um nome só dá uma letra.</summary>
	/// <param name="nome">Nome de exibição (ou e-mail).</param>
	public static string De(string nome)
	{
		var partes = nome.Split([' ', '@', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		return partes.Length switch
		{
			0 => "?",
			1 => partes[0][..1].ToUpperInvariant(),
			_ => string.Concat(partes[0][..1], partes[^1][..1]).ToUpperInvariant(),
		};
	}
}
```

Substitua `src/Secco.Intranet.Web/Models/Diretorio/DiretorioViewModel.cs` por:

```csharp
using Secco.Intranet.Application.Diretorio;

namespace Secco.Intranet.Web.Models.Diretorio;

/// <summary>Modelo da grade de pessoas.</summary>
/// <param name="Tela">Página de pessoas e setores para o filtro.</param>
/// <param name="Busca">Termo buscado, para repopular o campo.</param>
/// <param name="SetorSlug">Setor filtrado, para repopular o seletor.</param>
public sealed record DiretorioViewModel(PessoasDaTelaDto Tela, string? Busca, string? SetorSlug);

/// <summary>Modelo da página de uma pessoa.</summary>
/// <param name="Detalhe">Pessoa, gestor e equipe.</param>
/// <param name="PodeEditar">Se quem vê pode editar (o dono do perfil ou um admin).</param>
/// <param name="EditarUrl">Para onde o botão de editar leva.</param>
public sealed record PessoaViewModel(PessoaDetalheDto Detalhe, bool PodeEditar, string? EditarUrl);
```

```csharp
// src/Secco.Intranet.Web/Controllers/DiretorioController.cs
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Acesso;
using Secco.Intranet.Web.Models.Diretorio;
using Secco.Intranet.Web.Navigation;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Diretório organizacional: pessoas, perfil e (nas próximas etapas) organograma e importação.
/// Toda rota exige nível <c>Usuario</c> (<c>diretorio-user</c>, <c>diretorio-admin</c> ou
/// <c>intranet-admin</c>); quem não tem recebe 403 — não só o link escondido no menu.
/// Controller fino (ADR-0002): só orquestra handlers.
/// </summary>
/// <param name="listarPessoas">Grade de pessoas.</param>
/// <param name="obterPessoa">Detalhe de uma pessoa.</param>
[Route("diretorio")]
[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Usuario)]
public sealed class DiretorioController(
	ListarPessoasHandler listarPessoas,
	ObterPessoaHandler obterPessoa) : Controller
{
	/// <summary>Grade de pessoas com busca e filtro por setor.</summary>
	/// <param name="busca">Trecho de nome, cargo, setor ou e-mail.</param>
	/// <param name="setor">Slug do setor de lotação.</param>
	/// <param name="page">Página.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("")]
	public async Task<IActionResult> Index(string? busca, string? setor, int page = 1, CancellationToken cancellationToken = default)
	{
		var resultado = await listarPessoas.HandleAsync(new ListarPessoasQuery(busca, setor, page), cancellationToken);

		return resultado.IsFailure
			? Falha(resultado.Error)
			: View(new DiretorioViewModel(resultado.Value, busca, setor));
	}

	/// <summary>Perfil de uma pessoa.</summary>
	/// <param name="id">Id da pessoa no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{id:guid}")]
	public async Task<IActionResult> Pessoa(Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await obterPessoa.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			return Falha(resultado.Error);
		}

		string? editarUrl = null;

		if (AcessoAoDiretorio.TemNivel(User, NivelDeAcessoAoDiretorio.Administrador))
		{
			editarUrl = $"/diretorio/{id}/editar";
		}
		else if (AcessoAoDiretorio.UsuarioId(User) == id)
		{
			editarUrl = "/diretorio/perfil";
		}

		return View(new PessoaViewModel(resultado.Value, editarUrl is not null, editarUrl));
	}

	/// <summary>"Meu perfil": por ora leva à própria página; a etapa de edição o transforma no formulário.</summary>
	[HttpGet("perfil")]
	public IActionResult Perfil()
	{
		var id = AcessoAoDiretorio.UsuarioId(User);

		return id is null ? View("SemUsuario") : RedirectToAction(nameof(Pessoa), new { id });
	}

	/// <summary>
	/// Erro de leitura: pessoa inexistente é 404; o resto (SecureGate ausente ou fora do ar) vira a
	/// página que explica, com 200 — não é falha da Intranet.
	/// </summary>
	private IActionResult Falha(Error erro) =>
		erro.Type == ErrorType.NotFound
			? NotFound()
			: View("Indisponivel", new IndisponivelViewModel(erro.Description));
}
```

```cshtml
@* src/Secco.Intranet.Web/Views/Diretorio/Indisponivel.cshtml *@
@model IndisponivelViewModel
@{
    ViewData["Title"] = "Diretório";

    var cabecalho = new PageHeaderModel("Diretório", "Quem trabalha em cada setor, com ramal e e-mail.");
    var vazio = new EmptyStateModel("bi-plug", "Diretório indisponível", Model.Mensagem);
}

<partial name="_PageHeader" model="cabecalho" />
<partial name="_EmptyState" model="vazio" />
```

```cshtml
@* src/Secco.Intranet.Web/Views/Diretorio/SemUsuario.cshtml *@
@{
    ViewData["Title"] = "Meu perfil";

    var cabecalho = new PageHeaderModel("Meu perfil", "Seus dados de contato no diretório.");
    var vazio = new EmptyStateModel(
        "bi-person-x",
        "Não há usuário identificado",
        "Entre com a sua conta para ver e editar o seu perfil. No modo aberto de desenvolvimento não há usuário logado.");
}

<partial name="_PageHeader" model="cabecalho" />
<partial name="_EmptyState" model="vazio" />
```

```cshtml
@* src/Secco.Intranet.Web/Views/Diretorio/Index.cshtml *@
@using Secco.Intranet.Web.Models.Diretorio
@model DiretorioViewModel
@{
    ViewData["Title"] = "Diretório";

    var cabecalho = new PageHeaderModel("Diretório", "Quem trabalha em cada setor, com ramal e e-mail.");
    var pagina = Model.Tela.Pagina;

    string? PaginaUrl(int alvo) => Url.Action("Index", new { busca = Model.Busca, setor = Model.SetorSlug, page = alvo });

    var paginacao = new PaginationModel(
        pagina.Page,
        pagina.TotalPages,
        pagina.HasPreviousPage ? PaginaUrl(pagina.Page - 1) : null,
        pagina.HasNextPage ? PaginaUrl(pagina.Page + 1) : null);

    var vazio = new EmptyStateModel(
        "bi-people",
        "Ninguém encontrado",
        "Ajuste a busca ou o filtro de setor.");
}

<partial name="_PageHeader" model="cabecalho" />

<form method="get" asp-action="Index" class="row g-2 mb-3" role="search">
    <div class="col-sm-auto flex-grow-1">
        <label class="visually-hidden" for="busca">Buscar pessoa</label>
        <input class="form-control" type="search" id="busca" name="busca" value="@Model.Busca"
               placeholder="Buscar por nome, cargo, setor ou e-mail" />
    </div>
    <div class="col-sm-auto">
        <label class="visually-hidden" for="setor">Setor</label>
        <select class="form-select" id="setor" name="setor">
            <option value="">Todos os setores</option>
            @foreach (var setor in Model.Tela.Setores)
            {
                <option value="@setor.Slug" selected="@(string.Equals(setor.Slug, Model.SetorSlug, StringComparison.OrdinalIgnoreCase))">@setor.Nome</option>
            }
        </select>
    </div>
    <div class="col-sm-auto">
        <button class="btn btn-outline-secondary w-100" type="submit">Buscar</button>
    </div>
</form>

@if (pagina.Items.Count == 0)
{
    <partial name="_EmptyState" model="vazio" />
}
else
{
    <div class="row g-3">
        @foreach (var pessoa in pagina.Items)
        {
            <div class="col-12 col-md-6 col-xl-4">
                <article class="sc-card h-100" style="--sc-setor-hue:@SetorHue.From(pessoa.SetorSlug ?? pessoa.Nome)">
                    <div class="d-flex gap-3">
                        <span class="sc-avatar sc-avatar--lg flex-shrink-0" aria-hidden="true">@Iniciais.De(pessoa.Nome)</span>
                        <div class="min-w-0">
                            <h3 class="sc-card__title">
                                <a class="text-reset text-decoration-none" asp-action="Pessoa" asp-route-id="@pessoa.UsuarioId">@pessoa.Nome</a>
                            </h3>
                            @if (!string.IsNullOrWhiteSpace(pessoa.Cargo))
                            {
                                <p class="mb-1 text-body-secondary small">@pessoa.Cargo</p>
                            }
                            @if (pessoa.SetorNome is not null)
                            {
                                <partial name="_Badge" model="@(new BadgeModel(pessoa.SetorNome, pessoa.SetorAtivo ? BadgeVariante.Setor : BadgeVariante.Neutro, pessoa.SetorSlug))" />
                            }
                            <p class="sc-meta mt-2 mb-0">
                                @pessoa.Email
                                @if (!string.IsNullOrWhiteSpace(pessoa.Ramal))
                                {
                                    <br />
                                    <text>Ramal @pessoa.Ramal</text>
                                }
                            </p>
                        </div>
                    </div>
                </article>
            </div>
        }
    </div>

    <partial name="_Pagination" model="paginacao" />
}
```

```cshtml
@* src/Secco.Intranet.Web/Views/Diretorio/Pessoa.cshtml *@
@using Secco.Intranet.Web.Models.Diretorio
@model PessoaViewModel
@{
    var pessoa = Model.Detalhe.Pessoa;

    ViewData["Title"] = pessoa.Nome;

    var acoes = new List<PageActionModel> { new("Voltar", Url.Action("Index")!, "bi-arrow-left") };

    if (Model.PodeEditar && Model.EditarUrl is not null)
    {
        acoes.Insert(0, new PageActionModel("Editar", Model.EditarUrl, "bi-pencil", Primaria: true));
    }

    var cabecalho = new PageHeaderModel(pessoa.Nome, pessoa.Cargo, pessoa.SetorSlug, acoes);
}

<partial name="_PageHeader" model="cabecalho" />

<div class="sc-panel" style="--sc-setor-hue:@SetorHue.From(pessoa.SetorSlug ?? pessoa.Nome)">
    <div class="d-flex gap-3 align-items-center mb-4">
        <span class="sc-avatar sc-avatar--lg" aria-hidden="true">@Iniciais.De(pessoa.Nome)</span>
        <div>
            <h2 class="h5 mb-1">@pessoa.Nome</h2>
            @if (!string.IsNullOrWhiteSpace(pessoa.Cargo))
            {
                <p class="mb-1 text-body-secondary small">@pessoa.Cargo</p>
            }
            @if (pessoa.SetorNome is not null)
            {
                <partial name="_Badge" model="@(new BadgeModel(pessoa.SetorNome, pessoa.SetorAtivo ? BadgeVariante.Setor : BadgeVariante.Neutro, pessoa.SetorSlug))" />
            }
        </div>
    </div>

    <dl class="row mb-0">
        <dt class="col-sm-3">E-mail</dt>
        <dd class="col-sm-9"><span class="sc-meta">@pessoa.Email</span></dd>

        <dt class="col-sm-3">Ramal</dt>
        <dd class="col-sm-9"><span class="sc-meta">@(pessoa.Ramal ?? "—")</span></dd>

        <dt class="col-sm-3">Reporta a</dt>
        <dd class="col-sm-9">
            @if (Model.Detalhe.Gestor is { } gestor)
            {
                <a asp-action="Pessoa" asp-route-id="@gestor.UsuarioId">@gestor.Nome</a>
            }
            else if (pessoa.GestorInativo)
            {
                <span class="text-body-secondary">Gestor inativo</span>
            }
            else
            {
                <text>—</text>
            }
        </dd>

        <dt class="col-sm-3">Sobre</dt>
        <dd class="col-sm-9 mb-0">@(pessoa.Sobre ?? "—")</dd>
    </dl>
</div>

@if (Model.Detalhe.Equipe.Count > 0)
{
    <div class="sc-panel mt-3">
        <h2 class="h6">Equipe direta (@Model.Detalhe.Equipe.Count)</h2>
        <ul class="sc-list mb-0">
            @foreach (var membro in Model.Detalhe.Equipe)
            {
                <li class="sc-list__item">
                    <span class="sc-avatar" aria-hidden="true">@Iniciais.De(membro.Nome)</span>
                    <div class="sc-list__text">
                        <p class="sc-list__title">
                            <a class="text-reset text-decoration-none" asp-action="Pessoa" asp-route-id="@membro.UsuarioId">@membro.Nome</a>
                        </p>
                        @if (!string.IsNullOrWhiteSpace(membro.Cargo))
                        {
                            <span class="sc-list__sub">@membro.Cargo</span>
                        }
                    </div>
                </li>
            }
        </ul>
    </div>
}
```

O construtor de `PageHeaderModel` é `(Titulo, Subtitulo?, SetorSlug?, Acoes?)`; se o compilador reclamar da chamada posicional acima, use `new PageHeaderModel(pessoa.Nome, pessoa.Cargo, SetorSlug: pessoa.SetorSlug, Acoes: acoes)`.

- [ ] **Step 5: Rodar tudo, checar e commitar**

Run: `dotnet build` (0 avisos) e `dotnet test` (suíte inteira verde, incluindo `DiretorioLeituraTests`).

Confira que não sobrou referência à demonstração: `grep -rn "DemoOptions\|DemoHabilitado\|DiretorioDemonstracao\|Intranet:Demo" src tests --include=*.cs --include=*.cshtml --include=*.json` deve devolver nada.

```bash
git add src/Secco.Intranet.Web tests/Secco.Intranet.Tests
git commit -m "feat(diretorio): telas de leitura reais, menu por nivel de acesso e fim da demonstracao

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

## Task 7: Application — regras de gestor e handlers de escrita

Dois comandos, dois handlers: **contato** (o colaborador e o admin) e **dados funcionais** (só o admin — quem exige isso é o controller, mas o handler nunca mistura os dois conjuntos de campos).

**Files:**
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs`
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/RegrasDeGestor.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/EdicaoDePerfil.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/EditarContatoHandler.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/EditarDadosFuncionaisHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/RegrasDeGestorTests.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/EscritaDoDiretorioHandlersTests.cs`

**Interfaces:**
- Consumes: `PerfilColaborador` (Task 1), `IPerfilColaboradorRepository`, `IUsuariosParaDiretorio`, `ISetorRepository.GetByIdAsync`, `ITrilhaDeAuditoria`, dublês da Task 4.
- Produces:
  - `RegrasDeGestor.CriariaCiclo(IReadOnlyDictionary<Guid, Guid> gestorPorUsuario, Guid usuarioId, Guid novoGestorId) : bool` e `RegrasDeGestor.ProfundidadeMaxima = 20`.
  - `EditarContatoHandler(IUsuariosParaDiretorio, IPerfilColaboradorRepository, ITrilhaDeAuditoria).HandleAsync(EditarContatoCommand(Guid UsuarioId, string? Nome, string? Ramal, string? Sobre), ct) : Task<Result>`.
  - `EditarDadosFuncionaisHandler(IUsuariosParaDiretorio, IPerfilColaboradorRepository, ISetorRepository, ITrilhaDeAuditoria).HandleAsync(EditarDadosFuncionaisCommand(Guid UsuarioId, string? Cargo, Guid? SetorId, Guid? GestorUsuarioId), ct) : Task<Result>`.
  - `IntranetErrors.Diretorio.{NomeTooLong, CargoTooLong, RamalTooLong, SobreTooLong, SetorInvalido, GestorInvalido, GestorEhOProprio, GestorCriariaCiclo}`.
  - `VerbosDeAuditoria.DiretorioPerfilEditar`, `DiretorioDadosFuncionaisEditar`, `DiretorioImportar`; `RecursosDeAuditoria.Diretorio`.
  - `EdicaoDePerfil.AplicarAsync(repo, usuarioId, editar, ct)` (interno, reusado pela importação).

- [ ] **Step 1: Erros e verbos**

Em `IntranetErrors.cs`, dentro de `IntranetErrors.Diretorio` (criada na Task 5), acrescente:

```csharp
		/// <summary>Nome acima do limite.</summary>
		public static Error NomeTooLong(int limite) =>
			Error.Validation("Intranet.Diretorio.NomeTooLong", $"O nome excede o limite de {limite} caracteres.");

		/// <summary>Cargo acima do limite.</summary>
		public static Error CargoTooLong(int limite) =>
			Error.Validation("Intranet.Diretorio.CargoTooLong", $"O cargo excede o limite de {limite} caracteres.");

		/// <summary>Ramal acima do limite.</summary>
		public static Error RamalTooLong(int limite) =>
			Error.Validation("Intranet.Diretorio.RamalTooLong", $"O ramal excede o limite de {limite} caracteres.");

		/// <summary>Texto "sobre" acima do limite.</summary>
		public static Error SobreTooLong(int limite) =>
			Error.Validation("Intranet.Diretorio.SobreTooLong", $"O texto \"sobre\" excede o limite de {limite} caracteres.");

		/// <summary>Setor inexistente, ou inativo para uma lotação nova.</summary>
		public static readonly Error SetorInvalido =
			Error.Validation("Intranet.Diretorio.SetorInvalido", "Escolha um setor existente e ativo.");

		/// <summary>Gestor que não é um usuário ativo do tenant.</summary>
		public static readonly Error GestorInvalido =
			Error.Validation("Intranet.Diretorio.GestorInvalido", "O gestor precisa ser um usuário ativo.");

		/// <summary>Autogestor.</summary>
		public static readonly Error GestorEhOProprio =
			Error.Validation("Intranet.Diretorio.GestorEhOProprio", "Ninguém pode ser gestor de si mesmo.");

		/// <summary>A escolha fecharia um ciclo de gestão.</summary>
		public static readonly Error GestorCriariaCiclo =
			Error.Validation(
				"Intranet.Diretorio.GestorCriariaCiclo",
				"Essa escolha faria a pessoa reportar, direta ou indiretamente, a si mesma.");
```

Em `VerbosDeAuditoria.cs`, dentro de `VerbosDeAuditoria`:

```csharp
	/// <summary>Contato (nome, ramal, sobre) alterado.</summary>
	public const string DiretorioPerfilEditar = "diretorio.perfil-editar";

	/// <summary>Dados funcionais (cargo, setor, gestor) alterados.</summary>
	public const string DiretorioDadosFuncionaisEditar = "diretorio.dados-funcionais-editar";

	/// <summary>Importação CSV aplicada.</summary>
	public const string DiretorioImportar = "diretorio.importar";
```

e em `RecursosDeAuditoria`:

```csharp
	/// <summary>Perfil de colaborador no Diretório.</summary>
	public const string Diretorio = "diretorio";
```

- [ ] **Step 2: Testes da regra de ciclo (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/RegrasDeGestorTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application.Diretorio;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class RegrasDeGestorTests
{
	private static readonly Guid A = Guid.NewGuid();
	private static readonly Guid B = Guid.NewGuid();
	private static readonly Guid C = Guid.NewGuid();
	private static readonly Guid D = Guid.NewGuid();

	[Fact]
	public void SemNenhumGestorDefinido_NaoCriaCiclo()
	{
		RegrasDeGestor.CriariaCiclo(new Dictionary<Guid, Guid>(), A, B).Should().BeFalse();
	}

	[Fact]
	public void CicloDireto_AReportaAB_BReportaAA()
	{
		// B já reporta a A; fazer A reportar a B fecha o ciclo.
		var mapa = new Dictionary<Guid, Guid> { [B] = A };

		RegrasDeGestor.CriariaCiclo(mapa, A, B).Should().BeTrue();
	}

	[Fact]
	public void CicloDeTres_AReportaAC_CReportaAB_BReportaAA()
	{
		var mapa = new Dictionary<Guid, Guid> { [B] = A, [C] = B };

		RegrasDeGestor.CriariaCiclo(mapa, A, C).Should().BeTrue();
	}

	[Fact]
	public void CorrenteQueNaoPassaPeloUsuario_NaoEUmCiclo()
	{
		var mapa = new Dictionary<Guid, Guid> { [C] = D };

		RegrasDeGestor.CriariaCiclo(mapa, A, C).Should().BeFalse();
	}

	[Fact]
	public void TrocarDeGestorNaMesmaArvore_NaoEUmCiclo()
	{
		// B e C reportam a A; mover C para debaixo de B é legítimo.
		var mapa = new Dictionary<Guid, Guid> { [B] = A, [C] = A };

		RegrasDeGestor.CriariaCiclo(mapa, C, B).Should().BeFalse();
	}

	[Fact]
	public void DadoJaCorrompidoEmOutroLugar_NaoTravaNemAcusaCicloAlheio()
	{
		// Ciclo gravado à mão entre C e D, que não envolve A: a regra termina e diz "não".
		var mapa = new Dictionary<Guid, Guid> { [C] = D, [D] = C };

		var resultado = () => RegrasDeGestor.CriariaCiclo(mapa, A, C);

		resultado.Should().NotThrow();
		RegrasDeGestor.CriariaCiclo(mapa, A, C).Should().BeFalse();
	}
}
```

- [ ] **Step 3: Implementar a regra**

```csharp
// src/Secco.Intranet.Application/Diretorio/RegrasDeGestor.cs
namespace Secco.Intranet.Application.Diretorio;

/// <summary>Regras da relação "reporta a" que não cabem na entidade (dependem do conjunto todo).</summary>
public static class RegrasDeGestor
{
	/// <summary>Teto de profundidade do organograma — defesa contra dado corrompido, não regra de negócio.</summary>
	public const int ProfundidadeMaxima = 20;

	/// <summary>
	/// Indica se fazer <paramref name="usuarioId"/> reportar a <paramref name="novoGestorId"/> fecharia
	/// um ciclo: sobe a cadeia de gestores a partir do novo gestor e vê se chega ao próprio usuário.
	/// Um ciclo já gravado que <b>não</b> envolve o usuário não trava a regra — a subida termina ao
	/// repetir um nó.
	/// </summary>
	/// <param name="gestorPorUsuario">Mapa atual usuário → gestor (só quem tem gestor).</param>
	/// <param name="usuarioId">Quem está mudando de gestor.</param>
	/// <param name="novoGestorId">O gestor proposto.</param>
	public static bool CriariaCiclo(IReadOnlyDictionary<Guid, Guid> gestorPorUsuario, Guid usuarioId, Guid novoGestorId)
	{
		ArgumentNullException.ThrowIfNull(gestorPorUsuario);

		var visitados = new HashSet<Guid>();
		var atual = novoGestorId;

		while (visitados.Add(atual))
		{
			if (!gestorPorUsuario.TryGetValue(atual, out var proximo))
			{
				return false;
			}

			if (proximo == usuarioId)
			{
				return true;
			}

			atual = proximo;
		}

		return false;
	}
}
```

- [ ] **Step 4: Aplicação de edição com criação sob demanda**

```csharp
// src/Secco.Intranet.Application/Diretorio/EdicaoDePerfil.cs
using Secco.Intranet.Domain.Diretorio;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>
/// Aplica uma edição a um perfil, criando-o na primeira vez. Não persiste um perfil que a edição
/// deixaria vazio, e contorna a corrida de dois primeiros salvamentos (o índice único barra o
/// segundo; o repositório avisa e este método reaplica sobre o perfil que o outro criou).
/// </summary>
internal static class EdicaoDePerfil
{
	/// <summary>Devolve os nomes dos campos que de fato mudaram (vazio = nada foi gravado).</summary>
	public static async Task<IReadOnlyList<string>> AplicarAsync(
		IPerfilColaboradorRepository repositorio,
		Guid usuarioId,
		Func<PerfilColaborador, IReadOnlyList<string>> editar,
		CancellationToken cancellationToken)
	{
		var existente = await repositorio.GetParaEdicaoAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (existente is not null)
		{
			return await AplicarNoExistenteAsync(repositorio, existente, editar, cancellationToken).ConfigureAwait(false);
		}

		var novo = new PerfilColaborador(usuarioId);
		var campos = editar(novo);

		if (campos.Count == 0)
		{
			return campos;
		}

		if (await repositorio.TentarAdicionarAsync(novo, cancellationToken).ConfigureAwait(false))
		{
			return campos;
		}

		var concorrente = await repositorio.GetParaEdicaoAsync(usuarioId, cancellationToken).ConfigureAwait(false)
			?? throw new InvalidOperationException("O perfil que impediu a criação não foi encontrado.");

		return await AplicarNoExistenteAsync(repositorio, concorrente, editar, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<IReadOnlyList<string>> AplicarNoExistenteAsync(
		IPerfilColaboradorRepository repositorio,
		PerfilColaborador perfil,
		Func<PerfilColaborador, IReadOnlyList<string>> editar,
		CancellationToken cancellationToken)
	{
		var campos = editar(perfil);

		if (campos.Count > 0)
		{
			await repositorio.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return campos;
	}
}
```

- [ ] **Step 5: Testes dos handlers (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/EscritaDoDiretorioHandlersTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class EscritaDoDiretorioHandlersTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();
	private static readonly Guid Fora = Guid.NewGuid();

	private static UsuariosParaDiretorioFalso Usuarios() =>
		new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com").Com(Carla, "carla@x.com");

	private static EditarContatoHandler Contato(UsuariosParaDiretorioFalso usuarios, PerfisColaboradorFalso perfis, TrilhaDeAcessoFalsa trilha) =>
		new(usuarios, perfis, trilha);

	private static EditarDadosFuncionaisHandler Funcionais(
		UsuariosParaDiretorioFalso usuarios, PerfisColaboradorFalso perfis, SetoresFalsos setores, TrilhaDeAcessoFalsa trilha) =>
		new(usuarios, perfis, setores, trilha);

	// --- Contato ---

	[Fact]
	public async Task Contato_PrimeiraEdicao_CriaOPerfilEAuditaSoNomesDeCampo()
	{
		var perfis = new PerfisColaboradorFalso();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await Contato(Usuarios(), perfis, trilha)
			.HandleAsync(new EditarContatoCommand(Ana, "Ana Ribeiro", "2100", null));

		resultado.IsSuccess.Should().BeTrue();
		perfis.Perfis.Should().ContainSingle().Which.NomeExibicao.Should().Be("Ana Ribeiro");
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.DiretorioPerfilEditar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Diretorio);
		registro.RecursoId.Should().Be(Ana.ToString());
		registro.Metadata.Should().Contain("nome").And.Contain("ramal");
		registro.Metadata.Should().NotContain("Ana Ribeiro", "a trilha guarda o nome do campo, não o valor");
		registro.Metadata.Should().NotContain("2100");
	}

	[Fact]
	public async Task Contato_SemMudanca_NaoCriaPerfilVazioNemAudita()
	{
		var perfis = new PerfisColaboradorFalso();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await Contato(Usuarios(), perfis, trilha).HandleAsync(new EditarContatoCommand(Ana, null, "  ", ""));

		resultado.IsSuccess.Should().BeTrue();
		perfis.Perfis.Should().BeEmpty("um perfil sem nada não deve ser gravado");
		trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Contato_PerfilExistente_Atualiza()
	{
		var existente = new PerfilColaborador(Ana);
		existente.EditarContato("Ana", null, null);
		var perfis = new PerfisColaboradorFalso().Com(existente);

		var resultado = await Contato(Usuarios(), perfis, new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarContatoCommand(Ana, "Ana Ribeiro", null, null));

		resultado.IsSuccess.Should().BeTrue();
		existente.NomeExibicao.Should().Be("Ana Ribeiro");
		perfis.Salvou.Should().Be(1);
	}

	[Fact]
	public async Task Contato_UsuarioDesativadoOuInexistente_NotFound()
	{
		var resultado = await Contato(Usuarios(), new PerfisColaboradorFalso(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarContatoCommand(Fora, "Fantasma", null, null));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.PessoaNaoEncontrada);
	}

	[Theory]
	[InlineData(PerfilColaborador.NomeMaxLength + 1, 0, 0)]
	[InlineData(0, PerfilColaborador.RamalMaxLength + 1, 0)]
	[InlineData(0, 0, PerfilColaborador.SobreMaxLength + 1)]
	public async Task Contato_AcimaDoLimite_RecusaSemGravar(int nome, int ramal, int sobre)
	{
		var perfis = new PerfisColaboradorFalso();

		var resultado = await Contato(Usuarios(), perfis, new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarContatoCommand(Ana, new string('n', nome), new string('1', ramal), new string('s', sobre)));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Type.Should().Be(Secco.SharedKernel.Results.ErrorType.Validation);
		perfis.Perfis.Should().BeEmpty();
	}

	[Fact]
	public async Task Contato_SecureGateForaDoAr_PropagaOErro()
	{
		var usuarios = Usuarios();
		usuarios.FalharCom = IntranetErrors.Acesso.Indisponivel;

		var resultado = await Contato(usuarios, new PerfisColaboradorFalso(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarContatoCommand(Ana, "Ana", null, null));

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task Contato_NuncaMexeEmDadosFuncionais()
	{
		var existente = new PerfilColaborador(Ana);
		existente.EditarDadosFuncionais("Diretora", null, Bruno);
		var perfis = new PerfisColaboradorFalso().Com(existente);

		await Contato(Usuarios(), perfis, new TrilhaDeAcessoFalsa()).HandleAsync(new EditarContatoCommand(Ana, "Ana", null, null));

		existente.Cargo.Should().Be("Diretora");
		existente.GestorUsuarioId.Should().Be(Bruno);
	}

	// --- Dados funcionais ---

	[Fact]
	public async Task Funcionais_DefineCargoSetorEGestor_EAudita()
	{
		var setores = new SetoresFalsos();
		var financeiro = setores.Com("Financeiro", "financeiro");
		var perfis = new PerfisColaboradorFalso();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await Funcionais(Usuarios(), perfis, setores, trilha)
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, "Controller", financeiro.Id, Bruno));

		resultado.IsSuccess.Should().BeTrue();
		var perfil = perfis.Perfis.Single();
		perfil.Cargo.Should().Be("Controller");
		perfil.SetorId.Should().Be(financeiro.Id);
		perfil.GestorUsuarioId.Should().Be(Bruno);
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.DiretorioDadosFuncionaisEditar);
		trilha.Registros.Single().Metadata.Should().Contain("cargo").And.Contain("setor").And.Contain("gestor");
	}

	[Fact]
	public async Task Funcionais_GestorEhOProprio_Recusa()
	{
		var resultado = await Funcionais(Usuarios(), new PerfisColaboradorFalso(), new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, null, Ana));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.GestorEhOProprio);
	}

	[Fact]
	public async Task Funcionais_GestorQueNaoEUsuarioAtivo_Recusa()
	{
		var resultado = await Funcionais(Usuarios(), new PerfisColaboradorFalso(), new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, null, Fora));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.GestorInvalido);
	}

	[Fact]
	public async Task Funcionais_CicloDireto_Recusa()
	{
		var bruno = new PerfilColaborador(Bruno);
		bruno.EditarDadosFuncionais(null, null, Ana);
		var perfis = new PerfisColaboradorFalso().Com(bruno);

		var resultado = await Funcionais(Usuarios(), perfis, new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, null, Bruno));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.GestorCriariaCiclo);
		perfis.Perfis.Should().ContainSingle("nada foi criado para a Ana");
	}

	[Fact]
	public async Task Funcionais_CicloDeTresPessoas_Recusa()
	{
		var bruno = new PerfilColaborador(Bruno);
		bruno.EditarDadosFuncionais(null, null, Ana);
		var carla = new PerfilColaborador(Carla);
		carla.EditarDadosFuncionais(null, null, Bruno);
		var perfis = new PerfisColaboradorFalso().Com(bruno).Com(carla);

		var resultado = await Funcionais(Usuarios(), perfis, new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, null, Carla));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.GestorCriariaCiclo);
	}

	[Fact]
	public async Task Funcionais_SetorInexistente_Recusa()
	{
		var resultado = await Funcionais(Usuarios(), new PerfisColaboradorFalso(), new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, Guid.NewGuid(), null));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.SetorInvalido);
	}

	[Fact]
	public async Task Funcionais_SetorInativo_RecusadoParaLotacaoNova_MasMantidoSeJaEra()
	{
		var setores = new SetoresFalsos();
		var antigo = setores.Com("Antigo", "antigo", ativo: false);
		var perfis = new PerfisColaboradorFalso();
		var handler = Funcionais(Usuarios(), perfis, setores, new TrilhaDeAcessoFalsa());

		(await handler.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, antigo.Id, null)))
			.Error.Should().Be(IntranetErrors.Diretorio.SetorInvalido, "lotação nova exige setor ativo");

		var jaLotada = new PerfilColaborador(Bruno);
		jaLotada.EditarDadosFuncionais(null, antigo.Id, null);
		perfis.Com(jaLotada);

		(await handler.HandleAsync(new EditarDadosFuncionaisCommand(Bruno, "Novo cargo", antigo.Id, null)))
			.IsSuccess.Should().BeTrue("quem já estava lotado no setor desativado continua podendo ter o cargo editado");
	}

	[Fact]
	public async Task Funcionais_CargoAcimaDoLimite_Recusa()
	{
		var resultado = await Funcionais(Usuarios(), new PerfisColaboradorFalso(), new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, new string('c', PerfilColaborador.CargoMaxLength + 1), null, null));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.CargoTooLong(PerfilColaborador.CargoMaxLength));
	}

	[Fact]
	public async Task Funcionais_NuncaMexeNoContato()
	{
		var existente = new PerfilColaborador(Ana);
		existente.EditarContato("Ana", "2100", "Sobre");
		var perfis = new PerfisColaboradorFalso().Com(existente);

		await Funcionais(Usuarios(), perfis, new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, "Diretora", null, null));

		existente.NomeExibicao.Should().Be("Ana");
		existente.Ramal.Should().Be("2100");
		existente.Sobre.Should().Be("Sobre");
	}

	[Fact]
	public async Task Funcionais_LimparOGestor_Funciona()
	{
		var existente = new PerfilColaborador(Ana);
		existente.EditarDadosFuncionais(null, null, Bruno);
		var perfis = new PerfisColaboradorFalso().Com(existente);

		var resultado = await Funcionais(Usuarios(), perfis, new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, null, null));

		resultado.IsSuccess.Should().BeTrue();
		existente.GestorUsuarioId.Should().BeNull();
	}
}
```

- [ ] **Step 6: Implementar os handlers**

```csharp
// src/Secco.Intranet.Application/Diretorio/EditarContatoHandler.cs
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Pedido de edição do contato de uma pessoa: só nome, ramal e "sobre".</summary>
/// <param name="UsuarioId">Pessoa cujo contato muda.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Ramal">Ramal.</param>
/// <param name="Sobre">Texto livre.</param>
public sealed record EditarContatoCommand(Guid UsuarioId, string? Nome, string? Ramal, string? Sobre);

/// <summary>
/// Edita o contato de uma pessoa (o próprio colaborador ou um admin — quem pode é decisão do
/// controller). Não conhece cargo, setor nem gestor: é por isso que o formulário do colaborador
/// não consegue alterá-los, mesmo forjado.
/// </summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarContatoHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(EditarContatoCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (Tamanho(command.Nome) > PerfilColaborador.NomeMaxLength)
		{
			return Result.Failure(IntranetErrors.Diretorio.NomeTooLong(PerfilColaborador.NomeMaxLength));
		}

		if (Tamanho(command.Ramal) > PerfilColaborador.RamalMaxLength)
		{
			return Result.Failure(IntranetErrors.Diretorio.RamalTooLong(PerfilColaborador.RamalMaxLength));
		}

		if (Tamanho(command.Sobre) > PerfilColaborador.SobreMaxLength)
		{
			return Result.Failure(IntranetErrors.Diretorio.SobreTooLong(PerfilColaborador.SobreMaxLength));
		}

		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure(ativos.Error);
		}

		if (!ativos.Value.Any(usuario => usuario.Id == command.UsuarioId))
		{
			return Result.Failure(IntranetErrors.Diretorio.PessoaNaoEncontrada);
		}

		var campos = await EdicaoDePerfil
			.AplicarAsync(perfis, command.UsuarioId, perfil => perfil.EditarContato(command.Nome, command.Ramal, command.Sobre), cancellationToken)
			.ConfigureAwait(false);

		if (campos.Count > 0)
		{
			await trilha.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DiretorioPerfilEditar,
					RecursosDeAuditoria.Diretorio,
					command.UsuarioId.ToString(),
					JsonSerializer.Serialize(new { usuarioId = command.UsuarioId, campos })),
				cancellationToken).ConfigureAwait(false);
		}

		return Result.Success();
	}

	private static int Tamanho(string? valor) => valor?.Trim().Length ?? 0;
}
```

```csharp
// src/Secco.Intranet.Application/Diretorio/EditarDadosFuncionaisHandler.cs
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Pedido de edição dos dados funcionais: cargo, setor de lotação e gestor. Valores nulos limpam o campo.</summary>
/// <param name="UsuarioId">Pessoa cujos dados mudam.</param>
/// <param name="Cargo">Cargo.</param>
/// <param name="SetorId">Setor de lotação.</param>
/// <param name="GestorUsuarioId">Id do gestor no SecureGate.</param>
public sealed record EditarDadosFuncionaisCommand(Guid UsuarioId, string? Cargo, Guid? SetorId, Guid? GestorUsuarioId);

/// <summary>
/// Edita cargo, setor e gestor. Só o admin do diretório chama isto (o controller garante). Aplica as
/// regras que dependem do conjunto: gestor ativo, sem autogestão e sem ciclo; setor existente e
/// ativo para lotação nova.
/// </summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarDadosFuncionaisHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(EditarDadosFuncionaisCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if ((command.Cargo?.Trim().Length ?? 0) > PerfilColaborador.CargoMaxLength)
		{
			return Result.Failure(IntranetErrors.Diretorio.CargoTooLong(PerfilColaborador.CargoMaxLength));
		}

		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure(ativos.Error);
		}

		if (!ativos.Value.Any(usuario => usuario.Id == command.UsuarioId))
		{
			return Result.Failure(IntranetErrors.Diretorio.PessoaNaoEncontrada);
		}

		var gestor = command.GestorUsuarioId == Guid.Empty ? null : command.GestorUsuarioId;
		var setor = command.SetorId == Guid.Empty ? null : command.SetorId;

		if (gestor is { } gestorId)
		{
			var erroDeGestor = await ValidarGestorAsync(command.UsuarioId, gestorId, ativos.Value, cancellationToken).ConfigureAwait(false);

			if (erroDeGestor is not null)
			{
				return Result.Failure(erroDeGestor);
			}
		}

		if (setor is { } setorId)
		{
			var atual = await perfis.GetByUsuarioIdAsync(command.UsuarioId, cancellationToken).ConfigureAwait(false);
			var encontrado = await setores.GetByIdAsync(setorId, cancellationToken).ConfigureAwait(false);

			// Setor desativado depois da lotação: quem já estava lá pode ser editado sem trocar de setor.
			if (encontrado is null || (!encontrado.Ativo && atual?.SetorId != setorId))
			{
				return Result.Failure(IntranetErrors.Diretorio.SetorInvalido);
			}
		}

		var campos = await EdicaoDePerfil
			.AplicarAsync(perfis, command.UsuarioId, perfil => perfil.EditarDadosFuncionais(command.Cargo, setor, gestor), cancellationToken)
			.ConfigureAwait(false);

		if (campos.Count > 0)
		{
			await trilha.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DiretorioDadosFuncionaisEditar,
					RecursosDeAuditoria.Diretorio,
					command.UsuarioId.ToString(),
					JsonSerializer.Serialize(new { usuarioId = command.UsuarioId, campos })),
				cancellationToken).ConfigureAwait(false);
		}

		return Result.Success();
	}

	private async Task<Error?> ValidarGestorAsync(
		Guid usuarioId, Guid gestorId, IReadOnlyList<UsuarioParaDiretorio> ativos, CancellationToken cancellationToken)
	{
		if (gestorId == usuarioId)
		{
			return IntranetErrors.Diretorio.GestorEhOProprio;
		}

		if (!ativos.Any(usuario => usuario.Id == gestorId))
		{
			return IntranetErrors.Diretorio.GestorInvalido;
		}

		var todos = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);
		var mapa = todos
			.Where(perfil => perfil.GestorUsuarioId is not null)
			.ToDictionary(perfil => perfil.UsuarioId, perfil => perfil.GestorUsuarioId!.Value);

		return RegrasDeGestor.CriariaCiclo(mapa, usuarioId, gestorId) ? IntranetErrors.Diretorio.GestorCriariaCiclo : null;
	}
}
```

Registre no DI (`IntranetApplicationExtensions.cs`):

```csharp
		services.AddScoped<EditarContatoHandler>();
		services.AddScoped<EditarDadosFuncionaisHandler>();
```

- [ ] **Step 7: Rodar e commitar**

Run: `dotnet test --filter "RegrasDeGestorTests|EscritaDoDiretorioHandlersTests"` → PASS; `dotnet build` 0 avisos.

```bash
git add src/Secco.Intranet.Application tests/Secco.Intranet.Tests/Unit/RegrasDeGestorTests.cs tests/Secco.Intranet.Tests/Unit/EscritaDoDiretorioHandlersTests.cs
git commit -m "feat(diretorio): handlers de contato e dados funcionais com as regras de gestor

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 8: Web — "Meu perfil" e edição pelo admin

**Files:**
- Create: `src/Secco.Intranet.Application/Diretorio/ObterPessoaParaEdicaoHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Create: `src/Secco.Intranet.Web/Models/Diretorio/FormulariosDoDiretorio.cs`
- Modify: `src/Secco.Intranet.Web/Models/Diretorio/DiretorioViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/DiretorioController.cs`
- Create: `src/Secco.Intranet.Web/Views/Diretorio/Perfil.cshtml`, `Views/Diretorio/Editar.cshtml`
- Create: `tests/Secco.Intranet.Tests/Unit/ObterPessoaParaEdicaoHandlerTests.cs`
- Create: `tests/Secco.Intranet.Tests/Integration/DiretorioEdicaoTests.cs`

**Interfaces:**
- Consumes: `EditarContatoHandler`, `EditarDadosFuncionaisHandler` (Task 7); `AcessoAoDiretorio`, `[ExigeNivelNoDiretorio]` (Task 3); `MontadorDePessoas` (Task 5).
- Produces:
  - `ObterPessoaParaEdicaoHandler.HandleAsync(Guid, ct) : Task<Result<PessoaParaEdicaoDto>>`; `PessoaParaEdicaoDto(PessoaDto Pessoa, IReadOnlyList<SetorDto> SetoresAtivos, IReadOnlyList<PessoaDto> GestoresPossiveis)` (todas as pessoas menos ela própria, por nome).
  - `EditarContatoForm { Nome, Ramal, Sobre }` e `EditarPessoaForm : EditarContatoForm { Cargo, SetorId, GestorUsuarioId }`, com `[StringLength]` das constantes de `PerfilColaborador`.
  - Rotas: `GET/POST /diretorio/perfil` (nível `Usuario`, **sempre** o `sub` do logado); `GET/POST /diretorio/{id:guid}/editar` (nível `Administrador`).

- [ ] **Step 1: Handler que alimenta o formulário do admin (teste + código)**

```csharp
// tests/Secco.Intranet.Tests/Unit/ObterPessoaParaEdicaoHandlerTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ObterPessoaParaEdicaoHandlerTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();

	[Fact]
	public async Task TrazSetoresAtivosEOsPossiveisGestoresSemAPropriaPessoa()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com");
		var setores = new SetoresFalsos();
		setores.Com("Ativo", "ativo");
		setores.Com("Inativo", "inativo", ativo: false);

		var resultado = await new ObterPessoaParaEdicaoHandler(usuarios, new PerfisColaboradorFalso(), setores).HandleAsync(Ana);

		resultado.Value.Pessoa.UsuarioId.Should().Be(Ana);
		resultado.Value.SetoresAtivos.Select(s => s.Slug).Should().Equal("ativo");
		resultado.Value.GestoresPossiveis.Select(p => p.UsuarioId).Should().Equal(Bruno);
	}

	[Fact]
	public async Task PessoaInexistente_NotFound()
	{
		var resultado = await new ObterPessoaParaEdicaoHandler(new UsuariosParaDiretorioFalso(), new PerfisColaboradorFalso(), new SetoresFalsos())
			.HandleAsync(Ana);

		resultado.Error.Should().Be(IntranetErrors.Diretorio.PessoaNaoEncontrada);
	}
}
```

```csharp
// src/Secco.Intranet.Application/Diretorio/ObterPessoaParaEdicaoHandler.cs
using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>O que o formulário de edição do admin precisa.</summary>
/// <param name="Pessoa">A pessoa em edição, com os valores atuais.</param>
/// <param name="SetoresAtivos">Setores que podem ser escolhidos para lotação.</param>
/// <param name="GestoresPossiveis">Todas as outras pessoas ativas, por nome. Ciclo é barrado ao salvar.</param>
public sealed record PessoaParaEdicaoDto(
	PessoaDto Pessoa, IReadOnlyList<SetorDto> SetoresAtivos, IReadOnlyList<PessoaDto> GestoresPossiveis);

/// <summary>Dados para o formulário de edição completa de uma pessoa.</summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
public sealed class ObterPessoaParaEdicaoHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Pessoa a editar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PessoaParaEdicaoDto>> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<PessoaParaEdicaoDto>(ativos.Error);
		}

		var todosOsSetores = await ListarPessoasHandler.CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);
		var pessoas = MontadorDePessoas.Montar(ativos.Value, todosOsPerfis, todosOsSetores);

		var pessoa = pessoas.FirstOrDefault(candidata => candidata.UsuarioId == usuarioId);

		if (pessoa is null)
		{
			return Result.Failure<PessoaParaEdicaoDto>(IntranetErrors.Diretorio.PessoaNaoEncontrada);
		}

		IReadOnlyList<SetorDto> ativosParaEscolha =
			[.. todosOsSetores.Where(setor => setor.Ativo).OrderBy(setor => setor.Nome, StringComparer.OrdinalIgnoreCase)];

		IReadOnlyList<PessoaDto> gestores =
			[.. pessoas.Where(candidata => candidata.UsuarioId != usuarioId).OrderBy(candidata => candidata.Nome, StringComparer.OrdinalIgnoreCase)];

		return new PessoaParaEdicaoDto(pessoa, ativosParaEscolha, gestores);
	}
}
```

Registre no DI: `services.AddScoped<ObterPessoaParaEdicaoHandler>();`.

- [ ] **Step 2: Formulários e ViewModels**

```csharp
// src/Secco.Intranet.Web/Models/Diretorio/FormulariosDoDiretorio.cs
using System.ComponentModel.DataAnnotations;
using Secco.Intranet.Domain.Diretorio;

namespace Secco.Intranet.Web.Models.Diretorio;

/// <summary>
/// Formulário do <b>próprio colaborador</b>: só contato. Não tem cargo, setor nem gestor de
/// propósito — um campo forjado no corpo da requisição não tem onde ser ligado (o binder ignora
/// o que o modelo não declara), e o handler correspondente também não os conhece.
/// </summary>
public class EditarContatoForm
{
	/// <summary>Nome de exibição.</summary>
	[StringLength(PerfilColaborador.NomeMaxLength)]
	public string? Nome { get; set; }

	/// <summary>Ramal.</summary>
	[StringLength(PerfilColaborador.RamalMaxLength)]
	public string? Ramal { get; set; }

	/// <summary>Texto livre sobre a pessoa.</summary>
	[StringLength(PerfilColaborador.SobreMaxLength)]
	public string? Sobre { get; set; }
}

/// <summary>Formulário do <b>admin do diretório</b>: contato mais os dados funcionais.</summary>
public sealed class EditarPessoaForm : EditarContatoForm
{
	/// <summary>Cargo.</summary>
	[StringLength(PerfilColaborador.CargoMaxLength)]
	public string? Cargo { get; set; }

	/// <summary>Setor de lotação.</summary>
	public Guid? SetorId { get; set; }

	/// <summary>Id do gestor no SecureGate.</summary>
	public Guid? GestorUsuarioId { get; set; }
}
```

Acrescente a `DiretorioViewModel.cs`:

```csharp
/// <summary>Modelo de "Meu perfil".</summary>
/// <param name="Pessoa">A pessoa logada, com os valores atuais.</param>
/// <param name="Form">Formulário de contato.</param>
public sealed record MeuPerfilViewModel(PessoaDto Pessoa, EditarContatoForm Form);

/// <summary>Modelo da edição completa pelo admin.</summary>
/// <param name="Dados">Pessoa, setores e possíveis gestores.</param>
/// <param name="Form">Formulário completo.</param>
public sealed record EditarPessoaViewModel(PessoaParaEdicaoDto Dados, EditarPessoaForm Form);
```

- [ ] **Step 3: Testes de integração da edição (falham)**

```csharp
// tests/Secco.Intranet.Tests/Integration/DiretorioEdicaoTests.cs
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Edição do Diretório contra o banco real: o colaborador edita só o próprio contato, e o que ele
/// não pode editar — cargo, setor, gestor — não muda nem com o corpo forjado.
/// </summary>
public class DiretorioEdicaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(UsuariosParaDiretorioFalso usuarios, Guid? usuarioId, params string[] roles)
	{
		factory.UsuariosDoDiretorio = usuarios;
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		if (usuarioId is not null)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, usuarioId.ToString());
		}

		return client;
	}

	private async Task<PessoaDto?> LerAsync(UsuariosParaDiretorioFalso usuarios, Guid usuarioId)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		factory.UsuariosDoDiretorio = usuarios;

		var resultado = await escopo.ServiceProvider.GetRequiredService<ObterPessoaHandler>().HandleAsync(usuarioId);

		return resultado.IsSuccess ? resultado.Value.Pessoa : null;
	}

	private async Task<SetorDto> CriarSetorAsync(bool ativo = true)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var sufixo = Guid.NewGuid().ToString("N")[..8];

		var criado = await escopo.ServiceProvider.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand($"Setor {sufixo}", $"dir-{sufixo}", false, null));
		criado.IsSuccess.Should().BeTrue();

		if (!ativo)
		{
			var editado = await escopo.ServiceProvider.GetRequiredService<EditarSetorHandler>()
				.HandleAsync(new EditarSetorCommand(criado.Value.Id, criado.Value.Nome, criado.Value.Icone, false));
			editado.IsSuccess.Should().BeTrue();
		}

		return criado.Value;
	}

	[Fact]
	public async Task Meu_perfil_ColaboradorEditaOProprioContato_EGravaDeVerdade()
	{
		var eu = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com");
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");

		var resposta = await client.PostAsync("/diretorio/perfil", Form(
			("Nome", "Eva Souza"), ("Ramal", "2222"), ("Sobre", "Trabalho com dados."), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "salvar redireciona de volta para Meu perfil");
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Perfil atualizado");

		var lida = await LerAsync(usuarios, eu);
		lida!.Nome.Should().Be("Eva Souza");
		lida.Ramal.Should().Be("2222");
		lida.Sobre.Should().Be("Trabalho com dados.");
	}

	[Fact]
	public async Task Meu_perfil_ComCamposFuncionaisForjados_NaoAlteraCargoSetorNemGestor()
	{
		var eu = Guid.NewGuid();
		var chefe = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com").Com(chefe, "chefe@x.com");
		var setor = await CriarSetorAsync();

		// Estado inicial definido pelo admin.
		var admin = CriarCliente(usuarios, Guid.NewGuid(), "diretorio-admin");
		var tokenAdmin = await TokenAsync(admin, $"/diretorio/{eu}/editar");
		(await admin.PostAsync($"/diretorio/{eu}/editar", Form(
			("Nome", "Eva"), ("Cargo", "Analista"), ("SetorId", setor.Id.ToString()), ("GestorUsuarioId", chefe.ToString()),
			("__RequestVerificationToken", tokenAdmin)))).StatusCode.Should().Be(HttpStatusCode.OK);

		// O colaborador tenta se promover.
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");
		await client.PostAsync("/diretorio/perfil", Form(
			("Nome", "Eva Souza"),
			("Cargo", "Diretora Geral"),
			("SetorId", Guid.NewGuid().ToString()),
			("GestorUsuarioId", string.Empty),
			("__RequestVerificationToken", token)));

		var lida = await LerAsync(usuarios, eu);
		lida!.Nome.Should().Be("Eva Souza", "o contato é dela e mudou");
		lida.Cargo.Should().Be("Analista", "cargo forjado é ignorado");
		lida.SetorId.Should().Be(setor.Id, "setor forjado é ignorado");
		lida.GestorUsuarioId.Should().Be(chefe, "gestor forjado é ignorado");
	}

	[Fact]
	public async Task Meu_perfil_ComUsuarioIdForjado_EditaSoOProprio()
	{
		var eu = Guid.NewGuid();
		var outro = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com").Com(outro, "outro@x.com");
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");

		await client.PostAsync("/diretorio/perfil", Form(
			("Nome", "Eva"), ("UsuarioId", outro.ToString()), ("usuarioId", outro.ToString()), ("id", outro.ToString()),
			("__RequestVerificationToken", token)));

		(await LerAsync(usuarios, eu))!.Nome.Should().Be("Eva");
		(await LerAsync(usuarios, outro))!.TemPerfil.Should().BeFalse("o id de quem sou vem do claim, nunca do formulário");
	}

	[Fact]
	public async Task Meu_perfil_NomeAcimaDoLimite_MostraOErroENaoGrava()
	{
		var eu = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com");
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");

		var resposta = await client.PostAsync("/diretorio/perfil", Form(("Nome", new string('n', 121)), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await LerAsync(usuarios, eu))!.TemPerfil.Should().BeFalse();
	}

	[Fact]
	public async Task Meu_perfil_UsuarioNaoEstaEntreOsAtivos_404_SemQuebrar()
	{
		var eu = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com");
		var client = CriarCliente(usuarios, eu, "diretorio-user");
		var token = await TokenAsync(client, "/diretorio/perfil");
		usuarios.Usuarios.Clear();

		var resposta = await client.PostAsync("/diretorio/perfil", Form(("Nome", "Eva"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound, "quem deixou de ser um usuário ativo não tem perfil — igual ao GET");
	}

	[Fact]
	public async Task Admin_EditaCargoSetorEGestor_EGravaDeVerdade()
	{
		var alvo = Guid.NewGuid();
		var chefe = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(alvo, "alvo@x.com").Com(chefe, "chefe@x.com");
		var setor = await CriarSetorAsync();
		var client = CriarCliente(usuarios, Guid.NewGuid(), "diretorio-admin");
		var token = await TokenAsync(client, $"/diretorio/{alvo}/editar");

		var resposta = await client.PostAsync($"/diretorio/{alvo}/editar", Form(
			("Nome", "Alvo"), ("Cargo", "Coordenador"), ("SetorId", setor.Id.ToString()), ("GestorUsuarioId", chefe.ToString()),
			("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var lida = await LerAsync(usuarios, alvo);
		lida!.Cargo.Should().Be("Coordenador");
		lida.SetorId.Should().Be(setor.Id);
		lida.GestorUsuarioId.Should().Be(chefe);
	}

	[Fact]
	public async Task Admin_CicloDeGestor_RecusadoComToastDeErro()
	{
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(a, "a@x.com").Com(b, "b@x.com");
		var client = CriarCliente(usuarios, Guid.NewGuid(), "intranet-admin");
		var token = await TokenAsync(client, $"/diretorio/{b}/editar");
		await client.PostAsync($"/diretorio/{b}/editar", Form(("GestorUsuarioId", a.ToString()), ("__RequestVerificationToken", token)));

		var tokenA = await TokenAsync(client, $"/diretorio/{a}/editar");
		var resposta = await client.PostAsync($"/diretorio/{a}/editar", Form(("GestorUsuarioId", b.ToString()), ("__RequestVerificationToken", tokenA)));

		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("reportar, direta ou indiretamente, a si mesma");
		(await LerAsync(usuarios, a))!.GestorUsuarioId.Should().BeNull();
	}

	[Fact]
	public async Task Admin_SetorInativo_RecusadoParaLotacaoNova()
	{
		var alvo = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(alvo, "alvo@x.com");
		var inativo = await CriarSetorAsync(ativo: false);
		var client = CriarCliente(usuarios, Guid.NewGuid(), "diretorio-admin");
		var token = await TokenAsync(client, $"/diretorio/{alvo}/editar");

		var resposta = await client.PostAsync($"/diretorio/{alvo}/editar", Form(("SetorId", inativo.Id.ToString()), ("__RequestVerificationToken", token)));

		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Escolha um setor existente e ativo");
		(await LerAsync(usuarios, alvo))!.SetorId.Should().BeNull();
	}

	[Fact]
	public async Task Editar_TelaDoAdmin_MostraOsCampos()
	{
		var alvo = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(alvo, "alvo@x.com");
		var client = CriarCliente(usuarios, Guid.NewGuid(), "diretorio-admin");

		var html = await client.GetStringAsync($"/diretorio/{alvo}/editar");

		html.Should().Contain("name=\"Cargo\"").And.Contain("name=\"GestorUsuarioId\"").And.Contain("name=\"SetorId\"");
	}

	[Fact]
	public async Task Meu_perfil_TelaDoColaborador_NaoTemOsCamposFuncionais()
	{
		var eu = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com"), eu, "diretorio-user");

		var html = await client.GetStringAsync("/diretorio/perfil");

		html.Should().Contain("name=\"Nome\"").And.Contain("name=\"Ramal\"");
		html.Should().NotContain("name=\"Cargo\"").And.NotContain("name=\"GestorUsuarioId\"").And.NotContain("name=\"SetorId\"");
	}

	public static TheoryData<string[]> SemPermissaoParaEditarOsOutros => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-admin" },
		new[] { "inventario-admin" },
		new[] { "diretorio-user" },
	};

	[Theory]
	[MemberData(nameof(SemPermissaoParaEditarOsOutros))]
	public async Task EditarOutraPessoa_SemSerAdmin_403_MesmoSemToken(string[] roles)
	{
		var alvo = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(alvo, "alvo@x.com"), Guid.NewGuid(), roles);

		(await client.GetAsync($"/diretorio/{alvo}/editar")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await client.PostAsync($"/diretorio/{alvo}/editar", Form(("Cargo", "Chefe")))).StatusCode
			.Should().Be(HttpStatusCode.Forbidden, "403 do gate, e não 400 do antifalsificação");
	}

	[Theory]
	[InlineData("financeiro-admin")]
	[InlineData("inventario-admin")]
	public async Task MeuPerfil_SemNenhumDosTresPerfis_403_NoPost_MesmoSemToken(string role)
	{
		var eu = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(eu, "eu@x.com"), eu, role);

		var resposta = await client.PostAsync("/diretorio/perfil", Form(("Nome", "Eva")));

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Admin_PostSemToken_PassaDoGateEBateNoAntifalsificacao()
	{
		var alvo = Guid.NewGuid();
		var client = CriarCliente(new UsuariosParaDiretorioFalso().Com(alvo, "alvo@x.com"), Guid.NewGuid(), "diretorio-admin");

		var resposta = await client.PostAsync($"/diretorio/{alvo}/editar", Form(("Cargo", "X")));

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}
}
```

- [ ] **Step 4: Estender o controller**

Em `DiretorioController.cs`, troque o construtor, acrescente as actions e substitua a action `Perfil` da Task 6. O construtor:

```csharp
public sealed class DiretorioController(
	ListarPessoasHandler listarPessoas,
	ObterPessoaHandler obterPessoa,
	ObterPessoaParaEdicaoHandler obterPessoaParaEdicao,
	EditarContatoHandler editarContato,
	EditarDadosFuncionaisHandler editarDadosFuncionais) : Controller
```

(com os `<param>` correspondentes e `using Secco.Intranet.Web.ViewComponents;` para `FeedbackViewComponent`). Apague a action `Perfil()` antiga e acrescente:

```csharp
	/// <summary>"Meu perfil": o formulário de contato de quem está logado.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("perfil")]
	public async Task<IActionResult> Perfil(CancellationToken cancellationToken = default)
	{
		var id = AcessoAoDiretorio.UsuarioId(User);

		if (id is null)
		{
			return View("SemUsuario");
		}

		var resultado = await obterPessoa.HandleAsync(id.Value, cancellationToken);

		if (resultado.IsFailure)
		{
			return Falha(resultado.Error);
		}

		var pessoa = resultado.Value.Pessoa;

		return View(new MeuPerfilViewModel(pessoa, new EditarContatoForm { Nome = pessoa.TemPerfil ? pessoa.Nome : null, Ramal = pessoa.Ramal, Sobre = pessoa.Sobre }));
	}

	/// <summary>
	/// Salva o contato de quem está logado. O id vem <b>sempre</b> do claim <c>sub</c>, nunca do
	/// formulário, e o modelo só declara contato: cargo, setor e gestor forjados não têm onde entrar.
	/// </summary>
	/// <param name="form">Contato.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("perfil")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Perfil(EditarContatoForm form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var id = AcessoAoDiretorio.UsuarioId(User);

		if (id is null)
		{
			return View("SemUsuario");
		}

		if (ModelState.IsValid)
		{
			var salvo = await editarContato.HandleAsync(new EditarContatoCommand(id.Value, form.Nome, form.Ramal, form.Sobre), cancellationToken);

			if (salvo.IsSuccess)
			{
				TempData[FeedbackViewComponent.ChaveDaMensagem] = "Perfil atualizado.";

				return RedirectToAction(nameof(Perfil));
			}

			ModelState.AddModelError(string.Empty, salvo.Error.Description);
		}

		var atual = await obterPessoa.HandleAsync(id.Value, cancellationToken);

		return atual.IsFailure ? Falha(atual.Error) : View(new MeuPerfilViewModel(atual.Value.Pessoa, form));
	}

	/// <summary>Edição completa de uma pessoa pelo admin do diretório.</summary>
	/// <param name="id">Pessoa a editar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{id:guid}/editar")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	public async Task<IActionResult> Editar(Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await obterPessoaParaEdicao.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			return Falha(resultado.Error);
		}

		var pessoa = resultado.Value.Pessoa;

		return View(new EditarPessoaViewModel(
			resultado.Value,
			new EditarPessoaForm
			{
				Nome = pessoa.TemPerfil ? pessoa.Nome : null,
				Ramal = pessoa.Ramal,
				Sobre = pessoa.Sobre,
				Cargo = pessoa.Cargo,
				SetorId = pessoa.SetorId,
				GestorUsuarioId = pessoa.GestorUsuarioId,
			}));
	}

	/// <summary>Salva a edição completa. Dados funcionais primeiro (têm as regras); só se passarem, o contato.</summary>
	/// <param name="id">Pessoa a editar.</param>
	/// <param name="form">Contato e dados funcionais.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/editar")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Editar(Guid id, EditarPessoaForm form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (ModelState.IsValid)
		{
			var funcionais = await editarDadosFuncionais.HandleAsync(
				new EditarDadosFuncionaisCommand(id, form.Cargo, form.SetorId, form.GestorUsuarioId), cancellationToken);

			var resultado = funcionais.IsFailure
				? funcionais
				: await editarContato.HandleAsync(new EditarContatoCommand(id, form.Nome, form.Ramal, form.Sobre), cancellationToken);

			if (resultado.IsSuccess)
			{
				TempData[FeedbackViewComponent.ChaveDaMensagem] = "Dados atualizados.";

				return RedirectToAction(nameof(Pessoa), new { id });
			}

			// A resposta é a própria página (sem redirect), então o erro vai no ModelState e a view o
			// mostra no resumo de validação; TempData só apareceria no clique seguinte.
			ModelState.AddModelError(string.Empty, resultado.Error.Description);
		}

		var dados = await obterPessoaParaEdicao.HandleAsync(id, cancellationToken);

		return dados.IsFailure ? Falha(dados.Error) : View(new EditarPessoaViewModel(dados.Value, form));
	}
```

- [ ] **Step 5: Views**

```cshtml
@* src/Secco.Intranet.Web/Views/Diretorio/Perfil.cshtml *@
@using Secco.Intranet.Web.Models.Diretorio
@model MeuPerfilViewModel
@{
    ViewData["Title"] = "Meu perfil";

    var cabecalho = new PageHeaderModel(
        "Meu perfil",
        "Seu contato no diretório. Cargo, setor e gestor são mantidos pelo administrador do diretório.",
        Acoes: new[] { new PageActionModel("Ver como os outros veem", Url.Action("Pessoa", new { id = Model.Pessoa.UsuarioId })!, "bi-eye") });
}

<partial name="_PageHeader" model="cabecalho" />

<form method="post" asp-action="Perfil" class="sc-panel">
    <div asp-validation-summary="All" class="text-danger mb-3"></div>

    <div class="sc-form__field">
        <label class="form-label" for="Nome">Nome de exibição</label>
        <input class="form-control" id="Nome" name="Nome" value="@Model.Form.Nome" maxlength="120" placeholder="@Model.Pessoa.Email" />
        <div class="form-text">Se ficar em branco, o diretório mostra o seu e-mail.</div>
    </div>

    <div class="sc-form__field">
        <label class="form-label" for="Ramal">Ramal</label>
        <input class="form-control" id="Ramal" name="Ramal" value="@Model.Form.Ramal" maxlength="20" />
    </div>

    <div class="sc-form__field">
        <label class="form-label" for="Sobre">Sobre você</label>
        <textarea class="form-control" id="Sobre" name="Sobre" rows="4" maxlength="500">@Model.Form.Sobre</textarea>
    </div>

    <dl class="row mb-3">
        <dt class="col-sm-3">E-mail</dt>
        <dd class="col-sm-9"><span class="sc-meta">@Model.Pessoa.Email</span></dd>
        <dt class="col-sm-3">Cargo</dt>
        <dd class="col-sm-9">@(Model.Pessoa.Cargo ?? "—")</dd>
        <dt class="col-sm-3">Setor</dt>
        <dd class="col-sm-9 mb-0">@(Model.Pessoa.SetorNome ?? "—")</dd>
    </dl>

    <button class="btn btn-primary" type="submit">Salvar</button>
</form>
```

```cshtml
@* src/Secco.Intranet.Web/Views/Diretorio/Editar.cshtml *@
@using Secco.Intranet.Web.Models.Diretorio
@model EditarPessoaViewModel
@{
    var pessoa = Model.Dados.Pessoa;

    ViewData["Title"] = $"Editar {pessoa.Nome}";

    var cabecalho = new PageHeaderModel(
        $"Editar {pessoa.Nome}",
        pessoa.Email,
        Acoes: new[] { new PageActionModel("Voltar", Url.Action("Pessoa", new { id = pessoa.UsuarioId })!, "bi-arrow-left") });
}

<partial name="_PageHeader" model="cabecalho" />

<form method="post" asp-action="Editar" asp-route-id="@pessoa.UsuarioId" class="sc-panel">
    <div asp-validation-summary="All" class="text-danger mb-3"></div>

    <h2 class="h6">Contato</h2>

    <div class="sc-form__field">
        <label class="form-label" for="Nome">Nome de exibição</label>
        <input class="form-control" id="Nome" name="Nome" value="@Model.Form.Nome" maxlength="120" placeholder="@pessoa.Email" />
    </div>

    <div class="sc-form__field">
        <label class="form-label" for="Ramal">Ramal</label>
        <input class="form-control" id="Ramal" name="Ramal" value="@Model.Form.Ramal" maxlength="20" />
    </div>

    <div class="sc-form__field">
        <label class="form-label" for="Sobre">Sobre</label>
        <textarea class="form-control" id="Sobre" name="Sobre" rows="3" maxlength="500">@Model.Form.Sobre</textarea>
    </div>

    <h2 class="h6 mt-4">Dados funcionais</h2>

    <div class="sc-form__field">
        <label class="form-label" for="Cargo">Cargo</label>
        <input class="form-control" id="Cargo" name="Cargo" value="@Model.Form.Cargo" maxlength="120" />
    </div>

    <div class="sc-form__field">
        <label class="form-label" for="SetorId">Setor de lotação</label>
        <select class="form-select" id="SetorId" name="SetorId">
            <option value="">Sem setor</option>
            @foreach (var setor in Model.Dados.SetoresAtivos)
            {
                <option value="@setor.Id" selected="@(setor.Id == Model.Form.SetorId)">@setor.Nome</option>
            }
            @if (Model.Form.SetorId is { } atual && Model.Dados.SetoresAtivos.All(setor => setor.Id != atual))
            {
                <option value="@atual" selected>@(pessoa.SetorNome ?? "Setor desativado") (desativado)</option>
            }
        </select>
    </div>

    <div class="sc-form__field">
        <label class="form-label" for="GestorUsuarioId">Reporta a</label>
        <select class="form-select" id="GestorUsuarioId" name="GestorUsuarioId">
            <option value="">Ninguém</option>
            @foreach (var gestor in Model.Dados.GestoresPossiveis)
            {
                <option value="@gestor.UsuarioId" selected="@(gestor.UsuarioId == Model.Form.GestorUsuarioId)">@gestor.Nome</option>
            }
        </select>
        <div class="form-text">Não é possível criar um ciclo (A reporta a B e B reporta a A).</div>
    </div>

    <button class="btn btn-primary" type="submit">Salvar</button>
</form>
```

- [ ] **Step 6: Rodar e commitar**

Run: `dotnet build` (0 avisos) e `dotnet test` (suíte inteira verde, incluindo `DiretorioEdicaoTests`). Se `Meu_perfil_ComCamposFuncionaisForjados...` falhar, é o teste que prova a Review Focus nº 1 — **não** afrouxe: descubra qual campo entrou.

```bash
git add src/Secco.Intranet.Application src/Secco.Intranet.Web tests/Secco.Intranet.Tests
git commit -m "feat(diretorio): Meu perfil e edicao completa pelo admin, com campos funcionais fora do alcance do colaborador

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

## Task 9: Organograma

**Files:**
- Create: `src/Secco.Intranet.Application/Diretorio/ConstrutorDeOrganograma.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/MontarOrganogramaHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Modify: `src/Secco.Intranet.Web/Controllers/DiretorioController.cs`
- Modify: `src/Secco.Intranet.Web/Models/Diretorio/DiretorioViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Views/Diretorio/Index.cshtml`
- Create: `src/Secco.Intranet.Web/Views/Diretorio/Organograma.cshtml`, `Views/Diretorio/_NoOrganograma.cshtml`
- Create: `tests/Secco.Intranet.Tests/Unit/ConstrutorDeOrganogramaTests.cs`
- Create: `tests/Secco.Intranet.Tests/Integration/DiretorioOrganogramaTests.cs`

**Interfaces:**
- Consumes: `PessoaDto`, `MontadorDePessoas` (Task 5); `RegrasDeGestor.ProfundidadeMaxima` (Task 7).
- Produces: `NoDoOrganograma(PessoaDto Pessoa, IReadOnlyList<NoDoOrganograma> Equipe)`; `OrganogramaDto(IReadOnlyList<NoDoOrganograma> Raizes, IReadOnlyList<PessoaDto> SemPosicao)`; `ConstrutorDeOrganograma.Construir(IReadOnlyList<PessoaDto>) : OrganogramaDto`; `MontarOrganogramaHandler.HandleAsync(ct) : Task<Result<OrganogramaDto>>`; rota `GET /diretorio/organograma` (nível `Usuario`).

- [ ] **Step 1: Testes do construtor (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/ConstrutorDeOrganogramaTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application.Diretorio;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ConstrutorDeOrganogramaTests
{
	private static PessoaDto P(Guid id, string nome, Guid? gestor = null, bool gestorInativo = false) =>
		new(id, $"{nome}@x.com", nome, null, null, null, null, null, null, false, gestor, null, gestorInativo, true);

	private static string[] Nomes(IEnumerable<NoDoOrganograma> nos) => [.. nos.Select(no => no.Pessoa.Nome)];

	[Fact]
	public void ArvoreSimples_RaizComEquipeAninhada()
	{
		var ana = Guid.NewGuid();
		var bruno = Guid.NewGuid();
		var carla = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir([P(ana, "Ana"), P(bruno, "Bruno", ana), P(carla, "Carla", bruno)]);

		var raiz = organograma.Raizes.Should().ContainSingle().Subject;
		raiz.Pessoa.Nome.Should().Be("Ana");
		Nomes(raiz.Equipe).Should().Equal("Bruno");
		Nomes(raiz.Equipe[0].Equipe).Should().Equal("Carla");
		organograma.SemPosicao.Should().BeEmpty();
	}

	[Fact]
	public void QuemNaoTemGestorNemEquipe_VaiParaSemPosicao()
	{
		var ana = Guid.NewGuid();
		var solto = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir([P(ana, "Ana"), P(solto, "Solto")]);

		organograma.Raizes.Should().BeEmpty("Ana não tem equipe, então não é uma raiz");
		organograma.SemPosicao.Select(p => p.Nome).Should().BeEquivalentTo("Ana", "Solto");
	}

	[Fact]
	public void GestorInativo_AEquipeViraRaiz_ComAMarca()
	{
		var bruno = Guid.NewGuid();
		var carla = Guid.NewGuid();
		var fantasma = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir(
			[P(bruno, "Bruno", fantasma, gestorInativo: true), P(carla, "Carla", bruno)]);

		var raiz = organograma.Raizes.Should().ContainSingle().Subject;
		raiz.Pessoa.Nome.Should().Be("Bruno");
		raiz.Pessoa.GestorInativo.Should().BeTrue();
		Nomes(raiz.Equipe).Should().Equal("Carla");
	}

	[Fact]
	public void GestorInativoSemEquipe_VaiParaSemPosicao()
	{
		var bruno = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir([P(bruno, "Bruno", Guid.NewGuid(), gestorInativo: true)]);

		organograma.Raizes.Should().BeEmpty();
		organograma.SemPosicao.Should().ContainSingle(p => p.Nome == "Bruno");
	}

	[Fact]
	public void OrdenaPorNome_SemDiferenciarCaixa()
	{
		var chefe = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir(
			[P(chefe, "Chefe"), P(Guid.NewGuid(), "zélia", chefe), P(Guid.NewGuid(), "Ana", chefe), P(Guid.NewGuid(), "bruno", chefe)]);

		Nomes(organograma.Raizes[0].Equipe).Should().Equal("Ana", "bruno", "zélia");
	}

	[Fact]
	public void CicloJaGravadoNoBanco_NaoTravaENinguemSome()
	{
		// A reporta a B e B reporta a A, ambos ativos: nenhum é raiz. Sem a rede de segurança, os dois sumiriam.
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir([P(a, "A", b), P(b, "B", a)]);

		organograma.Raizes.Should().BeEmpty();
		organograma.SemPosicao.Select(p => p.Nome).Should().BeEquivalentTo("A", "B");
	}

	[Fact]
	public void CadeiaMaisFundaQueOTeto_TrunkaEColocaOResto_SemSumirNinguem()
	{
		var ids = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();
		var pessoas = ids.Select((id, i) => P(id, $"P{i:00}", i == 0 ? null : ids[i - 1])).ToList();

		var organograma = ConstrutorDeOrganograma.Construir(pessoas);

		var naArvore = Contar(organograma.Raizes);
		(naArvore + organograma.SemPosicao.Count).Should().Be(30, "toda pessoa aparece em exatamente um lugar");
		naArvore.Should().BeLessThanOrEqualTo(RegrasDeGestor.ProfundidadeMaxima + 1);
	}

	[Fact]
	public void ListaVazia_OrganogramaVazio()
	{
		var organograma = ConstrutorDeOrganograma.Construir([]);

		organograma.Raizes.Should().BeEmpty();
		organograma.SemPosicao.Should().BeEmpty();
	}

	private static int Contar(IEnumerable<NoDoOrganograma> nos) => nos.Sum(no => 1 + Contar(no.Equipe));
}
```

- [ ] **Step 2: Implementar o construtor e o handler**

```csharp
// src/Secco.Intranet.Application/Diretorio/ConstrutorDeOrganograma.cs
namespace Secco.Intranet.Application.Diretorio;

/// <summary>Um nó do organograma: uma pessoa e a equipe direta dela.</summary>
/// <param name="Pessoa">A pessoa.</param>
/// <param name="Equipe">Quem reporta diretamente, por nome.</param>
public sealed record NoDoOrganograma(PessoaDto Pessoa, IReadOnlyList<NoDoOrganograma> Equipe);

/// <summary>O organograma: as árvores e quem ficou fora delas.</summary>
/// <param name="Raizes">Quem tem equipe e não tem gestor ativo.</param>
/// <param name="SemPosicao">Quem não tem gestor ativo nem equipe — e qualquer um que a árvore não alcançou.</param>
public sealed record OrganogramaDto(IReadOnlyList<NoDoOrganograma> Raizes, IReadOnlyList<PessoaDto> SemPosicao);

/// <summary>
/// Monta a árvore por gestor. Função pura. Duas defesas para dado corrompido no banco: teto de
/// profundidade e uma rede de segurança que coloca em "sem posição" qualquer pessoa que a árvore
/// não alcançou (o caso típico é um ciclo gravado à mão, em que ninguém seria raiz).
/// </summary>
public static class ConstrutorDeOrganograma
{
	/// <summary>Monta o organograma a partir das pessoas ativas.</summary>
	/// <param name="pessoas">Pessoas ativas do diretório.</param>
	public static OrganogramaDto Construir(IReadOnlyList<PessoaDto> pessoas)
	{
		ArgumentNullException.ThrowIfNull(pessoas);

		// Só conta como equipe quem tem um gestor ATIVO (GestorInativo = gestor definido que saiu).
		var equipePorGestor = pessoas
			.Where(pessoa => pessoa.GestorUsuarioId is not null && !pessoa.GestorInativo)
			.GroupBy(pessoa => pessoa.GestorUsuarioId!.Value)
			.ToDictionary(grupo => grupo.Key, grupo => grupo.OrderBy(Nome, StringComparer.OrdinalIgnoreCase).ToList());

		var semGestorAtivo = pessoas.Where(pessoa => pessoa.GestorUsuarioId is null || pessoa.GestorInativo).ToList();

		var visitados = new HashSet<Guid>();

		IReadOnlyList<NoDoOrganograma> raizes =
		[
			.. semGestorAtivo
				.Where(pessoa => equipePorGestor.ContainsKey(pessoa.UsuarioId))
				.OrderBy(Nome, StringComparer.OrdinalIgnoreCase)
				.Select(pessoa => No(pessoa, 0)),
		];

		var semPosicao = semGestorAtivo
			.Where(pessoa => !equipePorGestor.ContainsKey(pessoa.UsuarioId))
			.ToList();

		foreach (var pessoa in semPosicao)
		{
			visitados.Add(pessoa.UsuarioId);
		}

		// Rede de segurança: quem não entrou em lugar nenhum (ciclo corrompido, ou cortado pelo teto).
		semPosicao.AddRange(pessoas.Where(pessoa => !visitados.Contains(pessoa.UsuarioId)));

		return new OrganogramaDto(raizes, [.. semPosicao.OrderBy(Nome, StringComparer.OrdinalIgnoreCase)]);

		NoDoOrganograma No(PessoaDto pessoa, int profundidade)
		{
			visitados.Add(pessoa.UsuarioId);

			var filhos = new List<NoDoOrganograma>();

			if (profundidade < RegrasDeGestor.ProfundidadeMaxima
				&& equipePorGestor.TryGetValue(pessoa.UsuarioId, out var equipe))
			{
				foreach (var membro in equipe.Where(membro => !visitados.Contains(membro.UsuarioId)))
				{
					filhos.Add(No(membro, profundidade + 1));
				}
			}

			return new NoDoOrganograma(pessoa, filhos);
		}
	}

	private static string Nome(PessoaDto pessoa) => pessoa.Nome;
}
```

Atenção ao teste `CadeiaMaisFundaQueOTeto`: a raiz tem profundidade 0 e o teto corta os filhos de quem está em `ProfundidadeMaxima`, então a árvore tem no máximo `ProfundidadeMaxima + 1` níveis; o resto vai, pela rede de segurança, para "sem posição". Se o conjunto `visitados` for consultado dentro do laço depois de a pessoa ser marcada, o teste de ciclo também passa.

```csharp
// src/Secco.Intranet.Application/Diretorio/MontarOrganogramaHandler.cs
using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Monta o organograma do tenant a partir dos usuários ativos e dos perfis locais.</summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
public sealed class MontarOrganogramaHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<OrganogramaDto>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<OrganogramaDto>(ativos.Error);
		}

		var todosOsSetores = await ListarPessoasHandler.CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);

		return ConstrutorDeOrganograma.Construir(MontadorDePessoas.Montar(ativos.Value, todosOsPerfis, todosOsSetores));
	}
}
```

Registre no DI: `services.AddScoped<MontarOrganogramaHandler>();`.

- [ ] **Step 3: Controller, ViewModel e views**

No `DiretorioController`, acrescente `MontarOrganogramaHandler montarOrganograma` ao construtor (e ao `<param>`) e a action (antes da `Falha`):

```csharp
	/// <summary>Organograma: árvore por gestor.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("organograma")]
	public async Task<IActionResult> Organograma(CancellationToken cancellationToken = default)
	{
		var resultado = await montarOrganograma.HandleAsync(cancellationToken);

		return resultado.IsFailure ? Falha(resultado.Error) : View(new OrganogramaViewModel(resultado.Value));
	}
```

Acrescente a `DiretorioViewModel.cs`:

```csharp
/// <summary>Modelo do organograma.</summary>
/// <param name="Organograma">Árvores e quem ficou fora delas.</param>
public sealed record OrganogramaViewModel(OrganogramaDto Organograma);
```

Em `Index.cshtml`, troque a criação do cabeçalho por uma com a ação do organograma:

```cshtml
    var cabecalho = new PageHeaderModel(
        "Diretório",
        "Quem trabalha em cada setor, com ramal e e-mail.",
        Acoes: new[] { new PageActionModel("Organograma", Url.Action("Organograma")!, "bi-diagram-3") });
```

```cshtml
@* src/Secco.Intranet.Web/Views/Diretorio/Organograma.cshtml *@
@using Secco.Intranet.Web.Models.Diretorio
@model OrganogramaViewModel
@{
    ViewData["Title"] = "Organograma";

    var cabecalho = new PageHeaderModel(
        "Organograma",
        "Quem reporta a quem. Clique para expandir ou recolher uma equipe.",
        Acoes: new[] { new PageActionModel("Voltar ao diretório", Url.Action("Index")!, "bi-arrow-left") });

    var vazio = new EmptyStateModel(
        "bi-diagram-3",
        "Ainda não há gestores definidos",
        "O administrador do diretório define, para cada pessoa, a quem ela reporta.");
}

<partial name="_PageHeader" model="cabecalho" />

@if (Model.Organograma.Raizes.Count == 0)
{
    <partial name="_EmptyState" model="vazio" />
}
else
{
    <div class="sc-panel">
        @foreach (var raiz in Model.Organograma.Raizes)
        {
            <partial name="_NoOrganograma" model="raiz" />
        }
    </div>
}

@if (Model.Organograma.SemPosicao.Count > 0)
{
    <details class="sc-panel mt-3">
        <summary>Sem posição no organograma (@Model.Organograma.SemPosicao.Count)</summary>
        <ul class="sc-list mt-2 mb-0">
            @foreach (var pessoa in Model.Organograma.SemPosicao)
            {
                <li class="sc-list__item">
                    <span class="sc-avatar" aria-hidden="true">@Iniciais.De(pessoa.Nome)</span>
                    <div class="sc-list__text">
                        <p class="sc-list__title">
                            <a class="text-reset text-decoration-none" asp-action="Pessoa" asp-route-id="@pessoa.UsuarioId">@pessoa.Nome</a>
                        </p>
                        @if (pessoa.GestorInativo)
                        {
                            <span class="sc-list__sub">Gestor inativo</span>
                        }
                    </div>
                </li>
            }
        </ul>
    </details>
}
```

```cshtml
@* src/Secco.Intranet.Web/Views/Diretorio/_NoOrganograma.cshtml *@
@using Secco.Intranet.Web.Models.Diretorio
@model Secco.Intranet.Application.Diretorio.NoDoOrganograma
@{
    var pessoa = Model.Pessoa;
}

@if (Model.Equipe.Count == 0)
{
    <div class="d-flex gap-2 align-items-center py-1 ps-3">
        <span class="sc-avatar" aria-hidden="true">@Iniciais.De(pessoa.Nome)</span>
        <a class="text-reset text-decoration-none" asp-action="Pessoa" asp-route-id="@pessoa.UsuarioId">@pessoa.Nome</a>
        @if (!string.IsNullOrWhiteSpace(pessoa.Cargo))
        {
            <span class="text-body-secondary small">@pessoa.Cargo</span>
        }
    </div>
}
else
{
    <details open class="ms-0 ps-0">
        <summary class="py-1">
            <span class="sc-avatar" aria-hidden="true">@Iniciais.De(pessoa.Nome)</span>
            <a class="text-reset text-decoration-none" asp-action="Pessoa" asp-route-id="@pessoa.UsuarioId">@pessoa.Nome</a>
            @if (!string.IsNullOrWhiteSpace(pessoa.Cargo))
            {
                <span class="text-body-secondary small">@pessoa.Cargo</span>
            }
            @if (pessoa.GestorInativo)
            {
                <span class="badge text-bg-secondary">gestor inativo</span>
            }
            <span class="text-body-secondary small">(@Model.Equipe.Count)</span>
        </summary>
        <div class="ms-4 border-start ps-2">
            @foreach (var membro in Model.Equipe)
            {
                <partial name="_NoOrganograma" model="membro" />
            }
        </div>
    </details>
}
```

- [ ] **Step 4: Testes de integração do organograma**

```csharp
// tests/Secco.Intranet.Tests/Integration/DiretorioOrganogramaTests.cs
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

public class DiretorioOrganogramaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(UsuariosParaDiretorioFalso? usuarios, params string[] roles)
	{
		factory.UsuariosDoDiretorio = usuarios;
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private async Task DefinirGestorAsync(UsuariosParaDiretorioFalso usuarios, Guid pessoa, Guid gestor, string? nome = null)
	{
		factory.UsuariosDoDiretorio = usuarios;
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		(await escopo.ServiceProvider.GetRequiredService<EditarDadosFuncionaisHandler>()
			.HandleAsync(new EditarDadosFuncionaisCommand(pessoa, null, null, gestor))).IsSuccess.Should().BeTrue();

		if (nome is not null)
		{
			(await escopo.ServiceProvider.GetRequiredService<EditarContatoHandler>()
				.HandleAsync(new EditarContatoCommand(pessoa, nome, null, null))).IsSuccess.Should().BeTrue();
		}
	}

	[Theory]
	[InlineData("financeiro-admin")]
	[InlineData("inventario-admin")]
	[InlineData("financeiro-user")]
	public async Task SemNenhumDosTresPerfis_403(string role)
	{
		var resposta = await CriarCliente(new UsuariosParaDiretorioFalso(), role).GetAsync("/diretorio/organograma");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task SemPerfil_403_ETambemSemRoleNenhuma()
	{
		var resposta = await CriarCliente(new UsuariosParaDiretorioFalso()).GetAsync("/diretorio/organograma");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task ComAcesso_MostraAChefiaComAEquipeAninhada()
	{
		var chefe = Guid.NewGuid();
		var membro = Guid.NewGuid();
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var usuarios = new UsuariosParaDiretorioFalso().Com(chefe, $"chefe{sufixo}@x.com").Com(membro, $"membro{sufixo}@x.com");
		await DefinirGestorAsync(usuarios, membro, chefe);

		var html = Decodificar(await CriarCliente(usuarios, "diretorio-user").GetStringAsync("/diretorio/organograma"));

		html.Should().Contain($"chefe{sufixo}@x.com").And.Contain($"membro{sufixo}@x.com");
		html.IndexOf($"chefe{sufixo}@x.com", StringComparison.Ordinal)
			.Should().BeLessThan(html.IndexOf($"membro{sufixo}@x.com", StringComparison.Ordinal), "a chefia vem antes da equipe");
	}

	[Fact]
	public async Task GestorDesativado_AEquipeApareceComAMarca()
	{
		var chefe = Guid.NewGuid();
		var membro = Guid.NewGuid();
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var todos = new UsuariosParaDiretorioFalso().Com(chefe, $"chefe{sufixo}@x.com").Com(membro, $"membro{sufixo}@x.com");
		await DefinirGestorAsync(todos, membro, chefe);

		// O chefe é desativado: sai da lista de usuários ativos, mas o perfil do membro ainda aponta para ele.
		var soMembro = new UsuariosParaDiretorioFalso().Com(membro, $"membro{sufixo}@x.com");
		var outro = Guid.NewGuid();
		soMembro.Com(outro, $"outro{sufixo}@x.com");
		await DefinirGestorAsync(soMembro.Com(Guid.NewGuid(), $"terceiro{sufixo}@x.com"), outro, membro);

		var html = Decodificar(await CriarCliente(soMembro, "diretorio-user").GetStringAsync("/diretorio/organograma"));

		html.Should().Contain("gestor inativo", "o membro perdeu o gestor, mas a equipe dele não some");
		html.Should().Contain($"outro{sufixo}@x.com");
	}

	[Fact]
	public async Task SemSecureGate_ExplicaSemQuebrar()
	{
		var resposta = await CriarCliente(usuarios: null, "diretorio-user").GetAsync("/diretorio/organograma");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
	}
}
```

- [ ] **Step 5: Rodar e commitar**

Run: `dotnet test --filter "ConstrutorDeOrganogramaTests|DiretorioOrganogramaTests"` → PASS; `dotnet build` 0 avisos; suíte toda verde.

```bash
git add src/Secco.Intranet.Application src/Secco.Intranet.Web tests/Secco.Intranet.Tests
git commit -m "feat(diretorio): organograma por gestor com rede de seguranca contra dado corrompido

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 10: Importação CSV — leitor e handler

**Files:**
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/LeitorDeCsvDoDiretorio.cs`
- Create: `src/Secco.Intranet.Application/Diretorio/ImportarDiretorioHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/LeitorDeCsvDoDiretorioTests.cs`
- Create: `tests/Secco.Intranet.Tests/Unit/ImportarDiretorioHandlerTests.cs`

**Interfaces:**
- Consumes: `IUsuariosParaDiretorio`, `IPerfilColaboradorRepository`, `ISetorRepository`, `RegrasDeGestor.CriariaCiclo`, `EdicaoDePerfil.AplicarAsync`, `ITrilhaDeAuditoria`, `VerbosDeAuditoria.DiretorioImportar`.
- Produces:
  - `LinhaDoCsv(int Numero, string Email, string? Nome, string? Cargo, string? Ramal, string? SetorSlug, string? GestorEmail)`, `ErroDeLinhaDoCsv(int Numero, string Mensagem)`, `LeituraDoCsv(IReadOnlyList<LinhaDoCsv> Linhas, IReadOnlyList<ErroDeLinhaDoCsv> Erros)`; `LeitorDeCsvDoDiretorio.Ler(string? texto) : Result<LeituraDoCsv>` (falha de arquivo = `Result.Failure`; falha de linha = item em `Erros`); constantes `MaximoDeLinhas = 5_000` e `MaximoDeCaracteres = 1_048_576`.
  - `enum StatusDaLinha { Criar, Atualizar, SemAlteracao, Erro }`; `LinhaDoRelatorio(int Numero, string Email, StatusDaLinha Status, string? Erro)`; `RelatorioDeImportacao(IReadOnlyList<LinhaDoRelatorio> Linhas, bool Aplicado)` com `Criados`, `Atualizados`, `SemAlteracao`, `ComErro`.
  - `ImportarDiretorioHandler.PrevisualizarAsync(ImportarDiretorioCommand, ct)` e `.AplicarAsync(ImportarDiretorioCommand, ct)` : `Task<Result<RelatorioDeImportacao>>`; `record ImportarDiretorioCommand(string Csv)`.
  - `IntranetErrors.Diretorio.CsvInvalido(string motivo)`.

- [ ] **Step 1: Erro do arquivo**

Em `IntranetErrors.Diretorio`, acrescente:

```csharp
		/// <summary>Arquivo CSV que não dá para ler (vazio, grande demais, cabeçalho inválido...).</summary>
		public static Error CsvInvalido(string motivo) =>
			Error.Validation("Intranet.Diretorio.CsvInvalido", $"Não foi possível ler o arquivo: {motivo}");
```

- [ ] **Step 2: Testes do leitor (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/LeitorDeCsvDoDiretorioTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application.Diretorio;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LeitorDeCsvDoDiretorioTests
{
	[Fact]
	public void PontoEVirgula_LeAsLinhas()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome;cargo;ramal;setor;gestor\nana@x.com;Ana;Diretora;2100;diretoria;\nbruno@x.com;Bruno;;;financeiro;ana@x.com\n");

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Erros.Should().BeEmpty();
		resultado.Value.Linhas.Should().HaveCount(2);
		var ana = resultado.Value.Linhas[0];
		ana.Email.Should().Be("ana@x.com");
		ana.Nome.Should().Be("Ana");
		ana.Cargo.Should().Be("Diretora");
		ana.Ramal.Should().Be("2100");
		ana.SetorSlug.Should().Be("diretoria");
		ana.GestorEmail.Should().BeNull("célula vazia vira nulo — significa 'não alterar'");
		resultado.Value.Linhas[1].GestorEmail.Should().Be("ana@x.com");
	}

	[Fact]
	public void Virgula_TambemFunciona()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email,nome\nana@x.com,Ana\n");

		resultado.Value.Linhas.Should().ContainSingle().Which.Nome.Should().Be("Ana");
	}

	[Fact]
	public void BomEQuebraDeLinhaWindows_SaoIgnorados()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("﻿email;nome\r\nana@x.com;Ana\r\nbruno@x.com;Bruno\r\n");

		resultado.Value.Linhas.Select(l => l.Email).Should().Equal("ana@x.com", "bruno@x.com");
	}

	[Fact]
	public void AspasComODelimitadorDentro_MantemOTextoInteiro()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome;cargo\nana@x.com;\"Ribeiro; Ana\";\"Diretora \"\"Geral\"\"\"\n");

		var linha = resultado.Value.Linhas.Single();
		linha.Nome.Should().Be("Ribeiro; Ana");
		linha.Cargo.Should().Be("Diretora \"Geral\"");
	}

	[Fact]
	public void LinhaEmBranco_EhIgnorada_MasANumeracaoContinuaContando()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome\nana@x.com;Ana\n\n;;\nbruno@x.com;Bruno\n");

		resultado.Value.Linhas.Select(l => l.Numero).Should().Equal(2, 5);
	}

	[Fact]
	public void CabecalhoEmMaiusculasEComEspacos_Aceita()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler(" EMAIL ; Nome \nana@x.com;Ana\n");

		resultado.Value.Linhas.Single().Nome.Should().Be("Ana");
	}

	[Fact]
	public void ColunaDesconhecida_FalhaOArquivoInteiro()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;salario\nana@x.com;10000\n");

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Description.Should().Contain("salario");
	}

	[Fact]
	public void SemAColunaEmail_Falha()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("nome;cargo\nAna;Diretora\n");

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Description.Should().Contain("email");
	}

	[Fact]
	public void CabecalhoComColunaRepetida_Falha()
	{
		LeitorDeCsvDoDiretorio.Ler("email;nome;nome\nana@x.com;A;B\n").IsFailure.Should().BeTrue();
	}

	[Fact]
	public void CelulaAMenos_TrataComoVazia_CelulaAMais_EErroDaLinha()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome;cargo\nana@x.com;Ana\nbruno@x.com;Bruno;Analista;sobrou\n");

		resultado.Value.Linhas.Should().ContainSingle(l => l.Email == "ana@x.com").Which.Cargo.Should().BeNull();
		resultado.Value.Erros.Should().ContainSingle(e => e.Numero == 3);
	}

	[Fact]
	public void EmailVazio_EErroDaLinha_ENaoDoArquivo()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome\n;Sem Email\nana@x.com;Ana\n");

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Erros.Should().ContainSingle(e => e.Numero == 2);
		resultado.Value.Linhas.Should().ContainSingle(l => l.Email == "ana@x.com");
	}

	[Fact]
	public void AspasSemFechar_FalhaOArquivo()
	{
		LeitorDeCsvDoDiretorio.Ler("email;nome\nana@x.com;\"Ana\n").IsFailure.Should().BeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   \n  ")]
	public void ArquivoVazio_Falha(string? texto)
	{
		LeitorDeCsvDoDiretorio.Ler(texto).IsFailure.Should().BeTrue();
	}

	[Fact]
	public void AcimaDoLimiteDeTamanho_Falha()
	{
		var enorme = "email;nome\n" + new string('a', LeitorDeCsvDoDiretorio.MaximoDeCaracteres);

		LeitorDeCsvDoDiretorio.Ler(enorme).IsFailure.Should().BeTrue();
	}

	[Fact]
	public void AcimaDoLimiteDeLinhas_Falha_EExatamenteNoLimite_Passa()
	{
		string Arquivo(int linhas) => "email\n" + string.Concat(Enumerable.Range(0, linhas).Select(i => $"u{i}@x.com\n"));

		LeitorDeCsvDoDiretorio.Ler(Arquivo(LeitorDeCsvDoDiretorio.MaximoDeLinhas)).IsSuccess.Should().BeTrue();
		LeitorDeCsvDoDiretorio.Ler(Arquivo(LeitorDeCsvDoDiretorio.MaximoDeLinhas + 1)).IsFailure.Should().BeTrue();
	}

	[Fact]
	public void SoOCabecalho_ArquivoValidoSemLinhas()
	{
		var resultado = LeitorDeCsvDoDiretorio.Ler("email;nome\n");

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Linhas.Should().BeEmpty();
	}
}
```

- [ ] **Step 3: Implementar o leitor**

```csharp
// src/Secco.Intranet.Application/Diretorio/LeitorDeCsvDoDiretorio.cs
using System.Text;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Uma linha lida do CSV. Célula vazia vira <c>null</c>, que na importação significa "não alterar".</summary>
/// <param name="Numero">Número do registro no arquivo (o cabeçalho é 1).</param>
/// <param name="Email">E-mail do usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Cargo">Cargo.</param>
/// <param name="Ramal">Ramal.</param>
/// <param name="SetorSlug">Slug do setor de lotação.</param>
/// <param name="GestorEmail">E-mail do gestor.</param>
public sealed record LinhaDoCsv(int Numero, string Email, string? Nome, string? Cargo, string? Ramal, string? SetorSlug, string? GestorEmail);

/// <summary>Erro de uma linha (o resto do arquivo segue valendo).</summary>
/// <param name="Numero">Número do registro no arquivo.</param>
/// <param name="Mensagem">O que está errado.</param>
public sealed record ErroDeLinhaDoCsv(int Numero, string Mensagem);

/// <summary>Resultado da leitura: as linhas boas e os erros de linha.</summary>
/// <param name="Linhas">Linhas lidas.</param>
/// <param name="Erros">Erros por linha.</param>
public sealed record LeituraDoCsv(IReadOnlyList<LinhaDoCsv> Linhas, IReadOnlyList<ErroDeLinhaDoCsv> Erros);

/// <summary>
/// Leitor do CSV do diretório. Cabeçalho <c>email;nome;cargo;ramal;setor;gestor</c> (só <c>email</c>
/// é obrigatório), delimitador <c>;</c> ou <c>,</c> detectado no cabeçalho, aspas do padrão CSV,
/// BOM tolerado. O conteúdo é tratado como texto puro: nada é interpretado ou avaliado. Erro do
/// <b>arquivo</b> (cabeçalho, limites, aspas abertas) derruba a leitura; erro de <b>linha</b> vira
/// item de <see cref="LeituraDoCsv.Erros"/>.
/// </summary>
public static class LeitorDeCsvDoDiretorio
{
	/// <summary>Máximo de linhas de dados.</summary>
	public const int MaximoDeLinhas = 5_000;

	/// <summary>Máximo de caracteres do arquivo (1 MB de texto).</summary>
	public const int MaximoDeCaracteres = 1_048_576;

	private static readonly string[] Conhecidas = ["email", "nome", "cargo", "ramal", "setor", "gestor"];

	/// <summary>Lê o texto do CSV.</summary>
	/// <param name="texto">Conteúdo do arquivo, já decodificado.</param>
	public static Result<LeituraDoCsv> Ler(string? texto)
	{
		if (string.IsNullOrWhiteSpace(texto))
		{
			return Falha("o arquivo está vazio.");
		}

		if (texto.Length > MaximoDeCaracteres)
		{
			return Falha("o arquivo passa do limite de 1 MB.");
		}

		var registros = Dividir(texto.TrimStart('﻿'), out var abertas);

		if (abertas)
		{
			return Falha("há aspas que não foram fechadas.");
		}

		var naoVazios = registros.Where(registro => registro.Celulas.Any(celula => !string.IsNullOrWhiteSpace(celula))).ToList();

		if (naoVazios.Count == 0)
		{
			return Falha("o arquivo está vazio.");
		}

		var cabecalho = naoVazios[0].Celulas.Select(celula => celula.Trim().ToLowerInvariant()).ToList();
		var desconhecidas = cabecalho.Where(coluna => coluna.Length > 0 && !Conhecidas.Contains(coluna)).ToList();

		if (desconhecidas.Count > 0)
		{
			return Falha($"coluna desconhecida: {string.Join(", ", desconhecidas)}. Use: {string.Join(", ", Conhecidas)}.");
		}

		if (cabecalho.Where(coluna => coluna.Length > 0).GroupBy(coluna => coluna).Any(grupo => grupo.Count() > 1))
		{
			return Falha("há colunas repetidas no cabeçalho.");
		}

		var indiceDoEmail = cabecalho.IndexOf("email");

		if (indiceDoEmail < 0)
		{
			return Falha("falta a coluna email no cabeçalho.");
		}

		var dados = naoVazios.Skip(1).ToList();

		if (dados.Count > MaximoDeLinhas)
		{
			return Falha($"o arquivo tem mais de {MaximoDeLinhas} linhas.");
		}

		var linhas = new List<LinhaDoCsv>();
		var erros = new List<ErroDeLinhaDoCsv>();

		foreach (var (numero, celulas) in dados)
		{
			if (celulas.Count > cabecalho.Count)
			{
				erros.Add(new ErroDeLinhaDoCsv(numero, "a linha tem mais colunas que o cabeçalho."));

				continue;
			}

			string? Valor(string coluna)
			{
				var indice = cabecalho.IndexOf(coluna);

				return indice >= 0 && indice < celulas.Count && !string.IsNullOrWhiteSpace(celulas[indice])
					? celulas[indice].Trim()
					: null;
			}

			var email = Valor("email");

			if (email is null)
			{
				erros.Add(new ErroDeLinhaDoCsv(numero, "o e-mail é obrigatório."));

				continue;
			}

			linhas.Add(new LinhaDoCsv(numero, email, Valor("nome"), Valor("cargo"), Valor("ramal"), Valor("setor"), Valor("gestor")));
		}

		return new LeituraDoCsv(linhas, erros);
	}

	private static Result<LeituraDoCsv> Falha(string motivo) =>
		Result.Failure<LeituraDoCsv>(IntranetErrors.Diretorio.CsvInvalido(motivo));

	private static List<(int Numero, List<string> Celulas)> Dividir(string texto, out bool aspasAbertas)
	{
		var delimitador = DetectarDelimitador(texto);
		var registros = new List<(int, List<string>)>();
		var celulas = new List<string>();
		var atual = new StringBuilder();
		var entreAspas = false;
		var numero = 1;

		for (var i = 0; i < texto.Length; i++)
		{
			var c = texto[i];

			if (entreAspas)
			{
				if (c == '"')
				{
					if (i + 1 < texto.Length && texto[i + 1] == '"')
					{
						atual.Append('"');
						i++;
					}
					else
					{
						entreAspas = false;
					}
				}
				else
				{
					atual.Append(c);
				}
			}
			else if (c == '"' && atual.Length == 0)
			{
				entreAspas = true;
			}
			else if (c == delimitador)
			{
				celulas.Add(atual.ToString());
				atual.Clear();
			}
			else if (c == '\n')
			{
				celulas.Add(atual.ToString());
				atual.Clear();
				registros.Add((numero, celulas));
				celulas = [];
				numero++;
			}
			else if (c != '\r')
			{
				atual.Append(c);
			}
		}

		if (atual.Length > 0 || celulas.Count > 0)
		{
			celulas.Add(atual.ToString());
			registros.Add((numero, celulas));
		}

		aspasAbertas = entreAspas;

		return registros;
	}

	private static char DetectarDelimitador(string texto)
	{
		var fim = texto.IndexOf('\n');
		var primeiraLinha = fim < 0 ? texto : texto[..fim];
		var pontoEVirgula = primeiraLinha.Count(c => c == ';');
		var virgula = primeiraLinha.Count(c => c == ',');

		return pontoEVirgula > 0 && pontoEVirgula >= virgula ? ';' : ',';
	}
}
```

Atenção: o teste `LinhaEmBranco_EhIgnorada_MasANumeracaoContinuaContando` espera os números `2` e `5` — `Dividir` numera **todo** registro (inclusive os em branco) e a filtragem de vazios acontece depois, então a numeração continua a do arquivo.

- [ ] **Step 4: Testes do handler de importação (falham)**

```csharp
// tests/Secco.Intranet.Tests/Unit/ImportarDiretorioHandlerTests.cs
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ImportarDiretorioHandlerTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();

	private const string Cabecalho = "email;nome;cargo;ramal;setor;gestor\n";

	private sealed record Ambiente(
		ImportarDiretorioHandler Handler,
		PerfisColaboradorFalso Perfis,
		TrilhaDeAcessoFalsa Trilha,
		SetoresFalsos Setores,
		UsuariosParaDiretorioFalso Usuarios);

	private static Ambiente Montar(Action<SetoresFalsos>? setores = null, Action<PerfisColaboradorFalso>? perfis = null)
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com").Com(Carla, "carla@x.com");
		var setoresFalsos = new SetoresFalsos();
		setores?.Invoke(setoresFalsos);
		var perfisFalsos = new PerfisColaboradorFalso();
		perfis?.Invoke(perfisFalsos);
		var trilha = new TrilhaDeAcessoFalsa();

		return new Ambiente(new ImportarDiretorioHandler(usuarios, perfisFalsos, setoresFalsos, trilha), perfisFalsos, trilha, setoresFalsos, usuarios);
	}

	[Fact]
	public async Task Previsualizar_NaoGravaNemAudita_MasDiz_O_Que_Faria()
	{
		var ambiente = Montar(setores => setores.Com("Financeiro", "financeiro"));

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana Ribeiro;Controller;2100;financeiro;\nbruno@x.com;Bruno;;;;ana@x.com\n"));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Aplicado.Should().BeFalse();
		resultado.Value.Criados.Should().Be(2);
		ambiente.Perfis.Perfis.Should().BeEmpty();
		ambiente.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Aplicar_CriaOsPerfisEAuditaUmaVezComOsTotais()
	{
		var ambiente = Montar(setores => setores.Com("Financeiro", "financeiro"));

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana Ribeiro;Controller;2100;financeiro;\nbruno@x.com;Bruno;;;;ana@x.com\n"));

		resultado.Value.Aplicado.Should().BeTrue();
		ambiente.Perfis.Perfis.Should().HaveCount(2);
		var ana = ambiente.Perfis.Perfis.Single(p => p.UsuarioId == Ana);
		ana.NomeExibicao.Should().Be("Ana Ribeiro");
		ana.Cargo.Should().Be("Controller");
		ana.SetorId.Should().NotBeNull();
		ambiente.Perfis.Perfis.Single(p => p.UsuarioId == Bruno).GestorUsuarioId.Should().Be(Ana);

		var registro = ambiente.Trilha.Registros.Should().ContainSingle("um registro por importação, não por linha").Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.DiretorioImportar);
		registro.Metadata.Should().Contain("\"criados\":2");
		registro.Metadata.Should().NotContain("Ana Ribeiro");
	}

	[Fact]
	public async Task Reimportar_OMesmoArquivo_NaoMudaNada()
	{
		var ambiente = Montar();
		var csv = new ImportarDiretorioCommand(Cabecalho + "ana@x.com;Ana;Diretora;2100;;\n");
		await ambiente.Handler.AplicarAsync(csv);

		var segunda = await ambiente.Handler.AplicarAsync(csv);

		segunda.Value.SemAlteracao.Should().Be(1);
		segunda.Value.Criados.Should().Be(0);
		segunda.Value.Atualizados.Should().Be(0);
	}

	[Fact]
	public async Task CelulaVazia_MantemOValorAtual_NaoLimpa()
	{
		var existente = new PerfilColaborador(Ana);
		existente.EditarContato("Ana", "2100", null);
		existente.EditarDadosFuncionais("Diretora", null, Bruno);
		var ambiente = Montar(perfis: perfis => perfis.Com(existente));

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;;Presidente;;;\n"));

		resultado.Value.Atualizados.Should().Be(1);
		existente.NomeExibicao.Should().Be("Ana");
		existente.Ramal.Should().Be("2100");
		existente.Cargo.Should().Be("Presidente");
		existente.GestorUsuarioId.Should().Be(Bruno, "gestor vazio no CSV não limpa o gestor atual");
	}

	[Fact]
	public async Task LinhaSoComEmail_SemPerfil_NaoCriaPerfilVazio()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;;;;;\n"));

		resultado.Value.SemAlteracao.Should().Be(1);
		ambiente.Perfis.Perfis.Should().BeEmpty();
	}

	[Fact]
	public async Task EmailQueNaoEUsuarioAtivo_ErroDaLinha_OutrasLinhasSeguem()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "fantasma@x.com;Fantasma;;;;\nana@x.com;Ana;;;;\n"));

		resultado.Value.ComErro.Should().Be(1);
		resultado.Value.Linhas.Single(l => l.Status == StatusDaLinha.Erro).Erro.Should().Contain("usuário ativo");
		ambiente.Perfis.Perfis.Should().ContainSingle(p => p.UsuarioId == Ana);
	}

	[Fact]
	public async Task EmailRepetidoNoArquivo_SegundaLinhaDaErro()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana;;;;\nANA@x.com;Outra;;;;\n"));

		resultado.Value.Linhas[0].Status.Should().Be(StatusDaLinha.Criar);
		resultado.Value.Linhas[1].Status.Should().Be(StatusDaLinha.Erro);
		resultado.Value.Linhas[1].Erro.Should().Contain("repetido");
	}

	[Fact]
	public async Task SetorInexistenteOuInativo_ErroDaLinha()
	{
		var ambiente = Montar(setores => setores.Com("Antigo", "antigo", ativo: false));

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;;;;nao-existe;\nbruno@x.com;;;;antigo;\n"));

		resultado.Value.ComErro.Should().Be(2);
	}

	[Fact]
	public async Task GestorQueNaoEUsuarioAtivo_EAutogestor_ErroDaLinha()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;;;;;fantasma@x.com\nbruno@x.com;;;;;bruno@x.com\n"));

		resultado.Value.Linhas.Should().OnlyContain(l => l.Status == StatusDaLinha.Erro);
	}

	[Fact]
	public async Task GestorQueSoApareceMaisAbaixoNoArquivo_Funciona()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "bruno@x.com;Bruno;;;;ana@x.com\nana@x.com;Ana;;;;\n"));

		resultado.Value.ComErro.Should().Be(0);
		ambiente.Perfis.Perfis.Single(p => p.UsuarioId == Bruno).GestorUsuarioId.Should().Be(Ana);
	}

	[Fact]
	public async Task CicloDentroDoMesmoLote_AUltimaLinhaQueOFechaDaErro()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "bruno@x.com;;;;;ana@x.com\nana@x.com;;;;;bruno@x.com\n"));

		resultado.Value.Linhas[0].Status.Should().Be(StatusDaLinha.Criar);
		resultado.Value.Linhas[1].Status.Should().Be(StatusDaLinha.Erro);
		resultado.Value.Linhas[1].Erro.Should().Contain("ciclo");
		ambiente.Perfis.Perfis.Should().ContainSingle(p => p.UsuarioId == Bruno);
	}

	[Fact]
	public async Task CicloComPerfilJaExistenteNoBanco_Recusa()
	{
		var bruno = new PerfilColaborador(Bruno);
		bruno.EditarDadosFuncionais(null, null, Ana);
		var ambiente = Montar(perfis: perfis => perfis.Com(bruno));

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;;;;;bruno@x.com\n"));

		resultado.Value.Linhas.Single().Status.Should().Be(StatusDaLinha.Erro);
	}

	[Fact]
	public async Task CamposAcimaDoLimite_ErroDaLinha()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + $"ana@x.com;{new string('n', PerfilColaborador.NomeMaxLength + 1)};;;;\n"));

		resultado.Value.Linhas.Single().Status.Should().Be(StatusDaLinha.Erro);
	}

	[Fact]
	public async Task ArquivoInvalido_FalhaSemGravarNada()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand("email;salario\nana@x.com;1\n"));

		resultado.IsFailure.Should().BeTrue();
		ambiente.Perfis.Perfis.Should().BeEmpty();
		ambiente.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task SecureGateForaDoAr_PropagaOErro()
	{
		var ambiente = Montar();
		ambiente.Usuarios.FalharCom = IntranetErrors.Acesso.Indisponivel;

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;Ana;;;;\n"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task ErrosDoLeitor_AparecemNoRelatorioComONumeroDaLinha()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(Cabecalho + ";Sem email;;;;\nana@x.com;Ana;;;;\n"));

		resultado.Value.Linhas.Should().Contain(l => l.Numero == 2 && l.Status == StatusDaLinha.Erro);
		resultado.Value.Linhas.Should().Contain(l => l.Numero == 3 && l.Status == StatusDaLinha.Criar);
	}
}
```

- [ ] **Step 5: Implementar o handler**

```csharp
// src/Secco.Intranet.Application/Diretorio/ImportarDiretorioHandler.cs
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Pedido de importação: o texto do CSV, já decodificado.</summary>
/// <param name="Csv">Conteúdo do arquivo.</param>
public sealed record ImportarDiretorioCommand(string Csv);

/// <summary>O que acontece (ou aconteceu) com uma linha.</summary>
public enum StatusDaLinha
{
	/// <summary>Cria um perfil novo.</summary>
	Criar = 0,

	/// <summary>Altera um perfil existente.</summary>
	Atualizar = 1,

	/// <summary>Nada muda.</summary>
	SemAlteracao = 2,

	/// <summary>A linha tem erro e não é aplicada.</summary>
	Erro = 3,
}

/// <summary>Uma linha do relatório de importação.</summary>
/// <param name="Numero">Número do registro no arquivo.</param>
/// <param name="Email">E-mail da linha (vazio se a linha nem chegou a ter).</param>
/// <param name="Status">O que acontece com ela.</param>
/// <param name="Erro">Motivo, quando <see cref="StatusDaLinha.Erro"/>.</param>
public sealed record LinhaDoRelatorio(int Numero, string Email, StatusDaLinha Status, string? Erro);

/// <summary>Relatório da pré-visualização ou da aplicação.</summary>
/// <param name="Linhas">Uma entrada por linha do arquivo, em ordem.</param>
/// <param name="Aplicado">Falso na pré-visualização; verdadeiro depois de gravar.</param>
public sealed record RelatorioDeImportacao(IReadOnlyList<LinhaDoRelatorio> Linhas, bool Aplicado)
{
	/// <summary>Perfis que serão (ou foram) criados.</summary>
	public int Criados => Linhas.Count(linha => linha.Status == StatusDaLinha.Criar);

	/// <summary>Perfis que serão (ou foram) alterados.</summary>
	public int Atualizados => Linhas.Count(linha => linha.Status == StatusDaLinha.Atualizar);

	/// <summary>Linhas que não mudam nada.</summary>
	public int SemAlteracao => Linhas.Count(linha => linha.Status == StatusDaLinha.SemAlteracao);

	/// <summary>Linhas com erro.</summary>
	public int ComErro => Linhas.Count(linha => linha.Status == StatusDaLinha.Erro);
}

/// <summary>
/// Importa o diretório de um CSV. <b>Não cria usuário</b>: o e-mail precisa ser de um usuário ativo
/// do SecureGate. Duas etapas sobre o mesmo planejamento: <see cref="PrevisualizarAsync"/> só valida
/// e conta; <see cref="AplicarAsync"/> planeja <b>de novo</b> (nunca confia numa pré-visualização
/// antiga) e grava as linhas válidas. Célula vazia significa "não alterar".
/// </summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class ImportarDiretorioHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores,
	ITrilhaDeAuditoria trilha)
{
	private sealed record LinhaPlanejada(
		LinhaDoRelatorio Relatorio,
		Guid UsuarioId,
		string? Nome,
		string? Cargo,
		string? Ramal,
		Guid? SetorId,
		Guid? GestorId);

	/// <summary>Valida o arquivo e conta o que aconteceria — sem gravar nada.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<RelatorioDeImportacao>> PrevisualizarAsync(ImportarDiretorioCommand command, CancellationToken cancellationToken = default)
	{
		var plano = await PlanejarAsync(command, cancellationToken).ConfigureAwait(false);

		return plano.IsFailure
			? Result.Failure<RelatorioDeImportacao>(plano.Error)
			: new RelatorioDeImportacao([.. plano.Value.Select(linha => linha.Relatorio)], Aplicado: false);
	}

	/// <summary>Planeja de novo e grava as linhas válidas; audita uma vez, com os totais.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<RelatorioDeImportacao>> AplicarAsync(ImportarDiretorioCommand command, CancellationToken cancellationToken = default)
	{
		var plano = await PlanejarAsync(command, cancellationToken).ConfigureAwait(false);

		if (plano.IsFailure)
		{
			return Result.Failure<RelatorioDeImportacao>(plano.Error);
		}

		foreach (var linha in plano.Value.Where(linha => linha.Relatorio.Status is StatusDaLinha.Criar or StatusDaLinha.Atualizar))
		{
			await EdicaoDePerfil.AplicarAsync(
				perfis,
				linha.UsuarioId,
				perfil => [
					.. perfil.EditarContato(linha.Nome ?? perfil.NomeExibicao, linha.Ramal ?? perfil.Ramal, perfil.Sobre),
					.. perfil.EditarDadosFuncionais(linha.Cargo ?? perfil.Cargo, linha.SetorId ?? perfil.SetorId, linha.GestorId ?? perfil.GestorUsuarioId),
				],
				cancellationToken).ConfigureAwait(false);
		}

		var relatorio = new RelatorioDeImportacao([.. plano.Value.Select(linha => linha.Relatorio)], Aplicado: true);

		await trilha.RegistrarAsync(
			new RegistroDeAuditoria(
				VerbosDeAuditoria.DiretorioImportar,
				RecursosDeAuditoria.Diretorio,
				"importacao",
				JsonSerializer.Serialize(new
				{
					criados = relatorio.Criados,
					atualizados = relatorio.Atualizados,
					semAlteracao = relatorio.SemAlteracao,
					comErro = relatorio.ComErro,
				})),
			cancellationToken).ConfigureAwait(false);

		return relatorio;
	}

	private async Task<Result<IReadOnlyList<LinhaPlanejada>>> PlanejarAsync(ImportarDiretorioCommand command, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(command);

		var leitura = LeitorDeCsvDoDiretorio.Ler(command.Csv);

		if (leitura.IsFailure)
		{
			return Result.Failure<IReadOnlyList<LinhaPlanejada>>(leitura.Error);
		}

		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<IReadOnlyList<LinhaPlanejada>>(ativos.Error);
		}

		var porEmail = ativos.Value
			.Where(usuario => !string.IsNullOrWhiteSpace(usuario.Email))
			.GroupBy(usuario => usuario.Email, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(grupo => grupo.Key, grupo => grupo.First(), StringComparer.OrdinalIgnoreCase);

		var todosOsSetores = await ListarPessoasHandler.CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var setorPorSlug = todosOsSetores
			.Where(setor => setor.Ativo)
			.ToDictionary(setor => setor.Slug, setor => setor.Id, StringComparer.OrdinalIgnoreCase);

		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);
		var perfilPorUsuario = todosOsPerfis.GroupBy(perfil => perfil.UsuarioId).ToDictionary(grupo => grupo.Key, grupo => grupo.First());
		var mapaDeGestores = todosOsPerfis
			.Where(perfil => perfil.GestorUsuarioId is not null)
			.ToDictionary(perfil => perfil.UsuarioId, perfil => perfil.GestorUsuarioId!.Value);

		var planejadas = new List<LinhaPlanejada>();
		var vistos = new HashSet<Guid>();

		foreach (var erro in leitura.Value.Erros)
		{
			planejadas.Add(Rejeitada(erro.Numero, string.Empty, erro.Mensagem));
		}

		foreach (var linha in leitura.Value.Linhas)
		{
			planejadas.Add(Planejar(linha, porEmail, setorPorSlug, perfilPorUsuario, mapaDeGestores, vistos));
		}

		return planejadas.OrderBy(linha => linha.Relatorio.Numero).ToList();
	}

	private static LinhaPlanejada Planejar(
		LinhaDoCsv linha,
		Dictionary<string, UsuarioParaDiretorio> porEmail,
		Dictionary<string, Guid> setorPorSlug,
		Dictionary<Guid, PerfilColaborador> perfilPorUsuario,
		Dictionary<Guid, Guid> mapaDeGestores,
		HashSet<Guid> vistos)
	{
		if (!porEmail.TryGetValue(linha.Email, out var usuario))
		{
			return Rejeitada(linha.Numero, linha.Email, "o e-mail não é de um usuário ativo do SecureGate.");
		}

		if (!vistos.Add(usuario.Id))
		{
			return Rejeitada(linha.Numero, linha.Email, "e-mail repetido no arquivo.");
		}

		if ((linha.Nome?.Length ?? 0) > PerfilColaborador.NomeMaxLength
			|| (linha.Cargo?.Length ?? 0) > PerfilColaborador.CargoMaxLength
			|| (linha.Ramal?.Length ?? 0) > PerfilColaborador.RamalMaxLength)
		{
			return Rejeitada(linha.Numero, linha.Email, "nome, cargo ou ramal acima do limite de caracteres.");
		}

		Guid? setorId = null;

		if (linha.SetorSlug is not null)
		{
			if (!setorPorSlug.TryGetValue(linha.SetorSlug, out var encontrado))
			{
				return Rejeitada(linha.Numero, linha.Email, $"o setor '{linha.SetorSlug}' não existe ou está inativo.");
			}

			setorId = encontrado;
		}

		Guid? gestorId = null;

		if (linha.GestorEmail is not null)
		{
			if (!porEmail.TryGetValue(linha.GestorEmail, out var gestor))
			{
				return Rejeitada(linha.Numero, linha.Email, $"o gestor '{linha.GestorEmail}' não é um usuário ativo.");
			}

			if (gestor.Id == usuario.Id)
			{
				return Rejeitada(linha.Numero, linha.Email, "ninguém pode ser gestor de si mesmo.");
			}

			if (RegrasDeGestor.CriariaCiclo(mapaDeGestores, usuario.Id, gestor.Id))
			{
				return Rejeitada(linha.Numero, linha.Email, "essa escolha de gestor criaria um ciclo.");
			}

			// A resolução é sequencial: as linhas seguintes já enxergam este gestor.
			mapaDeGestores[usuario.Id] = gestor.Id;
			gestorId = gestor.Id;
		}

		perfilPorUsuario.TryGetValue(usuario.Id, out var atual);

		var mudou = Mudou(atual?.NomeExibicao, linha.Nome)
			|| Mudou(atual?.Cargo, linha.Cargo)
			|| Mudou(atual?.Ramal, linha.Ramal)
			|| (setorId is not null && setorId != atual?.SetorId)
			|| (gestorId is not null && gestorId != atual?.GestorUsuarioId);

		var status = !mudou ? StatusDaLinha.SemAlteracao : atual is null ? StatusDaLinha.Criar : StatusDaLinha.Atualizar;

		return new LinhaPlanejada(
			new LinhaDoRelatorio(linha.Numero, linha.Email, status, null),
			usuario.Id,
			linha.Nome,
			linha.Cargo,
			linha.Ramal,
			setorId,
			gestorId);
	}

	private static bool Mudou(string? atual, string? novo) =>
		novo is not null && !string.Equals(atual, novo, StringComparison.Ordinal);

	private static LinhaPlanejada Rejeitada(int numero, string email, string motivo) =>
		new(new LinhaDoRelatorio(numero, email, StatusDaLinha.Erro, motivo), Guid.Empty, null, null, null, null, null);
}
```

Registre no DI: `services.AddScoped<ImportarDiretorioHandler>();`.

- [ ] **Step 6: Rodar e commitar**

Run: `dotnet test --filter "LeitorDeCsvDoDiretorioTests|ImportarDiretorioHandlerTests"` → PASS; `dotnet build` 0 avisos.

```bash
git add src/Secco.Intranet.Application tests/Secco.Intranet.Tests/Unit/LeitorDeCsvDoDiretorioTests.cs tests/Secco.Intranet.Tests/Unit/ImportarDiretorioHandlerTests.cs
git commit -m "feat(diretorio): leitor de CSV e handler de importacao em duas etapas

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

## Task 11: Web — tela de importação CSV

**Files:**
- Modify: `src/Secco.Intranet.Web/Controllers/DiretorioController.cs`
- Modify: `src/Secco.Intranet.Web/Models/Diretorio/DiretorioViewModel.cs`
- Modify: `src/Secco.Intranet.Web/Views/Diretorio/Index.cshtml` (botão "Importar" só para o admin)
- Create: `src/Secco.Intranet.Web/Views/Diretorio/Importar.cshtml`, `Views/Diretorio/ImportarRelatorio.cshtml`
- Create: `tests/Secco.Intranet.Tests/Integration/DiretorioImportacaoTests.cs`

**Interfaces:**
- Consumes: `ImportarDiretorioHandler`, `ImportarDiretorioCommand`, `RelatorioDeImportacao`, `StatusDaLinha` (Task 10); `[ExigeNivelNoDiretorio(Administrador)]` (Task 3).
- Produces: `GET /diretorio/importar`; `POST /diretorio/importar` (multipart, campo `arquivo` → pré-visualização, **não grava**); `POST /diretorio/importar/aplicar` (campo `csv` → revalida e grava); `ImportacaoViewModel(RelatorioDeImportacao Relatorio, string Csv)`. Todas exigem nível `Administrador`.

- [ ] **Step 1: Testes (falham)**

```csharp
// tests/Secco.Intranet.Tests/Integration/DiretorioImportacaoTests.cs
using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

public class DiretorioImportacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(UsuariosParaDiretorioFalso? usuarios, params string[] roles)
	{
		factory.UsuariosDoDiretorio = usuarios;
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private async Task<bool> TemPerfilAsync(UsuariosParaDiretorioFalso usuarios, Guid usuarioId)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		factory.UsuariosDoDiretorio = usuarios;

		var resultado = await escopo.ServiceProvider.GetRequiredService<ObterPessoaHandler>().HandleAsync(usuarioId);

		return resultado.IsSuccess && resultado.Value.Pessoa.TemPerfil;
	}

	private static MultipartFormDataContent Arquivo(string token, string conteudo, string nome = "pessoas.csv")
	{
		var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes(conteudo));
		arquivo.Headers.ContentType = new("text/csv");

		return new MultipartFormDataContent
		{
			{ new StringContent(token), "__RequestVerificationToken" },
			{ arquivo, "arquivo", nome },
		};
	}

	public static TheoryData<string[]> SemPermissaoDeAdmin => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-admin" },
		new[] { "inventario-admin" },
		new[] { "diretorio-user" },
	};

	[Theory]
	[MemberData(nameof(SemPermissaoDeAdmin))]
	public async Task SemSerAdminDoDiretorio_ToDaRotaDaImportacao_403(string[] roles)
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), roles);

		(await client.GetAsync("/diretorio/importar")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await client.PostAsync("/diretorio/importar", Arquivo("x", "email\na@x.com\n"))).StatusCode
			.Should().Be(HttpStatusCode.Forbidden, "403 do gate, e não 400 do antifalsificação");
		(await client.PostAsync("/diretorio/importar/aplicar", Form(("csv", "email\na@x.com\n")))).StatusCode
			.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Admin_TelaDeImportacaoAbre()
	{
		var resposta = await CriarCliente(new UsuariosParaDiretorioFalso(), "diretorio-admin").GetAsync("/diretorio/importar");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("email;nome;cargo;ramal;setor;gestor");
	}

	[Fact]
	public async Task Previsualizar_MostraOsTotais_ENaoGravaNada()
	{
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var ana = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(ana, $"ana{sufixo}@x.com");
		var client = CriarCliente(usuarios, "intranet-admin");
		var token = await TokenAsync(client, "/diretorio/importar");

		var resposta = await client.PostAsync("/diretorio/importar", Arquivo(token,
			$"email;nome;cargo\nana{sufixo}@x.com;Ana;Diretora\nfantasma{sufixo}@x.com;Fantasma;\n"));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var html = Decodificar(await resposta.Content.ReadAsStringAsync());
		html.Should().Contain("data-total=\"criados\">1<").And.Contain("data-total=\"erros\">1<");
		html.Should().Contain("usuário ativo", "o motivo do erro da linha aparece");
		html.Should().Contain("name=\"csv\"", "há o formulário de confirmação");
		(await TemPerfilAsync(usuarios, ana)).Should().BeFalse("a pré-visualização não grava");
	}

	[Fact]
	public async Task Aplicar_GravaDeVerdade_EMostraORelatorioFinal()
	{
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var ana = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(ana, $"ana{sufixo}@x.com");
		var client = CriarCliente(usuarios, "diretorio-admin");
		var token = await TokenAsync(client, "/diretorio/importar");

		var resposta = await client.PostAsync("/diretorio/importar/aplicar", Form(
			("csv", $"email;nome;cargo\nana{sufixo}@x.com;Ana Ribeiro;Diretora\n"), ("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		var html = Decodificar(await resposta.Content.ReadAsStringAsync());
		html.Should().Contain("Importação concluída").And.Contain("data-total=\"criados\">1<");
		(await TemPerfilAsync(usuarios, ana)).Should().BeTrue();
	}

	[Fact]
	public async Task ArquivoComColunaDesconhecida_ExplicaSemQuebrar()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), "diretorio-admin");
		var token = await TokenAsync(client, "/diretorio/importar");

		var resposta = await client.PostAsync("/diretorio/importar", Arquivo(token, "email;salario\na@x.com;10\n"));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("coluna desconhecida: salario");
	}

	[Fact]
	public async Task ArquivoGrandeDemais_Recusado_SemLerTudo()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), "diretorio-admin");
		var token = await TokenAsync(client, "/diretorio/importar");
		var enorme = "email\n" + new string('a', 3 * 1024 * 1024);

		var resposta = await client.PostAsync("/diretorio/importar", Arquivo(token, enorme));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("passa do limite");
	}

	[Fact]
	public async Task NenhumArquivoEnviado_ExplicaSemQuebrar()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), "diretorio-admin");
		var token = await TokenAsync(client, "/diretorio/importar");

		var resposta = await client.PostAsync("/diretorio/importar", new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } });

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("Escolha um arquivo");
	}

	[Fact]
	public async Task SemSecureGateConfigurado_ExplicaSemQuebrar()
	{
		var client = CriarCliente(usuarios: null, "diretorio-admin");
		var token = await TokenAsync(client, "/diretorio/importar");

		var resposta = await client.PostAsync("/diretorio/importar", Arquivo(token, "email\na@x.com\n"));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
	}

	[Fact]
	public async Task Admin_PostSemToken_PassaDoGateEBateNoAntifalsificacao()
	{
		var client = CriarCliente(new UsuariosParaDiretorioFalso(), "diretorio-admin");

		(await client.PostAsync("/diretorio/importar/aplicar", Form(("csv", "email\na@x.com\n")))).StatusCode
			.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task BotaoImportar_SoApareceParaOAdmin()
	{
		var usuarios = new UsuariosParaDiretorioFalso();

		var comoUsuario = await CriarCliente(usuarios, "diretorio-user").GetStringAsync("/diretorio");
		var comoAdmin = await CriarCliente(usuarios, "diretorio-admin").GetStringAsync("/diretorio");

		comoUsuario.Should().NotContain("/diretorio/importar");
		comoAdmin.Should().Contain("/diretorio/importar");
	}
}
```

- [ ] **Step 2: ViewModel e controller**

Acrescente a `DiretorioViewModel.cs`:

```csharp
/// <summary>Modelo do relatório de importação.</summary>
/// <param name="Relatorio">O que acontece (pré-visualização) ou aconteceu (aplicação).</param>
/// <param name="Csv">O texto do arquivo, devolvido no formulário de confirmação para ser revalidado.</param>
public sealed record ImportacaoViewModel(RelatorioDeImportacao Relatorio, string Csv);
```

No `DiretorioController`, acrescente `ImportarDiretorioHandler importar` ao construtor (e ao `<param>`), `using System.Text;`, e as actions (antes de `Falha`):

```csharp
	private const long LimiteDeBytesDoArquivo = 2 * 1024 * 1024;

	/// <summary>Formulário de importação CSV.</summary>
	[HttpGet("importar")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	public IActionResult Importar() => View();

	/// <summary>Lê o arquivo e mostra o que aconteceria. <b>Não grava.</b></summary>
	/// <param name="arquivo">CSV enviado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("importar")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Importar(IFormFile? arquivo, CancellationToken cancellationToken = default)
	{
		if (arquivo is null || arquivo.Length == 0)
		{
			ModelState.AddModelError(string.Empty, "Escolha um arquivo CSV para importar.");

			return View();
		}

		if (arquivo.Length > LimiteDeBytesDoArquivo)
		{
			ModelState.AddModelError(string.Empty, "O arquivo passa do limite de 1 MB.");

			return View();
		}

		string texto;

		using (var leitor = new StreamReader(arquivo.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
		{
			texto = await leitor.ReadToEndAsync(cancellationToken);
		}

		var relatorio = await importar.PrevisualizarAsync(new ImportarDiretorioCommand(texto), cancellationToken);

		if (relatorio.IsFailure)
		{
			ModelState.AddModelError(string.Empty, relatorio.Error.Description);

			return View();
		}

		return View("ImportarRelatorio", new ImportacaoViewModel(relatorio.Value, texto));
	}

	/// <summary>
	/// Aplica a importação. Revalida o CSV inteiro — nunca confia na pré-visualização, que passou
	/// pelo navegador — e grava as linhas válidas.
	/// </summary>
	/// <param name="csv">Texto do arquivo, devolvido pelo formulário de confirmação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("importar/aplicar")]
	[ExigeNivelNoDiretorio(NivelDeAcessoAoDiretorio.Administrador)]
	[ValidateAntiForgeryToken]
	[RequestFormLimits(ValueLengthLimit = 8 * 1024 * 1024)]
	public async Task<IActionResult> Aplicar(string? csv, CancellationToken cancellationToken = default)
	{
		var relatorio = await importar.AplicarAsync(new ImportarDiretorioCommand(csv ?? string.Empty), cancellationToken);

		if (relatorio.IsFailure)
		{
			ModelState.AddModelError(string.Empty, relatorio.Error.Description);

			return View(nameof(Importar));
		}

		return View("ImportarRelatorio", new ImportacaoViewModel(relatorio.Value, string.Empty));
	}
```

Como `Importar()` (GET) e `Importar(IFormFile?, ...)` (POST) têm o mesmo nome e rotas diferentes por verbo, o MVC as diferencia pelos atributos `[HttpGet]`/`[HttpPost]` — mesmo padrão do `Perfil` da Task 8.

Em `Index.cshtml`, o botão só para o admin. O `DiretorioViewModel` ainda não sabe o nível; acrescente `bool PodeImportar` a ele (`public sealed record DiretorioViewModel(PessoasDaTelaDto Tela, string? Busca, string? SetorSlug, bool PodeImportar);`), passe `AcessoAoDiretorio.TemNivel(User, NivelDeAcessoAoDiretorio.Administrador)` no `Index` do controller, e no cabeçalho da view:

```cshtml
    var acoes = new List<PageActionModel> { new("Organograma", Url.Action("Organograma")!, "bi-diagram-3") };

    if (Model.PodeImportar)
    {
        acoes.Add(new PageActionModel("Importar CSV", Url.Action("Importar")!, "bi-upload"));
    }

    var cabecalho = new PageHeaderModel("Diretório", "Quem trabalha em cada setor, com ramal e e-mail.", Acoes: acoes);
```

- [ ] **Step 3: Views**

```cshtml
@* src/Secco.Intranet.Web/Views/Diretorio/Importar.cshtml *@
@{
    ViewData["Title"] = "Importar diretório";

    var cabecalho = new PageHeaderModel(
        "Importar diretório",
        "Preencha nome, cargo, ramal, setor e gestor de muita gente de uma vez, por um arquivo CSV.",
        Acoes: new[] { new PageActionModel("Voltar", Url.Action("Index")!, "bi-arrow-left") });
}

<partial name="_PageHeader" model="cabecalho" />

<form method="post" asp-action="Importar" enctype="multipart/form-data" class="sc-panel">
    <div asp-validation-summary="All" class="text-danger mb-3"></div>

    <div class="sc-form__field">
        <label class="form-label" for="arquivo">Arquivo CSV</label>
        <input class="form-control" type="file" id="arquivo" name="arquivo" accept=".csv,text/csv" required />
    </div>

    <button class="btn btn-primary" type="submit">Ver o que vai acontecer</button>
    <div class="form-text mt-2">Nada é gravado nesta etapa.</div>
</form>

<div class="sc-panel mt-3">
    <h2 class="h6">Formato</h2>
    <p class="mb-1">Primeira linha (cabeçalho), separada por <code>;</code> ou <code>,</code>:</p>
    <pre class="mb-2">email;nome;cargo;ramal;setor;gestor</pre>
    <ul class="mb-0">
        <li>Só o <code>email</code> é obrigatório; o e-mail precisa ser de um usuário <strong>ativo</strong> — a importação não cria usuário.</li>
        <li><code>setor</code> é o <strong>slug</strong> do setor (por exemplo <code>financeiro</code>); <code>gestor</code> é o <strong>e-mail</strong> do gestor.</li>
        <li>Célula vazia significa <strong>não alterar</strong>, nunca "apagar". Reimportar o mesmo arquivo não muda nada.</li>
        <li>Até 5.000 linhas e 1 MB.</li>
    </ul>
</div>
```

```cshtml
@* src/Secco.Intranet.Web/Views/Diretorio/ImportarRelatorio.cshtml *@
@model ImportacaoViewModel
@{
    var relatorio = Model.Relatorio;

    ViewData["Title"] = relatorio.Aplicado ? "Importação concluída" : "Pré-visualização da importação";

    var cabecalho = new PageHeaderModel(
        relatorio.Aplicado ? "Importação concluída" : "Pré-visualização da importação",
        relatorio.Aplicado ? "O que foi gravado." : "Nada foi gravado ainda. Confira e confirme.",
        Acoes: new[] { new PageActionModel("Voltar ao diretório", Url.Action("Index")!, "bi-arrow-left") });

    var haOQueAplicar = relatorio.Criados + relatorio.Atualizados > 0;

    static BadgeModel Badge(StatusDaLinha status) => status switch
    {
        StatusDaLinha.Criar => new BadgeModel("Novo", BadgeVariante.Sucesso),
        StatusDaLinha.Atualizar => new BadgeModel("Atualiza", BadgeVariante.Aviso),
        StatusDaLinha.SemAlteracao => new BadgeModel("Sem alteração", BadgeVariante.Neutro),
        _ => new BadgeModel("Erro", BadgeVariante.Perigo),
    };
}

<partial name="_PageHeader" model="cabecalho" />

<div class="sc-panel">
    <dl class="row mb-0">
        <dt class="col-sm-3">@(relatorio.Aplicado ? "Criados" : "Novos")</dt>
        <dd class="col-sm-9"><strong data-total="criados">@relatorio.Criados</strong></dd>
        <dt class="col-sm-3">Atualizados</dt>
        <dd class="col-sm-9"><strong data-total="atualizados">@relatorio.Atualizados</strong></dd>
        <dt class="col-sm-3">Sem alteração</dt>
        <dd class="col-sm-9"><strong data-total="semalteracao">@relatorio.SemAlteracao</strong></dd>
        <dt class="col-sm-3">Com erro</dt>
        <dd class="col-sm-9 mb-0"><strong data-total="erros">@relatorio.ComErro</strong></dd>
    </dl>
</div>

@if (!relatorio.Aplicado && haOQueAplicar)
{
    <form method="post" asp-action="Aplicar" class="sc-panel mt-3">
        <input type="hidden" name="csv" value="@Model.Csv" />
        <button class="btn btn-primary" type="submit">Aplicar as @(relatorio.Criados + relatorio.Atualizados) alterações</button>
        <div class="form-text mt-2">
            As linhas com erro não são aplicadas. O arquivo é conferido de novo antes de gravar.
        </div>
    </form>
}
else if (relatorio.Aplicado)
{
    <p class="mt-3 mb-0">Importação concluída.</p>
}

@if (relatorio.Linhas.Count > 0)
{
    <div class="sc-panel mt-3">
        <table class="table table-sm mb-0">
            <thead>
                <tr><th>Linha</th><th>E-mail</th><th>Situação</th><th>Motivo</th></tr>
            </thead>
            <tbody>
                @foreach (var linha in relatorio.Linhas)
                {
                    <tr>
                        <td>@linha.Numero</td>
                        <td>@linha.Email</td>
                        <td><partial name="_Badge" model="Badge(linha.Status)" /></td>
                        <td>@linha.Erro</td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}
```

O teste `Aplicar_GravaDeVerdade...` procura "Importação concluída" — está no título, no cabeçalho e no parágrafo.

- [ ] **Step 4: Rodar e commitar**

Run: `dotnet build` (0 avisos) e `dotnet test` (suíte inteira verde).

```bash
git add src/Secco.Intranet.Web tests/Secco.Intranet.Tests/Integration/DiretorioImportacaoTests.cs
git commit -m "feat(diretorio): tela de importacao CSV com pre-visualizacao e confirmacao

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 12: Documentação, fumaça contra o SecureGate real e fechamento

**Files:**
- Modify: `tests/Secco.Intranet.Tests/Smoke/SecureGateGestaoDeAcessoFumacaTests.cs`
- Modify: `docs/roteiro-fumaca-securegate.md`
- Modify: `docs/roadmap.md`
- Modify: `README.md`
- Modify: `docs/specs/2026-09-24-diretorio-organizacional-design.md`

**Interfaces:**
- Consumes: `UsuariosParaDiretorioDoSecureGate` (Task 4), `FumacaFixture` (fumaça da área de acesso).

- [ ] **Step 1: Fumaça — a junção do diretório com a lista real de usuários**

Em `SecureGateGestaoDeAcessoFumacaTests.cs`, acrescente `using Microsoft.Extensions.Caching.Memory;`, `using Secco.Intranet.Application.Diretorio;`, `using Secco.Intranet.Infrastructure.Diretorio;` e, dentro da classe, antes de `TenantFixoDaFumaca`:

```csharp
	[FumacaFact]
	public async Task Diretorio_ListaOsAtivos_ENaoOsDesativados_ContraOSecureGateReal()
	{
		var ativo = await f.CriarUsuarioAsync("dir-ativo");
		var desativado = await f.CriarUsuarioAsync("dir-desativado");
		(await f.Gestao.DesativarUsuarioAsync(desativado)).IsSuccess.Should().BeTrue();

		var fonte = new UsuariosParaDiretorioDoSecureGate(
			f.Gestao, new MemoryCache(new MemoryCacheOptions()), new TenantFixoDaFumaca(f.Tenant));

		var resultado = await fonte.ListarAtivosAsync();

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Should().Contain(usuario => usuario.Id == ativo && usuario.Email.Contains("dir-ativo"));
		resultado.Value.Should().NotContain(usuario => usuario.Id == desativado,
			"o diretório só mostra quem está ativo no SecureGate de verdade");
	}
```

Rode-a como no roteiro (`SECCO_SMOKE_SECUREGATE_URL=http://localhost:4101 dotnet test --filter SecureGateGestaoDeAcessoFumacaTests`) com o SecureGate de DEV no ar; sem a variável, ela aparece como pulada.

- [ ] **Step 2: Roteiro de fumaça — passos manuais do Diretório**

Em `docs/roteiro-fumaca-securegate.md`, na seção "2. Parte manual", acrescente depois do passo 9 da área de acesso um bloco novo:

```markdown
### Diretório organizacional

Com a Intranet no ar contra o mesmo SecureGate, entre no modo aberto de DEV (sem `Authority`, todo acesso
é liberado) e confira `http://localhost:5250/diretorio`:

10. **Lista real.** Aparecem os usuários **ativos** do tenant (pelo e-mail, sem perfil ainda); os
    desativados não aparecem.
11. **Edição.** Abra uma pessoa → **Editar**: defina cargo, setor de lotação e gestor. Volte ao diretório:
    o cargo e o setor aparecem no cartão. Definir um gestor que criaria ciclo é recusado.
12. **Organograma.** `/diretorio/organograma` mostra a árvore; desative no SecureGate o gestor de alguém e
    recarregue: a equipe dele continua na tela, com a marca "gestor inativo".
13. **Importação.** `/diretorio/importar` com um CSV de teste (`email;nome;cargo`) mostra os totais **sem
    gravar**; confirme e a lista passa a mostrar os nomes. Reenviar o mesmo arquivo dá "sem alteração".

"Meu perfil" (`/diretorio/perfil`) precisa de um usuário logado — no modo aberto ele explica isso. Para
vê-lo de verdade, registre a Intranet como client OIDC e entre com login real, com a Role `diretorio-user`.
```

- [ ] **Step 3: Roadmap, README e spec**

1. `docs/roadmap.md`: no item "Diretório organizacional", troque o texto por um que diga que as **etapas 1–5 estão entregues** (perfil de colaborador, busca, organograma por gestor, importação CSV, nível de acesso com `diretorio-admin`/`diretorio-user`) e que **fica pendente só a foto**, bloqueada por `secco-platform#29`; mantenha o item como `- [ ]` até a foto sair. Mantenha o link da spec e os das issues #29 e #30.
2. `README.md`: na seção "Primeiro `intranet-admin`", acrescente um parágrafo curto: o Diretório organizacional usa duas Roles do produto, `diretorio-admin` (edita tudo e importa CSV) e `diretorio-user` (vê e edita o próprio contato); ambas são oferecidas na tela de acesso, na seção "Perfis do produto", e **ninguém vê o Diretório por padrão** — o `intranet-admin` atribui `diretorio-user` a cada usuário até o modelo de permissões permitir um perfil agrupador.
3. Spec (`docs/specs/2026-09-24-diretorio-organizacional-design.md`), três correções ao que foi implementado:
   - Onde diz "`UsuarioDoTenant` com a situação da conta" (tabela do levantamento, etapa 2 e "Fora de escopo"), troque por: a fonte de identidade do diretório é a porta `IUsuariosParaDiretorio`, implementada sobre `IGestaoDeAcesso` (que já devolve a situação da conta e falha como `Result`), com cache de 60 s por tenant só em sucesso; `UsuarioDoTenant` e `IDiretorioDeUsuarios` **não foram alterados**.
   - Em "Modo DEV sem SecureGate": o adaptador de DEV implementa `IUsuariosParaDiretorio` (não `IDiretorioDeUsuarios`).
   - Acrescente à seção "Foto": a etapa 6 tem plano próprio, a escrever quando a SDK da #29 existir.

- [ ] **Step 4: Verificação final**

Run: `dotnet build` (0 avisos) e `dotnet test` (suíte inteira verde; as fumaças aparecem como puladas sem a variável de ambiente).

Confira que não sobrou referência à demonstração: `grep -rn "DemoOptions\|DemoHabilitado\|DiretorioDemonstracao\|Intranet:Demo" src tests docs/roadmap.md README.md` deve devolver nada (o texto histórico dos planos e specs antigos pode citar; o resto não).

- [ ] **Step 5: Commit**

```bash
git add tests/Secco.Intranet.Tests/Smoke/SecureGateGestaoDeAcessoFumacaTests.cs docs/roteiro-fumaca-securegate.md docs/roadmap.md README.md docs/specs/2026-09-24-diretorio-organizacional-design.md
git commit -m "docs(diretorio): roteiro de fumaca, roadmap, README e spec alinhados ao que foi entregue

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

