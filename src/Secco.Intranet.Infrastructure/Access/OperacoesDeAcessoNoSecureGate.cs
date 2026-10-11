using System.Net;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Núcleo dos adaptadores de acesso (o do tenant da requisição e o do tenant administrado): a
/// chamada ao client, a tradução de falha em <see cref="Result"/> e os mapeamentos comuns. O tenant
/// é sempre decidido por quem chama. Falha de rede, de status e timeout viram
/// <see cref="IntranetErrors.Acesso.Indisponivel"/>; cancelamento pedido pelo chamador é relançado.
/// O log leva só a operação e o status, nunca corpo ou credencial (ADR-0020).
/// </summary>
internal static class OperacoesDeAcessoNoSecureGate
{
	/// <summary>Executa uma leitura.</summary>
	public static async Task<Result<T>> LerAsync<T>(
		ILogger logger,
		string operacao,
		Func<CancellationToken, Task<T>> chamada,
		Error? naoEncontrado,
		CancellationToken cancellationToken)
	{
		try
		{
			return Result.Success(await chamada(cancellationToken).ConfigureAwait(false));
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.NotFound && naoEncontrado is not null)
		{
			return Result.Failure<T>(naoEncontrado);
		}
		catch (Exception excecao) when (EhFalhaDeBorda(excecao, cancellationToken))
		{
			Registrar(logger, operacao, excecao);

			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}
	}

	/// <summary>Executa uma escrita sem retorno.</summary>
	public static async Task<Result> EscreverAsync(
		ILogger logger,
		string operacao,
		Func<CancellationToken, Task> chamada,
		Error? naoEncontrado,
		Func<ApiException, Error?>? conflito,
		CancellationToken cancellationToken)
	{
		var escrito = await EscreverAsync<bool>(
			logger,
			operacao,
			async token =>
			{
				await chamada(token).ConfigureAwait(false);

				return true;
			},
			naoEncontrado,
			conflito,
			cancellationToken).ConfigureAwait(false);

		return escrito.IsSuccess ? Result.Success() : Result.Failure(escrito.Error);
	}

	/// <summary>Executa uma escrita que devolve valor (ex.: o id criado). <paramref name="recusado"/> traduz o 400.</summary>
	public static async Task<Result<T>> EscreverAsync<T>(
		ILogger logger,
		string operacao,
		Func<CancellationToken, Task<T>> chamada,
		Error? naoEncontrado,
		Func<ApiException, Error?>? conflito,
		CancellationToken cancellationToken,
		Error? recusado = null)
	{
		try
		{
			return Result.Success(await chamada(cancellationToken).ConfigureAwait(false));
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.NotFound && naoEncontrado is not null)
		{
			return Result.Failure<T>(naoEncontrado);
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.Conflict
			&& conflito?.Invoke(apiException) is { } erro)
		{
			return Result.Failure<T>(erro);
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.BadRequest && recusado is not null)
		{
			return Result.Failure<T>(recusado);
		}
		catch (Exception excecao) when (EhFalhaDeBorda(excecao, cancellationToken))
		{
			Registrar(logger, operacao, excecao);

			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}
	}

	/// <summary>Situação da conta a partir do status textual da plataforma.</summary>
	public static SituacaoDoUsuario Situacao(string? status) => status?.ToLowerInvariant() switch
	{
		"active" => SituacaoDoUsuario.Ativo,
		"deactivated" => SituacaoDoUsuario.Desativado,
		"lockedout" => SituacaoDoUsuario.Bloqueado,
		_ => SituacaoDoUsuario.Desconhecida,
	};

	/// <summary>Se o ProblemDetails da resposta traz o código de erro informado.</summary>
	public static bool TemCodigo(ApiException excecao, string codigo) =>
		excecao.Response?.Contains(codigo, StringComparison.Ordinal) == true;

	// Timeout do HttpClient chega como OperationCanceledException sem o token do chamador cancelado:
	// é falha da borda, não cancelamento — escapar como exceção viraria um 500.
	private static bool EhFalhaDeBorda(Exception excecao, CancellationToken cancellationToken) => excecao switch
	{
		ApiException => true,
		HttpRequestException => true,
		OperationCanceledException => !cancellationToken.IsCancellationRequested,
		_ => false,
	};

	private static void Registrar(ILogger logger, string operacao, Exception excecao)
	{
		if (excecao is ApiException apiException)
		{
			logger.LogWarning("Falha ao {Operacao} no SecureGate (status {StatusCode}).", operacao, apiException.StatusCode);

			return;
		}

		logger.LogWarning(excecao, "Falha de rede ou timeout ao {Operacao} no SecureGate.", operacao);
	}
}
