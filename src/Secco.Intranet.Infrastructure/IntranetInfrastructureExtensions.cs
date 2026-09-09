using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Infrastructure.Access;
using Secco.Intranet.Infrastructure.Armazenamento;
using Secco.Intranet.Infrastructure.Auditoria;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.Intranet.Infrastructure.Notificacao;
using Secco.Intranet.Infrastructure.Repositories;
using Secco.Intranet.Infrastructure.Seeding;
using Secco.LogStream.Client;
using Secco.NotificationHub.Client;
using Secco.SDK.AspNetCore.Authentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Client.Administration;
using Secco.SecureGate.Client.Catalog;

namespace Secco.Intranet.Infrastructure;

/// <summary>Composição de DI da camada de infraestrutura.</summary>
public static class IntranetInfrastructureExtensions
{
	/// <summary>
	/// Registra o <see cref="IntranetDbContext"/> apontando para o banco do tenant da
	/// requisição atual (ADR-0005): a connection string vem do <see cref="ITenantConnectionFactory"/>
	/// — jamais fixa. Requer <c>AddSeccoTenancy()</c> (via <c>AddSeccoPlatform()</c>).
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddIntranetInfrastructure(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		// Bind LAZY (do IConfiguration do DI): fontes adicionadas por testes/hosting tardio são respeitadas
		services.AddSingleton(sp => BindSection(sp, "Intranet:Database", new IntranetDatabaseOptions()));
		services.AddSingleton(sp => BindSection(sp, "Intranet:Limits", new IntranetOptions()));
		services.AddSingleton(sp => BindSection(sp, DocumentoOptions.SectionKey, new DocumentoOptions()));
		services.AddSingleton(sp => BindSection(sp, ArquivoStoreOptions.SectionKey, new ArquivoStoreOptions()));
		services.AddSingleton(sp => BindSection(sp, ChaveMestraOptions.SectionKey, new ChaveMestraOptions()));
		services.AddSingleton(sp => BindSection(sp, NotificacaoOptions.SectionKey, new NotificacaoOptions()));
		services.AddSingleton(sp => BindSection(sp, AuditoriaOptions.SectionKey, new AuditoriaOptions()));

		// A cifragem em envelope nao depende do tenant: uma instancia serve a aplicacao toda.
		services.AddSingleton<EnvelopeCipher>();

		// Ja o armazenamento depende: o caminho sai do tenant da requisicao (ADR-0005).
		services.AddScoped<IArquivoStore, SistemaArquivosArquivoStore>();

		// Chave ausente ou malformada derruba o startup, nao o primeiro upload (ADR-0020).
		services.AddHostedService<ValidacaoChaveMestraHostedService>();

		services.AddDbContext<IntranetDbContext>((serviceProvider, options) =>
		{
			var connectionFactory = serviceProvider.GetRequiredService<ITenantConnectionFactory>();
			var databaseOptions = serviceProvider.GetRequiredService<IntranetDatabaseOptions>();

			// O catálogo padrão resolve de forma síncrona (ValueTask já concluída)
			var connectionString = connectionFactory.GetConnectionStringAsync().AsTask().GetAwaiter().GetResult();

			IntranetDatabaseProviderConfigurator.Configure(options, databaseOptions.Provider, connectionString);
		});

		services.AddScoped<ISetorRepository, SetorRepository>();
		services.AddScoped<IDocumentoRepository, DocumentoRepository>();
		services.AddScoped<IPublicacaoRepository, PublicacaoRepository>();

		// Guarda dupla da ADR-0019 (Development + flag) e aplicada pelo SeedSeccoDataAsync.
		services.AddScoped<IDevelopmentDataSeeder, SetoresDesenvolvimentoSeeder>();
		services.AddScoped<IDevelopmentDataSeeder, PublicacoesDesenvolvimentoSeeder>();

		// Composição lazy por configuração (mesmo padrão de AddSecureGateTenantCatalog na
		// plataforma): AddSecureGateAdminClient() já é seguro chamar sempre — o HttpClient só
		// ganha BaseAddress/handler de autenticação quando a seção Secco:SecureGate está
		// configurada (ver Secco.SecureGate.Client). A escolha do adapter (real vs. no-op) só
		// acontece na RESOLUÇÃO do ISetorAccessProvisioner, lendo o mesmo
		// SecureGateClientCredentialsOptions que o client usa — evita ler IConfiguration
		// diretamente aqui, seguindo o padrão de bind lazy já usado nesta classe.
		services.AddSecureGateAdminClient();
		services.AddScoped<ISetorAccessProvisioner>(serviceProvider =>
		{
			var secureGateOptions = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

			return secureGateOptions.IsConfigured
				? ActivatorUtilities.CreateInstance<SecureGateSetorAccessProvisioner>(serviceProvider)
				: ActivatorUtilities.CreateInstance<NullSetorAccessProvisioner>(serviceProvider);
		});

		// Store próprio para o Hub: um por recurso/scope (least privilege), como o SecureGate
		// faz. O client é registrado sempre e lê a URL na resolução — quem decide entre
		// adapter real e no-op são as options, não a presença do registro.
		var tokenStoreDoHub = new SeccoAccessTokenStore();

		services.AddHttpClient<INotificationHubClient, NotificationHubClient>()
			.ConfigureHttpClient((serviceProvider, client) =>
			{
				var notificacao = serviceProvider.GetRequiredService<NotificacaoOptions>();

				if (!string.IsNullOrWhiteSpace(notificacao.HubUrl))
				{
					client.BaseAddress = new Uri(notificacao.HubUrl, UriKind.Absolute);
				}
			})
			.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
			{
				// AddNotificationHubClient() do pacote só aceita BaseUrl, mas todo endpoint do
				// Hub exige permissão — então o handler de credenciais entra à mão aqui.
				var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

				// Validate() do pacote é internal, então a guarda aqui é IsConfigured, que já
				// exige BaseUrl, ClientId e ClientSecret juntos.
				if (credenciais.IsConfigured)
				{
					handlers.Add(new SeccoClientCredentialsHandler(
						credenciais.BaseUrl!,
						credenciais.ClientId!,
						credenciais.ClientSecret!,
						"notifications:read notifications:write",
						tokenStoreDoHub));
				}
			});

