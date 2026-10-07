using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Tenants;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Área de tenants administrados (ADR-0008): criar, adotar, ligar recursos da plataforma,
/// ativar/desativar. Só <c>intranet-admin</c> com segundo fator; toda action com
/// <c>{tenantId}</c> passa pelo cadastro antes de qualquer chamada (IDOR).
/// </summary>
/// <param name="listar">Lista do cadastro.</param>
/// <param name="obter">Detalhe.</param>
/// <param name="listarAdotaveis">Tenants adotáveis.</param>
/// <param name="criar">Criação.</param>
/// <param name="adotar">Adoção.</param>
/// <param name="ligar">Ligar recurso.</param>
/// <param name="situacao">Ativar/desativar.</param>
[Route("tenants")]
[SomenteIntranetAdmin]
[ExigeSegundoFator]
public sealed class TenantsController(
	ListarTenantsAdministradosHandler listar,
	ObterTenantAdministradoHandler obter,
	ListarTenantsAdotaveisHandler listarAdotaveis,
	CriarTenantHandler criar,
	AdotarTenantHandler adotar,
	LigarRecursoHandler ligar,
	AlterarSituacaoDoTenantHandler situacao) : Controller
{
	/// <summary>Lista dos tenants administrados.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("")]
	public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
	{
		var tenants = await listar.HandleAsync(cancellationToken);

		return tenants.IsFailure ? Indisponivel(tenants.Error) : View(tenants.Value);
	}

	/// <summary>Formulário de criação.</summary>
	[HttpGet("novo")]
	public IActionResult Novo() => View(new NovoTenantViewModel());

	/// <summary>Cria o tenant e o registra.</summary>
	/// <param name="modelo">Formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("novo")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Novo(NovoTenantViewModel modelo, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(modelo);

		var criado = await criar.HandleAsync(
			new CriarTenantCommand(modelo.Sistema, modelo.Responsavel, modelo.Nome, modelo.Slug), cancellationToken);

		if (criado.IsFailure)
		{
			modelo.Erro = criado.Error.Description;

			return View(modelo);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Tenant \"{modelo.Slug?.Trim()}\" criado. Ligue os recursos de que o sistema precisa.";

		return RedirectToAction(nameof(Detalhe), new { tenantId = criado.Value });
	}

	/// <summary>Formulário de adoção.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("adotar")]
	public async Task<IActionResult> Adotar(CancellationToken cancellationToken = default)
	{
		var adotaveis = await listarAdotaveis.HandleAsync(cancellationToken);

		return adotaveis.IsFailure
			? Indisponivel(adotaveis.Error)
			: View(new AdotarTenantViewModel { Adotaveis = adotaveis.Value });
	}

	/// <summary>Adota um tenant existente.</summary>
	/// <param name="modelo">Formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("adotar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Adotar(AdotarTenantViewModel modelo, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(modelo);

		var adotado = await adotar.HandleAsync(
			new AdotarTenantCommand(modelo.TenantId ?? Guid.Empty, modelo.Sistema, modelo.Responsavel), cancellationToken);

		if (adotado.IsSuccess)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = "Tenant adotado.";

			return RedirectToAction(nameof(Detalhe), new { tenantId = modelo.TenantId });
		}

		var adotaveis = await listarAdotaveis.HandleAsync(cancellationToken);
		modelo.Adotaveis = adotaveis.IsSuccess ? adotaveis.Value : [];
		modelo.Erro = adotado.Error.Description;

		return View(modelo);
	}

	/// <summary>Detalhe do tenant, com o painel dos recursos.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{tenantId:guid}")]
	[TenantAdministrado]
	public async Task<IActionResult> Detalhe(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var tenant = await obter.HandleAsync(tenantId, cancellationToken);

		return tenant.IsFailure ? Indisponivel(tenant.Error) : View(tenant.Value);
	}

	/// <summary>
	/// Liga um recurso. Em modo script, a resposta <b>é</b> a tela do script — nunca redirect com
	/// TempData, que gravaria a senha num cookie — e sai com <c>no-store</c>.
	/// </summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="recurso">Segmento do recurso (lista fechada).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{tenantId:guid}/recursos/{recurso}")]
	[TenantAdministrado]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> LigarRecurso(Guid tenantId, string recurso, CancellationToken cancellationToken = default)
	{
		var ligado = await ligar.HandleAsync(new LigarRecursoCommand(tenantId, recurso), cancellationToken);

		if (ligado.IsFailure)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = ligado.Error.Description;

			return RedirectToAction(nameof(Detalhe), new { tenantId });
		}

		RecursosDaPlataforma.TentarLer(recurso, out var lido);

		if (ligado.Value is { Aplicado: false, Script: { } script })
		{
			Response.Headers.CacheControl = "no-store";
			Response.Headers.Pragma = "no-cache";

			var detalhe = await obter.HandleAsync(tenantId, cancellationToken);
			var sistema = detalhe.IsSuccess ? detalhe.Value.Sistema : string.Empty;

			return View("Script", new ScriptDeProvisionamentoViewModel(tenantId, sistema, RecursosDaPlataforma.Nome(lido), script));
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"{RecursosDaPlataforma.Nome(lido)} ligado.";

		return RedirectToAction(nameof(Detalhe), new { tenantId });
	}

	/// <summary>Ativa o tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{tenantId:guid}/ativar")]
	[TenantAdministrado]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Ativar(Guid tenantId, CancellationToken cancellationToken = default) =>
		Concluir(await situacao.AtivarAsync(tenantId, cancellationToken), "Tenant ativado.", tenantId);

	/// <summary>Desativa o tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="confirmacao">Slug digitado para confirmar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{tenantId:guid}/desativar")]
	[TenantAdministrado]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Desativar(Guid tenantId, string? confirmacao, CancellationToken cancellationToken = default) =>
		Concluir(await situacao.DesativarAsync(tenantId, confirmacao, cancellationToken),
			"Tenant desativado. O login e o catálogo do sistema param em instantes.", tenantId);

	private RedirectToActionResult Concluir(Result resultado, string sucesso, Guid tenantId)
	{
		TempData[resultado.IsSuccess ? FeedbackViewComponent.ChaveDaMensagem : FeedbackViewComponent.ChaveDaMensagemDeErro] =
			resultado.IsSuccess ? sucesso : resultado.Error.Description;

		return RedirectToAction(nameof(Detalhe), new { tenantId });
	}

	/// <summary>Leitura que falhou: inexistente vira 404; SecureGate ausente ou fora, a página que explica.</summary>
	private IActionResult Indisponivel(Error erro) =>
		erro.Type == ErrorType.NotFound
			? NotFound()
			: View("Indisponivel", new TenantsIndisponivelViewModel(erro.Description));
}
