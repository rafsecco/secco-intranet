using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Vencimentos;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Domain.Vencimentos;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.Intranet.Infrastructure.Vencimentos;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Worker do aviso de vencimento: percorre os tenants do catálogo, isola a falha de um e respeita
/// as travas de configuração. Os ciclos são chamados direto (<c>ExecutarUmCicloAsync</c>); nenhum
/// teste espera o timer.
/// </summary>
public class VencimentosWorkerTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	/// <summary>Registra o que o handler fez em cada tenant, sem tocar no Hub nem no SecureGate.</summary>
	private sealed class Coletor
	{
		public List<(Guid? Tenant, MensagemParaEnviar Mensagem)> Enviadas { get; } = [];

		public List<Guid?> TenantsDoDiretorio { get; } = [];

		public HashSet<Guid> TenantsQueFalham { get; } = [];

		public List<string> Slugs { get; } = [];
	}

	private sealed class NotificadorDoTeste(Coletor coletor, ITenantContext tenant) : INotificadorDeMensagens
	{
		public Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default)
		{
			lock (coletor)
			{
				coletor.Enviadas.Add((tenant.TenantId, mensagem));
			}

			return Task.CompletedTask;
		}
	}

	private sealed class DiretorioDoTeste(Coletor coletor, ITenantContext tenant) : IDiretorioDeUsuarios
	{
		public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(CancellationToken cancellationToken = default)
		{
			lock (coletor)
			{
				coletor.TenantsDoDiretorio.Add(tenant.TenantId);
			}

			if (tenant.TenantId is { } id && coletor.TenantsQueFalham.Contains(id))
			{
				throw new InvalidOperationException("Falha simulada do diretório.");
			}

			string[] roles;

			lock (coletor)
			{
				roles = [.. coletor.Slugs.Select(slug => $"{slug}-admin")];
			}

			return Task.FromResult<IReadOnlyList<UsuarioDoTenant>>([new UsuarioDoTenant(Guid.NewGuid(), "admin@empresa.com", roles)]);
		}
	}

	private sealed class LogDoTeste : ILogger<VencimentosWorker>
	{
		public List<(LogLevel Nivel, string Mensagem)> Registros { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Registros.Add((logLevel, formatter(state, exception)));
	}

	private sealed class FabricaDeEscoposContadora(IServiceScopeFactory interna) : IServiceScopeFactory
	{
		public int Criados { get; private set; }

		public IServiceScope CreateScope()
		{
			Criados++;

			return interna.CreateScope();
		}
	}

	private static VencimentosOptions Opcoes(bool habilitado = true, int intervalo = 60) =>
		new() { Habilitado = habilitado, IntervaloEmMinutos = intervalo };

	private WebApplicationFactory<Program> ComDoubles(Coletor coletor, string? fuso = null) =>
		factory.WithWebHostBuilder(builder =>
		{
			if (fuso is not null)
			{
				builder.UseSetting("Intranet:Vencimentos:FusoHorario", fuso);
			}

			builder.ConfigureServices(services =>
			{
				services.AddSingleton(coletor);
				services.AddScoped<INotificadorDeMensagens, NotificadorDoTeste>();
				services.AddScoped<IDiretorioDeUsuarios, DiretorioDoTeste>();
			});
		});

	private static VencimentosWorker NovoWorker(
		IServiceProvider servicos,
		VencimentosOptions opcoes,
		ILogger<VencimentosWorker>? log = null,
		IServiceScopeFactory? escopos = null) =>
		new(
			escopos ?? servicos.GetRequiredService<IServiceScopeFactory>(),
			servicos.GetRequiredService<ITenantCatalog>(),
			opcoes,
			log ?? new LogDoTeste());

	/// <summary>Cria no tenant um setor com Vencimentos e um item que vence amanhã (marco 1 devido hoje).</summary>
	private static async Task<string> CriarItemDevidoAsync(IServiceProvider servicos, Guid tenantId, string titulo)
	{
		await using var escopo = servicos.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(tenantId);

		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
		var slug = $"wk-{Guid.NewGuid():N}"[..20];
		var setor = new Setor($"Setor {slug}", slug);
		var raiz = new ItemMenu(setor.Id, null, setor.Nome, slug, TipoDeItemMenu.Setor, null, null, 0);
		var recurso = new ItemMenu(setor.Id, raiz.Id, "Vencimentos", "vencimentos", TipoDeItemMenu.Vencimentos, null, null, 0);
		contexto.Setores.Add(setor);
		contexto.ItensMenu.AddRange(raiz, recurso);
		await contexto.SaveChangesAsync();

		var amanha = escopo.ServiceProvider.GetRequiredService<RelogioDeVencimentos>().Hoje().AddDays(1);
		await escopo.ServiceProvider.GetRequiredService<IItemVencimentoRepository>()
			.AddAsync(new ItemVencimento(setor.Id, titulo, null, amanha, null, [1]));

		return slug;
	}

	[Fact]
	public async Task UmCiclo_PercorreTodosOsTenants()
	{
		var coletor = new Coletor();
		await using var app = ComDoubles(coletor);
		var marca = Guid.NewGuid().ToString("N");
		coletor.Slugs.Add(await CriarItemDevidoAsync(app.Services, factory.TenantAlfa, $"Alfa {marca}"));
		coletor.Slugs.Add(await CriarItemDevidoAsync(app.Services, factory.TenantBeta, $"Beta {marca}"));

		await NovoWorker(app.Services, Opcoes()).ExecutarUmCicloAsync(CancellationToken.None);

		coletor.Enviadas.Where(e => e.Mensagem.Titulo.Contains($"Alfa {marca}")).Should().ContainSingle()
			.Which.Tenant.Should().Be(factory.TenantAlfa);
		coletor.Enviadas.Where(e => e.Mensagem.Titulo.Contains($"Beta {marca}")).Should().ContainSingle()
			.Which.Tenant.Should().Be(factory.TenantBeta);
	}

	[Fact]
	public async Task FalhaNumTenant_NaoParaOOutro()
	{
		var coletor = new Coletor();
		await using var app = ComDoubles(coletor);
		var marca = Guid.NewGuid().ToString("N");
		coletor.Slugs.Add(await CriarItemDevidoAsync(app.Services, factory.TenantAlfa, $"Alfa {marca}"));
		coletor.Slugs.Add(await CriarItemDevidoAsync(app.Services, factory.TenantBeta, $"Beta {marca}"));
		coletor.TenantsQueFalham.Add(factory.TenantAlfa);
		var log = new LogDoTeste();

		await NovoWorker(app.Services, Opcoes(), log).ExecutarUmCicloAsync(CancellationToken.None);

		coletor.Enviadas.Should().NotContain(e => e.Mensagem.Titulo.Contains($"Alfa {marca}"));
		coletor.Enviadas.Should().ContainSingle(e => e.Mensagem.Titulo.Contains($"Beta {marca}"));
		log.Registros.Should().Contain(r => r.Nivel == LogLevel.Error);
	}

	[Fact]
	public async Task Desabilitado_NaoRoda()
	{
		var coletor = new Coletor();
		await using var app = ComDoubles(coletor);
		coletor.Slugs.Add(await CriarItemDevidoAsync(app.Services, factory.TenantAlfa, $"Alfa {Guid.NewGuid():N}"));
		var escopos = new FabricaDeEscoposContadora(app.Services.GetRequiredService<IServiceScopeFactory>());
		using var worker = NovoWorker(app.Services, Opcoes(habilitado: false), escopos: escopos);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		await worker.StartAsync(cts.Token);
		await worker.ExecuteTask!;

		escopos.Criados.Should().Be(0);
		coletor.Enviadas.Should().BeEmpty();
		coletor.TenantsDoDiretorio.Should().BeEmpty();
	}

	[Fact]
	public async Task IntervaloInvalido_NaoRoda()
	{
		var coletor = new Coletor();
		await using var app = ComDoubles(coletor);
		var escopos = new FabricaDeEscoposContadora(app.Services.GetRequiredService<IServiceScopeFactory>());
		using var worker = NovoWorker(app.Services, Opcoes(intervalo: 0), escopos: escopos);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		await worker.StartAsync(cts.Token);
		await worker.ExecuteTask!;

		coletor.TenantsDoDiretorio.Should().BeEmpty();
	}

	[Fact]
	public async Task FusoInvalido_NaoRoda()
	{
		var coletor = new Coletor();
		await using var app = ComDoubles(coletor, fuso: "Nao/Existe");
		var log = new LogDoTeste();
		using var worker = NovoWorker(app.Services, Opcoes(), log);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		await worker.StartAsync(cts.Token);
		await worker.ExecuteTask!;

		coletor.TenantsDoDiretorio.Should().BeEmpty();
		log.Registros.Should().Contain(r => r.Nivel == LogLevel.Error);
	}

	[Fact]
	public async Task DiretorioDeUsuarios_ResolveOTenantDoEscopoDoWorker()
	{
		// SecureGateDiretorioDeUsuarios lê o tenant de ITenantContext: o que SetTenant preenche
		// precisa ser a mesma instância de escopo que o DI entrega como ITenantContext.
		await using var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantBeta);

		var contexto = escopo.ServiceProvider.GetRequiredService<ITenantContext>();

		contexto.IsResolved.Should().BeTrue();
		contexto.TenantId.Should().Be(factory.TenantBeta);
	}

	[Fact]
	public async Task UmCiclo_EntregaOTenantCertoAoDiretorio()
	{
		var coletor = new Coletor();
		await using var app = ComDoubles(coletor);
		coletor.Slugs.Add(await CriarItemDevidoAsync(app.Services, factory.TenantAlfa, $"Alfa {Guid.NewGuid():N}"));

		await NovoWorker(app.Services, Opcoes()).ExecutarUmCicloAsync(CancellationToken.None);

		coletor.TenantsDoDiretorio.Should().Contain(factory.TenantAlfa);
		coletor.TenantsDoDiretorio.Should().NotContainNulls();
	}

	[Fact]
	public void SemHubConfigurado_WorkerNaoRegistrado()
	{
		var hospedados = factory.Services.GetServices<IHostedService>().ToList();

		hospedados.Should().NotContain(h => h is VencimentosWorker);
		hospedados.Should().ContainSingle(h => h.GetType().Name == "AvisoDeVencimentoDesligadoHostedService");
	}
}
