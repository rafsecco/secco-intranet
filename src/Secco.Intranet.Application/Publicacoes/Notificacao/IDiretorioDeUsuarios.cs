namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>
/// Usuário do tenant atual, como o SecureGate o conhece. Não há nome: o <c>UserDto</c> expõe
/// identificador, e-mail e roles, e é por isso que o relatório de notificação conta em vez de
/// nomear.
/// </summary>
/// <param name="Id">Identificador do usuário, usado como destino in-app.</param>
/// <param name="Email">E-mail cadastrado; nulo ou vazio significa que não recebe e-mail.</param>
/// <param name="Roles">Roles do usuário no tenant, de onde sai o vínculo com o setor (ADR-0001).</param>
public sealed record UsuarioDoTenant(Guid Id, string? Email, IReadOnlyList<string> Roles);

/// <summary>Porta de leitura do cadastro de usuários do tenant atual.</summary>
public interface IDiretorioDeUsuarios
{
	/// <summary>Lista os usuários do tenant atual com seus roles, numa única chamada.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(CancellationToken cancellationToken = default);
}
