# Área administrativa de tenants, subsistema 1 — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Uma área `/tenants`, exclusiva do `intranet-admin` com 2FA ativo, em que ele cria tenants para os sistemas da empresa, adota tenants existentes, liga os recursos da plataforma (SecureGate, LogStream, NotificationHub), vê o status de cada um e ativa/desativa o tenant.

**Architecture:** Cadastro local `TenantAdministrado` (lista de permissão contra IDOR) no banco do tenant da Intranet. Uma porta `IGestaoDeTenants` na Application, com adaptador `SecureGateGestaoDeTenants` sobre o client administrativo já configurado e um no-op `GestaoDeTenantsIndisponivel`. Uma porta `ITenantsProtegidos` lista o que nunca é administrável (tenant de instalação, o próprio e todo tenant de Intranet do catálogo). Na Web, três filtros de autorização encadeados por `Order`: `[SomenteIntranetAdmin]` → `[ExigeSegundoFator]` → `[TenantAdministrado]`.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 (SQL Server + PostgreSQL), `Secco.SecureGate.Client` 0.14.0, `Secco.SharedKernel.Results`, xUnit + AwesomeAssertions.

**Spec:** [`docs/specs/2026-10-07-area-administrativa-tenants-design.md`](../specs/2026-10-07-area-administrativa-tenants-design.md)

## Divergências da spec (decididas ao planejar)

1. **Rota `/tenants`, não `/administracao/tenants`.** O setor mora na raiz da URL (`/{slug}`); uma empresa com o setor "Administração" (slug `administracao`) teria a página dele engolida. `/tenants` fica no mesmo nível de `/acesso` e `/setores`, e `tenants` entra em `SlugsReservados`.
2. **2FA pela porta que já existe.** `IGestaoDeAcesso.ObterUsuarioAsync` já devolve `UsuarioDetalheDto.DoisFatoresAtivo`; a porta de tenants não ganha `ObterSegundoFatorAsync`.
3. **Desativação recusada pela plataforma** (409, ex.: tenant de instalação) ganha erro próprio `Tenants.DesativacaoRecusada`.

A Task 10 corrige a spec nesses três pontos.

## Global Constraints

- **Branch `feat/area-administrativa-tenants`**, nunca `main`. Commit por caminho explícito, nunca `git add -A`. Trailer: `Co-Authored-By: <modelo que executar> <noreply@anthropic.com>`. **Não fazer push** sem pedido do usuário.
- **Só `intranet-admin` com 2FA ativo** acessa. `{slug}-admin`, `inventario-admin` e qualquer outra Role são bloqueados em toda rota, `GET` e `POST` (ADR-0008). O 403 vem antes do 400 do antiforgery.
- **Bypass de modo aberto** só em `AcessoAdministrativo.ModoAbertoDeDev(ambiente, configuracao)`, nunca no ambiente `Testing`.
- **Controller e filtro nunca acessam `DbContext`, repositório ou client do SecureGate** (ADR-0002): sempre via handler. View recebe ViewModel/DTO, nunca tipo do client gerado.
- **Falha do SecureGate vira `Result`** sem detalhe interno; timeout do `HttpClient` também; cancelamento do chamador é relançado.
- **Recursos**: lista fechada. Rota → produto: `securegate` (sem banco), `logstream` → `"logstream"`, `notificationhub` → `"notificationhub"`. Texto da rota nunca chega à plataforma.
- **Nunca administráveis:** tenant de instalação `018f0000-0000-7000-8000-0000000000ff` (espelho de `SecureGatePlatform.TenantId`), o tenant da requisição e todo tenant do catálogo da Intranet (`ITenantCatalog.ListAsync`).
- **Slug do tenant:** `^[a-z0-9]+(?:-[a-z0-9]+)*$`, até 50. **Nome:** até 200. **Sistema** e **Responsável:** obrigatórios, até 120.
- **Script de provisionamento:** só na resposta do próprio POST, com `Cache-Control: no-store`. Nunca em TempData, cookie, banco, log ou trilha.
- **Verbos de auditoria** (recurso `tenant`): `tenant.criar`, `tenant.adotar`, `tenant.recurso.ligar`, `tenant.recurso.script-gerado`, `tenant.ativar`, `tenant.desativar`. Metadata: tenant, sistema, recurso, aplicado. Nunca script, senha ou connection string.
- **Nomenclatura de banco pela convention** (ADR-0017): tabela `tb_tenants_administrados`; `TenantId` vira `tenant_id` (Guid não-chave fica sem prefixo, precedente `usuario_id`).
- **Views só com os partials do contrato de tema** (`_PageHeader`, `_Badge`, `_EmptyState`); nenhum arquivo em `Themes/*` muda.
- **Estilo:** tabs (4) em `.cs`; 4 espaços em `.cshtml`; `<summary>` em todo membro público; build com **0 avisos** (`TreatWarningsAsErrors`); suíte inteira verde antes de cada commit. Integração precisa de Docker (Testcontainers); sem ele, rode só os unitários e diga isso no relatório.
- **Nada no repositório, na documentação ou nos commits faz referência a sistema de terceiro analisado.**

## Review Focus

1. **Guid forjado no POST de adoção** (tenant de instalação, o próprio, outro tenant de Intranet, um já cadastrado) é recusado pela Application, não só escondido da lista. Task 4 (unitário) e Task 8 (HTTP).
2. **Criou no SecureGate e o banco local falhou** (corrida de índice único ou banco fora): o admin recebe "tenant criado e não registrado; use Adotar" com o slug, e o tenant aparece em Adotar. Task 4.
3. **`intranet-admin` sem 2FA, e SecureGate fora do ar durante a checagem:** o primeiro vê a tela que explica (403), o segundo vê "indisponível" (503) — a área nunca abre por falha da checagem. Task 6.
4. **Script de provisionamento** não aparece na trilha nem gera cookie de TempData, e a resposta tem `no-store`. Tasks 4 e 8.
5. **Ligar o mesmo recurso duas vezes** (duplo clique, aba velha) não reprovisiona: o segundo recebe "recurso já ligado" sem chamar a plataforma. Task 4.

---

## Task 1: Domínio e persistência — `TenantAdministrado`

**Files:**
- Create: `src/Secco.Intranet.Domain/Tenants/TenantAdministrado.cs`
- Create: `src/Secco.Intranet.Application/Tenants/ITenantsAdministrados.cs`
- Create: `src/Secco.Intranet.Infrastructure/Mappings/TenantAdministradoConfiguration.cs`
- Create: `src/Secco.Intranet.Infrastructure/Repositories/TenantsAdministradosRepository.cs`
- Modify: `src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs` (novo `DbSet`)
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (registro do repositório, junto dos outros, linha ~94)
- Create: migrations `TenantsAdministrados` nos dois providers (geradas por `dotnet ef`)
- Test: `tests/Secco.Intranet.Tests/Unit/TenantAdministradoTests.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/TenantsAdministradosPersistenciaTests.cs`

**Interfaces:**
- Produces: `OrigemDoTenant { Criado = 0, Adotado = 1 }`; `TenantAdministrado(Guid tenantId, string sistema, string responsavel, OrigemDoTenant origem, string registradoPor)` com `TenantId`, `Sistema`, `Responsavel`, `Origem`, `SecureGateHabilitado`, `RegistradoPor`, `CreatedAt`, `UpdatedAt`, `bool HabilitarSecureGate()`; constantes `SistemaMaxLength = 120`, `ResponsavelMaxLength = 120`, `RegistradoPorMaxLength = 200`.
- Produces: `ITenantsAdministrados` com `ListarAsync`, `ObterAsync(Guid tenantId)`, `ObterParaEdicaoAsync(Guid tenantId)`, `TentarAdicionarAsync(TenantAdministrado) : Task<bool>`, `SalvarAsync()`.

- [ ] **Step 1: Escrever os testes de domínio que falham**

`tests/Secco.Intranet.Tests/Unit/TenantAdministradoTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Domain.Tenants;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class TenantAdministradoTests
{
	private static TenantAdministrado Novo(
		Guid? tenantId = null, string sistema = "Sistema de compras", string responsavel = "Ana", string registradoPor = "admin@exemplo.com") =>
		new(tenantId ?? Guid.NewGuid(), sistema, responsavel, OrigemDoTenant.Criado, registradoPor);

	[Fact]
	public void Criar_GuardaOsDados_ENasceSemSecureGate()
	{
		var tenantId = Guid.NewGuid();

		var tenant = new TenantAdministrado(tenantId, "  Sistema de compras ", " Ana ", OrigemDoTenant.Adotado, "admin@exemplo.com");

		tenant.TenantId.Should().Be(tenantId);
		tenant.Sistema.Should().Be("Sistema de compras");
		tenant.Responsavel.Should().Be("Ana");
		tenant.Origem.Should().Be(OrigemDoTenant.Adotado);
		tenant.SecureGateHabilitado.Should().BeFalse();
		tenant.RegistradoPor.Should().Be("admin@exemplo.com");
		tenant.UpdatedAt.Should().BeNull();
	}

	[Fact]
	public void Criar_TenantVazio_Lanca()
	{
		var criar = () => Novo(Guid.Empty);

		criar.Should().Throw<DomainInvariantException>();
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Criar_SistemaVazio_Lanca(string sistema)
	{
		var criar = () => Novo(sistema: sistema);

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_SistemaAcimaDoLimite_Lanca()
	{
		var criar = () => Novo(sistema: new string('a', TenantAdministrado.SistemaMaxLength + 1));

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_ResponsavelVazio_Lanca()
	{
		var criar = () => Novo(responsavel: " ");

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Criar_RegistradoPorLongo_Trunca_NaoLanca()
	{
		var tenant = Novo(registradoPor: new string('x', 300));

		tenant.RegistradoPor.Should().HaveLength(TenantAdministrado.RegistradoPorMaxLength);
	}

	[Fact]
	public void HabilitarSecureGate_PrimeiraVez_LigaEMarcaAtualizacao()
	{
		var tenant = Novo();

		tenant.HabilitarSecureGate().Should().BeTrue();

		tenant.SecureGateHabilitado.Should().BeTrue();
		tenant.UpdatedAt.Should().NotBeNull();
	}

	[Fact]
	public void HabilitarSecureGate_JaHabilitado_DevolveFalse()
	{
		var tenant = Novo();
		tenant.HabilitarSecureGate();

		tenant.HabilitarSecureGate().Should().BeFalse();
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TenantAdministradoTests"`
Expected: erro de compilação — `Secco.Intranet.Domain.Tenants` não existe.

- [ ] **Step 3: Implementar a entidade**

`src/Secco.Intranet.Domain/Tenants/TenantAdministrado.cs`:

```csharp
using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Tenants;

/// <summary>Como o tenant entrou no cadastro da Intranet.</summary>
public enum OrigemDoTenant
{
	/// <summary>Criado pela própria Intranet.</summary>
	Criado = 0,

	/// <summary>Já existia no SecureGate e foi adotado explicitamente.</summary>
	Adotado = 1,
}

/// <summary>
/// Tenant de outro sistema da empresa que esta Intranet administra (ADR-0008). É a lista de
/// permissão da área de tenants: rota com um tenant fora daqui não existe. Nome, slug e situação
/// não são copiados — vêm do SecureGate a cada leitura, para não divergir.
/// </summary>
public sealed class TenantAdministrado : BaseEntity
{
	/// <summary>Tamanho máximo do nome do sistema.</summary>
	public const int SistemaMaxLength = 120;

	/// <summary>Tamanho máximo do responsável.</summary>
	public const int ResponsavelMaxLength = 120;

	/// <summary>Tamanho máximo do rótulo de quem registrou.</summary>
	public const int RegistradoPorMaxLength = 200;

	private TenantAdministrado()
	{
		// Construtor de rehidratação do EF Core
		Sistema = string.Empty;
		Responsavel = string.Empty;
		RegistradoPor = string.Empty;
	}

	/// <summary>Registra um tenant administrado.</summary>
	/// <param name="tenantId">Id do tenant no SecureGate. Obrigatório.</param>
	/// <param name="sistema">Sistema que o tenant representa. Obrigatório.</param>
	/// <param name="responsavel">Responsável pelo sistema, texto livre. Obrigatório.</param>
	/// <param name="origem">Criado aqui ou adotado.</param>
	/// <param name="registradoPor">Rótulo de quem registrou (o mesmo da trilha); truncado no limite.</param>
	/// <exception cref="DomainInvariantException">Se algum argumento obrigatório faltar ou exceder o limite.</exception>
	public TenantAdministrado(Guid tenantId, string sistema, string responsavel, OrigemDoTenant origem, string registradoPor)
	{
		if (tenantId == Guid.Empty)
		{
			throw new DomainInvariantException("Um tenant administrado exige o id do tenant.");
		}

		Sistema = Obrigatorio(sistema, SistemaMaxLength, "sistema");
		Responsavel = Obrigatorio(responsavel, ResponsavelMaxLength, "responsável");
		TenantId = tenantId;
		Origem = origem;

		var rotulo = string.IsNullOrWhiteSpace(registradoPor) ? "desconhecido" : registradoPor.Trim();
		RegistradoPor = rotulo.Length > RegistradoPorMaxLength ? rotulo[..RegistradoPorMaxLength] : rotulo;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Id do tenant no SecureGate (coluna <c>tenant_id</c>, único).</summary>
	public Guid TenantId { get; private set; }

	/// <summary>Sistema que o tenant representa (coluna <c>ds_sistema</c>).</summary>
	public string Sistema { get; private set; }

	/// <summary>Responsável pelo sistema (coluna <c>ds_responsavel</c>).</summary>
	public string Responsavel { get; private set; }

	/// <summary>Criado aqui ou adotado (coluna <c>ie_origem</c>).</summary>
	public OrigemDoTenant Origem { get; private set; }

	/// <summary>
	/// Gestão de perfis e usuários deste tenant habilitada na Intranet (coluna
	/// <c>fl_secure_gate_habilitado</c>). É a única marca local de recurso: o SecureGate não tem
	/// banco por tenant, então "ligado" é decisão da Intranet, não estado da plataforma.
	/// </summary>
	public bool SecureGateHabilitado { get; private set; }

	/// <summary>Quem registrou (coluna <c>ds_registrado_por</c>).</summary>
	public string RegistradoPor { get; private set; }

	/// <summary>Quando foi registrado (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Última alteração (coluna <c>dt_updated_at</c>).</summary>
	public DateTimeOffset? UpdatedAt { get; private set; }

	/// <summary>Habilita a gestão de perfis e usuários deste tenant.</summary>
	/// <returns><c>false</c> se já estava habilitada — nada muda.</returns>
	public bool HabilitarSecureGate()
	{
		if (SecureGateHabilitado)
		{
			return false;
		}

		SecureGateHabilitado = true;
		UpdatedAt = DateTimeOffset.UtcNow;

		return true;
	}

	private static string Obrigatorio(string? valor, int limite, string campo)
	{
		var aparado = valor?.Trim() ?? string.Empty;

		if (aparado.Length == 0)
		{
			throw new DomainInvariantException($"O {campo} é obrigatório.");
		}

		if (aparado.Length > limite)
		{
			throw new DomainInvariantException($"O {campo} excede {limite} caracteres.");
		}

		return aparado;
	}
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TenantAdministradoTests"`
Expected: PASS (9 testes).

- [ ] **Step 5: Porta do repositório**

`src/Secco.Intranet.Application/Tenants/ITenantsAdministrados.cs`:

```csharp
using Secco.Intranet.Domain.Tenants;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Cadastro local de tenants administrados, no banco do tenant da Intranet.</summary>
public interface ITenantsAdministrados
{
	/// <summary>Todos os tenants administrados, desrastreados, por sistema.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantAdministrado>> ListarAsync(CancellationToken cancellationToken = default);

	/// <summary>Busca pelo id do tenant no SecureGate, <b>desrastreado</b> — caminho de leitura.</summary>
	/// <param name="tenantId">Id do tenant no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantAdministrado?> ObterAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Busca <b>rastreado</b>, para alteração seguida de <see cref="SalvarAsync"/>.</summary>
	/// <param name="tenantId">Id do tenant no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantAdministrado?> ObterParaEdicaoAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Persiste um registro novo. Devolve <c>false</c> — sem lançar — se o tenant já está no
	/// cadastro (dois registros simultâneos do mesmo tenant).
	/// </summary>
	/// <param name="tenant">Registro novo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> TentarAdicionarAsync(TenantAdministrado tenant, CancellationToken cancellationToken = default);

	/// <summary>Grava as alterações dos registros rastreados.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SalvarAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Mapeamento, `DbSet` e repositório**

`src/Secco.Intranet.Infrastructure/Mappings/TenantAdministradoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.Intranet.Domain.Tenants;

namespace Secco.Intranet.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="TenantAdministrado"/>. Nomes de tabela e colunas vêm da convention
/// (ADR-0017); aqui só o que ela não decide.
/// </summary>
internal sealed class TenantAdministradoConfiguration : IEntityTypeConfiguration<TenantAdministrado>
{
	public void Configure(EntityTypeBuilder<TenantAdministrado> builder)
	{
		builder.Property(tenant => tenant.Sistema).HasMaxLength(TenantAdministrado.SistemaMaxLength);
		builder.Property(tenant => tenant.Responsavel).HasMaxLength(TenantAdministrado.ResponsavelMaxLength);
		builder.Property(tenant => tenant.RegistradoPor).HasMaxLength(TenantAdministrado.RegistradoPorMaxLength);

		// Um registro por tenant — é o que sustenta o "já administrado" e o TentarAdicionar.
		builder.HasIndex(tenant => tenant.TenantId).IsUnique();
	}
}
```

Em `IntranetDbContext.cs`, depois de `ItensMenu`:

```csharp
	/// <summary>Tenants de outros sistemas que esta Intranet administra (ADR-0008).</summary>
	public DbSet<TenantAdministrado> TenantsAdministrados => Set<TenantAdministrado>();
```

(com `using Secco.Intranet.Domain.Tenants;` no topo).

`src/Secco.Intranet.Infrastructure/Repositories/TenantsAdministradosRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.Intranet.Infrastructure.Contexts;

namespace Secco.Intranet.Infrastructure.Repositories;

/// <summary>Persistência do cadastro de tenants administrados no banco do tenant atual.</summary>
internal sealed class TenantsAdministradosRepository(IntranetDbContext context) : ITenantsAdministrados
{
	public async Task<IReadOnlyList<TenantAdministrado>> ListarAsync(CancellationToken cancellationToken = default) =>
		await context.TenantsAdministrados
			.AsNoTracking()
			.OrderBy(tenant => tenant.Sistema)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<TenantAdministrado?> ObterAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		await context.TenantsAdministrados
			.AsNoTracking()
			.FirstOrDefaultAsync(tenant => tenant.TenantId == tenantId, cancellationToken)
			.ConfigureAwait(false);

	public async Task<TenantAdministrado?> ObterParaEdicaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		await context.TenantsAdministrados
			.FirstOrDefaultAsync(tenant => tenant.TenantId == tenantId, cancellationToken)
			.ConfigureAwait(false);

	public async Task<bool> TentarAdicionarAsync(TenantAdministrado tenant, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(tenant);

		context.TenantsAdministrados.Add(tenant);

		try
		{
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

			return true;
		}
		catch (DbUpdateException)
		{
			context.Entry(tenant).State = EntityState.Detached;

			var jaExiste = await context.TenantsAdministrados
				.AsNoTracking()
				.AnyAsync(outro => outro.TenantId == tenant.TenantId, cancellationToken)
				.ConfigureAwait(false);

			// Outro pedido registrou o mesmo tenant um instante antes: não é falha de
			// infraestrutura. Qualquer outra causa continua subindo.
			if (jaExiste)
			{
				return false;
			}

			throw;
		}
	}

	public Task SalvarAsync(CancellationToken cancellationToken = default) =>
		context.SaveChangesAsync(cancellationToken);
}
```

Em `IntranetInfrastructureExtensions.cs`, depois de `services.AddScoped<IPerfilColaboradorRepository, PerfilColaboradorRepository>();`:

```csharp
		services.AddScoped<ITenantsAdministrados, TenantsAdministradosRepository>();
