using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Models.Tenants;

/// <summary>Aba aberta na tela de acesso do tenant administrado.</summary>
public enum AbaDoAcessoDoTenant
{
	/// <summary>Perfis do tenant.</summary>
	Perfis,

	/// <summary>Pessoas do tenant.</summary>
	Usuarios,

	/// <summary>Federação com o Entra ID.</summary>
	Federacao,
}

/// <summary>Tela principal do acesso do tenant: só o conteúdo da aba aberta vem preenchido.</summary>
/// <param name="Tenant">Cabeçalho do tenant.</param>
/// <param name="Aba">Aba aberta.</param>
/// <param name="Perfis">Perfis, na aba Perfis.</param>
/// <param name="Usuarios">Pessoas paginadas, na aba Usuários.</param>
/// <param name="Busca">Busca por e-mail aplicada.</param>
/// <param name="Federacao">Federação, na aba Federação.</param>
public sealed record TenantsAcessoIndexViewModel(
	CabecalhoDoTenantDto Tenant, AbaDoAcessoDoTenant Aba,
	IReadOnlyList<PerfilDoTenantDto>? Perfis, PagedResult<UsuarioDoTenantDto>? Usuarios, string? Busca, FederacaoDto? Federacao);

/// <summary>Tela de um perfil do tenant.</summary>
/// <param name="Tenant">Cabeçalho do tenant.</param>
/// <param name="Tela">Detalhe do perfil, membros e candidatos.</param>
public sealed record TenantsAcessoPerfilViewModel(CabecalhoDoTenantDto Tenant, PerfilDoTenantTelaDto Tela);

/// <summary>Tela de uma pessoa do tenant.</summary>
/// <param name="Tenant">Cabeçalho do tenant.</param>
/// <param name="Tela">Detalhe da pessoa e perfis atribuíveis.</param>
public sealed record TenantsAcessoUsuarioViewModel(CabecalhoDoTenantDto Tenant, UsuarioDoTenantTelaDto Tela);

/// <summary>Formulário de cadastro de pessoa no tenant.</summary>
public sealed class TenantsAcessoNovoViewModel
{
	/// <summary>Cabeçalho do tenant, preenchido pelo controller.</summary>
	public CabecalhoDoTenantDto? Tenant { get; set; }

	/// <summary>Perfis que podem ser atribuídos no cadastro.</summary>
	public IReadOnlyList<string> PerfisDisponiveis { get; set; } = [];

	/// <summary>Se o tenant tem a federação com o Entra ID habilitada.</summary>
	public bool FederacaoHabilitada { get; set; }

	/// <summary>E-mail da pessoa.</summary>
	public string? Email { get; set; }

	/// <summary>Nome da pessoa.</summary>
	public string? Nome { get; set; }

	/// <summary>Perfis escolhidos.</summary>
	public List<string> Perfis { get; set; } = [];

	/// <summary>Se a pessoa entra por senha (convite) em vez do Entra ID.</summary>
	public bool EntraPorSenha { get; set; } = true;

	/// <summary>Mensagem de erro do último envio.</summary>
	public string? Erro { get; set; }
}
