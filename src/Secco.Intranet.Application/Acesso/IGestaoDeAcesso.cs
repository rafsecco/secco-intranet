using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>
/// Porta da gestão de acesso do tenant atual: perfis (Roles) e usuários, no SecureGate
/// (ADR-0006: a Intranet não guarda identidade). Toda falha de infraestrutura volta como
/// <see cref="Result"/> — diferente da auditoria, aqui quem opera precisa saber que a ação
/// não aconteceu, então não há falha aberta.
/// </summary>
public interface IGestaoDeAcesso
{
	/// <summary>Lista os perfis do tenant.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<PerfilDto>>> ListarPerfisAsync(CancellationToken cancellationToken = default);

	/// <summary>Detalhe de um perfil.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<PerfilDetalheDto>> ObterPerfilAsync(string nome, CancellationToken cancellationToken = default);

	/// <summary>Uma página de membros de um perfil (100 por página).</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="pagina">Página (1-based).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<PaginaDeMembros>> ListarMembrosAsync(string nome, int pagina, CancellationToken cancellationToken = default);

	/// <summary>Lista todos os usuários do tenant (a API não pagina; busca e paginação são da tela).</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(CancellationToken cancellationToken = default);

	/// <summary>Detalhe de um usuário.</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<UsuarioDetalheDto>> ObterUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Cria um perfil.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> CriarPerfilAsync(string nome, CancellationToken cancellationToken = default);

	/// <summary>Exclui um perfil sem membros.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> ExcluirPerfilAsync(string nome, CancellationToken cancellationToken = default);

	/// <summary>Atribui um perfil a um usuário (idempotente).</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> AtribuirPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default);

	/// <summary>Retira um perfil de um usuário (idempotente).</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> RetirarPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default);

	/// <summary>Desativa a conta de um usuário.</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> DesativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Reativa a conta de um usuário.</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> ReativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Encerra as sessões de um usuário (efeito nos produtos em até um TTL de cache, ADR-0032 da plataforma).</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> EncerrarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken = default);
}