```

(com `using Secco.Intranet.Application.Tenants;`).

- [ ] **Step 7: Gerar as migrations nos dois providers**

```bash
dotnet ef migrations add TenantsAdministrados --project src/Secco.Intranet.Migrations.SqlServer --startup-project src/Secco.Intranet.Migrations.SqlServer --context IntranetDbContext
dotnet ef migrations add TenantsAdministrados --project src/Secco.Intranet.Migrations.Postgres --startup-project src/Secco.Intranet.Migrations.Postgres --context IntranetDbContext
```

Abra os dois `*_TenantsAdministrados.cs` e confirme: tabela `tb_tenants_administrados`; PK `id_pk_tenant_administrado` com constraint `pk_tenants_administrados`; colunas `tenant_id`, `ds_sistema` (120), `ds_responsavel` (120), `ie_origem`, `fl_secure_gate_habilitado`, `ds_registrado_por` (200), `dt_created_at`, `dt_updated_at` (nullable); índice único `uk_tenants_administrados_tenant_id`. Se um nome destoar, ajuste `TenantAdministradoConfiguration`, rode `dotnet ef migrations remove` nos dois e regenere.

- [ ] **Step 8: Teste de persistência**

`tests/Secco.Intranet.Tests/Integration/TenantsAdministradosPersistenciaTests.cs`:

```csharp
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>Mapeamento, índice único e rastreamento do cadastro de tenants, contra o banco de verdade.</summary>
public class TenantsAdministradosPersistenciaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
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
		var tenantId = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			(await escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>()
				.TentarAdicionarAsync(new TenantAdministrado(tenantId, "Sistema de compras", "Ana", OrigemDoTenant.Criado, "admin")))
				.Should().BeTrue();
		}

		using var leitura = NovoEscopo();
		var lido = await leitura.ServiceProvider.GetRequiredService<ITenantsAdministrados>().ObterAsync(tenantId);

		lido.Should().NotBeNull();
		lido!.Sistema.Should().Be("Sistema de compras");
		lido.Origem.Should().Be(OrigemDoTenant.Criado);
	}

	[Fact]
	public async Task MesmoTenantDuasVezes_DevolveFalse_SemLancar()
	{
		var tenantId = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			await escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>()
				.TentarAdicionarAsync(new TenantAdministrado(tenantId, "A", "Ana", OrigemDoTenant.Criado, "admin"));
		}

		using var outro = NovoEscopo();
		var repetido = await outro.ServiceProvider.GetRequiredService<ITenantsAdministrados>()
			.TentarAdicionarAsync(new TenantAdministrado(tenantId, "B", "Bia", OrigemDoTenant.Adotado, "admin"));

		repetido.Should().BeFalse("o índice único barra o segundo registro, e a corrida não vira 500");
	}

	[Fact]
	public async Task HabilitarSecureGate_Rastreado_GravaDeVerdade()
	{
		var tenantId = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			await escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>()
				.TentarAdicionarAsync(new TenantAdministrado(tenantId, "A", "Ana", OrigemDoTenant.Criado, "admin"));
		}

		using (var escopo = NovoEscopo())
		{
			var repositorio = escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>();
			var tenant = await repositorio.ObterParaEdicaoAsync(tenantId);
			tenant!.HabilitarSecureGate();
			await repositorio.SalvarAsync();
		}

		using var leitura = NovoEscopo();
		(await leitura.ServiceProvider.GetRequiredService<ITenantsAdministrados>().ObterAsync(tenantId))!
			.SecureGateHabilitado.Should().BeTrue();
	}
}
```

- [ ] **Step 9: Rodar a suíte inteira**

Run: `dotnet build Secco.Intranet.slnx -c Release` (0 avisos) e `dotnet test Secco.Intranet.slnx -c Release`
Expected: tudo verde, incluindo os 3 testes novos de persistência.

- [ ] **Step 10: Commit**

```bash
git add src/Secco.Intranet.Domain/Tenants/TenantAdministrado.cs src/Secco.Intranet.Application/Tenants/ITenantsAdministrados.cs src/Secco.Intranet.Infrastructure/Mappings/TenantAdministradoConfiguration.cs src/Secco.Intranet.Infrastructure/Repositories/TenantsAdministradosRepository.cs src/Secco.Intranet.Infrastructure/Contexts/IntranetDbContext.cs src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs src/Secco.Intranet.Migrations.SqlServer/Migrations src/Secco.Intranet.Migrations.Postgres/Migrations tests/Secco.Intranet.Tests/Unit/TenantAdministradoTests.cs tests/Secco.Intranet.Tests/Integration/TenantsAdministradosPersistenciaTests.cs
git commit -m "feat(tenants): cadastro local de tenants administrados" -m "Co-Authored-By: <modelo> <noreply@anthropic.com>"
```

---

## Task 2: Application — recursos, DTOs, portas, regras, erros e verbos

**Files:**
- Create: `src/Secco.Intranet.Application/Tenants/RecursosDaPlataforma.cs`
- Create: `src/Secco.Intranet.Application/Tenants/TenantsDtos.cs`
- Create: `src/Secco.Intranet.Application/Tenants/IGestaoDeTenants.cs`
- Create: `src/Secco.Intranet.Application/Tenants/ITenantsProtegidos.cs`
- Create: `src/Secco.Intranet.Application/Tenants/RegrasDeTenant.cs`
- Create: `src/Secco.Intranet.Application/Tenants/AuditoriaDeTenants.cs`
- Modify: `src/Secco.Intranet.Application/IntranetErrors.cs` (nova classe `Tenants`, antes do `}` final)
- Modify: `src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs` (verbos e recurso `tenant`)
- Test: `tests/Secco.Intranet.Tests/Unit/RecursosDaPlataformaTests.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/RegrasDeTenantTests.cs`

**Interfaces:**
- Consumes: nada das tasks anteriores além do namespace `Secco.Intranet.Application.Tenants`.
- Produces:
  - `enum RecursoDaPlataforma { SecureGate, LogStream, NotificationHub }`
  - `static class RecursosDaPlataforma`: `IReadOnlyList<RecursoDaPlataforma> Todos`; `bool TentarLer(string? valor, out RecursoDaPlataforma recurso)`; `string Rota(RecursoDaPlataforma)`; `string? Produto(RecursoDaPlataforma)`; `string Nome(RecursoDaPlataforma)`.
  - DTOs: `TenantDaPlataformaDto(Guid Id, string Nome, string Slug, bool Ativo)`; `TenantDaPlataformaDetalheDto(Guid Id, string Nome, string Slug, bool Ativo, IReadOnlyList<string> Produtos)`; `StatusDoBancoDto(string Produto, bool Responde, string? Motivo)`; `ProvisionamentoDto(bool Aplicado, string? Script)`; `enum SituacaoDoRecurso { NaoLigado, Ligado, LigadoSemResponder }`; `RecursoDoTenantDto(RecursoDaPlataforma Recurso, SituacaoDoRecurso Situacao, string? Motivo)`; `TenantAdministradoResumoDto(Guid TenantId, string Sistema, string Nome, string Slug, bool Ativo, bool EncontradoNaPlataforma, OrigemDoTenant Origem, IReadOnlyList<RecursoDaPlataforma> RecursosLigados)`; `TenantAdministradoDetalheDto(Guid TenantId, string Sistema, string Responsavel, string Nome, string Slug, bool Ativo, OrigemDoTenant Origem, string RegistradoPor, DateTimeOffset RegistradoEm, IReadOnlyList<RecursoDoTenantDto> Recursos)`.
  - `IGestaoDeTenants` (assinaturas no Step 5); `ITenantsProtegidos.ListarAsync() : Task<IReadOnlySet<Guid>>`.
  - `RegrasDeTenant.ValidarCriacao(string? sistema, string? responsavel, string? nome, string? slug) : Error?`; `RegrasDeTenant.ValidarRegistro(string? sistema, string? responsavel) : Error?`; `RegrasDeTenant.SlugValido(string?) : bool`; `RegrasDeTenant.RotuloDoAtor(IAtorAtual) : string`.
  - `IntranetErrors.Tenants.*` (Step 7); `VerbosDeAuditoria.Tenant*` e `RecursosDeAuditoria.Tenant`.
  - `AuditoriaDeTenants.TenantAsync(trilha, verbo, tenantId, sistema, ct)` e `RecursoAsync(trilha, verbo, tenantId, sistema, recurso, aplicado, ct)`.

- [ ] **Step 1: Testes que falham — recursos**

`tests/Secco.Intranet.Tests/Unit/RecursosDaPlataformaTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application.Tenants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class RecursosDaPlataformaTests
{
	[Theory]
	[InlineData("securegate", RecursoDaPlataforma.SecureGate)]
	[InlineData("logstream", RecursoDaPlataforma.LogStream)]
	[InlineData("NotificationHub", RecursoDaPlataforma.NotificationHub)]
	public void TentarLer_ValorDaLista_Reconhece(string valor, RecursoDaPlataforma esperado)
	{
		RecursosDaPlataforma.TentarLer(valor, out var recurso).Should().BeTrue();
		recurso.Should().Be(esperado);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("intranet")]
	[InlineData("compras")]
	[InlineData("logstream ")]
	[InlineData("0")]
	[InlineData("1")]
	public void TentarLer_ForaDaLista_Recusa(string? valor)
	{
		RecursosDaPlataforma.TentarLer(valor, out _).Should().BeFalse("o texto da rota nunca escolhe produto arbitrário");
	}

	[Fact]
	public void Produto_SoLogStreamENotificationHubTemBanco()
	{
		RecursosDaPlataforma.Produto(RecursoDaPlataforma.SecureGate).Should().BeNull();
		RecursosDaPlataforma.Produto(RecursoDaPlataforma.LogStream).Should().Be("logstream");
		RecursosDaPlataforma.Produto(RecursoDaPlataforma.NotificationHub).Should().Be("notificationhub");
	}

	[Fact]
	public void Rota_VoltaParaOMesmoRecurso()
	{
		foreach (var recurso in RecursosDaPlataforma.Todos)
		{
			RecursosDaPlataforma.TentarLer(RecursosDaPlataforma.Rota(recurso), out var lido).Should().BeTrue();
			lido.Should().Be(recurso);
		}
	}
}
```

- [ ] **Step 2: Testes que falham — regras**

`tests/Secco.Intranet.Tests/Unit/RegrasDeTenantTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class RegrasDeTenantTests
{
	[Theory]
	[InlineData("compras")]
	[InlineData("sistema-de-compras")]
	[InlineData("erp2")]
	[InlineData("a")]
	public void SlugValido_KebabCase_Aceita(string slug) =>
		RegrasDeTenant.SlugValido(slug).Should().BeTrue();

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("Compras")]
	[InlineData("sistema de compras")]
	[InlineData("-compras")]
	[InlineData("compras-")]
	[InlineData("com--pras")]
	[InlineData("compras_2")]
	[InlineData("compras;drop")]
	[InlineData("comprás")]
	public void SlugValido_ForaDoFormato_Recusa(string? slug) =>
		RegrasDeTenant.SlugValido(slug).Should().BeFalse();

	[Fact]
	public void SlugValido_51Caracteres_Recusa() =>
		RegrasDeTenant.SlugValido(new string('a', 51)).Should().BeFalse();

	[Fact]
	public void ValidarCriacao_TudoCerto_SemErro() =>
		RegrasDeTenant.ValidarCriacao("Sistema de compras", "Ana", "Compras", "compras").Should().BeNull();

	[Fact]
	public void ValidarCriacao_CadaCampo_TemErroProprio()
	{
		RegrasDeTenant.ValidarCriacao(" ", "Ana", "Compras", "compras").Should().Be(IntranetErrors.Tenants.SistemaRequired);
		RegrasDeTenant.ValidarCriacao(new string('a', 121), "Ana", "Compras", "compras").Should().Be(IntranetErrors.Tenants.SistemaTooLong);
		RegrasDeTenant.ValidarCriacao("S", "", "Compras", "compras").Should().Be(IntranetErrors.Tenants.ResponsavelRequired);
		RegrasDeTenant.ValidarCriacao("S", new string('a', 121), "Compras", "compras").Should().Be(IntranetErrors.Tenants.ResponsavelTooLong);
		RegrasDeTenant.ValidarCriacao("S", "Ana", null, "compras").Should().Be(IntranetErrors.Tenants.NomeRequired);
		RegrasDeTenant.ValidarCriacao("S", "Ana", new string('a', 201), "compras").Should().Be(IntranetErrors.Tenants.NomeTooLong);
		RegrasDeTenant.ValidarCriacao("S", "Ana", "Compras", "Compras X").Should().Be(IntranetErrors.Tenants.SlugInvalido);
	}

	[Fact]
	public void RotuloDoAtor_SemAtor_Desconhecido() =>
		RegrasDeTenant.RotuloDoAtor(new AtorDeAcessoFalso(null)).Should().Be("desconhecido");

	[Fact]
	public void RotuloDoAtor_ComAtor_UsaONome() =>
		RegrasDeTenant.RotuloDoAtor(new AtorDeAcessoFalso(Guid.NewGuid())).Should().Be("admin@exemplo.com");
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~RecursosDaPlataformaTests|FullyQualifiedName~RegrasDeTenantTests"`
Expected: erro de compilação — tipos inexistentes.

- [ ] **Step 4: Recursos e DTOs**

`src/Secco.Intranet.Application/Tenants/RecursosDaPlataforma.cs`:

```csharp
namespace Secco.Intranet.Application.Tenants;

/// <summary>Recurso da plataforma que a Intranet liga para um tenant administrado.</summary>
public enum RecursoDaPlataforma
{
	/// <summary>Usuários e perfis. Sem banco por tenant (identidade é dado de plataforma, ADR-0022).</summary>
	SecureGate,

	/// <summary>Log e trilha de auditoria. Banco por tenant.</summary>
	LogStream,

	/// <summary>Notificações. Banco por tenant.</summary>
	NotificationHub,
}

/// <summary>
/// Lista fechada dos recursos e de como cada um aparece na rota e no catálogo. O texto que vem da
/// rota é comparado com esta lista e nunca repassado à plataforma: a API aceita qualquer nome de
/// produto, e escolher um arbitrário a partir da Intranet é superfície que ninguém pediu.
/// </summary>
public static class RecursosDaPlataforma
{
	/// <summary>Todos, na ordem em que a tela os mostra.</summary>
	public static IReadOnlyList<RecursoDaPlataforma> Todos { get; } =
		[RecursoDaPlataforma.SecureGate, RecursoDaPlataforma.LogStream, RecursoDaPlataforma.NotificationHub];

	/// <summary>Lê o segmento de rota, sem diferenciar caixa e sem aparar.</summary>
	/// <param name="valor">Segmento recebido.</param>
	/// <param name="recurso">Recurso reconhecido.</param>
	/// <returns><c>false</c> para qualquer valor fora da lista, inclusive números.</returns>
	public static bool TentarLer(string? valor, out RecursoDaPlataforma recurso)
	{
		foreach (var candidato in Todos)
		{
			if (string.Equals(valor, Rota(candidato), StringComparison.OrdinalIgnoreCase))
			{
				recurso = candidato;

				return true;
			}
		}

		recurso = default;

		return false;
	}

	/// <summary>Segmento de rota do recurso.</summary>
	/// <param name="recurso">Recurso.</param>
	public static string Rota(RecursoDaPlataforma recurso) => recurso switch
	{
		RecursoDaPlataforma.SecureGate => "securegate",
		RecursoDaPlataforma.LogStream => "logstream",
		RecursoDaPlataforma.NotificationHub => "notificationhub",
		_ => throw new ArgumentOutOfRangeException(nameof(recurso)),
	};

	/// <summary>Nome do produto no catálogo do SecureGate, ou <c>null</c> se o recurso não tem banco.</summary>
	/// <param name="recurso">Recurso.</param>
	public static string? Produto(RecursoDaPlataforma recurso) => recurso switch
	{
		RecursoDaPlataforma.SecureGate => null,
		_ => Rota(recurso),
	};

	/// <summary>Nome de exibição.</summary>
	/// <param name="recurso">Recurso.</param>
	public static string Nome(RecursoDaPlataforma recurso) => recurso switch
	{
		RecursoDaPlataforma.SecureGate => "SecureGate",
		RecursoDaPlataforma.LogStream => "LogStream",
		RecursoDaPlataforma.NotificationHub => "NotificationHub",
		_ => throw new ArgumentOutOfRangeException(nameof(recurso)),
	};
}
```

`src/Secco.Intranet.Application/Tenants/TenantsDtos.cs`:

```csharp
using Secco.Intranet.Domain.Tenants;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Tenant como a plataforma o vê.</summary>
/// <param name="Id">Id no SecureGate.</param>
/// <param name="Nome">Nome.</param>
/// <param name="Slug">Slug.</param>
/// <param name="Ativo">Se está ativo.</param>
public sealed record TenantDaPlataformaDto(Guid Id, string Nome, string Slug, bool Ativo);

/// <summary>Tenant da plataforma com os produtos que têm banco no catálogo.</summary>
/// <param name="Id">Id no SecureGate.</param>
/// <param name="Nome">Nome.</param>
/// <param name="Slug">Slug.</param>
/// <param name="Ativo">Se está ativo.</param>
/// <param name="Produtos">Produtos com banco cadastrado (ex.: <c>logstream</c>).</param>
public sealed record TenantDaPlataformaDetalheDto(Guid Id, string Nome, string Slug, bool Ativo, IReadOnlyList<string> Produtos);

/// <summary>Estado de um banco do tenant, sem connection string (ADR-0020).</summary>
/// <param name="Produto">Produto.</param>
/// <param name="Responde">Se respondeu à sondagem.</param>
/// <param name="Motivo">Classificação da falha, quando não responde.</param>
public sealed record StatusDoBancoDto(string Produto, bool Responde, string? Motivo);

/// <summary>Resultado de ligar um recurso.</summary>
/// <param name="Aplicado"><c>true</c> se a plataforma criou o banco; <c>false</c> se devolveu script.</param>
/// <param name="Script">SQL para o DBA, com a senha gerada. Exibido uma vez; nunca persistido nem registrado.</param>
public sealed record ProvisionamentoDto(bool Aplicado, string? Script);

/// <summary>Situação de um recurso para um tenant.</summary>
public enum SituacaoDoRecurso
{
	/// <summary>Não ligado.</summary>
	NaoLigado,

	/// <summary>Ligado e respondendo.</summary>
	Ligado,

	/// <summary>Banco cadastrado no catálogo, mas não respondeu (ex.: script ainda não aplicado).</summary>
	LigadoSemResponder,
}

/// <summary>Um recurso no painel do tenant.</summary>
/// <param name="Recurso">Recurso.</param>
/// <param name="Situacao">Situação.</param>
/// <param name="Motivo">Motivo classificado, quando ligado sem responder.</param>
public sealed record RecursoDoTenantDto(RecursoDaPlataforma Recurso, SituacaoDoRecurso Situacao, string? Motivo);

/// <summary>Linha da lista de tenants administrados.</summary>
/// <param name="TenantId">Id no SecureGate.</param>
/// <param name="Sistema">Sistema.</param>
/// <param name="Nome">Nome na plataforma.</param>
/// <param name="Slug">Slug na plataforma.</param>
/// <param name="Ativo">Se está ativo.</param>
/// <param name="EncontradoNaPlataforma"><c>false</c> se o SecureGate não conhece mais o tenant.</param>
/// <param name="Origem">Criado ou adotado.</param>
/// <param name="RecursosLigados">Recursos ligados.</param>
public sealed record TenantAdministradoResumoDto(
	Guid TenantId,
	string Sistema,
	string Nome,
	string Slug,
	bool Ativo,
	bool EncontradoNaPlataforma,
	OrigemDoTenant Origem,
	IReadOnlyList<RecursoDaPlataforma> RecursosLigados);

/// <summary>Detalhe de um tenant administrado.</summary>
/// <param name="TenantId">Id no SecureGate.</param>
/// <param name="Sistema">Sistema.</param>
/// <param name="Responsavel">Responsável.</param>
/// <param name="Nome">Nome na plataforma.</param>
/// <param name="Slug">Slug na plataforma.</param>
/// <param name="Ativo">Se está ativo.</param>
/// <param name="Origem">Criado ou adotado.</param>
/// <param name="RegistradoPor">Quem registrou.</param>
/// <param name="RegistradoEm">Quando registrou.</param>
/// <param name="Recursos">Os três recursos, sempre na ordem de <see cref="RecursosDaPlataforma.Todos"/>.</param>
public sealed record TenantAdministradoDetalheDto(
	Guid TenantId,
	string Sistema,
	string Responsavel,
	string Nome,
	string Slug,
	bool Ativo,
	OrigemDoTenant Origem,
	string RegistradoPor,
	DateTimeOffset RegistradoEm,
	IReadOnlyList<RecursoDoTenantDto> Recursos);
```

- [ ] **Step 5: Portas**

`src/Secco.Intranet.Application/Tenants/IGestaoDeTenants.cs`:

```csharp
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>
/// Porta da API de tenants do SecureGate (ADR-0008), pelo client administrativo da Intranet.
/// Falha de rede, de status e timeout viram <see cref="Result"/> — quem administra precisa saber
/// que a ação não aconteceu. Nenhum método recebe produto livre: quem chama já passou pela lista
/// fechada de <see cref="RecursosDaPlataforma"/>.
/// </summary>
public interface IGestaoDeTenants
{
	/// <summary>Todos os tenants da instalação.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<TenantDaPlataformaDto>>> ListarTenantsAsync(CancellationToken cancellationToken = default);

	/// <summary>Um tenant, com os produtos que têm banco no catálogo.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<TenantDaPlataformaDetalheDto>> ObterTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Estado dos bancos do tenant.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<StatusDoBancoDto>>> ObterStatusDosBancosAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Cria um tenant.</summary>
	/// <param name="nome">Nome, já validado.</param>
	/// <param name="slug">Slug, já validado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<TenantDaPlataformaDto>> CriarTenantAsync(string nome, string slug, CancellationToken cancellationToken = default);

	/// <summary>Provisiona o banco do tenant no produto, com o alvo e os nomes padrão da plataforma.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="produto">Produto, de <see cref="RecursosDaPlataforma.Produto"/>.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<ProvisionamentoDto>> ProvisionarAsync(Guid tenantId, string produto, CancellationToken cancellationToken = default);

	/// <summary>Ativa o tenant.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> AtivarAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Desativa o tenant: login e catálogo param em até um TTL de cache.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> DesativarAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
```

`src/Secco.Intranet.Application/Tenants/ITenantsProtegidos.cs`:

```csharp
namespace Secco.Intranet.Application.Tenants;

/// <summary>
/// Tenants que esta Intranet nunca administra, qualquer que seja o pedido: o de instalação da
/// plataforma, o da própria requisição e todo tenant que é uma Intranet (catálogo do produto).
/// A última regra impede a filial A de adotar a filial B na mesma instalação.
/// </summary>
public interface ITenantsProtegidos
{
	/// <summary>Ids protegidos.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlySet<Guid>> ListarAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Regras e auditoria**

`src/Secco.Intranet.Application/Tenants/RegrasDeTenant.cs`:

```csharp
using System.Text.RegularExpressions;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Validação de entrada da área de tenants, checada antes de qualquer chamada à plataforma.</summary>
public static partial class RegrasDeTenant
{
	/// <summary>Tamanho máximo do nome (o da plataforma).</summary>
	public const int NomeMaxLength = 200;

	/// <summary>Tamanho máximo do slug (o da plataforma).</summary>
	public const int SlugMaxLength = 50;

	/// <summary>
	/// Kebab-case minúsculo, sem acento. Mais estrito que a plataforma (que só exige não vazio): o
	/// slug vira nome de banco e de login no provisionamento.
	/// </summary>
	public static bool SlugValido(string? slug) =>
		slug is { Length: > 0 and <= SlugMaxLength } && PadraoDoSlug().IsMatch(slug);

	/// <summary>Valida os campos de criação.</summary>
	/// <returns>O primeiro erro, ou <c>null</c>.</returns>
	public static Error? ValidarCriacao(string? sistema, string? responsavel, string? nome, string? slug)
	{
		if (ValidarRegistro(sistema, responsavel) is { } erro)
		{
			return erro;
		}

		var nomeAparado = nome?.Trim() ?? string.Empty;

		if (nomeAparado.Length == 0)
		{
			return IntranetErrors.Tenants.NomeRequired;
		}

		if (nomeAparado.Length > NomeMaxLength)
		{
			return IntranetErrors.Tenants.NomeTooLong;
		}

		return SlugValido(slug?.Trim()) ? null : IntranetErrors.Tenants.SlugInvalido;
	}

	/// <summary>Valida o que vai para o cadastro local (criar e adotar).</summary>
	/// <returns>O primeiro erro, ou <c>null</c>.</returns>
	public static Error? ValidarRegistro(string? sistema, string? responsavel)
	{
		var s = sistema?.Trim() ?? string.Empty;
		var r = responsavel?.Trim() ?? string.Empty;

		if (s.Length == 0)
		{
			return IntranetErrors.Tenants.SistemaRequired;
		}

		if (s.Length > TenantAdministrado.SistemaMaxLength)
		{
			return IntranetErrors.Tenants.SistemaTooLong;
		}

		if (r.Length == 0)
		{
			return IntranetErrors.Tenants.ResponsavelRequired;
		}

		return r.Length > TenantAdministrado.ResponsavelMaxLength ? IntranetErrors.Tenants.ResponsavelTooLong : null;
	}

