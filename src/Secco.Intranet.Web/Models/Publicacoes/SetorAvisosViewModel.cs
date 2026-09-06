using System.ComponentModel.DataAnnotations;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;

namespace Secco.Intranet.Web.Models.Publicacoes;

/// <summary>Dados do formulário de publicação.</summary>
public sealed class PublicacaoFormViewModel
{
	/// <summary>Identificador; vazio significa publicação nova.</summary>
	public Guid Id { get; set; }

	/// <summary>Título.</summary>
	[Required(ErrorMessage = "O título é obrigatório.")]
	[StringLength(256)]
	public string? Titulo { get; set; }

	/// <summary>Corpo em Markdown.</summary>
	[Required(ErrorMessage = "O corpo é obrigatório.")]
	[StringLength(4_000)]
	public string? Corpo { get; set; }

	/// <summary>Natureza.</summary>
	public TipoPublicacao Tipo { get; set; } = TipoPublicacao.Aviso;

	/// <summary>Quem enxerga.</summary>
	public Visibilidade Visibilidade { get; set; } = Visibilidade.Empresa;

	/// <summary>Urgência.</summary>
	public PrioridadePublicacao Prioridade { get; set; } = PrioridadePublicacao.Normal;

	/// <summary>
	/// Entrada no ar. O formulário usa <c>datetime-local</c>, que não carrega offset: o valor
	/// é interpretado no fuso do servidor (limitação registrada no spec).
	/// </summary>
	[DataType(DataType.DateTime)]
	public DateTime PublicadoEm { get; set; } = DateTime.Now;

	/// <summary>Expiração; vazio não expira.</summary>
	[DataType(DataType.DateTime)]
	public DateTime? ExpiraEm { get; set; }
}

/// <summary>Modelo da aba de avisos do setor.</summary>
/// <param name="Setor">Setor da página.</param>
/// <param name="Publicacoes">Publicações do setor, inclusive fora do ar.</param>
/// <param name="PodePublicar">Se o usuário administra este setor.</param>
/// <param name="Form">Dados do formulário.</param>
/// <param name="Agora">Instante usado para calcular a etiqueta de cada publicação.</param>
public sealed record SetorAvisosViewModel(
	SetorDto Setor,
	IReadOnlyList<PublicacaoDto> Publicacoes,
	bool PodePublicar,
	PublicacaoFormViewModel Form,
	DateTimeOffset Agora);
