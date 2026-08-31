using Microsoft.AspNetCore.Html;

namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>
/// Card de conteúdo — o padrão que as listagens da Intranet reaproveitam. O filete colorido
/// na borda esquerda vem do <see cref="SetorHue"/> do <paramref name="SetorSlug"/>.
/// </summary>
/// <param name="Titulo">Título do card.</param>
/// <param name="Meta">Metadado curto à direita do título (data, tamanho, contagem).</param>
/// <param name="SetorSlug">Slug do setor dono do conteúdo; define o matiz do filete e do badge.</param>
/// <param name="SetorNome">Nome do setor exibido no badge.</param>
/// <param name="Href">Destino ao clicar no card, quando ele é navegável.</param>
/// <param name="Texto">Corpo em texto simples.</param>
/// <param name="Corpo">Corpo em HTML já sanitizado, quando o conteúdo é rico.</param>
public sealed record CardModel(
	string Titulo,
	string? Meta = null,
	string? SetorSlug = null,
	string? SetorNome = null,
	string? Href = null,
	string? Texto = null,
	IHtmlContent? Corpo = null);
