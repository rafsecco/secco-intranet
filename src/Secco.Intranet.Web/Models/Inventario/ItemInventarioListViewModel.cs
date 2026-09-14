using Secco.Intranet.Application.Inventario;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Models.Inventario;

/// <summary>Modelo da listagem paginada de itens de inventário.</summary>
/// <param name="Page">Página de resultados retornada pela busca.</param>
/// <param name="Nome">Filtro de nome aplicado, para re-popular a busca na view.</param>
public sealed record ItemInventarioListViewModel(PagedResult<ItemInventarioDto> Page, string? Nome);
