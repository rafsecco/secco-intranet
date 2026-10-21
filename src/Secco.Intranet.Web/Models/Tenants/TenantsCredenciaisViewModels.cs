using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Application.Tenants.Credenciais;

namespace Secco.Intranet.Web.Models.Tenants;

/// <summary>
/// O que o admin preenche no formulário de uma credencial. Só os campos do formulário: tenant, client id
/// e tipo da credencial existente nunca vêm do corpo (o tenant e o client id são da rota).
/// </summary>
public sealed class EnvioDaCredencial
{
	/// <summary>Tipo escolhido na emissão (<c>maquina</c> ou <c>login</c>); na alteração vale o da credencial.</summary>
	public string? Tipo { get; set; }

	/// <summary>Nome de exibição.</summary>
	public string? Nome { get; set; }

	/// <summary>Produtos marcados.</summary>
	public List<string> Escopos { get; set; } = [];

	/// <summary>Perfis marcados (só de máquina).</summary>
	public List<string> Perfis { get; set; } = [];

	/// <summary>Endereços de retorno do login, um por linha.</summary>
	public string? RedirectUris { get; set; }

	/// <summary>Endereços de retorno do logout, um por linha.</summary>
	public string? RedirectUrisDeLogout { get; set; }

	/// <summary>Converte para o formulário do caso de uso.</summary>
	public FormularioDaCredencial ParaFormulario() => new(Nome, Escopos, Perfis, RedirectUris, RedirectUrisDeLogout);
}

/// <summary>Lista das credenciais do tenant.</summary>
/// <param name="Tenant">Cabeçalho do tenant.</param>
/// <param name="Credenciais">Credenciais, sem secret.</param>
public sealed record TenantsCredenciaisIndexViewModel(CabecalhoDoTenantDto Tenant, IReadOnlyList<CredencialDto> Credenciais);

/// <summary>Formulário de emissão (e o erro do último envio).</summary>
/// <param name="Tenant">Cabeçalho do tenant.</param>
/// <param name="Opcoes">Escopos, SecureGate e perfis oferecidos.</param>
/// <param name="Tipo">Máquina ou login.</param>
/// <param name="Campos">Campos preenchidos (vazios na primeira abertura).</param>
/// <param name="Erro">Mensagem do último envio, se falhou.</param>
public sealed record TenantsCredenciaisNovaViewModel(
	CabecalhoDoTenantDto Tenant, OpcoesDaCredencial Opcoes, TipoDeCredencial Tipo, EnvioDaCredencial Campos, string? Erro);

/// <summary>Detalhe de uma credencial, com o formulário de alteração.</summary>
/// <param name="Tenant">Cabeçalho do tenant.</param>
/// <param name="Credencial">Credencial atual, sem secret.</param>
/// <param name="Opcoes">Escopos, SecureGate e perfis oferecidos.</param>
/// <param name="Campos">Campos do formulário, com os valores atuais.</param>
public sealed record TenantsCredenciaisDetalheViewModel(
	CabecalhoDoTenantDto Tenant, CredencialDto Credencial, OpcoesDaCredencial Opcoes, EnvioDaCredencial Campos);

/// <summary>Tela única do secret, a resposta do POST que o emitiu ou rotacionou.</summary>
/// <param name="Tenant">Cabeçalho do tenant.</param>
/// <param name="Secret">Client id e secret, mostrados uma vez.</param>
/// <param name="Authority">Authority do SecureGate (vazia se não configurada).</param>
/// <param name="Rotacao">Se o secret veio de uma rotação, e não de uma emissão.</param>
public sealed record TenantsCredenciaisSecretViewModel(CabecalhoDoTenantDto Tenant, SecretDaCredencial Secret, string Authority, bool Rotacao);

/// <summary>Campos do formulário parcial de credencial.</summary>
/// <param name="Opcoes">Escopos, SecureGate e perfis oferecidos.</param>
/// <param name="Tipo">Máquina ou login.</param>
/// <param name="Campos">Valores dos campos.</param>
/// <param name="Acao">Destino do POST.</param>
/// <param name="Botao">Texto do botão de envio.</param>
/// <param name="TipoNoCorpo">Se o tipo vai num campo oculto (emissão).</param>
/// <param name="PerfisAtuais">Perfis já atribuídos, para não sumirem da tela se deixaram de ser oferecidos.</param>
public sealed record FormularioDaCredencialViewModel(
	OpcoesDaCredencial Opcoes, TipoDeCredencial Tipo, EnvioDaCredencial Campos, string Acao, string Botao, bool TipoNoCorpo,
	IReadOnlyList<string> PerfisAtuais);
