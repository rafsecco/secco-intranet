namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Um item de menu já resolvido para o usuário atual.</summary>
/// <param name="Texto">Rótulo exibido.</param>
/// <param name="Icone">
/// Classe do Bootstrap Icons (ex: <c>bi-megaphone</c>). Nos itens de setor, vem do cadastro
/// do próprio setor; nos fixos, do core.
/// </param>
/// <param name="Url">Destino do link.</param>
/// <param name="Ativo">Se corresponde à página atual.</param>
/// <param name="SetorSlug">Slug do setor, quando o item representa um; define o matiz do ícone.</param>
public sealed record NavigationItemModel(
	string Texto,
	string Icone,
	string Url,
	bool Ativo = false,
	string? SetorSlug = null);

/// <summary>Bloco de itens de menu separado por divisor.</summary>
/// <param name="Titulo">Rótulo do grupo; <c>null</c> renderiza só o divisor.</param>
/// <param name="Itens">Itens do grupo.</param>
public sealed record NavigationGroupModel(string? Titulo, IReadOnlyList<NavigationItemModel> Itens);

/// <summary>Menu completo entregue ao tema.</summary>
/// <param name="Grupos">Grupos, na ordem de exibição.</param>
public sealed record NavigationModel(IReadOnlyList<NavigationGroupModel> Grupos);
