namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Um item de menu já resolvido para o usuário atual.</summary>
/// <param name="Texto">Rótulo exibido.</param>
/// <param name="Icone">
/// Classe do Bootstrap Icons (ex: <c>bi-megaphone</c>); nulo = sem ícone. Nos itens de setor,
/// vem do cadastro do próprio setor; nos fixos, do core.
/// </param>
/// <param name="Url">Destino; nulo quando o item só agrupa os filhos (não é link).</param>
/// <param name="Ativo">
/// Se é a página atual <b>ou um ancestral dela</b> — o tema destaca o caminho inteiro e põe
/// <c>aria-current</c> só no item ativo sem filho ativo.
/// </param>
/// <param name="SetorSlug">Slug do setor, no item de nível 0 de um setor; define o matiz do ícone.</param>
/// <param name="Filhos">Subitens, já na ordem; nulo ou vazio = folha.</param>
public sealed record NavigationItemModel(
	string Texto,
	string? Icone,
	string? Url,
	bool Ativo = false,
	string? SetorSlug = null,
	IReadOnlyList<NavigationItemModel>? Filhos = null);

/// <summary>Bloco de itens de menu separado por divisor.</summary>
/// <param name="Titulo">Rótulo do grupo; <c>null</c> renderiza só o divisor.</param>
/// <param name="Itens">Itens do grupo.</param>
public sealed record NavigationGroupModel(string? Titulo, IReadOnlyList<NavigationItemModel> Itens);

/// <summary>Menu completo entregue ao tema.</summary>
/// <param name="Grupos">Grupos, na ordem de exibição.</param>
public sealed record NavigationModel(IReadOnlyList<NavigationGroupModel> Grupos);
