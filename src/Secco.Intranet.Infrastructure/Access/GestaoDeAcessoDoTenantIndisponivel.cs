using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>Adapter no-op para DEV e Testing sem a seção <c>Secco:SecureGate</c>.</summary>
public sealed class GestaoDeAcessoDoTenantIndisponivel : IGestaoDeAcessoDoTenant
{
	private static readonly Error Erro = IntranetErrors.Acesso.NaoConfigurado;

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<ProdutoDoCatalogoDto>>> ListarCatalogoDePermissoesAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<ProdutoDoCatalogoDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDoTenantDto>>> ListarPerfisAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<PerfilDoTenantDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<PerfilDoTenantDetalheDto>> ObterPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<PerfilDoTenantDetalheDto>(Erro));

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(Guid tenantId, string nome, int pagina, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<PaginaDeMembros>(Erro));

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDoTenantDto>>> ListarUsuariosAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<UsuarioDoTenantDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<UsuarioDoTenantDetalheDto>> ObterUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<UsuarioDoTenantDetalheDto>(Erro));

	/// <inheritdoc />
	public Task<Result<FederacaoDto>> ObterFederacaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<FederacaoDto>(Erro));

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> DefinirPermissoesDoPerfilAsync(Guid tenantId, string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result<Guid>> CadastrarUsuarioAsync(Guid tenantId, NovoUsuarioDoTenant novo, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<Guid>(Erro));

	/// <inheritdoc />
	public Task<Result> ReenviarConviteAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> DefinirFederacaoAsync(Guid tenantId, Guid directoryId, bool habilitada, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result<Guid>> IncluirMembroAsync(Guid tenantId, string email, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<Guid>(Erro));

	/// <inheritdoc />
	public Task<Result> SuspenderVinculoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> ReativarVinculoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> RemoverVinculoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));
}
