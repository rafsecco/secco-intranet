using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Secco.Intranet.Application.Salas;

namespace Secco.Intranet.Web.Models.Salas;

/// <summary>Dados do formulário de reserva, compartilhado pela criação e pela alteração.</summary>
public sealed class ReservaFormViewModel
{
	/// <summary>Reserva alterada; <c>null</c> ao criar.</summary>
	public Guid? Id { get; set; }

	/// <summary>Sala escolhida.</summary>
	[Required(ErrorMessage = "Escolha a sala.")]
	public Guid? SalaId { get; set; }

	/// <summary>Dia, no fuso configurado.</summary>
	[Required(ErrorMessage = "Informe o dia.")]
	public DateOnly? Data { get; set; }

	/// <summary>Hora de início, de 15 em 15 minutos.</summary>
	[Required(ErrorMessage = "Informe a hora de início.")]
	public TimeOnly? Inicio { get; set; }

	/// <summary>Hora de fim, de 15 em 15 minutos.</summary>
	[Required(ErrorMessage = "Informe a hora de fim.")]
	public TimeOnly? Fim { get; set; }

	/// <summary>Título da reserva.</summary>
	public string? Titulo { get; set; }

	/// <summary>Descrição opcional.</summary>
	public string? Descricao { get; set; }

	/// <summary>Convidados marcados.</summary>
	public List<Guid> Convidados { get; set; } = [];

	/// <summary>Salas ativas que o formulário oferece; preenchidas pelo controller, nunca pelo formulário.</summary>
	[BindNever]
	public IReadOnlyList<SalaDto> Salas { get; set; } = [];

	/// <summary>Pessoas que podem ser convidadas; preenchidas pelo controller, nunca pelo formulário.</summary>
	[BindNever]
	public IReadOnlyList<PessoaDaReservaDto> Pessoas { get; set; } = [];
}

/// <summary>Detalhe de uma reserva com o que a tela decide só por quem olha.</summary>
/// <param name="Reserva">A reserva.</param>
/// <param name="VeEspelhamento">Se quem olha é dono ou administrador (e portanto vê o estado do espelhamento).</param>
/// <param name="PodeTentarDeNovo">Se o espelhamento falhou e quem olha é administrador (e portanto vê o botão de tentar de novo).</param>
public sealed record ReservaDetalheViewModel(ReservaDto Reserva, bool VeEspelhamento, bool PodeTentarDeNovo);
