using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Salas;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Salas de reunião e suas reservas. Controller fino (ADR-0002): só orquestra os handlers. O cadastro
/// das salas (<c>gerenciar</c>) é exclusivo do <c>intranet-admin</c> (ADR-0008).
/// </summary>
/// <param name="criarSala">Caso de uso de criação de sala.</param>
/// <param name="editarSala">Caso de uso de edição de sala.</param>
/// <param name="excluirSala">Caso de uso de exclusão de sala.</param>
/// <param name="listarSalas">Listagem das salas.</param>
/// <param name="obterSala">Leitura pontual de sala.</param>
[Route("salas")]
public sealed class SalasController(
	CriarSalaHandler criarSala,
	EditarSalaHandler editarSala,
	ExcluirSalaHandler excluirSala,
	ListarSalasHandler listarSalas,
	ObterSalaHandler obterSala) : Controller
{
	/// <summary>Lista todas as salas, ativas e inativas.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("gerenciar")]
	[SomenteIntranetAdmin]
	public async Task<IActionResult> Gerenciar(CancellationToken cancellationToken = default) =>
		View(await listarSalas.HandleAsync(somenteAtivas: false, cancellationToken));

	/// <summary>Formulário de sala nova.</summary>
	[HttpGet("gerenciar/nova")]
	[SomenteIntranetAdmin]
	public IActionResult Nova() => View("SalaForm", new SalaFormViewModel());

	/// <summary>Processa a criação de uma sala.</summary>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("gerenciar/nova")]
	[SomenteIntranetAdmin]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Nova(SalaFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (!ModelState.IsValid)
		{
			return View("SalaForm", form);
		}

		var resultado = await criarSala.HandleAsync(Comando(form, null), cancellationToken);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View("SalaForm", form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Sala \"{form.Nome!.Trim()}\" criada.";

		return RedirectToAction(nameof(Gerenciar));
	}

	/// <summary>Formulário de edição de sala.</summary>
	/// <param name="id">Identificador da sala.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("gerenciar/{id:guid}/editar")]
	[SomenteIntranetAdmin]
	public async Task<IActionResult> Editar(Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await obterSala.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		var sala = resultado.Value;

		return View("SalaForm", new SalaFormViewModel
		{
			Id = sala.Id,
			Nome = sala.Nome,
			Localizacao = sala.Localizacao,
			Capacidade = sala.Capacidade,
			Recursos = sala.Recursos,
			EmailNaAgenda = sala.EmailNaAgenda,
			Ordem = sala.Ordem,
			Ativa = sala.Ativa,
		});
	}

	/// <summary>Processa a edição de uma sala.</summary>
	/// <param name="id">Identificador da sala.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("gerenciar/{id:guid}/editar")]
	[SomenteIntranetAdmin]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Editar(Guid id, SalaFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		form.Id = id;

		if (!ModelState.IsValid)
		{
			return View("SalaForm", form);
		}

		var resultado = await editarSala.HandleAsync(Comando(form, id), cancellationToken);

		if (resultado.IsFailure)
		{
			if (resultado.Error.Type == ErrorType.NotFound)
			{
				return NotFound();
			}

			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View("SalaForm", form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Sala \"{form.Nome!.Trim()}\" salva.";

		return RedirectToAction(nameof(Gerenciar));
	}

	/// <summary>Exclui uma sala sem reservas futuras.</summary>
	/// <param name="id">Identificador da sala.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("gerenciar/{id:guid}/excluir")]
	[SomenteIntranetAdmin]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await excluirSala.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}
		else
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = "Sala excluída.";
		}

		return RedirectToAction(nameof(Gerenciar));
	}

	private static SalvarSalaCommand Comando(SalaFormViewModel form, Guid? id) =>
		new(id, form.Nome, form.Localizacao, form.Capacidade, form.Recursos, form.EmailNaAgenda, form.Ordem, form.Ativa);
}
