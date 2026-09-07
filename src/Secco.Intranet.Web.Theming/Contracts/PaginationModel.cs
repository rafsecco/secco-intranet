namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Navegação entre páginas de uma listagem.</summary>
/// <param name="Pagina">Página atual (1-based).</param>
/// <param name="TotalPaginas">Total de páginas.</param>
/// <param name="UrlAnterior">Destino da página anterior; <c>null</c> quando não há.</param>
/// <param name="UrlProxima">Destino da próxima página; <c>null</c> quando não há.</param>
public sealed record PaginationModel(int Pagina, int TotalPaginas, string? UrlAnterior, string? UrlProxima);
