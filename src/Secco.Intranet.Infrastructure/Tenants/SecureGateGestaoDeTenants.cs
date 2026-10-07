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