		services.AddScoped<IDiretorioDeUsuarios>(serviceProvider =>
		{
			var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

			return credenciais.IsConfigured
				? ActivatorUtilities.CreateInstance<SecureGateDiretorioDeUsuarios>(serviceProvider)
				: ActivatorUtilities.CreateInstance<DiretorioVazio>(serviceProvider);
		});

		services.AddScoped<INotificadorDeMensagens>(serviceProvider =>
			string.IsNullOrWhiteSpace(serviceProvider.GetRequiredService<NotificacaoOptions>().HubUrl)
				? ActivatorUtilities.CreateInstance<NotificadorSilencioso>(serviceProvider)
				: ActivatorUtilities.CreateInstance<NotificationHubNotificador>(serviceProvider));

		services.AddScoped<ICaixaDeNotificacoes>(serviceProvider =>
			string.IsNullOrWhiteSpace(serviceProvider.GetRequiredService<NotificacaoOptions>().HubUrl)
				? ActivatorUtilities.CreateInstance<CaixaVazia>(serviceProvider)
				: ActivatorUtilities.CreateInstance<NotificationHubCaixaDeNotificacoes>(serviceProvider));

		services.AddScoped<IConsultaDeEntregas>(serviceProvider =>
			string.IsNullOrWhiteSpace(serviceProvider.GetRequiredService<NotificacaoOptions>().HubUrl)
				? ActivatorUtilities.CreateInstance<ConsultaDeEntregasVazia>(serviceProvider)
				: ActivatorUtilities.CreateInstance<NotificationHubConsultaDeEntregas>(serviceProvider));

		// Store próprio para o LogStream: um por recurso/scope (least privilege), como o
		// SecureGate e o NotificationHub. O client é registrado sempre e lê a URL na
		// resolução; quem decide entre adapter real e no-op são as options.
		var tokenStoreDoLogStream = new SeccoAccessTokenStore();

		services.AddHttpClient<ILogStreamClient, LogStreamClient>()
			.ConfigureHttpClient((serviceProvider, client) =>
			{
				var auditoria = serviceProvider.GetRequiredService<AuditoriaOptions>();

				if (!string.IsNullOrWhiteSpace(auditoria.LogStreamUrl))
				{
					client.BaseAddress = new Uri(auditoria.LogStreamUrl, UriKind.Absolute);
				}
			})
			.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
			{
				// AddLogStreamClient() do pacote só aceita BaseUrl, mas os endpoints de
				// auditoria exigem AuditEntries.Write — o handler de credenciais entra à mão.
				var credenciais = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

				if (credenciais.IsConfigured)
				{
					// Só write: nada no produto lê a trilha (ler é fora de escopo, ver spec de
					// auditoria) — pedir audit-entries:read violaria o least privilege do
					// comentário acima sem nenhum uso correspondente.
					handlers.Add(new SeccoClientCredentialsHandler(
						credenciais.BaseUrl!,
						credenciais.ClientId!,
						credenciais.ClientSecret!,
						"audit-entries:write",
						tokenStoreDoLogStream));
				}
			});

		services.AddScoped<ITrilhaDeAuditoria>(serviceProvider =>
			string.IsNullOrWhiteSpace(serviceProvider.GetRequiredService<AuditoriaOptions>().LogStreamUrl)
				? ActivatorUtilities.CreateInstance<TrilhaSilenciosa>(serviceProvider)
				: ActivatorUtilities.CreateInstance<LogStreamTrilhaDeAuditoria>(serviceProvider));

		return services;
	}

	/// <summary>
	/// Aplica as migrations pendentes no banco de <b>cada tenant</b> do catálogo.
	/// Uso: startup em Development e processos controlados de provisionamento (ADR-0005) —
	/// nunca no startup de produção.
	/// </summary>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task MigrateIntranetTenantDatabasesAsync(
		this IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		using var scope = serviceProvider.CreateScope();
		var catalog = scope.ServiceProvider.GetRequiredService<ITenantCatalog>();
		var databaseOptions = scope.ServiceProvider.GetRequiredService<IntranetDatabaseOptions>();

		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			var options = IntranetDatabaseProviderConfigurator.CreateOptions(
				databaseOptions.Provider, tenant.ConnectionString);

			await using var context = new IntranetDbContext(options);
			await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	private static TOptions BindSection<TOptions>(IServiceProvider serviceProvider, string sectionKey, TOptions options)
		where TOptions : class
	{
		serviceProvider.GetRequiredService<IConfiguration>().GetSection(sectionKey).Bind(options);
		return options;
	}
}
