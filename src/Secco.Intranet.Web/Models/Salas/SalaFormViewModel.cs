using System.ComponentModel.DataAnnotations;

namespace Secco.Intranet.Web.Models.Salas;

/// <summary>Dados do formulário de sala, compartilhado pela criação e pela edição.</summary>
public sealed class SalaFormViewModel
{
	/// <summary>Sala editada; <c>null</c> ao criar.</summary>
	public Guid? Id { get; set; }

	/// <summary>Nome de exibição, único sem diferenciar maiúsculas.</summary>
	[Required(ErrorMessage = "O nome da sala é obrigatório.")]
	public string? Nome { get; set; }

	/// <summary>Onde a sala fica.</summary>
	public string? Localizacao { get; set; }

	/// <summary>Número de lugares.</summary>
	public int? Capacidade { get; set; }

	/// <summary>Recursos disponíveis, em texto livre.</summary>
	public string? Recursos { get; set; }

	/// <summary>E-mail do recurso da sala na agenda externa.</summary>
	public string? EmailNaAgenda { get; set; }

	/// <summary>Posição na listagem.</summary>
	public int Ordem { get; set; }

	/// <summary>Se a sala aceita novas reservas.</summary>
	public bool Ativa { get; set; } = true;
}