	/// <summary>Rótulo de quem age, o mesmo da trilha; <c>desconhecido</c> sem ator.</summary>
	/// <param name="ator">Ator atual.</param>
	public static string RotuloDoAtor(IAtorAtual ator)
	{
		ArgumentNullException.ThrowIfNull(ator);

		return ator.Atual()?.Nome is { Length: > 0 } nome ? nome : "desconhecido";
	}

	[GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
	private static partial Regex PadraoDoSlug();
}
```

`src/Secco.Intranet.Application/Tenants/AuditoriaDeTenants.cs`:

```csharp
using System.Text.Json;
using Secco.Intranet.Application.Auditoria;

namespace Secco.Intranet.Application.Tenants;

/// <summary>
/// Registro na trilha das ações sobre tenants — só depois que a plataforma aceitou. Na plataforma,
/// o ator dessas chamadas é o client da Intranet; esta trilha é o único registro de <b>quem</b>.
/// Nunca recebe script, senha ou connection string.
/// </summary>
internal static class AuditoriaDeTenants
{
	/// <summary>Registra uma ação sobre o tenant.</summary>
	public static Task TenantAsync(
		ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, string sistema, CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Tenant, tenantId.ToString(),
				JsonSerializer.Serialize(new { tenantId, sistema })),
			cancellationToken);

	/// <summary>Registra uma ação sobre um recurso do tenant.</summary>
	public static Task RecursoAsync(
		ITrilhaDeAuditoria trilha, string verbo, Guid tenantId, string sistema, RecursoDaPlataforma recurso, bool aplicado,
		CancellationToken cancellationToken) =>
		trilha.RegistrarAsync(
			new RegistroDeAuditoria(verbo, RecursosDeAuditoria.Tenant, tenantId.ToString(),
				JsonSerializer.Serialize(new { tenantId, sistema, recurso = RecursosDaPlataforma.Rota(recurso), aplicado })),
			cancellationToken);
}
```

- [ ] **Step 7: Erros e verbos**

Em `IntranetErrors.cs`, nova classe antes do `}` final do arquivo:

```csharp
	/// <summary>Erros da área de tenants administrados.</summary>
	public static class Tenants
	{
		/// <summary>SecureGate não configurado neste ambiente.</summary>
		public static readonly Error NaoConfigurado =
			Error.Unavailable(
				"Intranet.Tenants.NaoConfigurado",
				"O SecureGate não está configurado neste ambiente, então a administração de tenants não está disponível.");

		/// <summary>SecureGate fora do ar, lento ou recusando — sem detalhe interno (ADR-0020).</summary>
		public static readonly Error Indisponivel =
			Error.Unavailable(
				"Intranet.Tenants.Indisponivel",
				"Não foi possível falar com o SecureGate agora. Tente novamente em instantes.");

		/// <summary>Tenant fora do cadastro, ou inexistente na plataforma.</summary>
		public static readonly Error NaoEncontrado =
			Error.NotFound("Intranet.Tenants.NaoEncontrado", "Tenant não encontrado.");

		/// <summary>Sistema não informado.</summary>
		public static readonly Error SistemaRequired =
			Error.Validation("Intranet.Tenants.SistemaRequired", "Informe o sistema que o tenant representa.");

		/// <summary>Sistema acima do limite.</summary>
		public static readonly Error SistemaTooLong =
			Error.Validation("Intranet.Tenants.SistemaTooLong", "O nome do sistema excede 120 caracteres.");

		/// <summary>Responsável não informado.</summary>
		public static readonly Error ResponsavelRequired =
			Error.Validation("Intranet.Tenants.ResponsavelRequired", "Informe o responsável pelo sistema.");

		/// <summary>Responsável acima do limite.</summary>
		public static readonly Error ResponsavelTooLong =
			Error.Validation("Intranet.Tenants.ResponsavelTooLong", "O responsável excede 120 caracteres.");

		/// <summary>Nome do tenant não informado.</summary>
		public static readonly Error NomeRequired =
			Error.Validation("Intranet.Tenants.NomeRequired", "Informe o nome do tenant.");

		/// <summary>Nome do tenant acima do limite.</summary>
		public static readonly Error NomeTooLong =
			Error.Validation("Intranet.Tenants.NomeTooLong", "O nome do tenant excede 200 caracteres.");

		/// <summary>Slug fora do formato.</summary>
		public static readonly Error SlugInvalido =
			Error.Validation(
				"Intranet.Tenants.SlugInvalido",
				"O slug aceita só letras minúsculas sem acento, dígitos e hífen entre eles, com até 50 caracteres (ex.: sistema-de-compras).");

		/// <summary>Slug já usado por outro tenant.</summary>
		public static readonly Error SlugJaExiste =
			Error.Conflict("Intranet.Tenants.SlugJaExiste", "Já existe um tenant com esse slug.");

		/// <summary>Tenant protegido: instalação, a própria Intranet ou outra Intranet.</summary>
		public static readonly Error NaoAdotavel =
			Error.Validation(
				"Intranet.Tenants.NaoAdotavel",
				"Este tenant não pode ser administrado por aqui: é o da plataforma ou o de uma Intranet.");

		/// <summary>Tenant já no cadastro.</summary>
		public static readonly Error JaAdministrado =
			Error.Conflict("Intranet.Tenants.JaAdministrado", "Este tenant já é administrado por esta Intranet.");

		/// <summary>Recurso fora da lista fechada.</summary>
		public static readonly Error RecursoInvalido =
			Error.Validation("Intranet.Tenants.RecursoInvalido", "Recurso inválido.");

		/// <summary>Recurso já ligado.</summary>
		public static readonly Error RecursoJaLigado =
			Error.Conflict("Intranet.Tenants.RecursoJaLigado", "Este recurso já está ligado para o tenant.");

		/// <summary>Slug digitado na confirmação não confere.</summary>
		public static readonly Error ConfirmacaoNaoConfere =
			Error.Validation(
				"Intranet.Tenants.ConfirmacaoNaoConfere",
				"Para desativar, digite exatamente o slug do tenant.");

		/// <summary>A plataforma recusou a desativação.</summary>
		public static readonly Error DesativacaoRecusada =
			Error.Conflict("Intranet.Tenants.DesativacaoRecusada", "A plataforma recusou a desativação deste tenant.");

		/// <summary>Criado no SecureGate, mas o cadastro local falhou.</summary>
		/// <param name="slug">Slug criado, para o admin achar o tenant em Adotar.</param>
		public static Error RegistroLocalFalhou(string slug) =>
			Error.Failure(
				"Intranet.Tenants.RegistroLocalFalhou",
				$"O tenant '{slug}' foi criado no SecureGate, mas não foi registrado nesta Intranet. Use Adotar para registrá-lo.");
	}
```

Em `VerbosDeAuditoria.cs`, depois de `DiretorioImportar`:

```csharp
	/// <summary>Criação de tenant administrado.</summary>
	public const string TenantCriar = "tenant.criar";

	/// <summary>Adoção de tenant existente.</summary>
	public const string TenantAdotar = "tenant.adotar";

	/// <summary>Recurso ligado para um tenant.</summary>
	public const string TenantRecursoLigar = "tenant.recurso.ligar";

	/// <summary>Script de provisionamento gerado (o script em si nunca entra na trilha).</summary>
	public const string TenantRecursoScriptGerado = "tenant.recurso.script-gerado";

	/// <summary>Tenant ativado.</summary>
	public const string TenantAtivar = "tenant.ativar";

	/// <summary>Tenant desativado.</summary>
	public const string TenantDesativar = "tenant.desativar";
```

E em `RecursosDeAuditoria`, depois de `Diretorio`:

```csharp
	/// <summary>Tenant administrado (ADR-0008).</summary>
	public const string Tenant = "tenant";
```

- [ ] **Step 8: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~RecursosDaPlataformaTests|FullyQualifiedName~RegrasDeTenantTests"`
Expected: PASS.

- [ ] **Step 9: Suíte inteira e commit**

Run: `dotnet build Secco.Intranet.slnx -c Release` (0 avisos) e `dotnet test Secco.Intranet.slnx -c Release`.

```bash
git add src/Secco.Intranet.Application/Tenants src/Secco.Intranet.Application/IntranetErrors.cs src/Secco.Intranet.Application/Auditoria/VerbosDeAuditoria.cs tests/Secco.Intranet.Tests/Unit/RecursosDaPlataformaTests.cs tests/Secco.Intranet.Tests/Unit/RegrasDeTenantTests.cs
git commit -m "feat(tenants): portas, recursos da plataforma e regras de entrada" -m "Co-Authored-By: <modelo> <noreply@anthropic.com>"
```

---

## Task 3: Application — dublês e handlers de leitura

**Files:**
- Create: `tests/Secco.Intranet.Tests/Support/DublesDeTenants.cs`
- Create: `src/Secco.Intranet.Application/Tenants/ListarTenantsAdministradosHandler.cs`
- Create: `src/Secco.Intranet.Application/Tenants/ObterTenantAdministradoHandler.cs`
- Create: `src/Secco.Intranet.Application/Tenants/ListarTenantsAdotaveisHandler.cs`
- Create: `src/Secco.Intranet.Application/Tenants/VerificarTenantAdministradoHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs` (registro dos handlers)
- Test: `tests/Secco.Intranet.Tests/Unit/LeituraDeTenantsHandlersTests.cs`

**Interfaces:**
- Consumes (Tasks 1–2): `ITenantsAdministrados`, `IGestaoDeTenants`, `ITenantsProtegidos`, DTOs, `IntranetErrors.Tenants`.
- Produces:
  - `ListarTenantsAdministradosHandler.HandleAsync(ct) : Task<Result<IReadOnlyList<TenantAdministradoResumoDto>>>`
  - `ObterTenantAdministradoHandler.HandleAsync(Guid tenantId, ct) : Task<Result<TenantAdministradoDetalheDto>>`
  - `ListarTenantsAdotaveisHandler.HandleAsync(ct) : Task<Result<IReadOnlyList<TenantDaPlataformaDto>>>`
  - `VerificarTenantAdministradoHandler.HandleAsync(Guid tenantId, ct) : Task<bool>`
  - Dublês (para Tasks 4, 6, 8): `GestaoDeTenantsFalsa`, `TenantsAdministradosFalso`, `TenantsProtegidosFixos`.

- [ ] **Step 1: Dublês**

`tests/Secco.Intranet.Tests/Support/DublesDeTenants.cs`:

```csharp
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// API de tenants em memória. Toda chamada fica em <see cref="Chamadas"/>; criar e provisionar
/// mudam o que as leituras seguintes devolvem, como na plataforma.
/// </summary>
public sealed class GestaoDeTenantsFalsa : IGestaoDeTenants
{
	/// <summary>Tenants da instalação, com os produtos que têm banco.</summary>
	public List<TenantDaPlataformaDetalheDto> Tenants { get; } = [];

	/// <summary>Bancos que não respondem: (tenant, produto) → motivo.</summary>
	public Dictionary<(Guid, string), string> BancosSemResponder { get; } = [];

	/// <summary>Chamadas, na ordem (ex.: <c>provisionar:{id}:logstream</c>).</summary>
	public List<string> Chamadas { get; } = [];

	/// <summary>Quando definido, toda chamada falha com este erro.</summary>
	public Error? FalharCom { get; set; }

	/// <summary>Quando <c>true</c>, provisionar devolve script em vez de aplicar.</summary>
	public bool ModoScript { get; set; }

	/// <summary>Script devolvido em <see cref="ModoScript"/> — com uma senha falsa reconhecível.</summary>
	public const string Script = "CREATE LOGIN [x] WITH PASSWORD = 'SENHA-SECRETA-DE-TESTE';";

	/// <summary>Acrescenta um tenant.</summary>
	public GestaoDeTenantsFalsa ComTenant(Guid id, string slug, bool ativo = true, params string[] produtos)
	{
		Tenants.Add(new TenantDaPlataformaDetalheDto(id, $"Tenant {slug}", slug, ativo, produtos));

		return this;
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<TenantDaPlataformaDto>>> ListarTenantsAsync(CancellationToken cancellationToken = default)
	{
		Chamadas.Add("listar");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<IReadOnlyList<TenantDaPlataformaDto>>(erro));
		}

		IReadOnlyList<TenantDaPlataformaDto> lista = [.. Tenants.Select(t => new TenantDaPlataformaDto(t.Id, t.Nome, t.Slug, t.Ativo))];

		return Task.FromResult(Result.Success(lista));
	}

	/// <inheritdoc />
	public Task<Result<TenantDaPlataformaDetalheDto>> ObterTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		Chamadas.Add($"obter:{tenantId}");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<TenantDaPlataformaDetalheDto>(erro));
		}

		var tenant = Tenants.FirstOrDefault(t => t.Id == tenantId);

		return Task.FromResult(tenant is null
			? Result.Failure<TenantDaPlataformaDetalheDto>(IntranetErrors.Tenants.NaoEncontrado)
			: Result.Success(tenant));
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<StatusDoBancoDto>>> ObterStatusDosBancosAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		Chamadas.Add($"status:{tenantId}");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<IReadOnlyList<StatusDoBancoDto>>(erro));
		}

		var tenant = Tenants.FirstOrDefault(t => t.Id == tenantId);
		IReadOnlyList<StatusDoBancoDto> status = tenant is null
			? []
			: [.. tenant.Produtos.Select(p => BancosSemResponder.TryGetValue((tenantId, p), out var motivo)
				? new StatusDoBancoDto(p, false, motivo)
				: new StatusDoBancoDto(p, true, null))];

		return Task.FromResult(Result.Success(status));
	}

	/// <inheritdoc />
	public Task<Result<TenantDaPlataformaDto>> CriarTenantAsync(string nome, string slug, CancellationToken cancellationToken = default)
	{
		Chamadas.Add($"criar:{slug}");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<TenantDaPlataformaDto>(erro));
		}

		if (Tenants.Any(t => t.Slug == slug))
		{
			return Task.FromResult(Result.Failure<TenantDaPlataformaDto>(IntranetErrors.Tenants.SlugJaExiste));
		}

		var novo = new TenantDaPlataformaDetalheDto(Guid.NewGuid(), nome, slug, true, []);
		Tenants.Add(novo);

		return Task.FromResult(Result.Success(new TenantDaPlataformaDto(novo.Id, novo.Nome, novo.Slug, novo.Ativo)));
	}

	/// <inheritdoc />
	public Task<Result<ProvisionamentoDto>> ProvisionarAsync(Guid tenantId, string produto, CancellationToken cancellationToken = default)
	{
		Chamadas.Add($"provisionar:{tenantId}:{produto}");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<ProvisionamentoDto>(erro));
		}

		var indice = Tenants.FindIndex(t => t.Id == tenantId);

		if (indice < 0)
		{
			return Task.FromResult(Result.Failure<ProvisionamentoDto>(IntranetErrors.Tenants.NaoEncontrado));
		}

		Tenants[indice] = Tenants[indice] with { Produtos = [.. Tenants[indice].Produtos, produto] };

		return Task.FromResult(Result.Success(ModoScript ? new ProvisionamentoDto(false, Script) : new ProvisionamentoDto(true, null)));
	}

	/// <inheritdoc />
	public Task<Result> AtivarAsync(Guid tenantId, CancellationToken cancellationToken = default) => Situacao(tenantId, true);

	/// <inheritdoc />
	public Task<Result> DesativarAsync(Guid tenantId, CancellationToken cancellationToken = default) => Situacao(tenantId, false);

	private Task<Result> Situacao(Guid tenantId, bool ativo)
	{
		Chamadas.Add($"{(ativo ? "ativar" : "desativar")}:{tenantId}");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure(erro));
		}

		var indice = Tenants.FindIndex(t => t.Id == tenantId);

		if (indice < 0)
		{
			return Task.FromResult(Result.Failure(IntranetErrors.Tenants.NaoEncontrado));
		}

		Tenants[indice] = Tenants[indice] with { Ativo = ativo };

		return Task.FromResult(Result.Success());
	}
}

/// <summary>Cadastro local em memória. <see cref="FalharAoAdicionar"/> simula o banco fora do ar.</summary>
public sealed class TenantsAdministradosFalso : ITenantsAdministrados
{
	/// <summary>Registros.</summary>
	public List<TenantAdministrado> Registros { get; } = [];

	/// <summary>Quando <c>true</c>, <see cref="TentarAdicionarAsync"/> lança, como um banco fora do ar.</summary>
	public bool FalharAoAdicionar { get; set; }

	/// <summary>Quantas vezes <see cref="SalvarAsync"/> foi chamado.</summary>
	public int Salvamentos { get; private set; }

	/// <summary>Acrescenta um registro.</summary>
	public TenantsAdministradosFalso Com(Guid tenantId, string sistema = "Sistema de compras", bool secureGate = false)
	{
		var registro = new TenantAdministrado(tenantId, sistema, "Ana", OrigemDoTenant.Criado, "admin@exemplo.com");

		if (secureGate)
		{
			registro.HabilitarSecureGate();
		}

		Registros.Add(registro);

		return this;
	}

	/// <inheritdoc />
	public Task<IReadOnlyList<TenantAdministrado>> ListarAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<TenantAdministrado>>([.. Registros.OrderBy(r => r.Sistema)]);

	/// <inheritdoc />
	public Task<TenantAdministrado?> ObterAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registros.FirstOrDefault(r => r.TenantId == tenantId));

	/// <inheritdoc />
	public Task<TenantAdministrado?> ObterParaEdicaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		ObterAsync(tenantId, cancellationToken);

	/// <inheritdoc />
	public Task<bool> TentarAdicionarAsync(TenantAdministrado tenant, CancellationToken cancellationToken = default)
	{
		if (FalharAoAdicionar)
		{
			throw new InvalidOperationException("banco fora do ar (simulado)");
		}

		if (Registros.Any(r => r.TenantId == tenant.TenantId))
		{
			return Task.FromResult(false);
		}

		Registros.Add(tenant);

		return Task.FromResult(true);
	}

	/// <inheritdoc />
	public Task SalvarAsync(CancellationToken cancellationToken = default)
	{
		Salvamentos++;

		return Task.CompletedTask;
	}
}

/// <summary>Lista fixa de tenants protegidos.</summary>
/// <param name="protegidos">Ids protegidos.</param>
public sealed class TenantsProtegidosFixos(params Guid[] protegidos) : ITenantsProtegidos
{
	/// <inheritdoc />
	public Task<IReadOnlySet<Guid>> ListarAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlySet<Guid>>(protegidos.ToHashSet());
}
```

- [ ] **Step 2: Testes que falham**

`tests/Secco.Intranet.Tests/Unit/LeituraDeTenantsHandlersTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LeituraDeTenantsHandlersTests
{
	private static readonly Guid Compras = Guid.NewGuid();
	private static readonly Guid Erp = Guid.NewGuid();

	[Fact]
	public async Task Listar_SoOsDoCadastro_ComRecursosLigados()
	{
		var gestao = new GestaoDeTenantsFalsa()
			.ComTenant(Compras, "compras", true, "logstream")
			.ComTenant(Erp, "erp");
		var cadastro = new TenantsAdministradosFalso().Com(Compras, "Sistema de compras", secureGate: true);

		var resultado = await new ListarTenantsAdministradosHandler(cadastro, gestao).HandleAsync();

		resultado.IsSuccess.Should().BeTrue();
		var linha = resultado.Value.Should().ContainSingle().Subject;
		linha.TenantId.Should().Be(Compras);
		linha.Slug.Should().Be("compras");
		linha.EncontradoNaPlataforma.Should().BeTrue();
		linha.RecursosLigados.Should().Equal(RecursoDaPlataforma.SecureGate, RecursoDaPlataforma.LogStream);
	}

	[Fact]
	public async Task Listar_TenantSumiuDaPlataforma_AindaApareceMarcado()
	{
		var cadastro = new TenantsAdministradosFalso().Com(Compras);

		var resultado = await new ListarTenantsAdministradosHandler(cadastro, new GestaoDeTenantsFalsa()).HandleAsync();

		var linha = resultado.Value.Should().ContainSingle().Subject;
		linha.EncontradoNaPlataforma.Should().BeFalse();
		linha.Ativo.Should().BeFalse();
	}

	[Fact]
	public async Task Listar_SecureGateFora_Falha()
	{
		var gestao = new GestaoDeTenantsFalsa { FalharCom = IntranetErrors.Tenants.Indisponivel };
		var cadastro = new TenantsAdministradosFalso().Com(Compras);

		var resultado = await new ListarTenantsAdministradosHandler(cadastro, gestao).HandleAsync();

		resultado.Error.Should().Be(IntranetErrors.Tenants.Indisponivel);
	}

	[Fact]
	public async Task Obter_ForaDoCadastro_NaoEncontrado_SemChamarAPlataforma()
	{
		var gestao = new GestaoDeTenantsFalsa().ComTenant(Erp, "erp");

		var resultado = await new ObterTenantAdministradoHandler(new TenantsAdministradosFalso(), gestao).HandleAsync(Erp);

		resultado.Error.Should().Be(IntranetErrors.Tenants.NaoEncontrado);
		gestao.Chamadas.Should().BeEmpty("tenant fora do cadastro não existe para a área");
	}

	[Fact]
	public async Task Obter_MontaOsTresRecursos()
	{
		var gestao = new GestaoDeTenantsFalsa().ComTenant(Compras, "compras", true, "logstream", "notificationhub");
		gestao.BancosSemResponder[(Compras, "notificationhub")] = "login-failed";
		var cadastro = new TenantsAdministradosFalso().Com(Compras);

		var resultado = await new ObterTenantAdministradoHandler(cadastro, gestao).HandleAsync(Compras);

		resultado.Value.Recursos.Should().Equal(
			new RecursoDoTenantDto(RecursoDaPlataforma.SecureGate, SituacaoDoRecurso.NaoLigado, null),
			new RecursoDoTenantDto(RecursoDaPlataforma.LogStream, SituacaoDoRecurso.Ligado, null),
			new RecursoDoTenantDto(RecursoDaPlataforma.NotificationHub, SituacaoDoRecurso.LigadoSemResponder, "login-failed"));
	}

	[Fact]
	public async Task Adotaveis_ExcluiProtegidosEJaAdministrados()
	{
		var instalacao = Guid.NewGuid();
		var livre = Guid.NewGuid();
		var gestao = new GestaoDeTenantsFalsa()
			.ComTenant(instalacao, "instalacao")
			.ComTenant(Compras, "compras")
			.ComTenant(livre, "erp");
		var cadastro = new TenantsAdministradosFalso().Com(Compras);

		var resultado = await new ListarTenantsAdotaveisHandler(gestao, cadastro, new TenantsProtegidosFixos(instalacao))
			.HandleAsync();

		resultado.Value.Select(t => t.Id).Should().Equal(livre);
	}

	[Fact]
	public async Task Verificar_SoOsDoCadastro()
	{
		var handler = new VerificarTenantAdministradoHandler(new TenantsAdministradosFalso().Com(Compras));

		(await handler.HandleAsync(Compras)).Should().BeTrue();
		(await handler.HandleAsync(Erp)).Should().BeFalse();
		(await handler.HandleAsync(Guid.Empty)).Should().BeFalse();
	}
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~LeituraDeTenantsHandlersTests"`
Expected: erro de compilação — handlers inexistentes.

