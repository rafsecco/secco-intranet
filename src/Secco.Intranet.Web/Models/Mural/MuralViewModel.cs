using Microsoft.AspNetCore.Html;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Models.Mural;

/// <summary>Uma publicação pronta para exibição, com o corpo já renderizado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Corpo">Corpo em HTML, renderizado a partir do Markdown; nulo quando o texto está vazio.</param>
/// <param name="Tipo">Natureza.</param>
/// <param name="Prioridade">Urgência.</param>
/// <param name="PublicadoEm">Entrada no ar.</param>
/// <param name="SetorNome">Nome do setor autor.</param>
/// <param name="SetorSlug">Slug do setor autor.</param>
/// <param name="ImagemUrl">Endereço da imagem; nulo quando a publicação não tem.</param>
/// <param name="ImagemDescricao">Descrição da imagem para leitor de tela.</param>
public sealed record PublicacaoViewModel(
	Guid Id,
	string Titulo,
	IHtmlContent? Corpo,
	TipoPublicacao Tipo,
	PrioridadePublicacao Prioridade,
	DateTimeOffset PublicadoEm,
	string SetorNome,
	string SetorSlug,
	string? ImagemUrl = null,
	string? ImagemDescricao = null);

/// <summary>Modelo da página do mural.</summary>
/// <param name="Pagina">Página de publicações.</param>
/// <param name="Filtro">Tipo selecionado; nulo é todos.</param>
public sealed record MuralViewModel(PagedResult<PublicacaoViewModel> Pagina, TipoPublicacao? Filtro);

/// <summary>Modelo da página de uma publicação.</summary>
/// <param name="Publicacao">A publicação, com o corpo já renderizado.</param>
/// <param name="Entrega">
/// Resumo da entrega dos avisos, só para quem administra o setor. Nulo quando o usuário não
/// administra, quando não há Hub configurado, ou quando a consulta falhou.
/// </param>
public sealed record PublicacaoDetalheViewModel(
	PublicacaoViewModel Publicacao,
	ResumoDeEntrega? Entrega);
