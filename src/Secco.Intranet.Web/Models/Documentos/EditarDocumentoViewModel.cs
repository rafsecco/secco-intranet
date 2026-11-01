using System.ComponentModel.DataAnnotations;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;

namespace Secco.Intranet.Web.Models.Documentos;

/// <summary>Dados do formulário de edição de documento.</summary>
public sealed class EditarDocumentoFormViewModel
{
	/// <summary>Título de exibição.</summary>
	[Required(ErrorMessage = "O título é obrigatório.")]
	[StringLength(256)]
	public string? Titulo { get; set; }

	/// <summary>Descrição livre.</summary>
	[StringLength(4_096)]
	public string? Descricao { get; set; }

	/// <summary>Quem enxerga o documento; ignorada em grupo.</summary>
	public Visibilidade Visibilidade { get; set; } = Visibilidade.Setor;
}

/// <summary>Tela de edição de um documento ou grupo (ADR-0002 regra 2: a view recebe ViewModel, nunca entidade).</summary>
/// <param name="Setor">Setor da página.</param>
/// <param name="Id">Identificador do documento.</param>
/// <param name="EhGrupo">Se o item é um grupo, sem arquivo nem visibilidade.</param>
/// <param name="NomeArquivoAtual">Nome do arquivo atual; nulo no grupo.</param>
/// <param name="Form">Dados do formulário.</param>
/// <param name="UrlDeVolta">Endereço da tela de organizar, para "Cancelar".</param>
public sealed record EditarDocumentoViewModel(
	SetorDto Setor,
	Guid Id,
	bool EhGrupo,
	string? NomeArquivoAtual,
	EditarDocumentoFormViewModel Form,
	string UrlDeVolta);
