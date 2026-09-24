using System.Net;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Adapter real de <see cref="IGestaoDeAcesso"/>, sobre o client administrativo do SecureGate
/// (mesma credencial de <see cref="SecureGateSetorAccessProvisioner"/>). Só é resolvido com a
/// seção <c>Secco:SecureGate</c> configurada — ver <see cref="GestaoDeAcessoIndisponivel"/>.
/// Falha de rede, de status e timeout viram <see cref="Result"/>: quem administra precisa saber
/// que a ação não aconteceu. Cancelamento pedido pelo chamador é relançado.
/// </summary>
/// <param name="client">Client administrativo do SecureGate.</param>
/// <param name="tenantContext">Tenant da requisição atual (ADR-0005).</param>
/// <param name="logger">Log de falhas — só operação e status, nunca corpo ou credencial (ADR-0020).</param>
public sealed class SecureGateGestaoDeAcesso(
	ISecureGateClient client,
	ITenantContext tenantContext,
	ILogger<SecureGateGestaoDeAcesso> logger) : IGestaoDeAcesso
{
	private const int TamanhoDaPaginaDeMembros = 100;

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDto>>> ListarPerfisAsync(CancellationToken cancellationToken = default) =>
		LerAsync<IReadOnlyList<PerfilDto>>(
			"listar perfis",
			async (tenantId, token) =>
			{
				var perfis = await client.ListRolesAsync(tenantId, token).ConfigureAwait(false);
				IReadOnlyList<PerfilDto> lista =
					[.. perfis.Select(p => new PerfilDto(
						p.Name,
						p.Permissions?.Count ?? 0,
						ClassificacaoDePerfil.Tipo(p.Name),
						ClassificacaoDePerfil.EhReservado(p.Name)))];

				return lista;
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<PerfilDetalheDto>> ObterPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		LerAsync(
			"obter perfil",
			async (tenantId, token) =>
			{
				var papel = await client.GetRoleAsync(tenantId, nome, token).ConfigureAwait(false);

				return new PerfilDetalheDto(
					papel.Name,
					[.. papel.Permissions ?? []],
					papel.IsReserved,
					papel.MemberCount,
					ClassificacaoDePerfil.Tipo(papel.Name));
			},
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(
		string nome, int pagina, CancellationToken cancellationToken = default) =>
		LerAsync(
			"listar membros",
			async (tenantId, token) =>
			{
				var lida = await client
					.ListRoleMembersAsync(tenantId, nome, pagina, TamanhoDaPaginaDeMembros, token)
					.ConfigureAwait(false);

				IReadOnlyList<MembroDoPerfilDto> itens =
					[.. (lida.Items ?? []).Select(m => new MembroDoPerfilDto(m.UserId, m.Email, Situacao(m.Status)))];

				return new PaginaDeMembros(itens, lida.Page, lida.TotalPages, lida.TotalCount);
			},
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(CancellationToken cancellationToken = default) =>
		LerAsync<IReadOnlyList<UsuarioDto>>(
			"listar usuarios",
			async (tenantId, token) =>
			{
				var usuarios = await client.ListUsersAsync(tenantId, token).ConfigureAwait(false);
				IReadOnlyList<UsuarioDto> lista =
					[.. usuarios.Select(u => new UsuarioDto(u.Id, u.Email, Situacao(u.Status), [.. u.Roles ?? []]))];

				return lista;
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<UsuarioDetalheDto>> ObterUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		LerAsync(
			"obter usuario",
			async (tenantId, token) =>
			{
				var u = await client.GetUserAsync(tenantId, usuarioId, token).ConfigureAwait(false);

				return new UsuarioDetalheDto(
					u.Id,
					u.Email,
					Situacao(u.Status),
					u.LockoutEnd,
					[.. u.Roles ?? []],
					[.. u.EffectivePermissions ?? []],
					[.. u.ExternalLogins ?? []],
					u.TwoFactorEnabled);
			},
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"criar perfil",
			(tenantId, token) => client.CreateRoleAsync(tenantId, new CreateRoleRequest { Name = nome }, token),
			null,
			IntranetErrors.Acesso.PerfilJaExiste,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"excluir perfil",
			(tenantId, token) => client.DeleteRoleAsync(tenantId, nome, token),
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			IntranetErrors.Acesso.PerfilComMembros,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"atribuir perfil",
			(tenantId, token) => client.AddUserRoleAsync(tenantId, usuarioId, perfil, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"retirar perfil",
			(tenantId, token) => client.RemoveUserRoleAsync(tenantId, usuarioId, perfil, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"desativar usuario",
			(tenantId, token) => client.DeactivateUserAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			IntranetErrors.Acesso.DesativacaoRecusada,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"reativar usuario",
			(tenantId, token) => client.ActivateUserAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		EscreverAsync(
			"encerrar sessoes",
			(tenantId, token) => client.RevokeUserSessionsAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			null,
			cancellationToken);

	private static SituacaoDoUsuario Situacao(string? status) => status?.ToLowerInvariant() switch
	{
		"active" => SituacaoDoUsuario.Ativo,
		"deactivated" => SituacaoDoUsuario.Desativado,
		"lockedout" => SituacaoDoUsuario.Bloqueado,
		_ => SituacaoDoUsuario.Desconhecida,
	};

	private async Task<Result<T>> LerAsync<T>(
		string operacao,
		Func<Guid, CancellationToken, Task<T>> chamada,
		Error? naoEncontrado,
		CancellationToken cancellationToken)
	{
		if (!tenantContext.IsResolved)
		{
			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}

		try
		{
			return Result.Success(await chamada(tenantContext.TenantId!.Value, cancellationToken).ConfigureAwait(false));
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.NotFound && naoEncontrado is not null)
		{
			return Result.Failure<T>(naoEncontrado);
		}
		catch (ApiException apiException)
		{
			logger.LogWarning("Falha ao {Operacao} no SecureGate (status {StatusCode}).", operacao, apiException.StatusCode);

			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao {Operacao} no SecureGate.", operacao);

			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}
		catch (OperationCanceledException operationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Timeout do HttpClient, não cancelamento do chamador: o SecureGate aceitou a
			// conexão e não respondeu a tempo. Escapar como exceção viraria um 500.
			logger.LogWarning(operationCanceledException, "Timeout ao {Operacao} no SecureGate.", operacao);

			return Result.Failure<T>(IntranetErrors.Acesso.Indisponivel);
		}
	}

	private async Task<Result> EscreverAsync(
		string operacao,
		Func<Guid, CancellationToken, Task> chamada,
		Error? naoEncontrado,
		Error? conflito,
		CancellationToken cancellationToken)
	{
		if (!tenantContext.IsResolved)
		{
			return Result.Failure(IntranetErrors.Acesso.Indisponivel);
		}

		try
		{
			await chamada(tenantContext.TenantId!.Value, cancellationToken).ConfigureAwait(false);

			return Result.Success();
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.NotFound && naoEncontrado is not null)
		{
			return Result.Failure(naoEncontrado);
		}
		catch (ApiException apiException) when (apiException.StatusCode == (int)HttpStatusCode.Conflict && conflito is not null)
		{
			return Result.Failure(conflito);
		}
		catch (ApiException apiException)
		{
			logger.LogWarning("Falha ao {Operacao} no SecureGate (status {StatusCode}).", operacao, apiException.StatusCode);

			return Result.Failure(IntranetErrors.Acesso.Indisponivel);
		}
		catch (HttpRequestException httpRequestException)
		{
			logger.LogWarning(httpRequestException, "Falha de rede ao {Operacao} no SecureGate.", operacao);

			return Result.Failure(IntranetErrors.Acesso.Indisponivel);
		}
		catch (OperationCanceledException operationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			logger.LogWarning(operationCanceledException, "Timeout ao {Operacao} no SecureGate.", operacao);

			return Result.Failure(IntranetErrors.Acesso.Indisponivel);
		}
	}
}