- [ ] **Step 4: Implementar**

`src/Secco.Intranet.Application/Tenants/ListarTenantsAdministradosHandler.cs`:

```csharp
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>
/// Lista do cadastro, com nome, slug, situação e recursos vindos da plataforma. Uma chamada por
/// tenant: a lista de uma empresa tem poucos sistemas, e o resumo da plataforma não traz produtos.
/// </summary>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="gestao">API de tenants.</param>
public sealed class ListarTenantsAdministradosHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao)
{
	/// <summary>Executa a consulta.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<TenantAdministradoResumoDto>>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var registros = await cadastro.ListarAsync(cancellationToken).ConfigureAwait(false);
		var linhas = new List<TenantAdministradoResumoDto>(registros.Count);

		foreach (var registro in registros)
		{
			var tenant = await gestao.ObterTenantAsync(registro.TenantId, cancellationToken).ConfigureAwait(false);

			if (tenant.IsFailure && tenant.Error != IntranetErrors.Tenants.NaoEncontrado)
			{
				return Result.Failure<IReadOnlyList<TenantAdministradoResumoDto>>(tenant.Error);
			}

			var recursos = new List<RecursoDaPlataforma>();

			if (registro.SecureGateHabilitado)
			{
				recursos.Add(RecursoDaPlataforma.SecureGate);
			}

			if (tenant.IsSuccess)
			{
				recursos.AddRange(RecursosDaPlataforma.Todos.Where(r =>
					RecursosDaPlataforma.Produto(r) is { } produto
					&& tenant.Value.Produtos.Contains(produto, StringComparer.OrdinalIgnoreCase)));
			}

			linhas.Add(new TenantAdministradoResumoDto(
				registro.TenantId,
				registro.Sistema,
				tenant.IsSuccess ? tenant.Value.Nome : "(não encontrado no SecureGate)",
				tenant.IsSuccess ? tenant.Value.Slug : string.Empty,
				tenant.IsSuccess && tenant.Value.Ativo,
				tenant.IsSuccess,
				registro.Origem,
				recursos));
		}

		return Result.Success<IReadOnlyList<TenantAdministradoResumoDto>>(linhas);
	}
}
```

`src/Secco.Intranet.Application/Tenants/ObterTenantAdministradoHandler.cs`:

```csharp
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Detalhe de um tenant do cadastro, com o painel dos três recursos.</summary>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="gestao">API de tenants.</param>
public sealed class ObterTenantAdministradoHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao)
{
	/// <summary>Executa a consulta. Tenant fora do cadastro é <c>NaoEncontrado</c> sem chamar a plataforma.</summary>
	/// <param name="tenantId">Id do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<TenantAdministradoDetalheDto>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var registro = await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (registro is null)
		{
			return Result.Failure<TenantAdministradoDetalheDto>(IntranetErrors.Tenants.NaoEncontrado);
		}

		var tenant = await gestao.ObterTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (tenant.IsFailure)
		{
			return Result.Failure<TenantAdministradoDetalheDto>(tenant.Error);
		}

		var status = await gestao.ObterStatusDosBancosAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (status.IsFailure)
		{
			return Result.Failure<TenantAdministradoDetalheDto>(status.Error);
		}

		var recursos = RecursosDaPlataforma.Todos
			.Select(recurso => Situacao(recurso, registro.SecureGateHabilitado, tenant.Value.Produtos, status.Value))
			.ToList();

		return Result.Success(new TenantAdministradoDetalheDto(
			registro.TenantId,
			registro.Sistema,
			registro.Responsavel,
			tenant.Value.Nome,
			tenant.Value.Slug,
			tenant.Value.Ativo,
			registro.Origem,
			registro.RegistradoPor,
			registro.CreatedAt,
			recursos));
	}

	private static RecursoDoTenantDto Situacao(
		RecursoDaPlataforma recurso, bool secureGateHabilitado, IReadOnlyList<string> produtos, IReadOnlyList<StatusDoBancoDto> status)
	{
		if (RecursosDaPlataforma.Produto(recurso) is not { } produto)
		{
			return new RecursoDoTenantDto(recurso, secureGateHabilitado ? SituacaoDoRecurso.Ligado : SituacaoDoRecurso.NaoLigado, null);
		}

		if (!produtos.Contains(produto, StringComparer.OrdinalIgnoreCase))
		{
			return new RecursoDoTenantDto(recurso, SituacaoDoRecurso.NaoLigado, null);
		}

		var banco = status.FirstOrDefault(s => string.Equals(s.Produto, produto, StringComparison.OrdinalIgnoreCase));

		return banco is { Responde: true }
			? new RecursoDoTenantDto(recurso, SituacaoDoRecurso.Ligado, null)
			: new RecursoDoTenantDto(recurso, SituacaoDoRecurso.LigadoSemResponder, banco?.Motivo);
	}
}
```

`src/Secco.Intranet.Application/Tenants/ListarTenantsAdotaveisHandler.cs`:

```csharp
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Tenants da instalação que podem ser adotados: fora do cadastro e não protegidos.</summary>
/// <param name="gestao">API de tenants.</param>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="protegidos">Tenants que nunca são administrados.</param>
public sealed class ListarTenantsAdotaveisHandler(IGestaoDeTenants gestao, ITenantsAdministrados cadastro, ITenantsProtegidos protegidos)
{
	/// <summary>Executa a consulta.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<TenantDaPlataformaDto>>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var todos = await gestao.ListarTenantsAsync(cancellationToken).ConfigureAwait(false);

		if (todos.IsFailure)
		{
			return todos;
		}

		var bloqueados = await protegidos.ListarAsync(cancellationToken).ConfigureAwait(false);
		var administrados = (await cadastro.ListarAsync(cancellationToken).ConfigureAwait(false))
			.Select(r => r.TenantId)
			.ToHashSet();

		IReadOnlyList<TenantDaPlataformaDto> adotaveis =
			[.. todos.Value.Where(t => !bloqueados.Contains(t.Id) && !administrados.Contains(t.Id)).OrderBy(t => t.Nome)];

		return Result.Success(adotaveis);
	}
}
```

`src/Secco.Intranet.Application/Tenants/VerificarTenantAdministradoHandler.cs`:

```csharp
namespace Secco.Intranet.Application.Tenants;

/// <summary>
/// Se o tenant está no cadastro — a checagem de IDOR que o filtro <c>[TenantAdministrado]</c> faz
/// antes de qualquer action com <c>{tenantId}</c>. Existe como handler para a Web não tocar no
/// repositório (ADR-0002).
/// </summary>
/// <param name="cadastro">Cadastro local.</param>
public sealed class VerificarTenantAdministradoHandler(ITenantsAdministrados cadastro)
{
	/// <summary>Executa a checagem.</summary>
	/// <param name="tenantId">Id recebido na rota.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<bool> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		tenantId != Guid.Empty
		&& await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false) is not null;
}
```

Em `IntranetApplicationExtensions.cs`, junto dos registros de handlers (com `using Secco.Intranet.Application.Tenants;`):

```csharp
		services.AddScoped<ListarTenantsAdministradosHandler>();
		services.AddScoped<ObterTenantAdministradoHandler>();
		services.AddScoped<ListarTenantsAdotaveisHandler>();
		services.AddScoped<VerificarTenantAdministradoHandler>();
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~LeituraDeTenantsHandlersTests"`
Expected: PASS (7 testes).

- [ ] **Step 6: Suíte inteira e commit**

Run: `dotnet build Secco.Intranet.slnx -c Release` e `dotnet test Secco.Intranet.slnx -c Release`.

```bash
git add tests/Secco.Intranet.Tests/Support/DublesDeTenants.cs src/Secco.Intranet.Application/Tenants/ListarTenantsAdministradosHandler.cs src/Secco.Intranet.Application/Tenants/ObterTenantAdministradoHandler.cs src/Secco.Intranet.Application/Tenants/ListarTenantsAdotaveisHandler.cs src/Secco.Intranet.Application/Tenants/VerificarTenantAdministradoHandler.cs src/Secco.Intranet.Application/IntranetApplicationExtensions.cs tests/Secco.Intranet.Tests/Unit/LeituraDeTenantsHandlersTests.cs
git commit -m "feat(tenants): leitura do cadastro, detalhe com recursos e adotaveis" -m "Co-Authored-By: <modelo> <noreply@anthropic.com>"
```

---

## Task 4: Application — handlers de escrita com as regras e a trilha

**Files:**
- Create: `src/Secco.Intranet.Application/Tenants/CriarTenantHandler.cs`
- Create: `src/Secco.Intranet.Application/Tenants/AdotarTenantHandler.cs`
- Create: `src/Secco.Intranet.Application/Tenants/LigarRecursoHandler.cs`
- Create: `src/Secco.Intranet.Application/Tenants/AlterarSituacaoDoTenantHandler.cs`
- Modify: `src/Secco.Intranet.Application/IntranetApplicationExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/EscritaDeTenantsHandlersTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 1–3; `ITrilhaDeAuditoria`, `IAtorAtual` (existentes); dublês `TrilhaDeAcessoFalsa`, `AtorDeAcessoFalso`.
- Produces:
  - `CriarTenantCommand(string? Sistema, string? Responsavel, string? Nome, string? Slug)`; `CriarTenantHandler.HandleAsync(cmd, ct) : Task<Result<Guid>>`
  - `AdotarTenantCommand(Guid TenantId, string? Sistema, string? Responsavel)`; `AdotarTenantHandler.HandleAsync(cmd, ct) : Task<Result>`
  - `LigarRecursoCommand(Guid TenantId, string? Recurso)`; `LigarRecursoHandler.HandleAsync(cmd, ct) : Task<Result<ProvisionamentoDto>>`
  - `AlterarSituacaoDoTenantHandler.AtivarAsync(Guid tenantId, ct) : Task<Result>` e `DesativarAsync(Guid tenantId, string? confirmacaoSlug, ct) : Task<Result>`

- [ ] **Step 1: Testes que falham**

`tests/Secco.Intranet.Tests/Unit/EscritaDeTenantsHandlersTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class EscritaDeTenantsHandlersTests
{
	private static readonly Guid Instalacao = Guid.NewGuid();
	private static readonly Guid PropriaIntranet = Guid.NewGuid();
	private static readonly Guid OutraIntranet = Guid.NewGuid();

	private readonly GestaoDeTenantsFalsa _gestao = new();
	private readonly TenantsAdministradosFalso _cadastro = new();
	private readonly TrilhaDeAcessoFalsa _trilha = new();
	private readonly AtorDeAcessoFalso _ator = new(Guid.NewGuid());
	private readonly TenantsProtegidosFixos _protegidos = new(Instalacao, PropriaIntranet, OutraIntranet);

	private CriarTenantHandler Criar() => new(_gestao, _cadastro, _trilha, _ator);

	private AdotarTenantHandler Adotar() => new(_gestao, _cadastro, _protegidos, _trilha, _ator);

	private LigarRecursoHandler Ligar() => new(_cadastro, _gestao, _trilha);

	private AlterarSituacaoDoTenantHandler Situacao() => new(_cadastro, _gestao, _trilha);

	// ---- Criar

	[Fact]
	public async Task Criar_CriaNaPlataformaERegistraLocal_EAudita()
	{
		var resultado = await Criar().HandleAsync(new CriarTenantCommand(" Sistema de compras ", "Ana", "Compras", "compras"));

		resultado.IsSuccess.Should().BeTrue();
		_gestao.Chamadas.Should().Equal("criar:compras");
		var registro = _cadastro.Registros.Should().ContainSingle().Subject;
		registro.TenantId.Should().Be(resultado.Value);
		registro.Sistema.Should().Be("Sistema de compras");
		registro.Origem.Should().Be(OrigemDoTenant.Criado);
		registro.RegistradoPor.Should().Be("admin@exemplo.com");
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantCriar);
	}

	[Fact]
	public async Task Criar_EntradaInvalida_NaoChamaAPlataforma()
	{
		var resultado = await Criar().HandleAsync(new CriarTenantCommand("S", "Ana", "Compras", "Compras X"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.SlugInvalido);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_SlugDuplicado_DevolveOErroDaPlataforma_SemRegistrar()
	{
		_gestao.ComTenant(Guid.NewGuid(), "compras");

		var resultado = await Criar().HandleAsync(new CriarTenantCommand("S", "Ana", "Compras", "compras"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.SlugJaExiste);
		_cadastro.Registros.Should().BeEmpty();
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_PlataformaOkECadastroLocalFalha_OrientaAdotar_EAuditaACriacao()
	{
		_cadastro.FalharAoAdicionar = true;

		var resultado = await Criar().HandleAsync(new CriarTenantCommand("S", "Ana", "Compras", "compras"));

		resultado.Error.Code.Should().Be("Intranet.Tenants.RegistroLocalFalhou");
		resultado.Error.Description.Should().Contain("compras").And.Contain("Adotar");
		_trilha.Registros.Should().ContainSingle("o tenant existe na plataforma: a criação aconteceu e precisa constar")
			.Which.Verbo.Should().Be(VerbosDeAuditoria.TenantCriar);
	}

	// ---- Adotar

	[Theory]
	[MemberData(nameof(Protegidos))]
	public async Task Adotar_TenantProtegido_Recusa_SemChamarAPlataforma(Guid protegido)
	{
		var resultado = await Adotar().HandleAsync(new AdotarTenantCommand(protegido, "S", "Ana"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.NaoAdotavel);
		_gestao.Chamadas.Should().BeEmpty();
		_cadastro.Registros.Should().BeEmpty();
	}

	public static TheoryData<Guid> Protegidos => new() { Instalacao, PropriaIntranet, OutraIntranet };

	[Fact]
	public async Task Adotar_JaAdministrado_Recusa()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "erp");
		_cadastro.Com(id);

		var resultado = await Adotar().HandleAsync(new AdotarTenantCommand(id, "S", "Ana"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.JaAdministrado);
	}

	[Fact]
	public async Task Adotar_GuidQueNaoExisteNaPlataforma_NaoEncontrado()
	{
		var resultado = await Adotar().HandleAsync(new AdotarTenantCommand(Guid.NewGuid(), "S", "Ana"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.NaoEncontrado);
		_cadastro.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Adotar_Livre_RegistraComoAdotado_EAudita()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "erp");

		var resultado = await Adotar().HandleAsync(new AdotarTenantCommand(id, "ERP", "Bia"));

		resultado.IsSuccess.Should().BeTrue();
		_cadastro.Registros.Should().ContainSingle().Which.Origem.Should().Be(OrigemDoTenant.Adotado);
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantAdotar);
	}

	// ---- Ligar recurso

	[Fact]
	public async Task Ligar_TenantForaDoCadastro_NaoEncontrado()
	{
		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(Guid.NewGuid(), "logstream"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.NaoEncontrado);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("compras")]
	[InlineData("intranet")]
	public async Task Ligar_RecursoForaDaLista_Recusa(string? recurso)
	{
		var id = Guid.NewGuid();
		_cadastro.Com(id);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, recurso));

		resultado.Error.Should().Be(IntranetErrors.Tenants.RecursoInvalido);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Ligar_SecureGate_SoMarcaOCadastro()
	{
		var id = Guid.NewGuid();
		_cadastro.Com(id);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, "securegate"));

		resultado.Value.Should().Be(new ProvisionamentoDto(true, null));
		_cadastro.Registros[0].SecureGateHabilitado.Should().BeTrue();
		_cadastro.Salvamentos.Should().Be(1);
		_gestao.Chamadas.Should().BeEmpty("o SecureGate não tem banco por tenant");
	}

	[Fact]
	public async Task Ligar_SecureGateJaLigado_RecursoJaLigado()
	{
		var id = Guid.NewGuid();
		_cadastro.Com(id, secureGate: true);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, "securegate"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.RecursoJaLigado);
	}

	[Fact]
	public async Task Ligar_LogStream_ProvisionaComOProdutoDaLista_EAudita()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras");
		_cadastro.Com(id);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, "LogStream"));

		resultado.Value.Aplicado.Should().BeTrue();
		_gestao.Chamadas.Should().Contain($"provisionar:{id}:logstream");
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantRecursoLigar);
	}

	[Fact]
	public async Task Ligar_DuasVezes_NaoReprovisiona()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras", true, "logstream");
		_cadastro.Com(id);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, "logstream"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.RecursoJaLigado);
		_gestao.Chamadas.Should().NotContain(c => c.StartsWith("provisionar", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Ligar_ModoScript_DevolveOScript_ETrilhaNuncaOContem()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras");
		_gestao.ModoScript = true;
		_cadastro.Com(id);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, "notificationhub"));

		resultado.Value.Should().Be(new ProvisionamentoDto(false, GestaoDeTenantsFalsa.Script));
		_trilha.Registros.Select(r => r.Verbo).Should().Equal(
			VerbosDeAuditoria.TenantRecursoLigar, VerbosDeAuditoria.TenantRecursoScriptGerado);
		_trilha.Registros.Should().OnlyContain(r => r.Metadata == null || !r.Metadata.Contains("SENHA-SECRETA-DE-TESTE"),
			"o script tem a senha db_owner e nunca vai para a trilha");
	}

	// ---- Ativar / desativar

	[Fact]
	public async Task Desativar_ConfirmacaoErrada_NaoChamaAPlataforma()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras");
		_cadastro.Com(id);

		var resultado = await Situacao().DesativarAsync(id, "Compras");

		resultado.Error.Should().Be(IntranetErrors.Tenants.ConfirmacaoNaoConfere);
		_gestao.Chamadas.Should().NotContain($"desativar:{id}");
	}

	[Fact]
	public async Task Desativar_ConfirmacaoCerta_DesativaEAudita()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras");
		_cadastro.Com(id);

		var resultado = await Situacao().DesativarAsync(id, " compras ");

		resultado.IsSuccess.Should().BeTrue();
		_gestao.Tenants[0].Ativo.Should().BeFalse();
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantDesativar);
	}

	[Fact]
	public async Task Ativar_ForaDoCadastro_NaoEncontrado()
	{
		var resultado = await Situacao().AtivarAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Tenants.NaoEncontrado);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Ativar_Cadastrado_AtivaEAudita()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras", ativo: false);
		_cadastro.Com(id);

		var resultado = await Situacao().AtivarAsync(id);

		resultado.IsSuccess.Should().BeTrue();
		_gestao.Tenants[0].Ativo.Should().BeTrue();
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantAtivar);
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~EscritaDeTenantsHandlersTests"`
Expected: erro de compilação — handlers inexistentes.

- [ ] **Step 3: Implementar**

`src/Secco.Intranet.Application/Tenants/CriarTenantHandler.cs`:

```csharp
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Dados de criação.</summary>
/// <param name="Sistema">Sistema que o tenant representa.</param>
/// <param name="Responsavel">Responsável.</param>
/// <param name="Nome">Nome do tenant na plataforma.</param>
/// <param name="Slug">Slug do tenant na plataforma.</param>
public sealed record CriarTenantCommand(string? Sistema, string? Responsavel, string? Nome, string? Slug);

/// <summary>
/// Cria o tenant no SecureGate e o registra no cadastro. Não há transação distribuída: se a
/// gravação local falhar depois do sucesso remoto, o erro diz o slug e manda adotar — falha
/// visível e recuperável pelo próprio fluxo.
/// </summary>
/// <param name="gestao">API de tenants.</param>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="ator">Quem age.</param>
public sealed class CriarTenantHandler(IGestaoDeTenants gestao, ITenantsAdministrados cadastro, ITrilhaDeAuditoria trilha, IAtorAtual ator)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Dados de criação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>O id do tenant criado.</returns>
	public async Task<Result<Guid>> HandleAsync(CriarTenantCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (RegrasDeTenant.ValidarCriacao(command.Sistema, command.Responsavel, command.Nome, command.Slug) is { } invalido)
		{
			return Result.Failure<Guid>(invalido);
		}

		var sistema = command.Sistema!.Trim();
		var slug = command.Slug!.Trim();
		var criado = await gestao.CriarTenantAsync(command.Nome!.Trim(), slug, cancellationToken).ConfigureAwait(false);

		if (criado.IsFailure)
		{
			return Result.Failure<Guid>(criado.Error);
		}

		// A partir daqui o tenant existe na plataforma: a criação é auditada aconteça o que
		// acontecer com o cadastro local.
		await AuditoriaDeTenants.TenantAsync(trilha, VerbosDeAuditoria.TenantCriar, criado.Value.Id, sistema, cancellationToken)
			.ConfigureAwait(false);

		var registro = new TenantAdministrado(
			criado.Value.Id, sistema, command.Responsavel!.Trim(), OrigemDoTenant.Criado, RegrasDeTenant.RotuloDoAtor(ator));

		bool registrado;

		try
		{
			registrado = await cadastro.TentarAdicionarAsync(registro, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception excecao) when (excecao is not OperationCanceledException)
		{
			return Result.Failure<Guid>(IntranetErrors.Tenants.RegistroLocalFalhou(slug));
		}

		return registrado
			? Result.Success(criado.Value.Id)
			: Result.Failure<Guid>(IntranetErrors.Tenants.RegistroLocalFalhou(slug));
	}
}
```

> O `catch (Exception)` é deliberado e estreito: o único passo dentro do `try` é a gravação local, e a mensagem de recuperação vale para qualquer causa (banco fora, timeout). O analisador CA1031 está ligado via `AnalysisLevel`: se ele reprovar, suprima **só nesta linha** com `#pragma warning disable CA1031` e o comentário acima — não amplie o `try`.

`src/Secco.Intranet.Application/Tenants/AdotarTenantHandler.cs`:

