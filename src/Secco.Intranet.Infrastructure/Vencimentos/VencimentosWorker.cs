using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Vencimentos;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.Intranet.Infrastructure.Vencimentos;

/// <summary>
/// Varredura periódica dos avisos de vencimento (ADR-0015 da plataforma, camada 1: manutenção em
/// processo; perder um ciclo por restart é aceitável, o ciclo seguinte recupera). Percorre os
/// tenants do catálogo e, em um escopo próprio de cada um, roda o
/// <see cref="ProcessarAvisosDeVencimentoHandler"/>. Postura fail-safe: configuração desligada ou
/// inválida, ou fuso inexistente, deixa o worker inativo.
/// </summary>
/// <param name="scopes">Fábrica de escopos, um por tenant.</param>
/// <param name="catalogo">Catálogo de tenants.</param>
/// <param name="opcoes">Configuração do aviso.</param>
/// <param name="logger">Log.</param>
internal sealed partial class VencimentosWorker(
	IServiceScopeFactory scopes,
	ITenantCatalog catalogo,
	VencimentosOptions opcoes,
	ILogger<VencimentosWorker> logger) : BackgroundService
{
	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!opcoes.Habilitado)
		{
			LogDesabilitado(logger);
			return;
		}

		if (opcoes.IntervaloEmMinutos < 1)
		{
			LogIntervaloInvalido(logger, opcoes.IntervaloEmMinutos);
			return;
		}

		if (!FusoEhValido())
		{
			return;
		}

		using var timer = new PeriodicTimer(TimeSpan.FromMinutes(opcoes.IntervaloEmMinutos));

		do
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
		while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
	}

	/// <summary>Roda uma varredura em todos os tenants; a falha de um não interrompe os demais.</summary>
	/// <param name="ct">Token de cancelamento.</param>
	internal async Task ExecutarUmCicloAsync(CancellationToken ct)
	{
		foreach (var tenant in await catalogo.ListAsync(ct).ConfigureAwait(false))
		{
			try
			{
				await using var escopo = scopes.CreateAsyncScope();
				escopo.ServiceProvider.SetTenant(tenant.TenantId);

				var enviados = await escopo.ServiceProvider
					.GetRequiredService<ProcessarAvisosDeVencimentoHandler>()
					.HandleAsync(ct)
					.ConfigureAwait(false);

				LogTenantProcessado(logger, tenant.TenantId, enviados);
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

	// O relógio resolve o fuso no construtor e lança se o identificador não existe.
	private bool FusoEhValido()
	{
		try
		{
			using var escopo = scopes.CreateScope();
			_ = escopo.ServiceProvider.GetRequiredService<RelogioDeVencimentos>();

			return true;
		}
		catch (Exception exception)
		{
			LogFusoInvalido(logger, exception, opcoes.FusoHorario);

			return false;
		}
	}

	[LoggerMessage(EventId = 1, Level = LogLevel.Information,
		Message = "Avisos de vencimento desligados: 'Intranet:Vencimentos:Habilitado' é falso.")]
	private static partial void LogDesabilitado(ILogger logger);

	[LoggerMessage(EventId = 2, Level = LogLevel.Warning,
		Message = "Avisos de vencimento inativos: 'Intranet:Vencimentos:IntervaloEmMinutos' inválido ({Intervalo}); o mínimo é 1.")]
	private static partial void LogIntervaloInvalido(ILogger logger, int intervalo);

	[LoggerMessage(EventId = 3, Level = LogLevel.Error,
		Message = "Avisos de vencimento inativos: fuso horário '{Fuso}' inválido em 'Intranet:Vencimentos:FusoHorario' (fail-safe).")]
	private static partial void LogFusoInvalido(ILogger logger, Exception exception, string fuso);

	[LoggerMessage(EventId = 4, Level = LogLevel.Information,
		Message = "Avisos de vencimento do tenant {TenantId}: {Enviados} item(ns) avisado(s).")]
	private static partial void LogTenantProcessado(ILogger logger, Guid tenantId, int enviados);

	[LoggerMessage(EventId = 5, Level = LogLevel.Error,
		Message = "Falha nos avisos de vencimento do tenant {TenantId} — os demais tenants seguem.")]
	private static partial void LogFalhaNoTenant(ILogger logger, Exception exception, Guid tenantId);

	[LoggerMessage(EventId = 6, Level = LogLevel.Error,
		Message = "Falha na varredura dos avisos de vencimento; nova tentativa no próximo ciclo.")]
	private static partial void LogFalhaNoCiclo(ILogger logger, Exception exception);
}
