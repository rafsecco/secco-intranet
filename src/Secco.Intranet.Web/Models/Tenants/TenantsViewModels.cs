using Secco.Intranet.Application.Tenants;

namespace Secco.Intranet.Web.Models.Tenants;

/// <summary>Tela que explica por que a área de tenants exige segundo fator.</summary>
/// <param name="UrlDoCadastro">Página de cadastro do 2FA no SecureGate, quando a URL é conhecida.</param>
public sealed record SegundoFatorViewModel(string? UrlDoCadastro);

/// <summary>Página que explica por que a área não abre (SecureGate ausente ou fora do ar).</summary>
/// <param name="Mensagem">Mensagem do erro, sem detalhe interno.</param>
public sealed record TenantsIndisponivelViewModel(string Mensagem);

/// <summary>Formulário de criação.</summary>
public sealed class NovoTenantViewModel
{
	/// <summary>Sistema que o tenant representa.</summary>
	public string? Sistema { get; set; }

	/// <summary>Responsável.</summary>
	public string? Responsavel { get; set; }

	/// <summary>Nome do tenant.</summary>
	public string? Nome { get; set; }

	/// <summary>Slug do tenant.</summary>
	public string? Slug { get; set; }

	/// <summary>Erro da última tentativa.</summary>
	public string? Erro { get; set; }
}

/// <summary>Formulário de adoção.</summary>
public sealed class AdotarTenantViewModel
{
	/// <summary>Tenants adotáveis.</summary>
	public IReadOnlyList<TenantDaPlataformaDto> Adotaveis { get; set; } = [];

	/// <summary>Tenant escolhido.</summary>
	public Guid? TenantId { get; set; }

	/// <summary>Sistema que o tenant representa.</summary>
	public string? Sistema { get; set; }

	/// <summary>Responsável.</summary>
	public string? Responsavel { get; set; }

	/// <summary>Erro da última tentativa.</summary>
	public string? Erro { get; set; }
}

/// <summary>Exibição única do script de provisionamento.</summary>
/// <param name="TenantId">Tenant.</param>
/// <param name="Sistema">Sistema.</param>
/// <param name="Recurso">Nome do recurso.</param>
/// <param name="Script">SQL com a senha gerada.</param>
public sealed record ScriptDeProvisionamentoViewModel(Guid TenantId, string Sistema, string Recurso, string Script);