```csharp
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Dados de adoção.</summary>
/// <param name="TenantId">Tenant escolhido (vem do formulário — tratado como não confiável).</param>
/// <param name="Sistema">Sistema que o tenant representa.</param>
/// <param name="Responsavel">Responsável.</param>
public sealed record AdotarTenantCommand(Guid TenantId, string? Sistema, string? Responsavel);

/// <summary>
/// Traz para o cadastro um tenant criado por fora. As exclusões valem aqui, e não só na lista da
/// tela: um POST com Guid forjado também é recusado.
/// </summary>
/// <param name="gestao">API de tenants.</param>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="protegidos">Tenants que nunca são administrados.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="ator">Quem age.</param>
public sealed class AdotarTenantHandler(
	IGestaoDeTenants gestao, ITenantsAdministrados cadastro, ITenantsProtegidos protegidos, ITrilhaDeAuditoria trilha, IAtorAtual ator)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Dados de adoção.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(AdotarTenantCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (RegrasDeTenant.ValidarRegistro(command.Sistema, command.Responsavel) is { } invalido)
		{
			return Result.Failure(invalido);
		}

		if (command.TenantId == Guid.Empty
			|| (await protegidos.ListarAsync(cancellationToken).ConfigureAwait(false)).Contains(command.TenantId))
		{
			return Result.Failure(IntranetErrors.Tenants.NaoAdotavel);
		}

		if (await cadastro.ObterAsync(command.TenantId, cancellationToken).ConfigureAwait(false) is not null)
		{
			return Result.Failure(IntranetErrors.Tenants.JaAdministrado);
		}

		var tenant = await gestao.ObterTenantAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

		if (tenant.IsFailure)
		{
			return Result.Failure(tenant.Error);
		}

		var sistema = command.Sistema!.Trim();
		var registro = new TenantAdministrado(
			command.TenantId, sistema, command.Responsavel!.Trim(), OrigemDoTenant.Adotado, RegrasDeTenant.RotuloDoAtor(ator));

		if (!await cadastro.TentarAdicionarAsync(registro, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(IntranetErrors.Tenants.JaAdministrado);
		}

		await AuditoriaDeTenants.TenantAsync(trilha, VerbosDeAuditoria.TenantAdotar, command.TenantId, sistema, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
```

`src/Secco.Intranet.Application/Tenants/LigarRecursoHandler.cs`:

```csharp
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Dados para ligar um recurso.</summary>
/// <param name="TenantId">Tenant.</param>
/// <param name="Recurso">Segmento de rota do recurso (não confiável).</param>
public sealed record LigarRecursoCommand(Guid TenantId, string? Recurso);

/// <summary>
/// Liga um recurso para o tenant. O SecureGate só marca o cadastro; LogStream e NotificationHub
/// provisionam o banco com os padrões da plataforma. Recurso já ligado não é reprovisionado. Em
/// modo script, o script volta para quem chamou e <b>nunca</b> entra na trilha.
/// </summary>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="gestao">API de tenants.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class LigarRecursoHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Tenant e recurso.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ProvisionamentoDto>> HandleAsync(LigarRecursoCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var registro = await cadastro.ObterParaEdicaoAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

		if (registro is null)
		{
			return Result.Failure<ProvisionamentoDto>(IntranetErrors.Tenants.NaoEncontrado);
		}

		if (!RecursosDaPlataforma.TentarLer(command.Recurso, out var recurso))
		{
			return Result.Failure<ProvisionamentoDto>(IntranetErrors.Tenants.RecursoInvalido);
		}

		if (RecursosDaPlataforma.Produto(recurso) is not { } produto)
		{
			if (!registro.HabilitarSecureGate())
			{
				return Result.Failure<ProvisionamentoDto>(IntranetErrors.Tenants.RecursoJaLigado);
			}

			await cadastro.SalvarAsync(cancellationToken).ConfigureAwait(false);
			await AuditoriaDeTenants.RecursoAsync(
				trilha, VerbosDeAuditoria.TenantRecursoLigar, registro.TenantId, registro.Sistema, recurso, true, cancellationToken)
				.ConfigureAwait(false);

			return Result.Success(new ProvisionamentoDto(true, null));
		}

		var tenant = await gestao.ObterTenantAsync(registro.TenantId, cancellationToken).ConfigureAwait(false);

		if (tenant.IsFailure)
		{
			return Result.Failure<ProvisionamentoDto>(tenant.Error);
		}

		if (tenant.Value.Produtos.Contains(produto, StringComparer.OrdinalIgnoreCase))
		{
			return Result.Failure<ProvisionamentoDto>(IntranetErrors.Tenants.RecursoJaLigado);
		}

		var provisionado = await gestao.ProvisionarAsync(registro.TenantId, produto, cancellationToken).ConfigureAwait(false);

		if (provisionado.IsFailure)
		{
			return provisionado;
		}

		await AuditoriaDeTenants.RecursoAsync(
			trilha, VerbosDeAuditoria.TenantRecursoLigar, registro.TenantId, registro.Sistema, recurso,
			provisionado.Value.Aplicado, cancellationToken).ConfigureAwait(false);

		if (!provisionado.Value.Aplicado)
		{
			await AuditoriaDeTenants.RecursoAsync(
				trilha, VerbosDeAuditoria.TenantRecursoScriptGerado, registro.TenantId, registro.Sistema, recurso, false,
				cancellationToken).ConfigureAwait(false);
		}

		return provisionado;
	}
}
```

`src/Secco.Intranet.Application/Tenants/AlterarSituacaoDoTenantHandler.cs`:

```csharp
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Ativa ou desativa um tenant do cadastro. Desativar exige o slug digitado.</summary>
/// <param name="cadastro">Cadastro local.</param>
/// <param name="gestao">API de tenants.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class AlterarSituacaoDoTenantHandler(ITenantsAdministrados cadastro, IGestaoDeTenants gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Ativa o tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> AtivarAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var registro = await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (registro is null)
		{
			return Result.Failure(IntranetErrors.Tenants.NaoEncontrado);
		}

		var ativado = await gestao.AtivarAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (ativado.IsSuccess)
		{
			await AuditoriaDeTenants.TenantAsync(trilha, VerbosDeAuditoria.TenantAtivar, tenantId, registro.Sistema, cancellationToken)
				.ConfigureAwait(false);
		}

		return ativado;
	}

	/// <summary>Desativa o tenant: login e catálogo do sistema param em até um TTL de cache.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="confirmacaoSlug">Slug digitado pelo admin; precisa ser igual ao do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> DesativarAsync(Guid tenantId, string? confirmacaoSlug, CancellationToken cancellationToken = default)
	{
		var registro = await cadastro.ObterAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (registro is null)
		{
			return Result.Failure(IntranetErrors.Tenants.NaoEncontrado);
		}

		var tenant = await gestao.ObterTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (tenant.IsFailure)
		{
			return Result.Failure(tenant.Error);
		}

		if (!string.Equals(confirmacaoSlug?.Trim(), tenant.Value.Slug, StringComparison.Ordinal))
		{
			return Result.Failure(IntranetErrors.Tenants.ConfirmacaoNaoConfere);
		}

		var desativado = await gestao.DesativarAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (desativado.IsSuccess)
		{
			await AuditoriaDeTenants.TenantAsync(trilha, VerbosDeAuditoria.TenantDesativar, tenantId, registro.Sistema, cancellationToken)
				.ConfigureAwait(false);
		}

		return desativado;
	}
}
```

Em `IntranetApplicationExtensions.cs`, junto dos handlers de leitura da Task 3:

```csharp
		services.AddScoped<CriarTenantHandler>();
		services.AddScoped<AdotarTenantHandler>();
		services.AddScoped<LigarRecursoHandler>();
		services.AddScoped<AlterarSituacaoDoTenantHandler>();
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~EscritaDeTenantsHandlersTests"`
Expected: PASS (22 testes, contando os 3 casos do teorema de protegidos e os 3 de recurso inválido).

- [ ] **Step 5: Suíte inteira e commit**

Run: `dotnet build Secco.Intranet.slnx -c Release` e `dotnet test Secco.Intranet.slnx -c Release`.

```bash
git add src/Secco.Intranet.Application/Tenants/CriarTenantHandler.cs src/Secco.Intranet.Application/Tenants/AdotarTenantHandler.cs src/Secco.Intranet.Application/Tenants/LigarRecursoHandler.cs src/Secco.Intranet.Application/Tenants/AlterarSituacaoDoTenantHandler.cs src/Secco.Intranet.Application/IntranetApplicationExtensions.cs tests/Secco.Intranet.Tests/Unit/EscritaDeTenantsHandlersTests.cs
git commit -m "feat(tenants): criar, adotar, ligar recurso e ativar/desativar" -m "Co-Authored-By: <modelo> <noreply@anthropic.com>"
```

---

## Task 5: Infrastructure — adaptador do SecureGate, no-op, tenants protegidos e composição

**Files:**
- Create: `src/Secco.Intranet.Infrastructure/Tenants/SecureGateGestaoDeTenants.cs`
- Create: `src/Secco.Intranet.Infrastructure/Tenants/GestaoDeTenantsIndisponivel.cs`
- Create: `src/Secco.Intranet.Infrastructure/Tenants/TenantsProtegidosDoCatalogo.cs`
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/TenantsProtegidosDoCatalogoTests.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/GestaoDeTenantsIndisponivelTests.cs`

**Interfaces:**
- Consumes: `IGestaoDeTenants`, `ITenantsProtegidos`, DTOs e erros (Task 2); `ISecureGateClient` (client gerado, `Secco.SecureGate.Client`): `CreateTenantAsync(CreateTenantRequest, ct) : TenantDto`, `ListTenantsAsync(ct) : ICollection<TenantDto>`, `GetTenantAsync(Guid, ct) : TenantDetailDto` (com `Products`), `ActivateTenantAsync(Guid, ct)`, `DeactivateTenantAsync(Guid, ct)`, `ProvisionTenantDatabaseAsync(Guid, string, ProvisionTenantDatabaseRequest, ct) : TenantDatabaseProvisioningDto` (`Applied`, `Script`), `GetTenantDatabaseStatusAsync(Guid, ct) : ICollection<TenantDatabaseStatusDto>` (`Product`, `Reachable`, `FailureReason`). `ITenantCatalog.ListAsync(ct) : IReadOnlyList<TenantInfo(TenantId, ConnectionString)>`; `ITenantContext { Guid? TenantId; bool IsResolved; }`.
- Produces: `SecureGateGestaoDeTenants : IGestaoDeTenants`; `GestaoDeTenantsIndisponivel : IGestaoDeTenants`; `TenantsProtegidosDoCatalogo : ITenantsProtegidos` com `public static readonly Guid TenantDeInstalacao`.

- [ ] **Step 1: Testes que falham**

`tests/Secco.Intranet.Tests/Unit/TenantsProtegidosDoCatalogoTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Infrastructure.Tenants;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class TenantsProtegidosDoCatalogoTests
{
	[Fact]
	public async Task Protege_Instalacao_TodaIntranetDoCatalogo_EOProprio()
	{
		var filialA = Guid.NewGuid();
		var filialB = Guid.NewGuid();
		var proprio = Guid.NewGuid();
		var catalogo = new CatalogoFixo(filialA, filialB);

		var protegidos = await new TenantsProtegidosDoCatalogo(catalogo, new TenantDaRequisicao(proprio)).ListarAsync();

		protegidos.Should().BeEquivalentTo([TenantsProtegidosDoCatalogo.TenantDeInstalacao, filialA, filialB, proprio]);
	}

	[Fact]
	public void TenantDeInstalacao_EspelhaOGuidFixoDaPlataforma() =>
		TenantsProtegidosDoCatalogo.TenantDeInstalacao.Should().Be(Guid.Parse("018f0000-0000-7000-8000-0000000000ff"));

	[Fact]
	public async Task SemTenantResolvido_AindaProtegeOCatalogo()
	{
		var filial = Guid.NewGuid();

		var protegidos = await new TenantsProtegidosDoCatalogo(new CatalogoFixo(filial), new TenantDaRequisicao(null)).ListarAsync();

		protegidos.Should().Contain(filial).And.Contain(TenantsProtegidosDoCatalogo.TenantDeInstalacao);
	}

	private sealed class CatalogoFixo(params Guid[] tenants) : ITenantCatalog
	{
		public ValueTask<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(tenants.Contains(tenantId) ? new TenantInfo(tenantId, "x") : null);

		public ValueTask<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<TenantInfo>>([.. tenants.Select(t => new TenantInfo(t, "x"))]);
	}

	private sealed class TenantDaRequisicao(Guid? tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => tenantId is not null;
	}
}
```

> Se `ITenantCatalog` tiver mais membros na versão instalada do SDK, implemente-os no `CatalogoFixo` lançando `NotSupportedException` — o teste só usa `ListAsync`.

`tests/Secco.Intranet.Tests/Unit/GestaoDeTenantsIndisponivelTests.cs`:

```csharp
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Infrastructure.Tenants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class GestaoDeTenantsIndisponivelTests
{
	[Fact]
	public async Task TodaOperacao_RespondeNaoConfigurado_NuncaFingeSucesso()
	{
		var gestao = new GestaoDeTenantsIndisponivel();
		var id = Guid.NewGuid();

		(await gestao.ListarTenantsAsync()).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.ObterTenantAsync(id)).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.ObterStatusDosBancosAsync(id)).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.CriarTenantAsync("n", "s")).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.ProvisionarAsync(id, "logstream")).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.AtivarAsync(id)).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
		(await gestao.DesativarAsync(id)).Error.Should().Be(IntranetErrors.Tenants.NaoConfigurado);
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TenantsProtegidosDoCatalogoTests|FullyQualifiedName~GestaoDeTenantsIndisponivelTests"`
Expected: erro de compilação.

- [ ] **Step 3: Tenants protegidos e no-op**

`src/Secco.Intranet.Infrastructure/Tenants/TenantsProtegidosDoCatalogo.cs`:

```csharp
using Secco.Intranet.Application.Tenants;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.Intranet.Infrastructure.Tenants;

/// <summary>
/// Tenants protegidos: o de instalação da plataforma, todo tenant que tem banco no catálogo da
/// Intranet (ela própria e as filiais da mesma instalação) e o da requisição, por garantia —
/// em DEV o catálogo pode vir de configuração e não listar o tenant em uso.
/// </summary>
/// <param name="catalogo">Catálogo de tenants do produto Intranet.</param>
/// <param name="tenantContext">Tenant da requisição.</param>
public sealed class TenantsProtegidosDoCatalogo(ITenantCatalog catalogo, ITenantContext tenantContext) : ITenantsProtegidos
{
	/// <summary>
	/// Tenant de instalação da plataforma — espelho de <c>SecureGatePlatform.TenantId</c> no
	/// <c>secco-platform</c>. É um Guid fixo por decisão de lá (localizado sempre por Guid, nunca por slug).
	/// </summary>
	public static readonly Guid TenantDeInstalacao = Guid.Parse("018f0000-0000-7000-8000-0000000000ff");

	/// <inheritdoc />
	public async Task<IReadOnlySet<Guid>> ListarAsync(CancellationToken cancellationToken = default)
	{
		var protegidos = new HashSet<Guid> { TenantDeInstalacao };

		foreach (var tenant in await catalogo.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			protegidos.Add(tenant.TenantId);
		}

		if (tenantContext.IsResolved && tenantContext.TenantId is { } proprio)
		{
			protegidos.Add(proprio);
		}

		return protegidos;
	}
}
```

`src/Secco.Intranet.Infrastructure/Tenants/GestaoDeTenantsIndisponivel.cs`:

```csharp
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Tenants;

/// <summary>
/// No-op de <see cref="IGestaoDeTenants"/> para DEV e Testing sem <c>Secco:SecureGate</c>. Responde
/// "não configurado" em tudo: fingir que um tenant foi criado seria mentir para quem administra.
/// </summary>
public sealed class GestaoDeTenantsIndisponivel : IGestaoDeTenants
{
	private static readonly Error Erro = IntranetErrors.Tenants.NaoConfigurado;

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<TenantDaPlataformaDto>>> ListarTenantsAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<TenantDaPlataformaDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<TenantDaPlataformaDetalheDto>> ObterTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<TenantDaPlataformaDetalheDto>(Erro));

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<StatusDoBancoDto>>> ObterStatusDosBancosAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<StatusDoBancoDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<TenantDaPlataformaDto>> CriarTenantAsync(string nome, string slug, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<TenantDaPlataformaDto>(Erro));

	/// <inheritdoc />
	public Task<Result<ProvisionamentoDto>> ProvisionarAsync(Guid tenantId, string produto, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<ProvisionamentoDto>(Erro));

	/// <inheritdoc />
	public Task<Result> AtivarAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> DesativarAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));
}
```

- [ ] **Step 4: Adaptador real**

`src/Secco.Intranet.Infrastructure/Tenants/SecureGateGestaoDeTenants.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Tenants;

/// <summary>
/// Adapter real de <see cref="IGestaoDeTenants"/>, sobre o client administrativo do SecureGate
/// (a mesma credencial do <c>SecureGateGestaoDeAcesso</c>). Só é resolvido com <c>Secco:SecureGate</c>
/// configurado. Falha de rede, de status e timeout viram <see cref="Result"/>; cancelamento pedido
/// pelo chamador é relançado. O log leva só operação e status — <b>nunca</b> corpo de resposta: a do
/// provisionamento em modo script traz a senha do banco.
/// </summary>
/// <param name="client">Client administrativo do SecureGate.</param>
/// <param name="logger">Log de falhas.</param>
public sealed class SecureGateGestaoDeTenants(ISecureGateClient client, ILogger<SecureGateGestaoDeTenants> logger) : IGestaoDeTenants
{
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<TenantDaPlataformaDto>>> ListarTenantsAsync(CancellationToken cancellationToken = default) =>
		ExecutarAsync<IReadOnlyList<TenantDaPlataformaDto>>(
			"listar tenants",
			async token =>
			{
				var tenants = await client.ListTenantsAsync(token).ConfigureAwait(false);

				return [.. tenants.Select(t => new TenantDaPlataformaDto(t.Id, t.Name, t.Slug, t.IsActive))];
			},
			conflito: null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<TenantDaPlataformaDetalheDto>> ObterTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		ExecutarAsync(
			"obter tenant",
			async token =>
			{
				var t = await client.GetTenantAsync(tenantId, token).ConfigureAwait(false);

				return new TenantDaPlataformaDetalheDto(t.Id, t.Name, t.Slug, t.IsActive, [.. t.Products ?? []]);
			},
			conflito: null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<StatusDoBancoDto>>> ObterStatusDosBancosAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		ExecutarAsync<IReadOnlyList<StatusDoBancoDto>>(
			"obter status dos bancos",
			async token =>
			{
				var status = await client.GetTenantDatabaseStatusAsync(tenantId, token).ConfigureAwait(false);

				return [.. status.Select(s => new StatusDoBancoDto(s.Product, s.Reachable, s.FailureReason))];
			},
			conflito: null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<TenantDaPlataformaDto>> CriarTenantAsync(string nome, string slug, CancellationToken cancellationToken = default) =>
		ExecutarAsync(
			"criar tenant",
			async token =>
			{
				var t = await client.CreateTenantAsync(new CreateTenantRequest { Name = nome, Slug = slug }, token).ConfigureAwait(false);

				return new TenantDaPlataformaDto(t.Id, t.Name, t.Slug, t.IsActive);
			},
			conflito: IntranetErrors.Tenants.SlugJaExiste,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<ProvisionamentoDto>> ProvisionarAsync(Guid tenantId, string produto, CancellationToken cancellationToken = default) =>
		ExecutarAsync(
			"provisionar banco",
			async token =>
			{
				// Alvo, servidor e nomes vazios: a plataforma usa os padrões dela. A tela não oferece
				// escolher servidor arbitrário (spec, regra 5).
				var resposta = await client
					.ProvisionTenantDatabaseAsync(tenantId, produto, new ProvisionTenantDatabaseRequest { CreateDatabase = true }, token)
					.ConfigureAwait(false);

				return new ProvisionamentoDto(resposta.Applied, resposta.Applied ? null : resposta.Script);
			},
			conflito: IntranetErrors.Tenants.RecursoJaLigado,
			cancellationToken);

	/// <inheritdoc />
	public async Task<Result> AtivarAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		await ExecutarAsync(
			"ativar tenant",
			async token =>
			{
				await client.ActivateTenantAsync(tenantId, token).ConfigureAwait(false);

				return true;
			},
			conflito: null,
			cancellationToken).ConfigureAwait(false) is { IsFailure: true } falha
			? Result.Failure(falha.Error)
			: Result.Success();

	/// <inheritdoc />
	public async Task<Result> DesativarAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		await ExecutarAsync(
			"desativar tenant",
			async token =>
			{
				await client.DeactivateTenantAsync(tenantId, token).ConfigureAwait(false);

				return true;
			},
			conflito: IntranetErrors.Tenants.DesativacaoRecusada,
			cancellationToken).ConfigureAwait(false) is { IsFailure: true } falha
			? Result.Failure(falha.Error)
			: Result.Success();

	private async Task<Result<T>> ExecutarAsync<T>(
		string operacao,
		Func<CancellationToken, Task<T>> chamada,
		Error? conflito,
		CancellationToken cancellationToken)
	{
		try
		{
			return Result.Success(await chamada(cancellationToken).ConfigureAwait(false));
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.NotFound)
		{
			return Result.Failure<T>(IntranetErrors.Tenants.NaoEncontrado);
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.Conflict && conflito is not null)
		{
			return Result.Failure<T>(conflito);
		}
		catch (ApiException apiException)
		{
			logger.LogWarning("Falha ao {Operacao} no SecureGate (status {StatusCode}).", operacao, apiException.StatusCode);

			return Result.Failure<T>(IntranetErrors.Tenants.Indisponivel);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao {Operacao} no SecureGate.", operacao);

			return Result.Failure<T>(IntranetErrors.Tenants.Indisponivel);
		}
		catch (OperationCanceledException operationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Timeout do HttpClient, não cancelamento do chamador: escapar viraria um 500.
			logger.LogWarning(operationCanceledException, "Timeout ao {Operacao} no SecureGate.", operacao);

			return Result.Failure<T>(IntranetErrors.Tenants.Indisponivel);
		}
	}
}
```

> `ApiException` é o tipo gerado pelo NSwag no namespace `Secco.SecureGate.Client` — o mesmo que `SecureGateGestaoDeAcesso` captura. **Não** logue `apiException.Response`: na resposta do provisionamento ela pode conter o script.

- [ ] **Step 5: Composição**

