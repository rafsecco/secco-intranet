using Secco.Intranet.Domain.Diretorio;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Porta de persistência dos perfis de colaborador — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IPerfilColaboradorRepository
{
	/// <summary>Busca o perfil de um usuário, <b>desrastreado</b> — caminho de leitura.</summary>
	/// <param name="usuarioId">Id do usuário no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PerfilColaborador?> GetByUsuarioIdAsync(Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Busca o perfil <b>rastreado</b>, para alteração. Alterar o resultado de
	/// <see cref="GetByUsuarioIdAsync"/> e chamar <see cref="SaveChangesAsync"/> não gravaria nada.
	/// </summary>
	/// <param name="usuarioId">Id do usuário no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PerfilColaborador?> GetParaEdicaoAsync(Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Todos os perfis do tenant, desrastreados. O diretório junta isto em memória com os usuários.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<PerfilColaborador>> ListarTodosAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Persiste um perfil novo. Devolve <c>false</c> — sem lançar — se já existe um perfil desse
	/// usuário (dois primeiros salvamentos simultâneos): o chamador recarrega e reaplica.
	/// </summary>
	/// <param name="perfil">Perfil novo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> TentarAdicionarAsync(PerfilColaborador perfil, CancellationToken cancellationToken = default);

	/// <summary>Grava alterações pendentes de um perfil já rastreado.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
