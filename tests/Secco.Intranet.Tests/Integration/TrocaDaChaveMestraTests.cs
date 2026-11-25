using System.Data.Common;
using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Infrastructure;
using Secco.Intranet.Infrastructure.Armazenamento;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Cryptography;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A troca da chave mestra pelo verbo <c>migrate</c>: a chave nova entra como ativa, a antiga como
/// aposentada, e todo tenant do catálogo tem as chaves de arquivo reembrulhadas.
/// </summary>
/// <remarks>
/// Os bancos <c>intranet_alfa</c>/<c>intranet_beta</c> da fábrica são compartilhados com as outras
/// classes de teste, que rodam em paralelo. Reembrulhar ali mexeria nas linhas delas, então cada
/// cenário redireciona os dois tenants do catálogo para bancos próprios, apagados no fim.
/// </remarks>
public class TrocaDaChaveMestraTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private sealed class AmbienteFalso : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = "Production";

		public string ApplicationName { get; set; } = "Secco.Intranet.Tests";

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private sealed class MensagensCapturadas : ILoggerProvider
	{
		private readonly List<(string Categoria, string Mensagem)> _mensagens = [];

		public IReadOnlyList<(string Categoria, string Mensagem)> Todas
		{
			get
			{
				lock (_mensagens)
				{
					return [.. _mensagens];
				}
			}
		}

		public ILogger CreateLogger(string categoryName) => new Captura(this, categoryName);

		public void Dispose()
		{
		}

		private void Registrar(string categoria, string mensagem)
		{
			lock (_mensagens)
			{
				_mensagens.Add((categoria, mensagem));
			}
		}

		private sealed class Captura(MensagensCapturadas dono, string categoria) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => true;

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
			{
				var texto = formatter(state, exception) + (exception is null ? string.Empty : " " + exception);
				dono.Registrar(categoria, texto);
			}
		}
	}

	private sealed record Cenario(IServiceProvider Servicos, MensagensCapturadas Log, IReadOnlyList<TenantInfo> Tenants);

	private readonly string _chaveA = NovaChave();
	private readonly string _chaveB = NovaChave();
	private readonly string _chaveC = NovaChave();
	private readonly List<IDisposable> _descartaveis = [];
	private readonly List<(IntranetDatabaseProvider Provider, string ConnectionString)> _bancos = [];

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public async Task DisposeAsync()
	{
		foreach (var (provider, connectionString) in _bancos)
		{
			await using var contexto = new IntranetDbContext(IntranetDatabaseProviderConfigurator.CreateOptions(provider, connectionString));
			await contexto.Database.EnsureDeletedAsync();
		}

		foreach (var descartavel in _descartaveis)
		{
			descartavel.Dispose();
		}
	}

	private static string NovaChave() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

	private static ISeccoFileCipher Cifrador(string ativa, params string[] aposentadas) =>
		CifradorDeArquivos.Criar(
			new ChaveMestraOptions { ChaveAtiva = ativa, ChavesAposentadas = [.. aposentadas] },
			new AmbienteFalso());

	private static async Task<string> ChaveEmbrulhadaPorAsync(string chaveMestra)
	{
		using var origem = new MemoryStream("conteudo"u8.ToArray());
		using var destino = new MemoryStream();
		var resultado = await Cifrador(chaveMestra).EncryptAsync(origem, destino);

		return resultado.WrappedKey;
	}

	/// <summary>
	/// Host com B ativa e A aposentada, e os dois tenants do catálogo (mesmos ids da fábrica)
	/// apontando para bancos novos e exclusivos deste teste.
	/// </summary>
	private async Task<Cenario> MontarAsync()
	{
		var sufixo = Guid.NewGuid().ToString("N");
		var configuracao = new Dictionary<string, string?>
		{
			["Intranet:Documentos:Chave:ChaveAtiva"] = _chaveB,
			["Intranet:Documentos:Chave:ChavesAposentadas:0"] = _chaveA,
		};

		foreach (var (id, nome) in new[] { (factory.TenantAlfa, "alfa"), (factory.TenantBeta, "beta") })
		{
			using var escopoBase = factory.Services.CreateScope();
			var original = (await escopoBase.ServiceProvider.GetRequiredService<ITenantCatalog>().FindAsync(id))!;
			var builder = new DbConnectionStringBuilder { ConnectionString = original.ConnectionString };
			builder["Database"] = $"intranet_troca_{nome}_{sufixo}";
			configuracao[$"Secco:Tenancy:Tenants:{id}:ConnectionString"] = builder.ConnectionString;
		}

		var log = new MensagensCapturadas();
		var host = factory.WithWebHostBuilder(builder =>
		{
			builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuracao));
			builder.ConfigureLogging(logging => logging.AddProvider(log));
		});
		_descartaveis.Add(host);

		var provider = host.Services.GetRequiredService<IntranetDatabaseOptions>().Provider;
		var tenants = await host.Services.GetRequiredService<ITenantCatalog>().ListAsync();

		// Prova de que o catálogo foi redirecionado: nenhum tenant aponta para os bancos compartilhados.
		tenants.Should().HaveCount(2);
		tenants.Should().OnlyContain(t => t.ConnectionString.Contains("intranet_troca_", StringComparison.Ordinal));
		foreach (var tenant in tenants)
		{
			_bancos.Add((provider, tenant.ConnectionString));
		}

		// Primeira passada com os bancos vazios: cria o schema. As linhas entram depois.
		(await host.Services.RunSeccoMigrationsAsync()).Should().BeTrue();

		return new Cenario(host.Services, log, tenants);
	}

	private static IntranetDbContext Contexto(Cenario cenario, Guid tenantId) =>
		new(IntranetDatabaseProviderConfigurator.CreateOptions(
			cenario.Servicos.GetRequiredService<IntranetDatabaseOptions>().Provider,
			cenario.Tenants.Single(t => t.TenantId == tenantId).ConnectionString));

	private static async Task<Guid> CriarPerfilAsync(Cenario cenario, Guid tenantId, string chave)
	{
		using var contexto = Contexto(cenario, tenantId);
		var perfil = new PerfilColaborador(Guid.NewGuid());
		perfil.DefinirFoto($"f/{Guid.NewGuid():N}", chave);
		contexto.PerfisColaboradores.Add(perfil);
		await contexto.SaveChangesAsync();

		return perfil.Id;
	}

	private static async Task<string> LerChaveAsync(Cenario cenario, Guid tenantId, Guid perfilId)
	{
		using var contexto = Contexto(cenario, tenantId);

		return (await contexto.PerfisColaboradores.AsNoTracking().SingleAsync(p => p.Id == perfilId)).FotoChaveEmbrulhada!;
	}

	private static List<string> MensagensDoMigrator(Cenario cenario) =>
		[.. cenario.Log.Todas
			.Where(m => m.Categoria.Contains(nameof(IntranetTenantMigrator), StringComparison.Ordinal))
			.Select(m => m.Mensagem)];

	[Fact]
	public async Task Migrate_ComChaveAposentada_ReembrulhaNosDoisTenants()
	{
		var cenario = await MontarAsync();
		var alfa = await CriarPerfilAsync(cenario, factory.TenantAlfa, await ChaveEmbrulhadaPorAsync(_chaveA));
		var beta = await CriarPerfilAsync(cenario, factory.TenantBeta, await ChaveEmbrulhadaPorAsync(_chaveA));

		var sucesso = await cenario.Servicos.RunSeccoMigrationsAsync();

		sucesso.Should().BeTrue();
		var cifradorB = Cifrador(_chaveB, _chaveA);
		cifradorB.IsKeyWrappedWithActiveKey(await LerChaveAsync(cenario, factory.TenantAlfa, alfa)).Should().BeTrue();
		cifradorB.IsKeyWrappedWithActiveKey(await LerChaveAsync(cenario, factory.TenantBeta, beta)).Should().BeTrue();

		cenario.Log.Todas.Should().NotBeEmpty();
		cenario.Log.Todas.Should().OnlyContain(m =>
			!m.Mensagem.Contains(_chaveA, StringComparison.Ordinal)
			&& !m.Mensagem.Contains(_chaveB, StringComparison.Ordinal)
			&& !m.Mensagem.Contains("secco-enc:", StringComparison.Ordinal));
		MensagensDoMigrator(cenario).Should().NotBeEmpty("a troca fica registrada");
	}

	[Fact]
	public async Task Migrate_SegundaPassada_NaoMudaNadaNemLoga()
	{
		var cenario = await MontarAsync();
		var alfa = await CriarPerfilAsync(cenario, factory.TenantAlfa, await ChaveEmbrulhadaPorAsync(_chaveA));
		var beta = await CriarPerfilAsync(cenario, factory.TenantBeta, await ChaveEmbrulhadaPorAsync(_chaveA));

		(await cenario.Servicos.RunSeccoMigrationsAsync()).Should().BeTrue();
		var chaveAlfa = await LerChaveAsync(cenario, factory.TenantAlfa, alfa);
		var chaveBeta = await LerChaveAsync(cenario, factory.TenantBeta, beta);
		var mensagensAntes = MensagensDoMigrator(cenario).Count;

		var sucesso = await cenario.Servicos.RunSeccoMigrationsAsync();

		sucesso.Should().BeTrue();
		(await LerChaveAsync(cenario, factory.TenantAlfa, alfa)).Should().Be(chaveAlfa);
		(await LerChaveAsync(cenario, factory.TenantBeta, beta)).Should().Be(chaveBeta);
		MensagensDoMigrator(cenario).Should().HaveCount(mensagensAntes, "sem nada a reembrulhar, não há o que registrar");
	}

	[Fact]
	public async Task Migrate_ChaveSemChaveConhecida_FalhaOTenantEConvergeOOutro()
	{
		var cenario = await MontarAsync();
		var chaveC = await ChaveEmbrulhadaPorAsync(_chaveC);
		await CriarPerfilAsync(cenario, factory.TenantAlfa, chaveC);
		var beta = await CriarPerfilAsync(cenario, factory.TenantBeta, await ChaveEmbrulhadaPorAsync(_chaveA));

		var sucesso = await cenario.Servicos.RunSeccoMigrationsAsync();

		sucesso.Should().BeFalse();
		Cifrador(_chaveB, _chaveA).IsKeyWrappedWithActiveKey(await LerChaveAsync(cenario, factory.TenantBeta, beta)).Should().BeTrue();

		MensagensDoMigrator(cenario).Should().Contain(m => m.Contains(factory.TenantAlfa.ToString(), StringComparison.OrdinalIgnoreCase));
		cenario.Log.Todas.Should().OnlyContain(m =>
			!m.Mensagem.Contains(_chaveA, StringComparison.Ordinal)
			&& !m.Mensagem.Contains(_chaveB, StringComparison.Ordinal)
			&& !m.Mensagem.Contains(_chaveC, StringComparison.Ordinal)
			&& !m.Mensagem.Contains(chaveC, StringComparison.Ordinal)
			&& !m.Mensagem.Contains("secco-enc:", StringComparison.Ordinal));
	}
}
