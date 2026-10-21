using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants.Credenciais;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Adapter real de <see cref="IGestaoDeCredenciaisDoTenant"/>, sobre o client administrativo do
/// SecureGate. O tenant é sempre o recebido. O secret só passa pelo valor devolvido: nunca vai a log.
/// </summary>
/// <param name="client">Client administrativo do SecureGate.</param>
/// <param name="logger">Log de falhas — só operação, status e clientId.</param>
public sealed class SecureGateGestaoDeCredenciaisDoTenant(
	ISecureGateClient client,
	ILogger<SecureGateGestaoDeCredenciaisDoTenant> logger) : IGestaoDeCredenciaisDoTenant
{
	private const string TipoMaquina = "client_credentials";
	private const string TipoLogin = "authorization_code";

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<CredencialDto>>> ListarAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync<IReadOnlyList<CredencialDto>>(
			logger,
			"listar credenciais do tenant",
			async token =>
			{
				var clients = await client.ListProductClientsAsync(tenantId, token).ConfigureAwait(false);

				return
				[
					.. clients
						.Select(c => Mapear(c))
						.OfType<CredencialDto>(),
				];
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public async Task<Result<CredencialDto>> ObterAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default)
	{
		var lida = await OperacoesDeAcessoNoSecureGate.LerAsync<CredencialDto?>(
			logger,
			"obter credencial do tenant",
			async token =>
			{
				var c = await client.GetProductClientAsync(tenantId, clientId, token).ConfigureAwait(false);

				return Mapear(c);
			},
			IntranetErrors.Credenciais.NaoEncontrada,
			cancellationToken).ConfigureAwait(false);

		if (lida.IsFailure)
		{
			return Result.Failure<CredencialDto>(lida.Error);
		}

		return lida.Value is { } credencial
			? Result.Success(credencial)
			: Result.Failure<CredencialDto>(IntranetErrors.Acesso.Indisponivel);
	}

	/// <inheritdoc />
	public Task<Result<CredencialEmitidaDto>> EmitirAsync(Guid tenantId, DadosDaCredencial dados, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(dados);

		return OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger,
			"emitir credencial do tenant",
			async token =>
			{
				var c = await client.CreateProductClientAsync(tenantId, Montar(dados), token).ConfigureAwait(false);

				// O tipo é o pedido: o secret já emitido não pode se perder por um tipo ecoado que não se conheça.
				var credencial = Mapear(new ProductClientDto
				{
					ClientId = c.ClientId,
					Name = c.Name,
					Type = dados.Tipo == TipoDeCredencial.Login ? TipoLogin : TipoMaquina,
					Scopes = c.Scopes,
					Roles = c.Roles,
					CreatedAt = c.CreatedAt,
					RedirectUris = c.RedirectUris,
					PostLogoutRedirectUris = c.PostLogoutRedirectUris,
				})!;

				return new CredencialEmitidaDto(credencial, new SecretDaCredencial(c.ClientId, c.ClientSecret));
			},
			IntranetErrors.Credenciais.NaoEncontrada,
			Conflito,
			cancellationToken,
			Recusa);
	}

	/// <inheritdoc />
	public Task<Result> AlterarAsync(Guid tenantId, string clientId, DadosDaCredencial dados, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(dados);

		return OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger,
			"alterar credencial do tenant",
			token => client.UpdateProductClientAsync(tenantId, clientId, Montar(dados), token),
			IntranetErrors.Credenciais.NaoEncontrada,
			Conflito,
			cancellationToken,
			Recusa);
	}

	/// <inheritdoc />
	public Task<Result<SecretDaCredencial>> RotacionarSecretAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger,
			"rotacionar secret da credencial do tenant",
			async token =>
			{
				var novo = await client.RotateProductClientSecretAsync(tenantId, clientId, token).ConfigureAwait(false);

				return new SecretDaCredencial(novo.ClientId ?? clientId, novo.ClientSecret);
			},
			IntranetErrors.Credenciais.NaoEncontrada,
			Conflito,
			cancellationToken,
			Recusa);

	/// <inheritdoc />
	public Task<Result> RevogarAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger,
			"revogar credencial do tenant",
			token => client.DeleteProductClientAsync(tenantId, clientId, token),
			IntranetErrors.Credenciais.NaoEncontrada,
			Conflito,
			cancellationToken,
			Recusa);

	private static ProductClientRequest Montar(DadosDaCredencial dados)
	{
		var login = dados.Tipo == TipoDeCredencial.Login;

		return new ProductClientRequest
		{
			Name = dados.Nome,
			Type = login ? TipoLogin : TipoMaquina,
			Scopes = [.. dados.Escopos],
			Roles = login ? [] : [.. dados.Perfis],
			RedirectUris = login ? [.. dados.RedirectUris] : null,
			PostLogoutRedirectUris = login ? [.. dados.RedirectUrisDeLogout] : null,
		};
	}

	// Tipo desconhecido devolve null: quem chama decide entre omitir (lista) e falhar (leitura).
	private CredencialDto? Mapear(ProductClientDto c)
	{
		TipoDeCredencial mapeado;

		switch (c.Type)
		{
			case null or TipoMaquina:
				mapeado = TipoDeCredencial.Maquina;
				break;
			case TipoLogin:
				mapeado = TipoDeCredencial.Login;
				break;
			default:
				logger.LogWarning("Credencial {ClientId} com tipo desconhecido na plataforma.", c.ClientId);

				return null;
		}

		return new CredencialDto(
			c.ClientId,
			c.Name,
			mapeado,
			[.. c.Scopes ?? []],
			[.. c.Roles ?? []],
			[.. c.RedirectUris ?? []],
			[.. c.PostLogoutRedirectUris ?? []],
			c.CreatedAt);
	}

	private static Error? Conflito(ApiException excecao)
	{
		if (OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Client.NameAlreadyExists"))
		{
			return IntranetErrors.Credenciais.NomeEmUso;
		}

		return OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Client.TenantInactive")
			? IntranetErrors.Credenciais.TenantDesativado
			: null;
	}

	private static Error? Recusa(ApiException excecao)
	{
		if (OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Client.RoleNotFound")
			|| OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Client.RoleNotAssignable"))
		{
			return IntranetErrors.Credenciais.PerfilRecusado;
		}

		if (OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Client.RedirectUrisInvalid")
			|| OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Client.PostLogoutRedirectUrisInvalid"))
		{
			return IntranetErrors.Credenciais.EnderecoRecusado;
		}

		return excecao is ApiException<ProblemDetails> { Result: { } problema }
			? IntranetErrors.Credenciais.Recusada(problema.Detail)
			: null;
	}
}
