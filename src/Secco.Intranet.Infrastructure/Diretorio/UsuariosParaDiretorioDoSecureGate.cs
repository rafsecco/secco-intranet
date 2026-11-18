using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Diretorio;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>
/// Usuários ativos do tenant. A fonte preferida é a API de diretório do SecureGate (traz os
/// atributos do Entra); em qualquer falha dela cai para a porta da gestão de acesso (que já devolve
/// a situação da conta e falha como <see cref="Result"/>), sem atributos. O resultado fica em cache
/// por 60 s por tenant — <b>só quando dá certo</b>: uma falha cacheada esconderia por um minuto um
/// diretório que voltou.
/// </summary>
/// <param name="gestao">Porta da gestão de acesso.</param>
/// <param name="cache">Cache em memória.</param>
/// <param name="tenantContext">Tenant da requisição atual (ADR-0005).</param>
/// <param name="leitor">Leitura pela API de diretório; nula usa só a gestão de acesso.</param>
/// <param name="logger">Log do aviso de fallback.</param>
internal sealed partial class UsuariosParaDiretorioDoSecureGate(
	IGestaoDeAcesso gestao,
	IMemoryCache cache,
	ITenantContext tenantContext,
	ILeitorDoDiretorio? leitor,
	ILogger<UsuariosParaDiretorioDoSecureGate> logger) : IUsuariosParaDiretorio
{
	private static readonly TimeSpan Validade = TimeSpan.FromSeconds(60);

	// O aviso de fallback sai uma vez por processo: a falha se repete a cada 60 s e inundaria o log.
	private static int _avisou;

	/// <summary>Rearma o aviso único; só para testes.</summary>
	internal static void RearmarAviso() => Interlocked.Exchange(ref _avisou, 0);

	/// <inheritdoc />
	public async Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(
		CancellationToken cancellationToken = default)
	{
		if (!tenantContext.IsResolved)
		{
			return Result.Failure<IReadOnlyList<UsuarioParaDiretorio>>(IntranetErrors.Acesso.Indisponivel);
		}

		if (cache.TryGetValue(Chave, out IReadOnlyList<UsuarioParaDiretorio>? guardada) && guardada is not null)
		{
			return Result.Success(guardada);
		}

		var doDiretorio = await TentarLerDoDiretorioAsync(cancellationToken).ConfigureAwait(false);

		if (doDiretorio is not null)
		{
			cache.Set(Chave, doDiretorio, Validade);

			return Result.Success(doDiretorio);
		}

		var lida = await gestao.ListarUsuariosAsync(cancellationToken).ConfigureAwait(false);

		if (lida.IsFailure)
		{
			return Result.Failure<IReadOnlyList<UsuarioParaDiretorio>>(lida.Error);
		}

		IReadOnlyList<UsuarioParaDiretorio> ativos =
		[
			.. lida.Value
				.Where(usuario => usuario.Situacao != SituacaoDoUsuario.Desativado)
				.Select(usuario => new UsuarioParaDiretorio(usuario.Id, usuario.Email ?? string.Empty, usuario.Nome)),
		];

		cache.Set(Chave, ativos, Validade);

		return Result.Success(ativos);
	}

	/// <inheritdoc />
	public void Esquecer()
	{
		if (tenantContext.IsResolved)
		{
			cache.Remove(Chave);
		}
	}

	private async Task<IReadOnlyList<UsuarioParaDiretorio>?> TentarLerDoDiretorioAsync(CancellationToken cancellationToken)
	{
		if (leitor is null)
		{
			return null;
		}

		try
		{
			return await leitor.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested
			&& (ex is HttpRequestException or ApiException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException))
		{
			// Só o tipo da exceção: a mensagem pode carregar corpo de resposta (ADR-0020 da plataforma)
			if (Interlocked.Exchange(ref _avisou, 1) == 0)
			{
				AvisarFallback(ex.GetType().Name);
			}

			return null;
		}
	}

	[LoggerMessage(Level = LogLevel.Warning,
		Message = "Leitura do diretório do SecureGate falhou ({TipoDaExcecao}); usando a gestão de acesso, sem os atributos do Entra.")]
	private partial void AvisarFallback(string tipoDaExcecao);

	private string Chave => $"diretorio:usuarios:{tenantContext.TenantId}";
}
