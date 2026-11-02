using System.ComponentModel.DataAnnotations;

namespace Secco.Intranet.Web.Models.Bibliotecas;

/// <summary>
/// Dados dos formulários de criação e edição de biblioteca (ADR-0002 regra 2). Os limites são só
/// validação antecipada de UI: a autoritativa mora nos handlers. Na edição o slug é só exibido.
/// </summary>
public sealed class BibliotecaFormViewModel
{
	/// <summary>Identificador, preenchido na edição.</summary>
	public Guid Id { get; set; }

	/// <summary>Nome de exibição.</summary>
	[Required(ErrorMessage = "O nome é obrigatório.")]
	[StringLength(256)]
	public string? Nome { get; set; }

	/// <summary>Identificador curto da URL e das permissões; imutável depois de criada.</summary>
	[StringLength(64)]
	public string? Slug { get; set; }

	/// <summary>Classe do Bootstrap Icons; em branco usa o padrão.</summary>
	[StringLength(128)]
	public string? Icone { get; set; }

	/// <summary>Na criação: se todo membro do tenant lê a biblioteca. Marcado por padrão.</summary>
	public bool TodosLeem { get; set; } = true;

	/// <summary>Na edição: situação da biblioteca.</summary>
	public bool Ativo { get; set; } = true;
}
