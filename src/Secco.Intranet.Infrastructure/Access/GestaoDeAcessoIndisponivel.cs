using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Access;

/// <summary>
/// Adapter no-op de <see cref="IGestaoDeAcesso"/> para DEV e Testing (seção
/// <c>Secco:SecureGate</c> ausente). Diferente dos outros no-ops, que fingem sucesso, este
/// responde "não configurado": fingir que um perfil foi atribuído seria mentir para quem administra.
/// </summary>
public sealed class GestaoDeAcessoIndisponivel : IGestaoDeAcesso
{
	private static readonly Error Erro = IntranetErrors.Acesso.NaoConfigurado;

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDto>>> ListarPerfisAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<PerfilDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<PerfilDetalheDto>> ObterPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<PerfilDetalheDto>(Erro));

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(string nome, int pagina, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<PaginaDeMembros>(Erro));

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<UsuarioDto>>(Erro));

	/// <inheritdoc />
	public Task<Result<UsuarioDetalheDto>> ObterUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<UsuarioDetalheDto>(Erro));

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure(Erro));
}
