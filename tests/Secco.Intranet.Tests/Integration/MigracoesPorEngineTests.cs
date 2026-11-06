using System.Data.Common;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Infrastructure;
using Secco.Intranet.Infrastructure.Contexts;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Migrations de cada engine suportado (ADR-0018) contra um banco real: aplicam do zero e o
/// snapshot acompanha o modelo. Usa o mesmo configurador de provider do produto, então o
/// assembly de migrations escolhido é o de produção.
/// </summary>
public class MigracoesPorEngineTests(BancosDeMigracaoFixture bancos) : IClassFixture<BancosDeMigracaoFixture>
{
	public static TheoryData<IntranetDatabaseProvider> Engines => new()
	{
		IntranetDatabaseProvider.SqlServer,
		IntranetDatabaseProvider.PostgreSql,
	};

	[Theory]
	[MemberData(nameof(Engines))]
	public async Task Migrate_BancoVazio_AplicaTodasAsMigrationsSemSobrarPendente(IntranetDatabaseProvider provider)
	{
		await using var contexto = await bancos.CriarContextoAsync(provider);

		await contexto.Database.MigrateAsync();

		contexto.Database.GetMigrations().Should().NotBeEmpty("o assembly do engine precisa conter as migrations");
		(await contexto.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
		(await contexto.Database.GetAppliedMigrationsAsync()).Should().BeEquivalentTo(contexto.Database.GetMigrations());
	}

	[Theory]
	[MemberData(nameof(Engines))]
	public async Task HasPendingModelChanges_ModeloAtual_SnapshotEmDia(IntranetDatabaseProvider provider)
	{
		await using var contexto = await bancos.CriarContextoAsync(provider);

		contexto.Database.HasPendingModelChanges().Should().BeFalse(
			"o snapshot do engine {0} está defasado: falta gerar uma migration", provider);
	}
}

/// <summary>Um banco por engine, criado sob demanda e compartilhado pela classe de teste.</summary>
public sealed class BancosDeMigracaoFixture : IAsyncLifetime
{
	private readonly Dictionary<IntranetDatabaseProvider, Lazy<Task<(IAsyncDisposable Container, string ConnectionString)>>> _bancos = new()
	{
		[IntranetDatabaseProvider.SqlServer] = new(IniciarSqlServerAsync),
		[IntranetDatabaseProvider.PostgreSql] = new(IniciarPostgresAsync),
	};

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task<IntranetDbContext> CriarContextoAsync(IntranetDatabaseProvider provider)
	{
		var (_, connectionString) = await _bancos[provider].Value;

		// Banco novo por teste: "do zero" não pode depender da ordem de execução.
		var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
		builder["Database"] = $"migracao_{Guid.NewGuid():N}";

		return new IntranetDbContext(IntranetDatabaseProviderConfigurator.CreateOptions(provider, builder.ConnectionString));
	}

	public async Task DisposeAsync()
	{
		foreach (var banco in _bancos.Values.Where(lazy => lazy.IsValueCreated))
		{
			var (container, _) = await banco.Value;
			await container.DisposeAsync();
		}
	}

	private static async Task<(IAsyncDisposable, string)> IniciarSqlServerAsync()
	{
		var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
		await container.StartAsync();

		return (container, container.GetConnectionString());
	}

	private static async Task<(IAsyncDisposable, string)> IniciarPostgresAsync()
	{
		var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
		await container.StartAsync();

		return (container, container.GetConnectionString());
	}
}
