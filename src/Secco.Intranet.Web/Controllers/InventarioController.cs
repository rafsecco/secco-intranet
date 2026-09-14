using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Inventario;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Controller fino do recurso Inventário (ADR-0002 regra 1). Diferente de Setor, toda action
/// — inclusive <see cref="Index"/> — é gated: sem <c>intranet-admin</c> ou
/// <c>inventario-admin</c> a rota devolve 403, não só o link escondido no menu (ADR-0008,
/// spec de 2026-09-13).
/// </summary>
public sealed class InventarioController(
	CriarItemInventarioHandler criarHandler,
	EditarItemInventarioHandler editarHandler,
	MudarStatusItemInventarioHandler mudarStatusHandler,
	SearchItensInventarioHandler searchHandler,
	GetItemInventarioByIdHandler getByIdHandler,
	IDiretorioDeUsuarios diretorio,
	IConfiguration configuration,
	IWebHostEnvironment environment) : Controller
{
	private const string RoleEspecifica = "inventario-admin";

	/// <summary>Listagem paginada.</summary>
	[HttpGet]
	public async Task<IActionResult> Index(string? nome, int page = 1, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var criteria = new ItemInventarioSearchCriteria(NomeContains: nome, ApenasNaoBaixados: true, Page: new PageRequest(page));
		var resultado = await searchHandler.HandleAsync(criteria, cancellationToken);

		return View(new ItemInventarioListViewModel(resultado.Value, nome));
	}

	/// <summary>Detalhe de um item.</summary>
	[HttpGet]
	public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await getByIdHandler.HandleAsync(id, cancellationToken);

		return resultado.IsSuccess ? View(resultado.Value) : NotFound();
	}

	/// <summary>Formulário de criação.</summary>
	[HttpGet]
	public IActionResult Create()
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		return View(new ItemInventarioFormViewModel());
	}

	/// <summary>Processa a criação.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Create(ItemInventarioFormViewModel form, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		ArgumentNullException.ThrowIfNull(form);

		if (!ModelState.IsValid)
		{
			return View(form);
		}

		var resultado = await criarHandler.HandleAsync(
			new CriarItemInventarioCommand(form.Nome, form.Descricao, form.Categoria, form.CodigoPatrimonio, form.SetorId),
			cancellationToken);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View(form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Item \"{resultado.Value.Nome}\" cadastrado.";

		return RedirectToAction(nameof(Details), new { id = resultado.Value.Id });
	}

	/// <summary>Formulário de edição.</summary>
	[HttpGet]
	public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await getByIdHandler.HandleAsync(id, cancellationToken);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		var item = resultado.Value;

		return View(new ItemInventarioFormViewModel
		{
			Id = item.Id,
			Nome = item.Nome,
			Descricao = item.Descricao,
			Categoria = item.Categoria,
			CodigoPatrimonio = item.CodigoPatrimonio,
			SetorId = item.SetorId,
		});
	}

	/// <summary>Processa a edição.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Edit(ItemInventarioFormViewModel form, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		ArgumentNullException.ThrowIfNull(form);

		if (!ModelState.IsValid)
		{
			return View(form);
		}

		var resultado = await editarHandler.HandleAsync(
			new EditarItemInventarioCommand(form.Id, form.Nome, form.Descricao, form.Categoria, form.CodigoPatrimonio, form.SetorId),
			cancellationToken);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View(form);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Item \"{resultado.Value.Nome}\" salvo.";

		return RedirectToAction(nameof(Details), new { id = form.Id });
	}

	/// <summary>Atribui o item a um usuário do tenant.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Atribuir(Guid id, Guid usuarioId, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var usuarios = await diretorio.ListarDoTenantAtualAsync(cancellationToken);
		var usuario = usuarios.FirstOrDefault(u => u.Id == usuarioId);

		var resultado = await mudarStatusHandler.HandleAsync(
			new MudarStatusItemInventarioCommand(id, AcaoDeStatus.Atribuir, usuarioId, usuario?.Email), cancellationToken);

		return AposMudarStatus(id, resultado.IsFailure ? resultado.Error.Description : null);
	}

	/// <summary>Libera o item atribuído.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Desatribuir(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await mudarStatusHandler.HandleAsync(
			new MudarStatusItemInventarioCommand(id, AcaoDeStatus.Desatribuir), cancellationToken);

		return AposMudarStatus(id, resultado.IsFailure ? resultado.Error.Description : null);
	}

	/// <summary>Envia o item para manutenção.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EnviarParaManutencao(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await mudarStatusHandler.HandleAsync(
			new MudarStatusItemInventarioCommand(id, AcaoDeStatus.EnviarParaManutencao), cancellationToken);

		return AposMudarStatus(id, resultado.IsFailure ? resultado.Error.Description : null);
	}

	/// <summary>Volta o item da manutenção.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> VoltarDaManutencao(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await mudarStatusHandler.HandleAsync(
			new MudarStatusItemInventarioCommand(id, AcaoDeStatus.VoltarDaManutencao), cancellationToken);

		return AposMudarStatus(id, resultado.IsFailure ? resultado.Error.Description : null);
	}

	/// <summary>Dá baixa no item.</summary>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Baixar(Guid id, CancellationToken cancellationToken = default)
	{
		if (!PodeAdministrar())
		{
			return StatusCode(StatusCodes.Status403Forbidden);
		}

		var resultado = await mudarStatusHandler.HandleAsync(
			new MudarStatusItemInventarioCommand(id, AcaoDeStatus.Baixar), cancellationToken);

		return AposMudarStatus(id, resultado.IsFailure ? resultado.Error.Description : null);
	}

	/// <summary>
	/// Limitação conhecida e deliberada: <c>FeedbackViewComponent</c> hoje só produz
	/// <c>ToastVariante.Sucesso</c> — uma falha de transição (ex.: <c>TransicaoInvalida</c>)
	/// aparece no mesmo toast verde, só com texto de erro. Corrigir isso é dar ao componente
	/// de feedback o primeiro produtor real de <c>ToastVariante.Erro</c>, o que é maior que
	/// este recurso e mexe em algo compartilhado com Setor/Documento — fica para uma spec
	/// própria. Aceitável aqui porque o gatilho é defensivo (double-click, aba parada): os
	/// botões da tela já escondem/desabilitam a maioria das transições inválidas.
	/// </summary>
	private IActionResult AposMudarStatus(Guid id, string? mensagemDeErro)
	{
		TempData[FeedbackViewComponent.ChaveDaMensagem] = mensagemDeErro ?? "Item atualizado.";

		return RedirectToAction(nameof(Details), new { id });
	}

	/// <summary>
	/// Gate único de toda action. O bypass de "modo aberto de DEV" só vale em
	/// <c>IWebHostEnvironment.IsDevelopment()</c> de verdade — nunca no ambiente
	/// <c>Testing</c>, que também não configura autenticação: se o bypass valesse lá, não
	/// haveria como testar "sem a role, bloqueado" (ver docs/specs/2026-09-13-inventario-design.md).
	/// </summary>
	private bool PodeAdministrar() =>
		(environment.IsDevelopment() && !IntranetAuthenticationExtensions.IsConfigured(configuration))
		|| AcessoAdministrativo.TemAcesso(User, RoleEspecifica);
}
