using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Web.Models;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Controller fino do recurso Setor (ADR-0002 regra 1): nunca acessa
/// <c>DbContext</c>/repositório diretamente — só orquestra os handlers existentes da
/// Application layer, que carregam toda a regra de negócio (regra 3).
/// </summary>
/// <param name="createHandler">Caso de uso de criação de setor.</param>
/// <param name="getByIdHandler">Caso de uso de leitura pontual de setor.</param>
/// <param name="searchHandler">Caso de uso de busca paginada de setores.</param>
public sealed class SetoresController(
	CreateSetorHandler createHandler,
	GetSetorByIdHandler getByIdHandler,
	SearchSetoresHandler searchHandler) : Controller
{
	/// <summary>Busca paginada de setores do tenant atual.</summary>
	/// <param name="nome">Trecho do nome a filtrar (opcional).</param>
	/// <param name="page">Página desejada (1-based).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Index(string? nome, int page = 1, CancellationToken cancellationToken = default)
	{
		var criteria = new SetorSearchCriteria(NomeContains: nome, Page: new PageRequest(page));
		var result = await searchHandler.HandleAsync(criteria, cancellationToken);

		// A busca não tem caminho de falha de negócio hoje — o handler sempre retorna sucesso.
		return View(new SetorListViewModel(result.Value, nome));
	}

	/// <summary>Detalhe de um setor.</summary>
	/// <param name="id">Identificador do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
	{
		var result = await getByIdHandler.HandleAsync(id, cancellationToken);

		return result.IsSuccess ? View(result.Value) : NotFound();
	}

	/// <summary>Formulário de criação de setor.</summary>
	[HttpGet]
	public IActionResult Create() => View(new SetorFormViewModel());

	/// <summary>Processa a criação de um setor.</summary>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Create(SetorFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (!ModelState.IsValid)
		{
			return View(form);
		}

		var result = await createHandler.HandleAsync(
			new CreateSetorCommand(form.Nome, form.Slug, form.Fixo), cancellationToken);

		if (result.IsFailure)
		{
			ModelState.AddModelError(string.Empty, result.Error.Description);
			return View(form);
		}

		return RedirectToAction(nameof(Details), new { id = result.Value.Id });
	}
}
