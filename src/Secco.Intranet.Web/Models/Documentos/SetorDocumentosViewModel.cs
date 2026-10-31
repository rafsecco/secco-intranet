using System.ComponentModel.DataAnnotations;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Web.Theming.Contracts;

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

	/// <summary>Item da raiz sob o qual o documento entra; <c>null</c> publica na raiz.</summary>
	public Guid? ParentId { get; set; }
}

/// <summary>Uma opção do campo "Abaixo de" do formulário de publicação.</summary>
/// <param name="Id">Identificador do item da raiz.</param>
/// <param name="Titulo">Título de exibição.</param>
public sealed record OpcaoAbaixoDe(Guid Id, string Titulo);

/// <summary>
/// Aba de documentos de um setor (ADR-0002 regra 2: a view recebe ViewModel, nunca entidade).
/// </summary>
/// <param name="Setor">Setor da página.</param>
/// <param name="Lista">Documentos ativos do setor, em árvore e prontos para o tema desenhar.</param>
/// <param name="PodePublicar">Se o usuário atual administra este setor.</param>
/// <param name="Form">Dados do formulário de publicação.</param>
/// <param name="TamanhoMaximoBytes">Limite aceito por arquivo, para exibir na interface.</param>
/// <param name="Titulo">Nome do item da árvore que abriu a página.</param>
/// <param name="Trilho">Setor e ancestrais do item ("Financeiro › Relatórios"), para o subtítulo.</param>
/// <param name="AbaixoDe">Itens ativos da raiz, documentos e grupos, em ordem: os destinos possíveis de uma publicação.</param>
public sealed record SetorDocumentosViewModel(
	SetorDto Setor,
	ListaDeDocumentosModel Lista,
	bool PodePublicar,
	DocumentoFormViewModel Form,
	long TamanhoMaximoBytes,
	string Titulo,
	string Trilho,
	IReadOnlyList<OpcaoAbaixoDe> AbaixoDe);
