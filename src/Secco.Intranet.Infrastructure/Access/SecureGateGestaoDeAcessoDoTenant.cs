using Microsoft.Extensions.Logging;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.SecureGate.Client;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Adapter real de <see cref="IGestaoDeAcessoDoTenant"/>, sobre o client administrativo do
/// SecureGate. Diferente de <see cref="SecureGateGestaoDeAcesso"/>, não lê o tenant da requisição:
/// usa só o recebido. A plataforma confere que o usuário pertence ao tenant da rota.
/// </summary>
/// <param name="client">Client administrativo do SecureGate.</param>
/// <param name="logger">Log de falhas — só operação e status.</param>
public sealed class SecureGateGestaoDeAcessoDoTenant(
	ISecureGateClient client,
	ILogger<SecureGateGestaoDeAcessoDoTenant> logger) : IGestaoDeAcessoDoTenant
{
	private const int TamanhoDaPaginaDeMembros = 100;
	private const string PerfilUsadoPorClients = "SecureGate.Role.UsedByClients";
	private const string NaoEMembro = "SecureGate.Member.NotMember";

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<ProdutoDoCatalogoDto>>> ListarCatalogoDePermissoesAsync(CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync<IReadOnlyList<ProdutoDoCatalogoDto>>(
			logger,
			"listar catalogo de permissoes",
			async token =>
			{
				var produtos = await client.ListPermissionCatalogAsync(token).ConfigureAwait(false);

				return
				[
					.. produtos
						.OrderBy(produto => produto.Product, StringComparer.Ordinal)
						.Select(produto => new ProdutoDoCatalogoDto(
							produto.Product,
							[.. (produto.Permissions ?? []).Select(p => new PermissaoDoCatalogoDto(p.Name, p.Description, p.IsFamily))])),
				];
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<Guid>> IncluirMembroAsync(Guid tenantId, string email, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger,
			"incluir membro no tenant",
			async token => (await client.AddTenantMemberAsync(tenantId, new AddTenantMemberRequest { Email = email }, token)
				.ConfigureAwait(false)).UserId,
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			excecao => OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Member.AlreadyMember") ? IntranetErrors.Acesso.JaEMembro : null,
			cancellationToken,
			recusado: _ => IntranetErrors.Acesso.EmailInvalido);

	/// <inheritdoc />
	public Task<Result> SuspenderVinculoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "suspender vinculo no tenant",
			token => client.SuspendTenantMemberAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, VinculoDoDonoOu, cancellationToken);

	/// <inheritdoc />
	public Task<Result> ReativarVinculoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "reativar vinculo no tenant",
			token => client.SetTenantMemberAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, VinculoDoDonoOu, cancellationToken);

	/// <inheritdoc />
	public Task<Result> RemoverVinculoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "remover vinculo no tenant",
			token => client.RemoveTenantMemberAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, VinculoDoDonoOu, cancellationToken);

	// Vínculo da pessoa com o tenant dono dela não se suspende nem se remove (ADR-0040).
	private static Error? VinculoDoDonoOu(ApiException excecao) =>
		OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, "SecureGate.Member.OwnerMembership") ? IntranetErrors.Acesso.VinculoDoDono : null;

	// "Owner" | "Member" | "Suspended" (ADR-0040). Sem o campo (SecureGate anterior), a pessoa é dona.
	private static VinculoNoTenant Vinculo(string? membership) => membership switch
	{
		"Member" => VinculoNoTenant.Membro,
		"Suspended" => VinculoNoTenant.Suspenso,
		_ => VinculoNoTenant.Dono,
	};

	// Atribuir ou retirar perfil de quem não é membro do tenant: 409 com motivo próprio (ADR-0040).
	private static Error? NaoEMembroOu(ApiException excecao, Error? outroConflito) =>
		OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, NaoEMembro) ? IntranetErrors.Acesso.PessoaNaoEMembro : outroConflito;

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDoTenantDto>>> ListarPerfisAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync<IReadOnlyList<PerfilDoTenantDto>>(
			logger,
			"listar perfis do tenant",
			async token =>
			{
				var perfis = await client.ListRolesAsync(tenantId, token).ConfigureAwait(false);

				return [.. perfis.Select(p => new PerfilDoTenantDto(p.Name, p.Permissions?.Count ?? 0, ClassificacaoDePerfil.EhReservado(p.Name)))];
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<PerfilDoTenantDetalheDto>> ObterPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync(
			logger,
			"obter perfil do tenant",
			async token =>
			{
				var papel = await client.GetRoleAsync(tenantId, nome, token).ConfigureAwait(false);

				return new PerfilDoTenantDetalheDto(
					papel.Name, [.. papel.Permissions ?? []], papel.IsReserved, papel.MemberCount, [.. papel.UnknownPermissions ?? []]);
			},
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(Guid tenantId, string nome, int pagina, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync(
			logger,
			"listar membros do tenant",
			async token =>
			{
				var lida = await client.ListRoleMembersAsync(tenantId, nome, pagina, TamanhoDaPaginaDeMembros, token).ConfigureAwait(false);
				IReadOnlyList<MembroDoPerfilDto> itens =
					[.. (lida.Items ?? []).Select(m => new MembroDoPerfilDto(m.UserId, m.Email, OperacoesDeAcessoNoSecureGate.Situacao(m.Status)))];

				return new PaginaDeMembros(itens, lida.Page, lida.TotalPages, lida.TotalCount);
			},
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDoTenantDto>>> ListarUsuariosAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync<IReadOnlyList<UsuarioDoTenantDto>>(
			logger,
			"listar usuarios do tenant",
			async token =>
			{
				var usuarios = await client.ListUsersAsync(tenantId, token).ConfigureAwait(false);

				return
				[
					.. usuarios.Select(u => new UsuarioDoTenantDto(
						u.Id, u.Email, OperacoesDeAcessoNoSecureGate.Situacao(u.Status), [.. u.Roles ?? []], u.DisplayName, Vinculo(u.Membership))),
				];
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<UsuarioDoTenantDetalheDto>> ObterUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync(
			logger,
			"obter usuario do tenant",
			async token =>
			{
				var u = await client.GetUserAsync(tenantId, usuarioId, token).ConfigureAwait(false);

				return new UsuarioDoTenantDetalheDto(
					u.Id,
					u.Email,
					u.DisplayName,
					OperacoesDeAcessoNoSecureGate.Situacao(u.Status),
					u.LockoutEnd,
					[.. u.Roles ?? []],
					[.. u.ExternalLogins ?? []],
					u.TwoFactorEnabled,
					u.HasPassword,
					u.LocalLoginEnabled,
					Vinculo(u.Membership));
			},
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<FederacaoDto>> ObterFederacaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.LerAsync(
			logger,
			"obter federacao do tenant",
			async token =>
			{
				var federacao = (await client.GetTenantAsync(tenantId, token).ConfigureAwait(false)).Federation;

				return federacao is null
					? new FederacaoDto(null, false, null)
					: new FederacaoDto(federacao.DirectoryId, federacao.IsEnabled, federacao.UpdatedAt);
			},
			null,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "criar perfil do tenant",
			async token =>
				{
					_ = await client.CreateRoleAsync(tenantId, new CreateRoleRequest { Name = nome }, token).ConfigureAwait(false);
				},
			null, _ => IntranetErrors.Acesso.PerfilJaExiste, cancellationToken);

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "excluir perfil do tenant",
			token => client.DeleteRoleAsync(tenantId, nome, token),
			IntranetErrors.Acesso.PerfilNaoEncontrado,
			excecao => OperacoesDeAcessoNoSecureGate.TemCodigo(excecao, PerfilUsadoPorClients)
				? IntranetErrors.Acesso.PerfilUsadoPorClient
				: IntranetErrors.Acesso.PerfilComMembros,
			cancellationToken);

	/// <inheritdoc />
	public Task<Result> DefinirPermissoesDoPerfilAsync(
		Guid tenantId, string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "definir permissoes do tenant",
			token => client.SetRolePermissionsAsync(tenantId, nome, new SetRolePermissionsRequest { Permissions = [.. permissoes] }, token),
			IntranetErrors.Acesso.PerfilNaoEncontrado, null, cancellationToken,
			OperacoesDeAcessoNoSecureGate.PermissaoForaDoCatalogo);

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "atribuir perfil no tenant",
			token => client.AddUserRoleAsync(tenantId, usuarioId, perfil, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, excecao => NaoEMembroOu(excecao, null), cancellationToken);

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "retirar perfil no tenant",
			token => client.RemoveUserRoleAsync(tenantId, usuarioId, perfil, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado,
			excecao => NaoEMembroOu(excecao, IntranetErrors.Acesso.RetiradaRecusada),
			cancellationToken);

	/// <inheritdoc />
	public Task<Result<Guid>> CadastrarUsuarioAsync(Guid tenantId, NovoUsuarioDoTenant novo, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(novo);

		return OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger,
			"cadastrar usuario no tenant",
			async token => (await client.CreateUserAsync(
				tenantId,
				new CreateUserRequest { Email = novo.Email, DisplayName = novo.Nome, LocalLogin = novo.EntraPorSenha, Roles = [.. novo.Perfis] },
				token).ConfigureAwait(false)).Id,
			null,
			_ => IntranetErrors.Acesso.EmailJaCadastrado,
			cancellationToken,
			recusado: _ => IntranetErrors.Acesso.CadastroRecusado);
	}

	/// <inheritdoc />
	public Task<Result> ReenviarConviteAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "reenviar convite no tenant",
			token => client.ResendUserInviteAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, _ => IntranetErrors.Acesso.ConviteNaoCabe, cancellationToken);

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "desativar usuario no tenant",
			token => client.DeactivateUserAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, _ => IntranetErrors.Acesso.DesativacaoRecusada, cancellationToken);

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "reativar usuario no tenant",
			token => client.ActivateUserAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, null, cancellationToken);

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "encerrar sessoes no tenant",
			token => client.RevokeUserSessionsAsync(tenantId, usuarioId, token),
			IntranetErrors.Acesso.UsuarioNaoEncontrado, null, cancellationToken);

	/// <inheritdoc />
	public Task<Result> DefinirFederacaoAsync(Guid tenantId, Guid directoryId, bool habilitada, CancellationToken cancellationToken = default) =>
		OperacoesDeAcessoNoSecureGate.EscreverAsync(
			logger, "definir federacao do tenant",
			token => client.UpsertTenantFederationAsync(
				tenantId, new UpsertTenantFederationRequest { DirectoryId = directoryId, Enabled = habilitada }, token),
			null, null, cancellationToken);
}