Em `IntranetInfrastructureExtensions.cs`, depois de `services.AddScoped(CriarGestaoDeAcesso);` (com `using Secco.Intranet.Application.Tenants;` e `using Secco.Intranet.Infrastructure.Tenants;`):

```csharp
		services.AddScoped(CriarGestaoDeTenants);
		services.AddScoped<ITenantsProtegidos, TenantsProtegidosDoCatalogo>();
```

E, junto de `CriarGestaoDeAcesso`:

```csharp
	/// <summary>
	/// Escolhe o adapter da gestão de tenants pela mesma configuração do client administrativo:
	/// com <c>Secco:SecureGate</c> presente, o real; sem ela, o que responde "não configurado".
	/// </summary>
	private static IGestaoDeTenants CriarGestaoDeTenants(IServiceProvider serviceProvider)
	{
		var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

		return credenciais.IsConfigured
			? ActivatorUtilities.CreateInstance<SecureGateGestaoDeTenants>(serviceProvider)
			: new GestaoDeTenantsIndisponivel();
	}
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TenantsProtegidosDoCatalogoTests|FullyQualifiedName~GestaoDeTenantsIndisponivelTests"`
Expected: PASS.

- [ ] **Step 7: Suíte inteira e commit**

Run: `dotnet build Secco.Intranet.slnx -c Release` e `dotnet test Secco.Intranet.slnx -c Release`.

```bash
git add src/Secco.Intranet.Infrastructure/Tenants src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs tests/Secco.Intranet.Tests/Unit/TenantsProtegidosDoCatalogoTests.cs tests/Secco.Intranet.Tests/Unit/GestaoDeTenantsIndisponivelTests.cs
git commit -m "feat(tenants): adaptador do SecureGate e tenants protegidos" -m "Co-Authored-By: <modelo> <noreply@anthropic.com>"
```

---

## Task 6: Web — filtros `[ExigeSegundoFator]` e `[TenantAdministrado]`

**Files:**
- Create: `src/Secco.Intranet.Web/Authentication/ExigeSegundoFatorAttribute.cs`
- Create: `src/Secco.Intranet.Web/Authentication/TenantAdministradoAttribute.cs`
- Create: `src/Secco.Intranet.Web/Models/Tenants/TenantsViewModels.cs` (só `SegundoFatorViewModel` nesta task)
- Create: `src/Secco.Intranet.Web/Views/Tenants/SegundoFatorObrigatorio.cshtml`
- Create: `src/Secco.Intranet.Web/Views/Tenants/SegundoFatorIndisponivel.cshtml`
- Modify: `src/Secco.Intranet.Web/Views/_ViewImports.cshtml`
- Test: `tests/Secco.Intranet.Tests/Unit/ExigeSegundoFatorAttributeTests.cs`
- Test: `tests/Secco.Intranet.Tests/Unit/TenantAdministradoAttributeTests.cs`

**Interfaces:**
- Consumes: `IGestaoDeAcesso.ObterUsuarioAsync(Guid, ct) : Task<Result<UsuarioDetalheDto>>` (campo `DoisFatoresAtivo`); `IntranetErrors.Acesso.NaoConfigurado`; `AcessoAdministrativo.ModoAbertoDeDev`; `SeccoClaims.Subject`; `IMemoryCache` (já registrado por `AddMemoryCache()` na Infrastructure); `VerificarTenantAdministradoHandler` (Task 3).
- Produces:
  - `[ExigeSegundoFator]` — `IAsyncAuthorizationFilter`, `Order = int.MinValue + 1`. Resultados: passa; `StatusCodeResult(403)`; `ViewResult` `SegundoFatorObrigatorio` com status 403; `ViewResult` `SegundoFatorIndisponivel` com status 503.
  - `[TenantAdministrado]` — `IAsyncAuthorizationFilter`, `Order = int.MinValue + 2`, lê o route value `tenantId` e responde `NotFoundResult` fora do cadastro.
  - `SegundoFatorViewModel(string? UrlDoCadastro)`.

**Tabela de decisão do `[ExigeSegundoFator]`** (fail-closed em tudo que não for "tem 2FA" ou "SecureGate não configurado"):

| Situação | Resultado |
|---|---|
| Modo aberto de DEV | passa |
| Sem claim `sub` Guid | 403 |
| Cache diz "tem 2FA" | passa |
| `DoisFatoresAtivo = true` | passa e guarda no cache por 60 s (só o positivo) |
| `DoisFatoresAtivo = false` | view `SegundoFatorObrigatorio`, 403 |
| `Acesso.NaoConfigurado` | passa — a área inteira mostra "não configurado" |
| Usuário não encontrado | 403 |
| Qualquer outra falha (SecureGate fora, timeout) | view `SegundoFatorIndisponivel`, 503 |

Só o positivo vai para o cache: quem acabou de cadastrar o 2FA entra na hora, e quem desligou perde o acesso em até 60 s.

- [ ] **Step 1: Testes que falham — `[ExigeSegundoFator]`**

`tests/Secco.Intranet.Tests/Unit/ExigeSegundoFatorAttributeTests.cs`:

```csharp
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Authentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ExigeSegundoFatorAttributeTests
{
	private static readonly Guid Admin = Guid.NewGuid();

	private sealed class AmbienteFalso(string nome) : IWebHostEnvironment
	{
		public string ApplicationName { get; set; } = "Teste";

		public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

		public string WebRootPath { get; set; } = string.Empty;

		public string EnvironmentName { get; set; } = nome;

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private sealed class TenantContextFalso : ITenantContext
	{
		public Guid? TenantId { get; } = Guid.NewGuid();

		public bool IsResolved => true;
	}

	private static (AuthorizationFilterContext Contexto, IServiceProvider Servicos) Montar(
		IGestaoDeAcesso gestao, Guid? usuario, string ambiente = "Testing", IMemoryCache? cache = null)
	{
		var servicos = new ServiceCollection()
			.AddSingleton<IWebHostEnvironment>(new AmbienteFalso(ambiente))
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
			.AddSingleton<ITenantContext>(new TenantContextFalso())
			.AddSingleton(gestao)
			.AddSingleton(cache ?? new MemoryCache(new MemoryCacheOptions()))
			.AddSingleton<IModelMetadataProvider>(new EmptyModelMetadataProvider())
			.BuildServiceProvider();

		var http = new DefaultHttpContext { RequestServices = servicos };

		if (usuario is not null)
		{
			http.User = new ClaimsPrincipal(new ClaimsIdentity(
				[new Claim(SeccoClaims.Role, "intranet-admin"), new Claim(SeccoClaims.Subject, usuario.Value.ToString())], "Teste"));
		}

		return (new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []), servicos);
	}

	private static GestaoDeAcessoFalsa ComAdmin(bool doisFatores)
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Admin, "admin@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin");

		return doisFatores ? gestao.ComDoisFatores(Admin) : gestao;
	}

	[Fact]
	public async Task ComDoisFatores_Passa()
	{
		var (contexto, _) = Montar(ComAdmin(true), Admin);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public async Task SemDoisFatores_ViewQueExplica_403()
	{
		var (contexto, _) = Montar(ComAdmin(false), Admin);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		var view = contexto.Result.Should().BeOfType<ViewResult>().Subject;
		view.ViewName.Should().Be("SegundoFatorObrigatorio");
		view.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
	}

	[Fact]
	public async Task SecureGateFora_Indisponivel_503_NuncaAbre()
	{
		var gestao = ComAdmin(true);
		gestao.FalharCom = IntranetErrors.Acesso.Indisponivel;
		var (contexto, _) = Montar(gestao, Admin);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		var view = contexto.Result.Should().BeOfType<ViewResult>().Subject;
		view.ViewName.Should().Be("SegundoFatorIndisponivel");
		view.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
	}

	[Fact]
	public async Task SecureGateNaoConfigurado_Passa_AAreaExplica()
	{
		var gestao = ComAdmin(false);
		gestao.FalharCom = IntranetErrors.Acesso.NaoConfigurado;
		var (contexto, _) = Montar(gestao, Admin);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeNull();
	}

	[Fact]
	public async Task SemSub_403()
	{
		var (contexto, _) = Montar(ComAdmin(true), usuario: null);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
	}

	[Fact]
	public async Task UsuarioNaoEncontrado_403()
	{
		var (contexto, _) = Montar(new GestaoDeAcessoFalsa(), Admin);

		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
	}

	[Fact]
	public async Task Positivo_VemDoCache_NaSegundaVez()
	{
		var cache = new MemoryCache(new MemoryCacheOptions());
		var gestao = ComAdmin(true);
		var (primeiro, _) = Montar(gestao, Admin, cache: cache);
		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(primeiro);

		gestao.FalharCom = IntranetErrors.Acesso.Indisponivel;
		var (segundo, _) = Montar(gestao, Admin, cache: cache);
		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(segundo);

		segundo.Result.Should().BeNull("o positivo fica 60 s no cache");
	}

	[Fact]
	public async Task Negativo_NaoVaiParaOCache()
	{
		var cache = new MemoryCache(new MemoryCacheOptions());
		var semFator = ComAdmin(false);
		var (primeiro, _) = Montar(semFator, Admin, cache: cache);
		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(primeiro);

		var (segundo, _) = Montar(ComAdmin(true), Admin, cache: cache);
		await new ExigeSegundoFatorAttribute().OnAuthorizationAsync(segundo);

		segundo.Result.Should().BeNull("quem acabou de cadastrar o 2FA entra na hora");
	}

	[Fact]
	public void Ordem_DepoisDoGateDeRole_AntesDoAntifalsificacao() =>
		new ExigeSegundoFatorAttribute().Order.Should().BeGreaterThan(new SomenteIntranetAdminAttribute().Order).And.BeLessThan(1000);
}
```

Em `tests/Secco.Intranet.Tests/Support/DublesDeAcesso.cs`, acrescente à `GestaoDeAcessoFalsa`, logo depois de `ComUsuario` (sem mudar a assinatura dele — 39 chamadas existentes dependem dela):

```csharp
	/// <summary>Marca o usuário como tendo segundo fator ativo.</summary>
	public GestaoDeAcessoFalsa ComDoisFatores(Guid id)
	{
		var indice = Usuarios.FindIndex(u => u.Id == id);
		Usuarios[indice] = Usuarios[indice] with { DoisFatoresAtivo = true };

		return this;
	}
```

E, no início de `ObterUsuarioAsync` do mesmo dublê (hoje ele ignora `FalharCom`, que só vale para as escritas):

```csharp
		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<UsuarioDetalheDto>(erro));
		}
```

Rode os testes de acesso existentes depois desta mudança: nenhum deles define `FalharCom` antes de uma leitura de usuário esperando sucesso — se algum quebrar, é exatamente esse caso, e o teste deve passar a definir `FalharCom` só depois da leitura.

- [ ] **Step 2: Testes que falham — `[TenantAdministrado]`**

`tests/Secco.Intranet.Tests/Unit/TenantAdministradoAttributeTests.cs`:

```csharp
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Authentication;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class TenantAdministradoAttributeTests
{
	private static readonly Guid Cadastrado = Guid.NewGuid();

	private static AuthorizationFilterContext Contexto(object? tenantId)
	{
		var servicos = new ServiceCollection()
			.AddSingleton<ITenantsAdministrados>(new TenantsAdministradosFalso().Com(Cadastrado))
			.AddScoped<VerificarTenantAdministradoHandler>()
			.BuildServiceProvider();

		var rota = new RouteData();

		if (tenantId is not null)
		{
			rota.Values["tenantId"] = tenantId;
		}

		return new AuthorizationFilterContext(
			new ActionContext(new DefaultHttpContext { RequestServices = servicos }, rota, new ActionDescriptor()), []);
	}

	[Fact]
	public async Task Cadastrado_Passa()
	{
		var contexto = Contexto(Cadastrado.ToString());

		await new TenantAdministradoAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeNull();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("nao-e-guid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public async Task SemTenantValido_404(string? valor)
	{
		var contexto = Contexto(valor);

		await new TenantAdministradoAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeOfType<NotFoundResult>();
	}

	[Fact]
	public async Task ForaDoCadastro_404_Nunca403()
	{
		var contexto = Contexto(Guid.NewGuid().ToString());

		await new TenantAdministradoAttribute().OnAuthorizationAsync(contexto);

		contexto.Result.Should().BeOfType<NotFoundResult>("403 confirmaria que o tenant existe");
	}

	[Fact]
	public void Ordem_DepoisDoSegundoFator() =>
		new TenantAdministradoAttribute().Order.Should().BeGreaterThan(new ExigeSegundoFatorAttribute().Order).And.BeLessThan(1000);
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~ExigeSegundoFatorAttributeTests|FullyQualifiedName~TenantAdministradoAttributeTests"`
Expected: erro de compilação.

- [ ] **Step 4: ViewModel e views**

`src/Secco.Intranet.Web/Models/Tenants/TenantsViewModels.cs`:

```csharp
namespace Secco.Intranet.Web.Models.Tenants;

/// <summary>Tela que explica por que a área de tenants exige segundo fator.</summary>
/// <param name="UrlDoCadastro">Página de cadastro do 2FA no SecureGate, quando a URL é conhecida.</param>
public sealed record SegundoFatorViewModel(string? UrlDoCadastro);
```

`src/Secco.Intranet.Web/Views/Tenants/SegundoFatorObrigatorio.cshtml`:

```cshtml
@model SegundoFatorViewModel
@{
    ViewData["Title"] = "Tenants";

    var cabecalho = new PageHeaderModel("Tenants", "Sistemas da empresa administrados por esta Intranet.");
    var vazio = new EmptyStateModel(
        "bi-shield-exclamation",
        "Ative o segundo fator para continuar",
        "A administração de tenants cria sistemas, liga recursos e desativa acessos de toda a empresa. Por isso ela exige que a sua conta use segundo fator (aplicativo autenticador). Cadastre o segundo fator no SecureGate e volte para esta página.");
}

<partial name="_PageHeader" model="cabecalho" />
<partial name="_EmptyState" model="vazio" />

@if (Model.UrlDoCadastro is { } url)
{
    <p class="mt-3"><a class="btn btn-primary" href="@url">Cadastrar o segundo fator</a></p>
}
```

`src/Secco.Intranet.Web/Views/Tenants/SegundoFatorIndisponivel.cshtml`:

```cshtml
@model SegundoFatorViewModel
@{
    ViewData["Title"] = "Tenants";

    var cabecalho = new PageHeaderModel("Tenants", "Sistemas da empresa administrados por esta Intranet.");
    var vazio = new EmptyStateModel(
        "bi-plug",
        "Não foi possível confirmar o segundo fator",
        "O SecureGate não respondeu agora. Por segurança, a área fica fechada até a confirmação. Tente novamente em instantes.");
}

<partial name="_PageHeader" model="cabecalho" />
<partial name="_EmptyState" model="vazio" />
```

Em `_ViewImports.cshtml`, depois de `@using Secco.Intranet.Web.Models.Acesso`:

```cshtml
@using Secco.Intranet.Web.Models.Tenants
@using Secco.Intranet.Application.Tenants
@using Secco.Intranet.Domain.Tenants
```

- [ ] **Step 5: Filtros**

`src/Secco.Intranet.Web/Authentication/ExigeSegundoFatorAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Web.Models.Tenants;
using Secco.Intranet.Web.Navigation;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Exige segundo fator ativo na conta de quem acessa (spec da área de tenants, ponto crítico). O
/// token não traz <c>amr</c>, então a prova é o cadastro da conta no SecureGate
/// (<c>DoisFatoresAtivo</c>). <b>Fail-closed:</b> só passa com 2FA confirmado, ou com o SecureGate
/// não configurado (aí a área inteira já responde "não configurado"). Roda depois de
/// <see cref="SomenteIntranetAdminAttribute"/> e antes do antifalsificação.
/// </summary>
/// <remarks>
/// Resíduo conhecido: no login federado pelo Entra, o segundo fator é do Entra e a Intranet não o
/// enxerga — a conta pode não ter 2FA local. Correção completa depende da plataforma.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ExigeSegundoFatorAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
	private static readonly TimeSpan DuracaoDoPositivo = TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	public int Order => int.MinValue + 1;

	/// <inheritdoc />
	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var servicos = context.HttpContext.RequestServices;
		var configuracao = servicos.GetRequiredService<IConfiguration>();

		if (AcessoAdministrativo.ModoAbertoDeDev(servicos.GetRequiredService<IWebHostEnvironment>(), configuracao))
		{
			return;
		}

		if (!Guid.TryParse(context.HttpContext.User.FindFirst(SeccoClaims.Subject)?.Value, out var usuarioId))
		{
			context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);

			return;
		}

		var cache = servicos.GetRequiredService<IMemoryCache>();
		var chave = $"tenants:2fa:{servicos.GetRequiredService<ITenantContext>().TenantId}:{usuarioId}";

		if (cache.TryGetValue(chave, out bool confirmado) && confirmado)
		{
			return;
		}

		var usuario = await servicos.GetRequiredService<IGestaoDeAcesso>()
			.ObterUsuarioAsync(usuarioId, context.HttpContext.RequestAborted)
			.ConfigureAwait(false);

		if (usuario.IsSuccess && usuario.Value.DoisFatoresAtivo)
		{
			cache.Set(chave, true, DuracaoDoPositivo);

			return;
		}

		if (usuario.IsSuccess)
		{
			context.Result = Tela(context, servicos, configuracao, "SegundoFatorObrigatorio", StatusCodes.Status403Forbidden);

			return;
		}

		if (usuario.Error == IntranetErrors.Acesso.NaoConfigurado)
		{
			return;
		}

		context.Result = usuario.Error.Type == ErrorType.NotFound
			? new StatusCodeResult(StatusCodes.Status403Forbidden)
			: Tela(context, servicos, configuracao, "SegundoFatorIndisponivel", StatusCodes.Status503ServiceUnavailable);
	}

	private static ViewResult Tela(
		AuthorizationFilterContext context, IServiceProvider servicos, IConfiguration configuracao, string view, int status)
	{
		var authority = configuracao["Secco:SecureGate:Authority"]?.TrimEnd('/');
		var modelo = new SegundoFatorViewModel(string.IsNullOrWhiteSpace(authority) ? null : $"{authority}/Account/TwoFactor");

		return new ViewResult
		{
			ViewName = view,
			StatusCode = status,
			ViewData = new ViewDataDictionary<SegundoFatorViewModel>(
				servicos.GetRequiredService<IModelMetadataProvider>(), context.ModelState) { Model = modelo },
		};
	}
}
```

`src/Secco.Intranet.Web/Authentication/TenantAdministradoAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Tenants;

namespace Secco.Intranet.Web.Authentication;

/// <summary>
/// Barra qualquer action com <c>{tenantId}</c> fora do cadastro de tenants administrados — a
/// defesa contra IDOR da spec. Responde 404, nunca 403: 403 confirmaria que o tenant existe. Roda
/// depois de <see cref="ExigeSegundoFatorAttribute"/> e antes do antifalsificação.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TenantAdministradoAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
	/// <summary>Nome do route value lido.</summary>
	public const string ValorDeRota = "tenantId";

	/// <inheritdoc />
	public int Order => int.MinValue + 2;

	/// <inheritdoc />
	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!Guid.TryParse(context.RouteData.Values[ValorDeRota]?.ToString(), out var tenantId))
		{
			context.Result = new NotFoundResult();

			return;
		}

		var verificar = context.HttpContext.RequestServices.GetRequiredService<VerificarTenantAdministradoHandler>();

		if (!await verificar.HandleAsync(tenantId, context.HttpContext.RequestAborted).ConfigureAwait(false))
		{
			context.Result = new NotFoundResult();
		}
	}
}
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~ExigeSegundoFatorAttributeTests|FullyQualifiedName~TenantAdministradoAttributeTests"`
Expected: PASS.

- [ ] **Step 7: Suíte inteira e commit**

Run: `dotnet build Secco.Intranet.slnx -c Release` e `dotnet test Secco.Intranet.slnx -c Release` (os testes de acesso existentes continuam verdes depois da mudança no dublê).

```bash
git add src/Secco.Intranet.Web/Authentication/ExigeSegundoFatorAttribute.cs src/Secco.Intranet.Web/Authentication/TenantAdministradoAttribute.cs src/Secco.Intranet.Web/Models/Tenants/TenantsViewModels.cs src/Secco.Intranet.Web/Views/Tenants/SegundoFatorObrigatorio.cshtml src/Secco.Intranet.Web/Views/Tenants/SegundoFatorIndisponivel.cshtml src/Secco.Intranet.Web/Views/_ViewImports.cshtml tests/Secco.Intranet.Tests/Support/DublesDeAcesso.cs tests/Secco.Intranet.Tests/Unit/ExigeSegundoFatorAttributeTests.cs tests/Secco.Intranet.Tests/Unit/TenantAdministradoAttributeTests.cs
git commit -m "feat(tenants): filtros de segundo fator e de tenant administrado" -m "Co-Authored-By: <modelo> <noreply@anthropic.com>"
```

---

## Task 7: Web — `TenantsController`, telas, menu e slug reservado

**Files:**
- Create: `src/Secco.Intranet.Web/Controllers/TenantsController.cs`
- Modify: `src/Secco.Intranet.Web/Models/Tenants/TenantsViewModels.cs` (acrescentar os ViewModels abaixo)
- Create: `src/Secco.Intranet.Web/Views/Tenants/Index.cshtml`, `Novo.cshtml`, `Adotar.cshtml`, `Detalhe.cshtml`, `Script.cshtml`, `Indisponivel.cshtml`
- Modify: `src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs` (item "Tenants")
- Modify: `src/Secco.Intranet.Application/Setores/SlugsReservados.cs` (`"tenants"`)
- Modify: `tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs` (dublê de `IGestaoDeTenants`)
- Test: `tests/Secco.Intranet.Tests/Integration/TenantsTelasTests.cs`

**Interfaces:**
- Consumes: handlers das Tasks 3–4; filtros da Task 6; `[SomenteIntranetAdmin]`; `FeedbackViewComponent.ChaveDaMensagem` / `ChaveDaMensagemDeErro`; partials `_PageHeader`, `_Badge`, `_EmptyState`; `GestaoDeTenantsFalsa`, `GestaoDeAcessoFalsa.ComDoisFatores`.
- Produces: rotas

| Método | Rota | Action |
|---|---|---|
| GET | `/tenants` | `Index` |
| GET/POST | `/tenants/novo` | `Novo` |
| GET/POST | `/tenants/adotar` | `Adotar` |
| GET | `/tenants/{tenantId:guid}` | `Detalhe` |
| POST | `/tenants/{tenantId:guid}/recursos/{recurso}` | `LigarRecurso` |
| POST | `/tenants/{tenantId:guid}/ativar` | `Ativar` |
| POST | `/tenants/{tenantId:guid}/desativar` | `Desativar` |

  e `IntranetWebFactory.GestaoDeTenants { get; set; }`.

