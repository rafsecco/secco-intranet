using System.ComponentModel.DataAnnotations;

namespace Secco.Intranet.Web.Models.Inventario;

/// <summary>Dados do formulário de criação/edição de item de inventário (ADR-0002 regra 2).</summary>
public sealed class ItemInventarioFormViewModel
{
	/// <summary>Identificador — vazio na criação.</summary>
	public Guid Id { get; set; }

	/// <summary>Nome de exibição.</summary>
	[Required(ErrorMessage = "O nome é obrigatório.")]
	[StringLength(256)]
	public string? Nome { get; set; }

	/// <summary>Descrição livre.</summary>
	[StringLength(4_096)]
	public string? Descricao { get; set; }

	/// <summary>Categoria livre.</summary>
	[StringLength(128)]
	public string? Categoria { get; set; }

	/// <summary>Código de patrimônio, sem unicidade.</summary>
	[StringLength(128)]
	public string? CodigoPatrimonio { get; set; }

	/// <summary>Setor onde o item está — informativo.</summary>
	public Guid? SetorId { get; set; }
}
