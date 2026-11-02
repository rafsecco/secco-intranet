using System.ComponentModel.DataAnnotations;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain.Documentos;

namespace Secco.Intranet.Web.Models.Documentos;

/// <summary>Dados do formulário "Novo grupo" da tela de organizar.</summary>
public sealed class GrupoFormViewModel
{
	/// <summary>Título do grupo.</summary>
	[Required(ErrorMessage = "O título é obrigatório.")]
	[StringLength(256)]
	public string? Titulo { get; set; }

	/// <summary>Descrição livre.</summary>
	[StringLength(4_096)]
	public string? Descricao { get; set; }
}

/// <summary>Uma linha da tela de organizar, com o que cada botão pode fazer.</summary>
/// <param name="Id">Identificador do item.</param>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="EhGrupo">Se o item é um grupo.</param>
/// <param name="EhFilho">Se o item está recuado abaixo de outro.</param>
/// <param name="PodeSubir">Se há irmão acima para trocar de lugar.</param>
/// <param name="PodeDescer">Se há irmão abaixo para trocar de lugar.</param>
/// <param name="PodeRecuar">Se pode virar filho do irmão de cima.</param>
/// <param name="PodeDesrecuar">Se pode voltar para a raiz.</param>
/// <param name="PodeArquivar">Se não tem filhos ativos.</param>
public sealed record LinhaDeOrganizacao(
	Guid Id,
	string Titulo,
	bool EhGrupo,
	bool EhFilho,
	bool PodeSubir,
	bool PodeDescer,
	bool PodeRecuar,
	bool PodeDesrecuar,
	bool PodeArquivar);

/// <summary>Tela de organizar os documentos de um setor ou biblioteca.</summary>
/// <param name="Dono">Setor ou biblioteca da página.</param>
/// <param name="Titulo">Nome do item da árvore de menu que abriu a página de documentos.</param>
/// <param name="Trilho">Setor e ancestrais do item, para o subtítulo.</param>
/// <param name="UrlDaPagina">Endereço da página de leitura, para o "Voltar".</param>
/// <param name="Linhas">Itens ativos na ordem da árvore: cada item da raiz seguido dos filhos.</param>
/// <param name="Form">Dados do formulário "Novo grupo".</param>
public sealed record OrganizarDocumentosViewModel(
	DonoDaPaginaDeDocumentos Dono,
	string Titulo,
	string Trilho,
	string UrlDaPagina,
	IReadOnlyList<LinhaDeOrganizacao> Linhas,
	GrupoFormViewModel Form)
{
	/// <summary>
	/// Monta as linhas a partir da árvore. Os botões seguem as regras do caso de uso de mover:
	/// subir e descer valem entre irmãos; recuar vale para arquivo da raiz, sem filhos e com irmão
	/// acima; desrecuar vale para filho.
	/// </summary>
	/// <param name="nos">Itens da raiz com os filhos, em ordem.</param>
	public static IReadOnlyList<LinhaDeOrganizacao> MontarLinhas(IReadOnlyList<NoDeDocumentoDto> nos)
	{
		ArgumentNullException.ThrowIfNull(nos);

		var linhas = new List<LinhaDeOrganizacao>();

		for (var indice = 0; indice < nos.Count; indice++)
		{
			var no = nos[indice];
			var documento = no.Documento;
			var semFilhos = no.Filhos.Count == 0;

			linhas.Add(new LinhaDeOrganizacao(
				documento.Id,
				documento.Titulo,
				documento.Tipo == TipoDeDocumento.Grupo,
				false,
				PodeSubir: indice > 0,
				PodeDescer: indice < nos.Count - 1,
				PodeRecuar: documento.Tipo != TipoDeDocumento.Grupo && semFilhos && indice > 0,
				PodeDesrecuar: false,
				PodeArquivar: semFilhos));

			for (var posicao = 0; posicao < no.Filhos.Count; posicao++)
			{
				var filho = no.Filhos[posicao];

				linhas.Add(new LinhaDeOrganizacao(
					filho.Id,
					filho.Titulo,
					false,
					true,
					PodeSubir: posicao > 0,
					PodeDescer: posicao < no.Filhos.Count - 1,
					PodeRecuar: false,
					PodeDesrecuar: true,
					PodeArquivar: true));
			}
		}

		return linhas;
	}
}