- [ ] **Step 1: Dublê na fábrica de testes**

Em `IntranetWebFactory.cs`, ao lado de `GestaoDeAcesso`:

```csharp
	/// <summary>
	/// Dublê da API de tenants. Testes que precisam de tenants atribuem aqui; sem dublê, vale o
	/// no-op "não configurado" — o mesmo que o ambiente Testing teria.
	/// </summary>
	public IGestaoDeTenants? GestaoDeTenants { get; set; }
```

E em `ConfigureTestServices`, depois do registro de `IGestaoDeAcesso`:

```csharp
		services.AddScoped<IGestaoDeTenants>(_ => GestaoDeTenants ?? new GestaoDeTenantsIndisponivel());
```

(com `using Secco.Intranet.Application.Tenants;` e `using Secco.Intranet.Infrastructure.Tenants;`).

- [ ] **Step 2: Testes de tela que falham**

`tests/Secco.Intranet.Tests/Integration/TenantsTelasTests.cs`:

```csharp
using System.Net;
using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>Telas da área de tenants pelo host HTTP real, com dublês da plataforma.</summary>
public class TenantsTelasTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Admin = Guid.NewGuid();

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa()
			.ComUsuario(Admin, "admin@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin")
			.ComDoisFatores(Admin);
		factory.GestaoDeTenants = new GestaoDeTenantsFalsa();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Cliente()
	{
		var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, Admin.ToString());

		return client;
	}

	[Fact]
	public async Task Index_SemTenants_ExplicaECriaOuAdota()
	{
		var html = await Cliente().GetStringAsync("/tenants");

		html.Should().Contain("href=\"/tenants/novo\"").And.Contain("href=\"/tenants/adotar\"");
	}

	[Fact]
	public async Task Criar_PeloFormulario_ApareceNaLista()
	{
		var client = Cliente();
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/tenants/novo");

		var resposta = await client.PostAsync("/tenants/novo", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Sistema"] = "Sistema de compras",
			["Responsavel"] = "Ana",
			["Nome"] = "Compras",
			["Slug"] = "compras-tela",
		}));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		var lista = await client.GetStringAsync("/tenants");
		lista.Should().Contain("Sistema de compras").And.Contain("compras-tela");
	}

	[Fact]
	public async Task Criar_SlugInvalido_VoltaAoFormularioComOErro()
	{
		var client = Cliente();
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/tenants/novo");

		var resposta = await client.PostAsync("/tenants/novo", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Sistema"] = "S",
			["Responsavel"] = "Ana",
			["Nome"] = "Compras",
			["Slug"] = "Compras X",
		}));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("letras minúsculas sem acento");
	}

	[Fact]
	public async Task SemSecureGate_AreaExplica_NaoQuebra()
	{
		factory.GestaoDeTenants = null;

		try
		{
			var resposta = await Cliente().GetAsync("/tenants/adotar");

			resposta.StatusCode.Should().Be(HttpStatusCode.OK);
			AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("não está configurado");
		}
		finally
		{
			factory.GestaoDeTenants = new GestaoDeTenantsFalsa();
		}
	}

	[Fact]
	public async Task Menu_IntranetAdmin_VeTenants()
	{
		var html = await Cliente().GetStringAsync("/mural");

		html.Should().Contain("href=\"/tenants\"");
	}
}
```

> `AuxiliaresDeHttp.TokenAsync` e `AuxiliaresDeHttp.Decodificar` são auxiliares existentes. O Razor codifica acentos (`&#xE3;`), então toda asserção de texto acentuado passa por `Decodificar`.

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TenantsTelasTests"`
Expected: FAIL — 404 em `/tenants`.

- [ ] **Step 4: ViewModels**

Acrescente a `src/Secco.Intranet.Web/Models/Tenants/TenantsViewModels.cs`:

```csharp
using Secco.Intranet.Application.Tenants;
```

(no topo) e, depois de `SegundoFatorViewModel`:

```csharp
/// <summary>Página que explica por que a área não abre (SecureGate ausente ou fora do ar).</summary>
/// <param name="Mensagem">Mensagem do erro, sem detalhe interno.</param>
public sealed record TenantsIndisponivelViewModel(string Mensagem);

/// <summary>Formulário de criação.</summary>
public sealed class NovoTenantViewModel
{
	/// <summary>Sistema que o tenant representa.</summary>
	public string? Sistema { get; set; }

	/// <summary>Responsável.</summary>
	public string? Responsavel { get; set; }

	/// <summary>Nome do tenant.</summary>
	public string? Nome { get; set; }

	/// <summary>Slug do tenant.</summary>
	public string? Slug { get; set; }

	/// <summary>Erro da última tentativa.</summary>
	public string? Erro { get; set; }
}

/// <summary>Formulário de adoção.</summary>
public sealed class AdotarTenantViewModel
{
	/// <summary>Tenants adotáveis.</summary>
	public IReadOnlyList<TenantDaPlataformaDto> Adotaveis { get; set; } = [];

	/// <summary>Tenant escolhido.</summary>
	public Guid? TenantId { get; set; }

	/// <summary>Sistema que o tenant representa.</summary>
	public string? Sistema { get; set; }

	/// <summary>Responsável.</summary>
	public string? Responsavel { get; set; }

	/// <summary>Erro da última tentativa.</summary>
	public string? Erro { get; set; }
}

/// <summary>Exibição única do script de provisionamento.</summary>
/// <param name="TenantId">Tenant.</param>
/// <param name="Sistema">Sistema.</param>
/// <param name="Recurso">Nome do recurso.</param>
/// <param name="Script">SQL com a senha gerada.</param>
public sealed record ScriptDeProvisionamentoViewModel(Guid TenantId, string Sistema, string Recurso, string Script);
```

- [ ] **Step 5: Controller**

`src/Secco.Intranet.Web/Controllers/TenantsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Tenants;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Área de tenants administrados (ADR-0008): criar, adotar, ligar recursos da plataforma,
/// ativar/desativar. Só <c>intranet-admin</c> com segundo fator; toda action com
/// <c>{tenantId}</c> passa pelo cadastro antes de qualquer chamada (IDOR).
/// </summary>
/// <param name="listar">Lista do cadastro.</param>
/// <param name="obter">Detalhe.</param>
/// <param name="listarAdotaveis">Tenants adotáveis.</param>
/// <param name="criar">Criação.</param>
/// <param name="adotar">Adoção.</param>
/// <param name="ligar">Ligar recurso.</param>
/// <param name="situacao">Ativar/desativar.</param>
[Route("tenants")]
[SomenteIntranetAdmin]
[ExigeSegundoFator]
public sealed class TenantsController(
	ListarTenantsAdministradosHandler listar,
	ObterTenantAdministradoHandler obter,
	ListarTenantsAdotaveisHandler listarAdotaveis,
	CriarTenantHandler criar,
	AdotarTenantHandler adotar,
	LigarRecursoHandler ligar,
	AlterarSituacaoDoTenantHandler situacao) : Controller
{
	/// <summary>Lista dos tenants administrados.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("")]
	public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
	{
		var tenants = await listar.HandleAsync(cancellationToken);

		return tenants.IsFailure ? Indisponivel(tenants.Error) : View(tenants.Value);
	}

	/// <summary>Formulário de criação.</summary>
	[HttpGet("novo")]
	public IActionResult Novo() => View(new NovoTenantViewModel());

	/// <summary>Cria o tenant e o registra.</summary>
	/// <param name="modelo">Formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("novo")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Novo(NovoTenantViewModel modelo, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(modelo);

		var criado = await criar.HandleAsync(
			new CriarTenantCommand(modelo.Sistema, modelo.Responsavel, modelo.Nome, modelo.Slug), cancellationToken);

		if (criado.IsFailure)
		{
			modelo.Erro = criado.Error.Description;

			return View(modelo);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Tenant \"{modelo.Slug?.Trim()}\" criado. Ligue os recursos de que o sistema precisa.";

		return RedirectToAction(nameof(Detalhe), new { tenantId = criado.Value });
	}

	/// <summary>Formulário de adoção.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("adotar")]
	public async Task<IActionResult> Adotar(CancellationToken cancellationToken = default)
	{
		var adotaveis = await listarAdotaveis.HandleAsync(cancellationToken);

		return adotaveis.IsFailure
			? Indisponivel(adotaveis.Error)
			: View(new AdotarTenantViewModel { Adotaveis = adotaveis.Value });
	}

	/// <summary>Adota um tenant existente.</summary>
	/// <param name="modelo">Formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("adotar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Adotar(AdotarTenantViewModel modelo, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(modelo);

		var adotado = await adotar.HandleAsync(
			new AdotarTenantCommand(modelo.TenantId ?? Guid.Empty, modelo.Sistema, modelo.Responsavel), cancellationToken);

		if (adotado.IsSuccess)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = "Tenant adotado.";

			return RedirectToAction(nameof(Detalhe), new { tenantId = modelo.TenantId });
		}

		var adotaveis = await listarAdotaveis.HandleAsync(cancellationToken);
		modelo.Adotaveis = adotaveis.IsSuccess ? adotaveis.Value : [];
		modelo.Erro = adotado.Error.Description;

		return View(modelo);
	}

	/// <summary>Detalhe do tenant, com o painel dos recursos.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{tenantId:guid}")]
	[TenantAdministrado]
	public async Task<IActionResult> Detalhe(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var tenant = await obter.HandleAsync(tenantId, cancellationToken);

		return tenant.IsFailure ? Indisponivel(tenant.Error) : View(tenant.Value);
	}

	/// <summary>
	/// Liga um recurso. Em modo script, a resposta <b>é</b> a tela do script — nunca redirect com
	/// TempData, que gravaria a senha num cookie — e sai com <c>no-store</c>.
	/// </summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="recurso">Segmento do recurso (lista fechada).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{tenantId:guid}/recursos/{recurso}")]
	[TenantAdministrado]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> LigarRecurso(Guid tenantId, string recurso, CancellationToken cancellationToken = default)
	{
		var ligado = await ligar.HandleAsync(new LigarRecursoCommand(tenantId, recurso), cancellationToken);

		if (ligado.IsFailure)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = ligado.Error.Description;

			return RedirectToAction(nameof(Detalhe), new { tenantId });
		}

		RecursosDaPlataforma.TentarLer(recurso, out var lido);

		if (ligado.Value is { Aplicado: false, Script: { } script })
		{
			Response.Headers.CacheControl = "no-store";
			Response.Headers.Pragma = "no-cache";

			var detalhe = await obter.HandleAsync(tenantId, cancellationToken);
			var sistema = detalhe.IsSuccess ? detalhe.Value.Sistema : string.Empty;

			return View("Script", new ScriptDeProvisionamentoViewModel(tenantId, sistema, RecursosDaPlataforma.Nome(lido), script));
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"{RecursosDaPlataforma.Nome(lido)} ligado.";

		return RedirectToAction(nameof(Detalhe), new { tenantId });
	}

	/// <summary>Ativa o tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{tenantId:guid}/ativar")]
	[TenantAdministrado]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Ativar(Guid tenantId, CancellationToken cancellationToken = default) =>
		Concluir(await situacao.AtivarAsync(tenantId, cancellationToken), "Tenant ativado.", tenantId);

	/// <summary>Desativa o tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="confirmacao">Slug digitado para confirmar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{tenantId:guid}/desativar")]
	[TenantAdministrado]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Desativar(Guid tenantId, string? confirmacao, CancellationToken cancellationToken = default) =>
		Concluir(await situacao.DesativarAsync(tenantId, confirmacao, cancellationToken),
			"Tenant desativado. O login e o catálogo do sistema param em instantes.", tenantId);

	private RedirectToActionResult Concluir(Result resultado, string sucesso, Guid tenantId)
	{
		TempData[resultado.IsSuccess ? FeedbackViewComponent.ChaveDaMensagem : FeedbackViewComponent.ChaveDaMensagemDeErro] =
			resultado.IsSuccess ? sucesso : resultado.Error.Description;

		return RedirectToAction(nameof(Detalhe), new { tenantId });
	}

	/// <summary>Leitura que falhou: inexistente vira 404; SecureGate ausente ou fora, a página que explica.</summary>
	private IActionResult Indisponivel(Error erro) =>
		erro.Type == ErrorType.NotFound
			? NotFound()
			: View("Indisponivel", new TenantsIndisponivelViewModel(erro.Description));
}
```


- [ ] **Step 6: Views**

`src/Secco.Intranet.Web/Views/Tenants/Indisponivel.cshtml`:

```cshtml
@model TenantsIndisponivelViewModel
@{
    ViewData["Title"] = "Tenants";

    var cabecalho = new PageHeaderModel("Tenants", "Sistemas da empresa administrados por esta Intranet.");
    var vazio = new EmptyStateModel("bi-plug", "Administração de tenants indisponível", Model.Mensagem);
}

<partial name="_PageHeader" model="cabecalho" />
<partial name="_EmptyState" model="vazio" />
```

`src/Secco.Intranet.Web/Views/Tenants/Index.cshtml`:

```cshtml
@model IReadOnlyList<TenantAdministradoResumoDto>
@{
    ViewData["Title"] = "Tenants";

    var cabecalho = new PageHeaderModel("Tenants", "Sistemas da empresa administrados por esta Intranet — exclusivo do intranet-admin.");

    static BadgeModel BadgeDaSituacao(TenantAdministradoResumoDto t) =>
        !t.EncontradoNaPlataforma ? new BadgeModel("Não encontrado", BadgeVariante.Perigo)
        : t.Ativo ? new BadgeModel("Ativo", BadgeVariante.Sucesso)
        : new BadgeModel("Desativado", BadgeVariante.Aviso);
}

<partial name="_PageHeader" model="cabecalho" />

<div class="d-flex flex-wrap gap-2 mb-3">
    <a class="btn btn-primary" href="/tenants/novo">Novo tenant</a>
    <a class="btn btn-outline-primary" href="/tenants/adotar">Adotar tenant existente</a>
</div>

@if (Model.Count == 0)
{
    var vazio = new EmptyStateModel(
        "bi-diagram-3",
        "Nenhum tenant administrado",
        "Crie um tenant para um sistema da empresa, ou adote um que já exista no SecureGate.");
    <partial name="_EmptyState" model="vazio" />
}
else
{
    <div class="table-responsive">
        <table class="table align-middle">
            <thead>
                <tr><th>Sistema</th><th>Tenant</th><th>Situação</th><th>Recursos</th><th>Origem</th></tr>
            </thead>
            <tbody>
                @foreach (var t in Model)
                {
                    <tr>
                        <td><a href="/tenants/@t.TenantId">@t.Sistema</a></td>
                        <td>@t.Nome <span class="text-body-secondary">@t.Slug</span></td>
                        <td><partial name="_Badge" model="BadgeDaSituacao(t)" /></td>
                        <td>@(t.RecursosLigados.Count == 0 ? "—" : string.Join(", ", t.RecursosLigados.Select(RecursosDaPlataforma.Nome)))</td>
                        <td>@(t.Origem == OrigemDoTenant.Criado ? "Criado aqui" : "Adotado")</td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}
```

`src/Secco.Intranet.Web/Views/Tenants/Novo.cshtml`:

```cshtml
@model NovoTenantViewModel
@{
    ViewData["Title"] = "Novo tenant";

    var cabecalho = new PageHeaderModel("Novo tenant", "Um tenant por sistema da empresa. Os recursos são ligados depois, na página do tenant.");
}

<partial name="_PageHeader" model="cabecalho" />

@if (Model.Erro is { } erro)
{
    <div class="alert alert-danger" role="alert">@erro</div>
}

<form method="post" action="/tenants/novo" class="sc-panel" style="max-width: 40rem">
    @Html.AntiForgeryToken()
    <div class="mb-3">
        <label class="form-label" for="Sistema">Sistema</label>
        <input class="form-control" id="Sistema" name="Sistema" value="@Model.Sistema" maxlength="120" required placeholder="Sistema de compras" />
    </div>
    <div class="mb-3">
        <label class="form-label" for="Responsavel">Responsável</label>
        <input class="form-control" id="Responsavel" name="Responsavel" value="@Model.Responsavel" maxlength="120" required />
    </div>
    <div class="mb-3">
        <label class="form-label" for="Nome">Nome do tenant</label>
        <input class="form-control" id="Nome" name="Nome" value="@Model.Nome" maxlength="200" required />
    </div>
    <div class="mb-3">
        <label class="form-label" for="Slug">Slug</label>
        <input class="form-control" id="Slug" name="Slug" value="@Model.Slug" maxlength="50" required pattern="[a-z0-9]+(-[a-z0-9]+)*" aria-describedby="slug-ajuda" />
        <div id="slug-ajuda" class="form-text">Letras minúsculas sem acento, dígitos e hífen. Vira parte do nome do banco de cada recurso.</div>
    </div>
    <button class="btn btn-primary" type="submit">Criar tenant</button>
    <a class="btn btn-link" href="/tenants">Cancelar</a>
</form>
```

`src/Secco.Intranet.Web/Views/Tenants/Adotar.cshtml`:

```cshtml
@model AdotarTenantViewModel
@{
    ViewData["Title"] = "Adotar tenant";

    var cabecalho = new PageHeaderModel("Adotar tenant", "Traz para esta Intranet um tenant que já existe no SecureGate.");
}

<partial name="_PageHeader" model="cabecalho" />

@if (Model.Erro is { } erro)
{
    <div class="alert alert-danger" role="alert">@erro</div>
}

@if (Model.Adotaveis.Count == 0)
{
    var vazio = new EmptyStateModel("bi-inbox", "Nada para adotar", "Todos os tenants da instalação já são administrados, ou são da plataforma ou de uma Intranet.");
    <partial name="_EmptyState" model="vazio" />
}
else
{
    <form method="post" action="/tenants/adotar" class="sc-panel" style="max-width: 40rem">
        @Html.AntiForgeryToken()
        <div class="mb-3">
            <label class="form-label" for="TenantId">Tenant</label>
            <select class="form-select" id="TenantId" name="TenantId" required>
                @foreach (var t in Model.Adotaveis)
                {
                    <option value="@t.Id" selected="@(Model.TenantId == t.Id)">@t.Nome (@t.Slug)@(t.Ativo ? "" : " — desativado")</option>
                }
            </select>
        </div>
        <div class="mb-3">
            <label class="form-label" for="Sistema">Sistema</label>
            <input class="form-control" id="Sistema" name="Sistema" value="@Model.Sistema" maxlength="120" required />
        </div>
        <div class="mb-3">
            <label class="form-label" for="Responsavel">Responsável</label>
            <input class="form-control" id="Responsavel" name="Responsavel" value="@Model.Responsavel" maxlength="120" required />
        </div>
        <button class="btn btn-primary" type="submit">Adotar</button>
        <a class="btn btn-link" href="/tenants">Cancelar</a>
    </form>
}
```

`src/Secco.Intranet.Web/Views/Tenants/Detalhe.cshtml`:

```cshtml
@model TenantAdministradoDetalheDto
@{
    ViewData["Title"] = Model.Sistema;

    var cabecalho = new PageHeaderModel(Model.Sistema, $"Tenant {Model.Nome} ({Model.Slug}) — responsável: {Model.Responsavel}.");

    static BadgeModel BadgeDoRecurso(RecursoDoTenantDto r) => r.Situacao switch
    {
        SituacaoDoRecurso.Ligado => new BadgeModel("Ligado", BadgeVariante.Sucesso),
        SituacaoDoRecurso.LigadoSemResponder => new BadgeModel("Ligado, sem responder", BadgeVariante.Aviso),
        _ => new BadgeModel("Não ligado", BadgeVariante.Neutro),
    };
}

<partial name="_PageHeader" model="cabecalho" />

<p>
    <partial name="_Badge" model="@(Model.Ativo ? new BadgeModel("Ativo", BadgeVariante.Sucesso) : new BadgeModel("Desativado", BadgeVariante.Aviso))" />
    <span class="text-body-secondary ms-2">@(Model.Origem == OrigemDoTenant.Criado ? "Criado" : "Adotado") por @Model.RegistradoPor em @Model.RegistradoEm.ToString("dd/MM/yyyy")</span>
</p>

<h2 class="h5 mt-4">Recursos</h2>
<div class="table-responsive">
    <table class="table align-middle">
        <tbody>
            @foreach (var r in Model.Recursos)
            {
                <tr>
                    <th scope="row">@RecursosDaPlataforma.Nome(r.Recurso)</th>
                    <td><partial name="_Badge" model="BadgeDoRecurso(r)" /> @if (r.Motivo is { } motivo) { <span class="text-body-secondary">@motivo</span> }</td>
                    <td class="text-end">
                        @if (r.Situacao == SituacaoDoRecurso.NaoLigado)
                        {
                            <form method="post" action="/tenants/@Model.TenantId/recursos/@RecursosDaPlataforma.Rota(r.Recurso)" class="d-inline">
                                @Html.AntiForgeryToken()
                                <button class="btn btn-outline-primary btn-sm" type="submit">Ligar</button>
                            </form>
                        }
                    </td>
                </tr>
            }
        </tbody>
    </table>
</div>

<h2 class="h5 mt-4">Situação do tenant</h2>
@if (Model.Ativo)
{
    <form method="post" action="/tenants/@Model.TenantId/desativar" class="sc-panel" style="max-width: 40rem">
        @Html.AntiForgeryToken()
        <p>Desativar corta o login e o catálogo do sistema em instantes. Para confirmar, digite o slug <code>@Model.Slug</code>.</p>
        <div class="input-group">
            <input class="form-control" name="confirmacao" aria-label="Slug para confirmar" autocomplete="off" required />
            <button class="btn btn-outline-danger" type="submit">Desativar</button>
        </div>
    </form>
}
else
{
    <form method="post" action="/tenants/@Model.TenantId/ativar">
        @Html.AntiForgeryToken()
        <button class="btn btn-outline-primary" type="submit">Ativar</button>
    </form>
}
```

`src/Secco.Intranet.Web/Views/Tenants/Script.cshtml`:

```cshtml
@model ScriptDeProvisionamentoViewModel
@{
    ViewData["Title"] = "Script de provisionamento";

    var cabecalho = new PageHeaderModel("Script de provisionamento", $"{Model.Recurso} para {Model.Sistema}.");
}

<partial name="_PageHeader" model="cabecalho" />

<div class="alert alert-warning" role="alert">
    <strong>Esta é a única vez que este script aparece.</strong> Ele contém a senha do usuário do banco.
    Entregue ao DBA por um canal seguro; a Intranet não guarda cópia, e recarregar esta página não o mostra de novo.
    Depois de aplicado, o recurso passa a aparecer como "Ligado" na página do tenant.
</div>

<pre class="sc-panel" style="white-space: pre-wrap"><code>@Model.Script</code></pre>

<a class="btn btn-primary" href="/tenants/@Model.TenantId">Voltar ao tenant</a>
```

