using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Bibliotecas;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Cadastro das bibliotecas de documentos (ADR-0002 regra 1: só orquestra handlers). Exclusivo do
/// <c>intranet-admin</c> (ADR-0008): criar uma biblioteca mexe na gestão de acesso do SecureGate.
/// </summary>
/// <param name="criarHandler">Caso de uso de criação.</param>
/// <param name="editarHandler">Caso de uso de edição.</param>
/// <param name="listarHandler">Caso de uso de listagem.</param>
[SomenteIntranetAdmin]
[Route("bibliotecas")]
public sealed class BibliotecasController(
	CriarBibliotecaHandler criarHandler,
	EditarBibliotecaHandler editarHandler,
	ListarBibliotecasHandler listarHandler) : Controller
{
	/// <summary>Lista as bibliotecas, ativas e inativas.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("")]
	public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
	{
		var resultado = await listarHandler.HandleAsync(new ListarBibliotecasQuery(SomenteAtivas: false), cancellationToken);

		return View(resultado.IsSuccess ? resultado.Value : []);
	}

	/// <summary>Formulário de criação.</summary>
	[HttpGet("nova")]
	public IActionResult Criar() => View(new BibliotecaFormViewModel());

	/// <summary>Processa a criação.</summary>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("nova")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Criar(BibliotecaFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (string.IsNullOrWhiteSpace(form.Slug))
		{
			ModelState.AddModelError(nameof(form.Slug), "O slug é obrigatório.");
		}

		if (!ModelState.IsValid)
		{
			return View(form);
		}

		var resultado = await criarHandler.HandleAsync(
			new CriarBibliotecaCommand(form.Nome, form.Slug, form.Icone, form.TodosLeem), cancellationToken);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View(form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] =
			$"Biblioteca \"{resultado.Value.Nome}\" criada. Para definir quem publica nela, conceda \"Alterar\" ao perfil responsável em Acesso.";

		return RedirectToAction(nameof(Index));
	}

	/// <summary>Formulário de edição.</summary>
	/// <param name="id">Identificador da biblioteca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{id:guid}/editar")]
	public async Task<IActionResult> Editar(Guid id, CancellationToken cancellationToken = default)
	{
		var biblioteca = await ObterAsync(id, cancellationToken);

		if (biblioteca is null)
		{
			return NotFound();
		}

		return View(new BibliotecaFormViewModel
		{
			Id = biblioteca.Id,
			Nome = biblioteca.Nome,
			Slug = biblioteca.Slug,
			Icone = biblioteca.Icone,
			Ativo = biblioteca.Ativo,
		});
	}

	/// <summary>Processa a edição.</summary>
	/// <param name="id">Identificador da biblioteca.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/editar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Editar(Guid id, BibliotecaFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		form.Id = id;

		if (!ModelState.IsValid)
		{
			return View(form);
		}

		var resultado = await editarHandler.HandleAsync(
			new EditarBibliotecaCommand(id, form.Nome, form.Icone, form.Ativo), cancellationToken);

		if (resultado.IsFailure)
		{
			if (resultado.Error.Type == ErrorType.NotFound)
			{
				return NotFound();
			}

			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View(form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Biblioteca \"{resultado.Value.Nome}\" salva.";

		return RedirectToAction(nameof(Index));
	}

	private async Task<BibliotecaDto?> ObterAsync(Guid id, CancellationToken cancellationToken)
	{
		var lista = await listarHandler.HandleAsync(new ListarBibliotecasQuery(SomenteAtivas: false), cancellationToken);

		return lista.IsSuccess ? lista.Value.FirstOrDefault(b => b.Id == id) : null;
	}
}
