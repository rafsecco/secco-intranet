using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Application.Salas.Agenda;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.Intranet.Infrastructure.Salas;

/// <summary>
/// Processo em segundo plano que espelha as reservas na agenda externa (ADR-0015 da plataforma, camada 1:
/// manutenção em processo; perder um ciclo por restart é aceitável, o seguinte recupera). Percorre os
/// tenants do catálogo e, em um escopo próprio de cada um, roda o <see cref="ProcessarEspelhamentoHandler"/>.
/// Só é registrado com um provedor de agenda configurado.
/// </summary>
/// <param name="scopes">Fábrica de escopos, um por tenant.</param>
/// <param name="catalogo">Catálogo de tenants.</param>
/// <param name="opcoes">Configuração da agenda.</param>
/// <param name="logger">Log.</param>
internal sealed partial class EspelhamentoWorker(
	IServiceScopeFactory scopes,
	ITenantCatalog catalogo,
	AgendaOptions opcoes,
	ILogger<EspelhamentoWorker> logger) : BackgroundService
{
	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// O primeiro ciclo roda depois de um intervalo, não na subida: a aplicação termina de subir antes de falar com a agenda.
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(opcoes.IntervaloEmSegundos, AgendaOptions.IntervaloMinimoEmSegundos)));

		while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
		{
			try
			{
				await ExecutarUmCicloAsync(stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception exception)
			{
				LogFalhaNoCiclo(logger, exception);
			}
		}
	}

	/// <summary>Roda um ciclo em todos os tenants; a falha de um não interrompe os demais.</summary>
	/// <param name="ct">Token de cancelamento.</param>
	internal async Task ExecutarUmCicloAsync(CancellationToken ct)
	{
		foreach (var tenant in await catalogo.ListAsync(ct).ConfigureAwait(false))
		{
			try
			{
				await using var escopo = scopes.CreateAsyncScope();
				escopo.ServiceProvider.SetTenant(tenant.TenantId);

				var tratadas = await escopo.ServiceProvider
					.GetRequiredService<ProcessarEspelhamentoHandler>()
					.HandleAsync(ct)
					.ConfigureAwait(false);

				if (tratadas > 0)
				{
					LogTenantProcessado(logger, tenant.TenantId, tratadas);
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception exception)
			{
				LogFalhaNoTenant(logger, exception, tenant.TenantId);
			}
		}
	}

	[LoggerMessage(EventId = 1, Level = LogLevel.Information,
		Message = "Espelhamento de reservas do tenant {TenantId}: {Tratadas} pendência(s) tratada(s).")]
	private static partial void LogTenantProcessado(ILogger logger, Guid tenantId, int tratadas);

	[LoggerMessage(EventId = 2, Level = LogLevel.Error,
		Message = "Falha no espelhamento de reservas do tenant {TenantId} — os demais tenants seguem.")]
	private static partial void LogFalhaNoTenant(ILogger logger, Exception exception, Guid tenantId);

	[LoggerMessage(EventId = 3, Level = LogLevel.Error,
		Message = "Falha na varredura do espelhamento de reservas; nova tentativa no próximo ciclo.")]
	private static partial void LogFalhaNoCiclo(ILogger logger, Exception exception);
}
