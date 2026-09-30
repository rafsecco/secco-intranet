using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Web.Models;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Controller fino do recurso Setor (ADR-0002 regra 1): nunca acessa
/// <c>DbContext</c>/repositório diretamente — só orquestra os handlers existentes da
/// Application layer, que carregam toda a regra de negócio (regra 3). Exclusivo do
/// <c>intranet-admin</c> (ADR-0008): criar setor cria Roles no SecureGate.
/// </summary>
/// <param name="createHandler">Caso de uso de criação de setor.</param>
/// <param name="editarHandler">Caso de uso de edição de setor.</param>
/// <param name="getByIdHandler">Caso de uso de leitura pontual de setor.</param>
/// <param name="searchHandler">Caso de uso de busca paginada de setores.</param>
/// <param name="obterArvore">Leitura da árvore de itens de menu de um setor.</param>
/// <param name="criarItem">Criação de item na árvore.</param>
/// <param name="alternarItem">Ativação/desativação de item.</param>
/// <param name="excluirItem">Exclusão de item personalizado.</param>
/// <param name="moverItem">Reordenação de item entre os irmãos.</param>
[SomenteIntranetAdmin]
public sealed class SetoresController(
	CreateSetorHandler createHandler,
	EditarSetorHandler editarHandler,
	GetSetorByIdHandler getByIdHandler,
	SearchSetoresHandler searchHandler,
	ObterArvoreDeMenuHandler obterArvore,
	CriarItemMenuHandler criarItem,
	AtivarDesativarItemMenuHandler alternarItem,
	ExcluirItemMenuHandler excluirItem,
	MoverItemMenuHandler moverItem) : Controller
{
	/// <summary>Árvore de itens de menu da página de um setor.</summary>
	/// <param name="id">Identificador do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Menu(Guid id, CancellationToken cancellationToken = default)
	{
		var setor = await getByIdHandler.HandleAsync(id, cancellationToken);

		if (setor.IsFailure)
		{
			return NotFound();
		}

		var arvore = await obterArvore.HandleAsync(setor.Value.Id, cancellationToken);

		if (arvore.IsFailure)
		{
			// Setor criado antes da árvore existir e ainda não reconciliado.
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] =
				"Este setor ainda não tem itens de menu. Use \"Reconciliar itens de menu\" na lista de setores.";

			return RedirectToAction(nameof(Details), new { id });
		}

		return View(new SetorMenuViewModel(setor.Value, arvore.Value));
	}

	/// <summary>Cria um item na árvore de um setor.</summary>
	/// <param name="setorId">Setor dono da árvore.</param>
	/// <param name="parentId">Pai do novo item.</param>
	/// <param name="nome">Rótulo.</param>
	/// <param name="slug">Identificador entre irmãos.</param>
	/// <param name="tipo">Tipo do item.</param>
	/// <param name="rota">Rota opcional (só Personalizado).</param>
	/// <param name="icone">Classe do Bootstrap Icons, opcional.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> CriarItemDeMenu(
		Guid setorId,
		Guid parentId,
		string? nome,
		string? slug,
		TipoDeItemMenu tipo,
		string? rota,
		string? icone,
		CancellationToken cancellationToken = default)
	{
		var resultado = await criarItem.HandleAsync(
			new CriarItemMenuCommand(setorId, parentId, nome, slug, tipo, rota, icone), cancellationToken);

		return VoltarParaMenu(setorId, resultado.IsSuccess ? $"Item \"{resultado.Value.Nome}\" criado." : null, resultado);
	}

	/// <summary>Ativa ou desativa um item da árvore.</summary>
	/// <param name="setorId">Setor dono da árvore (só para voltar à tela).</param>
	/// <param name="itemId">Item a alterar.</param>
	/// <param name="ativar"><c>true</c> ativa, <c>false</c> desativa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AlternarItemDeMenu(Guid setorId, Guid itemId, bool ativar, CancellationToken cancellationToken = default)
	{
		var resultado = await alternarItem.HandleAsync(itemId, ativar, cancellationToken);

		return VoltarParaMenu(setorId, ativar ? "Item ativado." : "Item desativado.", resultado);
	}

	/// <summary>Exclui um item personalizado da árvore.</summary>
	/// <param name="setorId">Setor dono da árvore (só para voltar à tela).</param>
	/// <param name="itemId">Item a excluir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ExcluirItemDeMenu(Guid setorId, Guid itemId, CancellationToken cancellationToken = default)
	{
		var resultado = await excluirItem.HandleAsync(itemId, cancellationToken);

		return VoltarParaMenu(setorId, "Item excluído.", resultado);
	}

	/// <summary>Move um item para cima ou para baixo entre os irmãos.</summary>
	/// <param name="setorId">Setor dono da árvore (só para voltar à tela).</param>
	/// <param name="itemId">Item a mover.</param>
	/// <param name="paraCima"><c>true</c> troca com o anterior; <c>false</c>, com o próximo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> MoverItemDeMenu(Guid setorId, Guid itemId, bool paraCima, CancellationToken cancellationToken = default)
	{
		var resultado = await moverItem.HandleAsync(itemId, paraCima, cancellationToken);

		return VoltarParaMenu(setorId, null, resultado);
	}

	private RedirectToActionResult VoltarParaMenu(Guid setorId, string? sucesso, Result resultado)
	{
		if (resultado.IsFailure)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}
		else if (sucesso is not null)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = sucesso;
		}

		return RedirectToAction(nameof(Menu), new { id = setorId });
	}

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

	/// <summary>Formulário de edição de setor.</summary>
	/// <param name="id">Identificador do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await getByIdHandler.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		var setor = resultado.Value;

		return View(new SetorEditViewModel
		{
			Id = setor.Id,
			Slug = setor.Slug,
			Fixo = setor.Fixo,
			Nome = setor.Nome,
			Icone = setor.Icone,
			Ativo = setor.Ativo,
		});
	}

	/// <summary>Processa a edição de um setor.</summary>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Edit(SetorEditViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (!ModelState.IsValid)
		{
			return View(form);
		}

		var resultado = await editarHandler.HandleAsync(
			new EditarSetorCommand(form.Id, form.Nome, form.Icone, form.Ativo), cancellationToken);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View(form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Setor \"{resultado.Value.Nome}\" salvo.";

		return RedirectToAction(nameof(Details), new { id = form.Id });
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
			new CreateSetorCommand(form.Nome, form.Slug, form.Fixo, form.Icone, form.HabilitarDocumentos, form.HabilitarAvisos),
			cancellationToken);

		if (result.IsFailure)
		{
			ModelState.AddModelError(string.Empty, result.Error.Description);
			return View(form);
		}

		return RedirectToAction(nameof(Details), new { id = result.Value.Id });
	}
}
