using System.ComponentModel.DataAnnotations;

namespace Secco.Intranet.Web.Models;

/// <summary>
/// Dados do formulário de edição de setor (ADR-0002 regra 2: a view nunca recebe entidade de
/// domínio). O slug aparece na tela mas não é editável — é a base das Roles do SecureGate
/// (ADR-0001), e trocá-lo romperia o vínculo de todos os usuários do setor.
/// </summary>
public sealed class SetorEditViewModel
{
	/// <summary>Identificador do setor.</summary>
	public Guid Id { get; set; }

	/// <summary>Slug, exibido apenas para conferência.</summary>
	public string? Slug { get; set; }

	/// <summary>Se o setor é fixo do sistema — nesse caso não pode ser desativado.</summary>
	public bool Fixo { get; set; }

	/// <summary>Nome de exibição do setor.</summary>
	[Required(ErrorMessage = "O nome é obrigatório.")]
	[StringLength(256)]
	public string? Nome { get; set; }

	/// <summary>
	/// Classe do Bootstrap Icons exibida no menu, como <c>bi-cash-coin</c>. Em branco usa o
	/// ícone padrão.
	/// </summary>
	[StringLength(64)]
	[RegularExpression(
		"^bi-[a-z0-9-]+$",
		ErrorMessage = "O ícone precisa ser uma classe do Bootstrap Icons, como 'bi-cash-coin'.")]
	public string? Icone { get; set; }

	/// <summary>Situação do setor.</summary>
	public bool Ativo { get; set; } = true;
}
