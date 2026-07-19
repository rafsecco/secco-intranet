using System.ComponentModel.DataAnnotations;

namespace Secco.Intranet.Web.Models;

/// <summary>
/// Dados do formulário de criação de setor (ADR-0002 regra 2: a view nunca recebe
/// entidade de domínio). Os limites aqui são só uma validação antecipada de UI — a
/// validação autoritativa dos limites configuráveis (<c>Intranet:Limits</c>) acontece no
/// <c>CreateSetorHandler</c>, via <c>Result&lt;T&gt;</c>.
/// </summary>
public sealed class SetorFormViewModel
{
	/// <summary>Nome de exibição do setor.</summary>
	[Required(ErrorMessage = "O nome é obrigatório.")]
	[StringLength(256)]
	public string? Nome { get; set; }

	/// <summary>Identificador curto do setor (base das Roles no SecureGate).</summary>
	[Required(ErrorMessage = "O slug é obrigatório.")]
	[StringLength(64)]
	public string? Slug { get; set; }

	/// <summary>Se o setor nasce como fixo do sistema (não pode ser desativado/excluído).</summary>
	public bool Fixo { get; set; }
}
