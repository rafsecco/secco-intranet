using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Models;

/// <summary>
/// Modelo da listagem paginada de setores (ADR-0002 regra 2). Envolve o
/// <see cref="PagedResult{T}"/> de DTOs de leitura com o filtro de nome, para a view
/// re-popular a caixa de busca e montar os links de paginação.
/// </summary>
/// <param name="Page">Página de resultados retornada pela busca.</param>
/// <param name="Nome">Filtro de nome aplicado, para re-popular a busca na view.</param>
public sealed record SetorListViewModel(PagedResult<SetorDto> Page, string? Nome);
