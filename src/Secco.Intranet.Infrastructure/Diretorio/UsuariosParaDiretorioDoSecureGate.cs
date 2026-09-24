using Microsoft.Extensions.Caching.Memory;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Diretorio;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>
/// Usuários ativos do tenant, vindos do SecureGate pela mesma porta da gestão de acesso (que já
/// devolve a situação da conta e falha como <see cref="Result"/>). <c>ListUsers</c> devolve o
/// tenant inteiro e não pagina, então o resultado fica em cache por 60 s por tenant —
/// <b>só quando dá certo</b>: uma falha cacheada esconderia por um minuto um diretório que voltou.
/// </summary>
/// <param name="gestao">Porta da gestão de acesso.</param>
/// <param name="cache">Cache em memória.</param>
/// <param name="tenantContext">Tenant da requisição atual (ADR-0005).</param>
public sealed class UsuariosParaDiretorioDoSecureGate(
	IGestaoDeAcesso gestao,
	IMemoryCache cache,
	ITenantContext tenantContext) : IUsuariosParaDiretorio
{
	private static readonly TimeSpan Validade = TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	public async Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(
		CancellationToken cancellationToken = default)
	{
		if (!tenantContext.IsResolved)
		{
			return Result.Failure<IReadOnlyList<UsuarioParaDiretorio>>(IntranetErrors.Acesso.Indisponivel);
		}

		var chave = $"diretorio:usuarios:{tenantContext.TenantId}";

		if (cache.TryGetValue(chave, out IReadOnlyList<UsuarioParaDiretorio>? guardada) && guardada is not null)
		{
			return Result.Success(guardada);
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
				.Select(usuario => new UsuarioParaDiretorio(usuario.Id, usuario.Email ?? string.Empty)),
		];

		cache.Set(chave, ativos, Validade);

		return Result.Success(ativos);
	}
}
