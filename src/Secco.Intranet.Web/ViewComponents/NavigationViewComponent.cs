using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Navigation;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.ViewComponents;

/// <summary>
/// Monta o menu e entrega ao markup do tema (ADR-0004): a lógica fica no core, a aparência
/// no tema. O componente não acessa repositório — usa o handler da Application (ADR-0002).
/// </summary>
/// <remarks>
/// O handler é resolvido dentro do método, e não pelo construtor, de propósito. Resolvê-lo
/// por injeção construiria o <c>DbContext</c> do tenant, e o menu está no layout de
/// <b>todas</b> as páginas: numa requisição sem tenant resolvido — a página de erro, uma
/// rota anônima — isso derrubaria a página inteira em vez de apenas deixar o menu sem
/// setores.
/// </remarks>
/// <param name="serviceProvider">Escopo da requisição, para resolver o handler sob demanda.</param>
/// <param name="tenantContext">Tenant da requisição atual.</param>
/// <param name="configuration">Configuração do host, para saber se a autenticação está ativa.</param>
/// <param name="demoOptions">Estado das páginas de demonstração.</param>
/// <param name="logger">Log de diagnóstico.</param>
public sealed class NavigationViewComponent(
	IServiceProvider serviceProvider,
	ITenantContext tenantContext,
	IConfiguration configuration,
	DemoOptions demoOptions,
	ILogger<NavigationViewComponent> logger) : ViewComponent
{
	private const int LimiteSetoresNoMenu = 50;

	/// <summary>Renderiza o menu.</summary>
	public async Task<IViewComponentResult> InvokeAsync()
	{
		var autenticacaoAtiva = IntranetAuthenticationExtensions.IsConfigured(configuration);

		var request = new NavigationRequest(
			await CarregarSetoresAsync(HttpContext.User, autenticacaoAtiva).ConfigureAwait(false),
			HttpContext.Request.Path.Value ?? "/",
			MostrarAdministracao: !autenticacaoAtiva || SetorAcesso.AdministraAlgumSetor(HttpContext.User),
			demoOptions.Habilitado);

		return View(IntranetNavigation.Build(request));
	}

	private async Task<IReadOnlyList<SetorDto>> CarregarSetoresAsync(
		System.Security.Claims.ClaimsPrincipal usuario,
		bool autenticacaoAtiva)
	{
		if (!tenantContext.IsResolved)
		{
			// Sem tenant não há banco a consultar. O menu sai só com os itens fixos.
			return [];
		}

		try
		{
			var criteria = new SetorSearchCriteria(
				ApenasAtivos: true,
				Page: new PageRequest(1, LimiteSetoresNoMenu));

			var resultado = await serviceProvider
				.GetRequiredService<SearchSetoresHandler>()
				.HandleAsync(criteria, HttpContext.RequestAborted)
				.ConfigureAwait(false);

			return resultado.IsSuccess
				? SetorAcesso.Visiveis(resultado.Value.Items, usuario, exigirVinculo: autenticacaoAtiva)
				: [];
		}
#pragma warning disable CA1031 // O menu está no layout: uma falha aqui derrubaria até a própria página de erro.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			logger.LogWarning(exception, "Não foi possível carregar os setores do menu; exibindo apenas os itens fixos.");
			return [];
		}
	}
}
