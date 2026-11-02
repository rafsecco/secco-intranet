using System.ComponentModel.DataAnnotations;
using Secco.Intranet.Application.Documentos;
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
/// Página de documentos de um setor ou de uma biblioteca (ADR-0002 regra 2: a view recebe ViewModel, nunca entidade).
/// </summary>
/// <param name="Dono">Setor ou biblioteca da página.</param>
/// <param name="Lista">Documentos ativos do dono, em árvore e prontos para o tema desenhar.</param>
/// <param name="PodePublicar">Se o usuário atual escreve neste dono.</param>
/// <param name="Form">Dados do formulário de publicação.</param>
/// <param name="TamanhoMaximoBytes">Limite aceito por arquivo, para exibir na interface.</param>
/// <param name="Titulo">Nome do item da árvore que abriu a página (no setor) ou da biblioteca.</param>
/// <param name="Trilho">Setor e ancestrais do item ("Financeiro › Relatórios") ou o nome da biblioteca, para o subtítulo.</param>
/// <param name="AbaixoDe">Itens ativos da raiz, documentos e grupos, em ordem: os destinos possíveis de uma publicação.</param>
public sealed record DocumentosViewModel(
	DonoDaPaginaDeDocumentos Dono,
	ListaDeDocumentosModel Lista,
	bool PodePublicar,
	DocumentoFormViewModel Form,
	long TamanhoMaximoBytes,
	string Titulo,
	string Trilho,
	IReadOnlyList<OpcaoAbaixoDe> AbaixoDe)
{
	/// <summary>Monta a página a partir da árvore devolvida pela listagem.</summary>
	/// <param name="dono">Setor ou biblioteca da página.</param>
	/// <param name="nos">Itens da raiz com os filhos, em ordem.</param>
	/// <param name="urlDeDownload">Endereço do download de um documento, dado o identificador.</param>
	/// <param name="podePublicar">Se o usuário atual escreve neste dono.</param>
	/// <param name="form">Dados do formulário de publicação.</param>
	/// <param name="tamanhoMaximoBytes">Limite aceito por arquivo.</param>
	/// <param name="titulo">Título da página.</param>
	/// <param name="trilho">Subtítulo da página.</param>
	public static DocumentosViewModel Montar(
		DonoDaPaginaDeDocumentos dono,
		IReadOnlyList<NoDeDocumentoDto> nos,
		Func<Guid, string> urlDeDownload,
		bool podePublicar,
		DocumentoFormViewModel form,
		long tamanhoMaximoBytes,
		string titulo,
		string trilho)
	{
		ArgumentNullException.ThrowIfNull(nos);

		return new DocumentosViewModel(
			dono,
			ListaDeDocumentosFactory.Montar(nos, urlDeDownload),
			podePublicar,
			form,
			tamanhoMaximoBytes,
			titulo,
			trilho,
			[.. nos.Select(no => new OpcaoAbaixoDe(no.Documento.Id, no.Documento.Titulo))]);
	}
}
