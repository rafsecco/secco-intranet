using System.ComponentModel.DataAnnotations;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;

namespace Secco.Intranet.Web.Models.Documentos;

/// <summary>Dados do formulário de publicação de documento.</summary>
public sealed class DocumentoFormViewModel
{
	/// <summary>Título de exibição.</summary>
	[Required(ErrorMessage = "O título é obrigatório.")]
	[StringLength(256)]
	public string? Titulo { get; set; }

	/// <summary>Descrição livre.</summary>
	[StringLength(4_096)]
	public string? Descricao { get; set; }

	/// <summary>Quem enxerga o documento.</summary>
	public Visibilidade Visibilidade { get; set; } = Visibilidade.Setor;
}

/// <summary>
/// Aba de documentos de um setor (ADR-0002 regra 2: a view recebe ViewModel, nunca entidade).
/// </summary>
/// <param name="Setor">Setor da página.</param>
/// <param name="Documentos">Documentos ativos do setor.</param>
/// <param name="PodePublicar">Se o usuário atual administra este setor.</param>
/// <param name="Form">Dados do formulário de publicação.</param>
/// <param name="TamanhoMaximoBytes">Limite aceito por arquivo, para exibir na interface.</param>
public sealed record SetorDocumentosViewModel(
	SetorDto Setor,
	IReadOnlyList<DocumentoDto> Documentos,
	bool PodePublicar,
	DocumentoFormViewModel Form,
	long TamanhoMaximoBytes);