- [ ] **Step 7: Menu e slug reservado**

Em `IntranetNavigation.cs`, dentro de `if (request.MostrarAdministracao)`, depois do item "Acesso":

```csharp
			administracao.Add(new NavigationItemModel("Tenants", "bi-diagram-3", "/tenants", Corresponde(caminho, "/tenants")));
```

Em `SlugsReservados.cs`, na linha dos controllers, acrescente `"tenants"` em ordem alfabética (depois de `"setores"`).

- [ ] **Step 8: Rodar e ver passar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TenantsTelasTests|FullyQualifiedName~SlugsReservadosTests"`
Expected: PASS. O `SlugsReservadosTests` existente é quem prova que `tenants` entrou na lista.

- [ ] **Step 9: Conferência visual**

Suba a aplicação em DEV (`scripts/dev.ps1`) e abra `/tenants` nos dois temas (Vertical e Horizontal), em claro e escuro e a 375 px de largura. Sem SecureGate configurado, cada página mostra "não configurado" — para ver as telas preenchidas, use a fumaça da Task 9 ou um SecureGate local. Registre no relatório o que foi visto, e não marque como conferido o que não foi.

- [ ] **Step 10: Suíte inteira e commit**

Run: `dotnet build Secco.Intranet.slnx -c Release` e `dotnet test Secco.Intranet.slnx -c Release`.

```bash
git add src/Secco.Intranet.Web/Controllers/TenantsController.cs src/Secco.Intranet.Web/Models/Tenants/TenantsViewModels.cs src/Secco.Intranet.Web/Views/Tenants src/Secco.Intranet.Web/Navigation/IntranetNavigation.cs src/Secco.Intranet.Application/Setores/SlugsReservados.cs tests/Secco.Intranet.Tests/Integration/IntranetWebFactory.cs tests/Secco.Intranet.Tests/Integration/TenantsTelasTests.cs
git commit -m "feat(tenants): telas da area de tenants e item de menu" -m "Co-Authored-By: <modelo> <noreply@anthropic.com>"
```

---

## Task 8: Matriz de autorização, IDOR e segredo — pelo host HTTP real

**Files:**
- Test: `tests/Secco.Intranet.Tests/Integration/TenantsAutorizacaoTests.cs`
- Test: `tests/Secco.Intranet.Tests/Integration/TenantsSegurancaTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 1–7; `RolesDeTesteMiddleware.Header` / `HeaderUsuario`; `AuxiliaresDeHttp.TokenAsync` / `Form` / `Decodificar`.
- Produces: nada além dos testes. Se um teste daqui falhar, o defeito está nas tasks anteriores — corrija lá, no mesmo commit desta task, e diga no relatório qual foi.

- [ ] **Step 1: Matriz da ADR-0008 e do 2FA**

`tests/Secco.Intranet.Tests/Integration/TenantsAutorizacaoTests.cs`:

```csharp
using System.Net;
using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Critério de aceite da ADR-0008, repetido para a área de tenants, mais o segundo fator: só o
/// <c>intranet-admin</c> com 2FA entra — em toda rota, <c>GET</c> e <c>POST</c>, e o 403 vem antes
/// do 400 do antifalsificação.
/// </summary>
public class TenantsAutorizacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid ComFator = Guid.NewGuid();
	private static readonly Guid SemFator = Guid.NewGuid();
	private static readonly Guid Qualquer = Guid.NewGuid();

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa()
			.ComUsuario(ComFator, "com@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin")
			.ComDoisFatores(ComFator)
			.ComUsuario(SemFator, "sem@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		factory.GestaoDeTenants = new GestaoDeTenantsFalsa();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Cliente(Guid? usuario, params string[] roles)
	{
		var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		if (usuario is not null)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, usuario.Value.ToString());
		}

		return client;
	}

	public static TheoryData<string[]> UsuariosSemAcesso => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-user" },
		new[] { "financeiro-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin", "marketing-admin" },
		new[] { "inventario-admin" },
		new[] { "diretorio-admin" },
	};

	public static IEnumerable<string> RotasGet => ["/tenants", "/tenants/novo", "/tenants/adotar", $"/tenants/{Qualquer}"];

	public static IEnumerable<string> RotasPost =>
	[
		"/tenants/novo",
		"/tenants/adotar",
		$"/tenants/{Qualquer}/recursos/logstream",
		$"/tenants/{Qualquer}/ativar",
		$"/tenants/{Qualquer}/desativar",
	];

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task SemIntranetAdmin_Bloqueado403_EmTodaRota(string[] roles)
	{
		var client = Cliente(ComFator, roles);

		foreach (var rota in RotasGet)
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota}");
		}

		foreach (var rota in RotasPost)
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode
				.Should().Be(HttpStatusCode.Forbidden, $"POST {rota}: 403 do gate, não 400 do antifalsificação");
		}
	}

	[Fact]
	public async Task IntranetAdminSemSegundoFator_Bloqueado_ComATelaQueExplica()
	{
		var client = Cliente(SemFator, "intranet-admin");

		foreach (var rota in RotasGet)
		{
			var resposta = await client.GetAsync(rota);

			resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota}");
			AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync())
				.Should().Contain("Ative o segundo fator");
		}

		foreach (var rota in RotasPost)
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode
				.Should().Be(HttpStatusCode.Forbidden, $"POST {rota}");
		}
	}

	[Fact]
	public async Task IntranetAdminSemSub_Bloqueado()
	{
		var resposta = await Cliente(null, "intranet-admin").GetAsync("/tenants");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task IntranetAdminComSegundoFator_Liberado()
	{
		var client = Cliente(ComFator, "intranet-admin");

		foreach (var rota in new[] { "/tenants", "/tenants/novo", "/tenants/adotar" })
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.OK, $"GET {rota}");
		}
	}

	[Fact]
	public async Task IntranetAdminComSegundoFator_PostSemToken_PassaDosFiltrosEBateNoAntifalsificacao()
	{
		var client = Cliente(ComFator, "intranet-admin");

		foreach (var rota in new[] { "/tenants/novo", "/tenants/adotar" })
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode
				.Should().Be(HttpStatusCode.BadRequest, $"POST {rota}: os gates liberaram, o token barrou");
		}
	}

	[Fact]
	public async Task Menu_QuemNaoEIntranetAdmin_NaoVeTenants()
	{
		foreach (var roles in new string[][] { [], ["financeiro-admin"], ["inventario-admin"] })
		{
			var html = await Cliente(ComFator, roles).GetStringAsync("/mural");

			html.Should().NotContain("href=\"/tenants\"", $"roles: {string.Join(",", roles)}");
		}
	}
}
```

- [ ] **Step 2: IDOR, adoção forjada e segredo**

`tests/Secco.Intranet.Tests/Integration/TenantsSegurancaTests.cs`:

```csharp
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.Intranet.Infrastructure.Tenants;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Os pontos de segurança da spec pelo host HTTP real: tenant fora do cadastro é 404 em toda rota,
/// a adoção recusa Guid forjado de tenant protegido, e o script de provisionamento só existe na
/// resposta do POST — sem cookie de TempData, com <c>no-store</c>.
/// </summary>
public class TenantsSegurancaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Admin = Guid.NewGuid();
	private readonly Guid _cadastrado = Guid.NewGuid();
	private readonly Guid _naoCadastrado = Guid.NewGuid();
	private GestaoDeTenantsFalsa _gestao = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa()
			.ComUsuario(Admin, "admin@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin")
			.ComDoisFatores(Admin);
		_gestao = new GestaoDeTenantsFalsa()
			.ComTenant(_cadastrado, $"cad-{_cadastrado:N}"[..20])
			.ComTenant(_naoCadastrado, $"livre-{_naoCadastrado:N}"[..20])
			.ComTenant(TenantsProtegidosDoCatalogo.TenantDeInstalacao, "instalacao")
			.ComTenant(factory.TenantBeta, "outra-intranet");
		factory.GestaoDeTenants = _gestao;

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		await escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>()
			.TentarAdicionarAsync(new TenantAdministrado(_cadastrado, "Sistema de compras", "Ana", OrigemDoTenant.Criado, "admin"));
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Cliente()
	{
		var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, Admin.ToString());

		return client;
	}

	[Fact]
	public async Task TenantForaDoCadastro_404_EmTodaRota_SemTocarAPlataforma()
	{
		var client = Cliente();
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_cadastrado}");
		_gestao.Chamadas.Clear();

		(await client.GetAsync($"/tenants/{_naoCadastrado}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

		foreach (var rota in new[] { "recursos/logstream", "ativar", "desativar" })
		{
			var resposta = await client.PostAsync($"/tenants/{_naoCadastrado}/{rota}",
				AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("confirmacao", "x")));

			resposta.StatusCode.Should().Be(HttpStatusCode.NotFound, $"POST {rota}");
		}

		_gestao.Chamadas.Should().NotContain(c => c.Contains(_naoCadastrado.ToString(), StringComparison.Ordinal));
	}

	[Fact]
	public async Task TenantIdQueNaoEGuid_404() =>
		(await Cliente().GetAsync("/tenants/nao-e-guid")).StatusCode.Should().Be(HttpStatusCode.NotFound);

	[Fact]
	public async Task AdotarComGuidForjado_DeTenantProtegido_Recusa()
	{
		var client = Cliente();

		foreach (var protegido in new[] { TenantsProtegidosDoCatalogo.TenantDeInstalacao, factory.TenantAlfa, factory.TenantBeta })
		{
			var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_cadastrado}");
			var resposta = await client.PostAsync("/tenants/adotar", AuxiliaresDeHttp.Form(
				("__RequestVerificationToken", token), ("TenantId", protegido.ToString()), ("Sistema", "S"), ("Responsavel", "R")));

			resposta.StatusCode.Should().Be(HttpStatusCode.OK, "volta ao formulário com o erro");
			AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync())
				.Should().Contain("não pode ser administrado por aqui");
		}

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var cadastro = await escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>().ListarAsync();
		cadastro.Should().NotContain(t =>
			t.TenantId == TenantsProtegidosDoCatalogo.TenantDeInstalacao || t.TenantId == factory.TenantAlfa || t.TenantId == factory.TenantBeta);
	}

	[Fact]
	public async Task Adotaveis_NaoListaProtegidos()
	{
		var html = await Cliente().GetStringAsync("/tenants/adotar");

		html.Should().Contain(_naoCadastrado.ToString())
			.And.NotContain(TenantsProtegidosDoCatalogo.TenantDeInstalacao.ToString())
			.And.NotContain(factory.TenantBeta.ToString());
	}

	[Fact]
	public async Task ScriptDeProvisionamento_SoNaResposta_NoStore_SemCookie()
	{
		_gestao.ModoScript = true;
		var client = Cliente();
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_cadastrado}");

		var resposta = await client.PostAsync($"/tenants/{_cadastrado}/recursos/logstream",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "o script é a própria resposta, não um redirect");
		resposta.Headers.CacheControl!.NoStore.Should().BeTrue();
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("SENHA-SECRETA-DE-TESTE");
		resposta.Headers.TryGetValues("Set-Cookie", out var cookies);
		(cookies ?? []).Should().NotContain(c => c.Contains("TempData", StringComparison.OrdinalIgnoreCase),
			"a senha não pode ir para um cookie");

		var detalhe = await client.GetStringAsync($"/tenants/{_cadastrado}");
		detalhe.Should().NotContain("SENHA-SECRETA-DE-TESTE", "recarregar não mostra o script de novo");
	}

	[Fact]
	public async Task RecursoForaDaLista_NaoChamaAPlataforma()
	{
		var client = Cliente();
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_cadastrado}");
		_gestao.Chamadas.Clear();

		var resposta = await client.PostAsync($"/tenants/{_cadastrado}/recursos/intranet",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		_gestao.Chamadas.Should().NotContain(c => c.StartsWith("provisionar", StringComparison.Ordinal));
	}
}
```

> `factory.TenantAlfa` e `factory.TenantBeta` são os tenants do catálogo de configuração do ambiente Testing — por isso `TenantsProtegidosDoCatalogo` os protege, e eles fazem o papel de "a própria Intranet" e "outra Intranet da mesma instalação".

- [ ] **Step 3: Rodar**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~TenantsAutorizacaoTests|FullyQualifiedName~TenantsSegurancaTests"`
Expected: PASS. Se algum falhar, corrija o código da task dona (não o teste) e registre no relatório.

- [ ] **Step 4: Suíte inteira e commit**

Run: `dotnet build Secco.Intranet.slnx -c Release` e `dotnet test Secco.Intranet.slnx -c Release`.

```bash
git add tests/Secco.Intranet.Tests/Integration/TenantsAutorizacaoTests.cs tests/Secco.Intranet.Tests/Integration/TenantsSegurancaTests.cs
git commit -m "test(tenants): matriz de autorizacao, IDOR, adocao forjada e script" -m "Co-Authored-By: <modelo> <noreply@anthropic.com>"
```

---

## Task 9: Fumaça opt-in contra o SecureGate real

**Files:**
- Create: `tests/Secco.Intranet.Tests/Smoke/SecureGateGestaoDeTenantsFumacaTests.cs`
- Modify: `docs/roteiro-fumaca-securegate.md` (seção nova)

**Interfaces:**
- Consumes: `FumacaFactAttribute` (existente, pula sem `SECCO_SMOKE_SECUREGATE_URL`); `SecureGateGestaoDeTenants` (Task 5); `AddSecureGateAdminClient()`.
- Produces: nada além do teste.

**Aviso que vai no roteiro:** a plataforma **não exclui tenant**. Cada execução deixa um tenant `fumaca-<sufixo>` desativado no SecureGate de desenvolvimento. É o preço aceito; rode contra o SecureGate local do compose, nunca contra um compartilhado.

- [ ] **Step 1: Escrever a fumaça**

`tests/Secco.Intranet.Tests/Smoke/SecureGateGestaoDeTenantsFumacaTests.cs`:

```csharp
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Infrastructure.Tenants;
using Secco.SecureGate.Client;
using Secco.SecureGate.Client.Administration;
using Xunit;

namespace Secco.Intranet.Tests.Smoke;

/// <summary>
/// O adaptador de tenants contra um SecureGate <b>de verdade</b>: contrato do client gerado,
/// códigos de status e o formato de provisionamento. Só roda com <c>SECCO_SMOKE_SECUREGATE_URL</c>;
/// sem ela, aparece como pulado. Deixa um tenant desativado por execução — a plataforma não exclui
/// tenant (ver <c>docs/roteiro-fumaca-securegate.md</c>).
/// </summary>
public class SecureGateGestaoDeTenantsFumacaTests
{
	private static SecureGateGestaoDeTenants Gestao()
	{
		var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Secco:SecureGate:BaseUrl"] = Environment.GetEnvironmentVariable(FumacaFactAttribute.VariavelDaUrl),
			["Secco:SecureGate:ClientId"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_ID") ?? "secco-dev-console",
			["Secco:SecureGate:ClientSecret"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_SECRET")
				?? "secco-dev-console-secret-32-chars-min!",
		}).Build();

		var provider = new ServiceCollection()
			.AddSingleton<IConfiguration>(configuracao)
			.AddLogging()
			.AddSecureGateAdminClient()
			.BuildServiceProvider();

		return new SecureGateGestaoDeTenants(provider.GetRequiredService<ISecureGateClient>(), NullLogger<SecureGateGestaoDeTenants>.Instance);
	}

	[FumacaFact]
	public async Task CicloDeVida_CriarProvisionarStatusDesativar_ContraOContratoReal()
	{
		var gestao = Gestao();
		var slug = $"fumaca-{Guid.NewGuid():N}"[..20];

		var criado = await gestao.CriarTenantAsync($"Fumaça {slug}", slug);
		criado.IsSuccess.Should().BeTrue(criado.IsFailure ? criado.Error.Description : null);

		try
		{
			(await gestao.CriarTenantAsync("Repetido", slug)).Error.Code.Should().Be("Intranet.Tenants.SlugJaExiste");

			var listados = await gestao.ListarTenantsAsync();
			listados.Value.Should().Contain(t => t.Id == criado.Value.Id);

			var provisionado = await gestao.ProvisionarAsync(criado.Value.Id, "logstream");
			provisionado.IsSuccess.Should().BeTrue(provisionado.IsFailure ? provisionado.Error.Description : null);
			(provisionado.Value.Aplicado || provisionado.Value.Script is { Length: > 0 }).Should().BeTrue(
				"ou a plataforma aplicou, ou devolveu o script para o DBA");

			var detalhe = await gestao.ObterTenantAsync(criado.Value.Id);
			detalhe.Value.Produtos.Should().Contain("logstream", "em modo script a connection string entra no catálogo antes do banco existir");

			var status = await gestao.ObterStatusDosBancosAsync(criado.Value.Id);
			status.Value.Should().Contain(s => s.Produto == "logstream");
		}
		finally
		{
			(await gestao.DesativarAsync(criado.Value.Id)).IsSuccess.Should().BeTrue();
		}

		(await gestao.ObterTenantAsync(criado.Value.Id)).Value.Ativo.Should().BeFalse();
	}

	[FumacaFact]
	public async Task TenantInexistente_NaoEncontrado()
	{
		(await Gestao().ObterTenantAsync(Guid.NewGuid())).Error.Code.Should().Be("Intranet.Tenants.NaoEncontrado");
	}

	[FumacaFact]
	public async Task DesativarOTenantDeInstalacao_Recusado()
	{
		var resultado = await Gestao().DesativarAsync(TenantsProtegidosDoCatalogo.TenantDeInstalacao);

		resultado.IsFailure.Should().BeTrue("a plataforma protege o tenant de instalação (409)");
	}
}
```

> O teste `DesativarOTenantDeInstalacao_Recusado` prova o mapeamento do 409 contra a guarda real da plataforma. Ele **nunca** desativa a instalação se a guarda existir; se a plataforma aceitasse, o próprio teste seria o alarme — por isso fica só na fumaça opt-in, contra o SecureGate local.

- [ ] **Step 2: Roteiro**

Acrescente ao fim de `docs/roteiro-fumaca-securegate.md`:

```markdown
## Tenants administrados

`SecureGateGestaoDeTenantsFumacaTests` usa as mesmas variáveis (`SECCO_SMOKE_SECUREGATE_URL` e o
client de DEV, que tem `securegate:admin`). Ele cria um tenant `fumaca-<sufixo>`, provisiona o
banco do LogStream (aplicado ou em modo script, conforme o SecureGate local tenha credencial
privilegiada) e desativa o tenant no fim.

**A plataforma não exclui tenant:** cada execução deixa um tenant desativado no SecureGate. Rode
só contra o SecureGate local do compose. Para limpar, `docker compose down -v` no `secco-platform`.

O teste `DesativarOTenantDeInstalacao_Recusado` tenta desativar o tenant de instalação e espera a
recusa da plataforma. Não rode contra um SecureGate em que essa guarda não exista.
```

- [ ] **Step 3: Rodar sem a variável (precisa pular) e, se houver SecureGate local, com ela**

Run: `dotnet test tests/Secco.Intranet.Tests --filter "FullyQualifiedName~SecureGateGestaoDeTenantsFumacaTests"`
Expected sem a variável: 3 ignorados. Com o SecureGate do `secco-platform` no ar e `SECCO_SMOKE_SECUREGATE_URL=http://localhost:4101`: 3 aprovados. Diga no relatório qual dos dois foi executado.

- [ ] **Step 4: Commit**

```bash
git add tests/Secco.Intranet.Tests/Smoke/SecureGateGestaoDeTenantsFumacaTests.cs docs/roteiro-fumaca-securegate.md
git commit -m "test(tenants): fumaca opt-in do ciclo de vida contra o SecureGate real" -m "Co-Authored-By: <modelo> <noreply@anthropic.com>"
```

---

## Task 10: Documentação — spec, roadmap e README

**Files:**
- Modify: `docs/specs/2026-10-07-area-administrativa-tenants-design.md`
- Modify: `docs/roadmap.md`
- Modify: `README.md`

- [ ] **Step 1: Corrigir a spec nas três divergências**

Em `docs/specs/2026-10-07-area-administrativa-tenants-design.md`:

1. Troque toda ocorrência de `/administracao/tenants` por `/tenants` e acrescente, logo abaixo da tabela de **Telas**:

```markdown
> A rota é `/tenants`, e não `/administracao/tenants` como no primeiro rascunho: o setor mora na
> raiz da URL, e uma empresa com o setor "Administração" (slug `administracao`) perderia a página
> dele. `tenants` entrou em `SlugsReservados`.
```

2. Na seção **Arquitetura**, retire `ObterSegundoFatorAsync(usuarioId)` da tabela de `IGestaoDeTenants` e acrescente abaixo dela:

```markdown
O segundo fator é lido pela porta que já existe, `IGestaoDeAcesso.ObterUsuarioAsync`
(`UsuarioDetalheDto.DoisFatoresAtivo`) — a área de tenants não ganha porta própria para isso.
```

3. Na tabela de **Decisões**, acrescente a linha:

```markdown
| Desativação recusada pela plataforma (409, ex.: tenant de instalação) | Erro próprio `Tenants.DesativacaoRecusada`, não "indisponível" |
```

4. Troque `**Estado:** rascunho, aguardando revisão` por `**Estado:** implementado (subsistema 1)`.

- [ ] **Step 2: Roadmap**

Em `docs/roadmap.md`, no item da Área administrativa da Fase 2, marque `- [x] 1. Ciclo de vida do tenant…` e acrescente ao fim da linha: `— [plano](plans/2026-10-07-area-administrativa-tenants.md)`.

- [ ] **Step 3: README**

Em `README.md`, no parágrafo da área de Tenants (seção "Primeiro `intranet-admin`"), troque `(`/administracao/tenants`)` por `(`/tenants`)`.

- [ ] **Step 4: Suíte inteira e commit**

Run: `dotnet build Secco.Intranet.slnx -c Release` e `dotnet test Secco.Intranet.slnx -c Release` — o resultado final, com a contagem de testes, vai para o relatório.

```bash
git add docs/specs/2026-10-07-area-administrativa-tenants-design.md docs/roadmap.md README.md
git commit -m "docs(tenants): spec, roadmap e README alinhados ao implementado" -m "Co-Authored-By: <modelo> <noreply@anthropic.com>"
```
