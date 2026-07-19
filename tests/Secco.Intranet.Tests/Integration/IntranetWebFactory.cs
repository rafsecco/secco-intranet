using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Sobe o monolito real (<c>Secco.Intranet.Web</c>, ADR-0002) no ambiente <c>Testing</c>
/// (sem migrations/seed automáticos de DEV) com um SQL Server real via Testcontainers
/// (ADR-0012) e dois tenants no catálogo apontando para bancos distintos (ADR-0005).
/// Sem token/JWT de teste: o host atual não registra autenticação — a integração OIDC com
/// o Secco.SecureGate ainda é item futuro do roadmap (ver comentário no Program.cs do Web).
/// </summary>
public sealed class IntranetWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
	// Testcontainers 4.13+: imagem explícita obrigatória (construtor sem imagem é obsoleto)
	private readonly MsSqlContainer _container =
		new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
	private readonly SemaphoreSlim _migrationLock = new(1, 1);
	private bool _migrated;

	/// <summary>Identificador do tenant "Alfa" usado nos testes.</summary>
	public Guid TenantAlfa { get; } = Guid.NewGuid();

	/// <summary>Identificador do tenant "Beta" usado nos testes.</summary>
	public Guid TenantBeta { get; } = Guid.NewGuid();

	/// <summary>Monta a connection string de um banco de tenant dentro do container de testes.</summary>
	/// <param name="databaseName">Nome do banco do tenant.</param>
	public string GetTenantConnectionString(string databaseName) =>
		new SqlConnectionStringBuilder(_container.GetConnectionString())
		{
			InitialCatalog = databaseName,
		}.ConnectionString;

	/// <summary>Aplica as migrations nos bancos de tenant uma única vez por factory.</summary>
	public async Task EnsureTenantDatabasesMigratedAsync()
	{
		await _migrationLock.WaitAsync();

		try
		{
			if (!_migrated)
			{
				await Secco.Intranet.Infrastructure.IntranetInfrastructureExtensions
					.MigrateIntranetTenantDatabasesAsync(Services);
				_migrated = true;
			}
		}
		finally
		{
			_migrationLock.Release();
		}
	}

	/// <inheritdoc />
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.UseEnvironment("Testing");

		builder.ConfigureAppConfiguration((_, configuration) =>
			configuration.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[$"Secco:Tenancy:Tenants:{TenantAlfa}:ConnectionString"] =
					GetTenantConnectionString("secco_intranet_alfa"),
				[$"Secco:Tenancy:Tenants:{TenantBeta}:ConnectionString"] =
					GetTenantConnectionString("secco_intranet_beta"),
			}));
	}

	/// <inheritdoc />
	public async Task InitializeAsync() => await _container.StartAsync();

	async Task IAsyncLifetime.DisposeAsync()
	{
		await base.DisposeAsync();
		await _container.DisposeAsync();
	}
}
