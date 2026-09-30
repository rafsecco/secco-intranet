namespace Secco.Intranet.Web.Models;

/// <summary>Uma aba da barra de navegação dentro da página de um setor.</summary>
/// <param name="Nome">Rótulo.</param>
/// <param name="Icone">Classe do Bootstrap Icons; nulo = sem ícone.</param>
/// <param name="Url">Link da aba.</param>
/// <param name="Ativa">Se é a aba da página atual.</param>
public sealed record ItemMenuAbaDto(string Nome, string? Icone, string Url, bool Ativa);
