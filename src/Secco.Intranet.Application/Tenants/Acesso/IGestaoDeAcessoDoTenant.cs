using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>
/// Porta da gestão de acesso de um tenant administrado (subsistema 2 da Área de tenants). O tenant
/// é sempre explícito: quem chama já passou pelo cadastro (<c>[TenantAdministrado]</c>). Toda
/// falha volta como <see cref="Result"/>, sem falha aberta.
/// </summary>
public interface IGestaoDeAcessoDoTenant
{
	/// <summary>
	/// Catálogo de permissões da instalação, por produto (ADR-0039 da plataforma). Não é por tenant:
	/// é o que os produtos publicaram, de onde a tela tira as caixas de cada perfil.
	/// </summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<ProdutoDoCatalogoDto>>> ListarCatalogoDePermissoesAsync(CancellationToken cancellationToken = default);

	/// <summary>Lista os perfis do tenant.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<PerfilDoTenantDto>>> ListarPerfisAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Obtém o detalhe de um perfil.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<PerfilDoTenantDetalheDto>> ObterPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default);

	/// <summary>Lista uma página dos membros de um perfil.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="pagina">Número da página, a partir de 1.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<PaginaDeMembros>> ListarMembrosAsync(Guid tenantId, string nome, int pagina, CancellationToken cancellationToken = default);

	/// <summary>Lista as pessoas do tenant.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Obtém o detalhe de uma pessoa do tenant.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="usuarioId">Id da pessoa no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<UsuarioDoTenantDetalheDto>> ObterUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Obtém a federação com o Entra ID do tenant.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<FederacaoDto>> ObterFederacaoAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Cria um perfil sem permissões.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="nome">Nome do novo perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> CriarPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default);

	/// <summary>Exclui um perfil.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> ExcluirPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default);

	/// <summary>Substitui as permissões de um perfil.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="permissoes">Permissões que o perfil passa a ter.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> DefinirPermissoesDoPerfilAsync(Guid tenantId, string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default);

	/// <summary>Atribui um perfil a uma pessoa.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="usuarioId">Id da pessoa no SecureGate.</param>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> AtribuirPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default);

	/// <summary>Retira um perfil de uma pessoa.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="usuarioId">Id da pessoa no SecureGate.</param>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> RetirarPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default);

	/// <summary>Cadastra uma pessoa no tenant e devolve o seu id.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="novo">Dados da pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<Guid>> CadastrarUsuarioAsync(Guid tenantId, NovoUsuarioDoTenant novo, CancellationToken cancellationToken = default);

	/// <summary>Reenvia o convite para definir a senha.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="usuarioId">Id da pessoa no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> ReenviarConviteAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Desativa a conta de uma pessoa.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="usuarioId">Id da pessoa no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> DesativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Reativa a conta de uma pessoa.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="usuarioId">Id da pessoa no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> ReativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Encerra todas as sessões de uma pessoa.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="usuarioId">Id da pessoa no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> EncerrarSessoesAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default);

	/// <summary>Define a federação com o Entra ID do tenant.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="directoryId">Directory id do Entra ID.</param>
	/// <param name="habilitada">Se a federação fica habilitada.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> DefinirFederacaoAsync(Guid tenantId, Guid directoryId, bool habilitada, CancellationToken cancellationToken = default);
}
