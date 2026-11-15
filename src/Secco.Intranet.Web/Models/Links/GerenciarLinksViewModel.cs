using Secco.Intranet.Application.Links;
using Secco.Intranet.Application.Setores;

namespace Secco.Intranet.Web.Models.Links;

/// <summary>Dados do formulário de um link (ou só o nome, quando o item é um grupo).</summary>
public sealed class LinkFormViewModel
{
	/// <summary>Rótulo do link ou do grupo; o tamanho máximo é conferido pelo caso de uso.</summary>
	public string? Nome { get; set; }

	/// <summary>Endereço de destino; o formato é conferido pelo caso de uso.</summary>
	public string? Rota { get; set; }

	/// <summary>
	/// Abre em nova página. Sem valor inicial de propósito: caixa desmarcada não posta nada e o
	/// binding tem de dar <c>false</c>; quem marca o padrão do formulário novo é o GET.
	/// </summary>
	public bool NovaAba { get; set; }

	/// <summary>Grupo do link; <c>null</c> deixa o link solto.</summary>
	public Guid? GrupoId { get; set; }

	/// <summary>Formulário preenchido com os dados de um link existente.</summary>
	/// <param name="link">Link a editar.</param>
	public static LinkFormViewModel De(LinkDto link)
	{
		ArgumentNullException.ThrowIfNull(link);

		return new LinkFormViewModel { Nome = link.Nome, Rota = link.Rota, NovaAba = link.NovaAba, GrupoId = link.GrupoId };
	}
}

/// <summary>Tela de gestão dos links de um setor.</summary>
/// <param name="Setor">Setor da página.</param>
/// <param name="Links">Entradas na ordem do menu.</param>
/// <param name="Trilho">Setor e ancestrais do nó Links, para o subtítulo.</param>
public sealed record GerenciarLinksViewModel(SetorDto Setor, LinksDoSetorDto Links, string Trilho);

/// <summary>Página do formulário de link ou de grupo.</summary>
/// <param name="Setor">Setor da página.</param>
/// <param name="Form">Dados do formulário.</param>
/// <param name="Grupos">Grupos para o seletor.</param>
/// <param name="Id">Item em edição; <c>null</c> no cadastro.</param>
/// <param name="EhGrupo">Se o item em edição é um grupo (só o nome).</param>
/// <param name="Trilho">Setor e ancestrais do nó Links, para o subtítulo.</param>
public sealed record LinkFormPaginaViewModel(
	SetorDto Setor,
	LinkFormViewModel Form,
	IReadOnlyList<GrupoDeLinksDto> Grupos,
	Guid? Id,
	bool EhGrupo,
	string Trilho);

/// <summary>Botões de uma linha da tela de gestão: subir, descer, editar e excluir.</summary>
/// <param name="Slug">Slug do setor.</param>
/// <param name="Id">Item da linha.</param>
/// <param name="Nome">Nome do item, para o texto dos leitores de tela.</param>
public sealed record LinkAcoesModel(string Slug, Guid Id, string Nome);
