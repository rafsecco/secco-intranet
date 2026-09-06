using Microsoft.AspNetCore.Html;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Models.Mural;

/// <summary>Uma publicação pronta para exibição, com o corpo já renderizado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Corpo">Corpo em HTML, renderizado a partir do Markdown.</param>
/// <param name="Tipo">Natureza.</param>
/// <param name="Prioridade">Urgência.</param>
/// <param name="PublicadoEm">Entrada no ar.</param>
/// <param name="SetorNome">Nome do setor autor.</param>
/// <param name="SetorSlug">Slug do setor autor.</param>
public sealed record PublicacaoViewModel(
	Guid Id,
	string Titulo,
	IHtmlContent Corpo,
	TipoPublicacao Tipo,
	PrioridadePublicacao Prioridade,
	DateTimeOffset PublicadoEm,
	string SetorNome,
	string SetorSlug);

/// <summary>Modelo da página do mural.</summary>
/// <param name="Pagina">Página de publicações.</param>
/// <param name="Filtro">Tipo selecionado; nulo é todos.</param>
public sealed record MuralViewModel(PagedResult<PublicacaoViewModel> Pagina, TipoPublicacao? Filtro);
