using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Tela de ordenação do menu do tenant: sobe e desce itens do grupo principal e dos setores, e
/// restaura a ordem padrão. Exclusivo do <c>intranet-admin</c> (ADR-0008). O formulário nunca manda a
/// lista: a ordem efetiva é recalculada a cada requisição, então uma tela velha não sobrescreve nada.
/// Controller fino (ADR-0002): só orquestra handlers.
/// </summary>
/// <param name="obterPosicoes">Posições salvas do tenant.</param>
/// <param name="moverPosicao">Troca um item com o vizinho.</param>
/// <param name="restaurarOrdem">Volta um grupo à ordem padrão.</param>
/// <param name="buscarSetores">Setores ativos do tenant.</param>
/// <param name="catalogo">Itens extras registrados pelos projetos externos.</param>
[SomenteIntranetAdmin]
public sealed class MenuController(
	ObterPosicoesDoMenuHandler obterPosicoes,
	MoverPosicaoNoMenuHandler moverPosicao,
	RestaurarOrdemDoMenuHandler restaurarOrdem,
	SearchSetoresHandler buscarSetores,
	CatalogoDeItensDoMenuPrincipal catalogo) : Controller
{
	/// <summary>As duas listas na ordem efetiva.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
	{
		var principal = await LinhasAsync(GrupoDoMenu.Principal, cancellationToken);
		var setores = await LinhasAsync(GrupoDoMenu.Setores, cancellationToken);

		return View(new MenuOrdemViewModel(principal, setores));
	}

	/// <summary>Move um item para cima ou para baixo no grupo.</summary>
	/// <param name="grupo">Grupo do item.</param>
	/// <param name="chave">Chave do item.</param>
	/// <param name="paraCima"><c>true</c> troca com o anterior; <c>false</c>, com o próximo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Mover(GrupoDoMenu grupo, string? chave, bool paraCima, CancellationToken cancellationToken = default)
	{
		var linhas = await LinhasAsync(grupo, cancellationToken);
		var resultado = await moverPosicao.HandleAsync(
			grupo, [.. linhas.Select(linha => linha.Chave)], chave ?? string.Empty, paraCima, cancellationToken);

		return Voltar(resultado, null);
	}

	/// <summary>Apaga as posições salvas do grupo.</summary>
	/// <param name="grupo">Grupo a restaurar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Restaurar(GrupoDoMenu grupo, CancellationToken cancellationToken = default)
	{
		var resultado = await restaurarOrdem.HandleAsync(grupo, cancellationToken);

		return Voltar(resultado, "Ordem padrão restaurada.");
	}

	private RedirectToActionResult Voltar(Result resultado, string? sucesso)
	{
		if (resultado.IsFailure)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}
		else if (sucesso is not null)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = sucesso;
		}

		return RedirectToAction(nameof(Index));
	}

	/// <summary>Linhas do grupo na ordem efetiva (padrão + posições salvas). Principal sem gate: lista tudo o que existe.</summary>
	private async Task<IReadOnlyList<LinhaDaOrdem>> LinhasAsync(GrupoDoMenu grupo, CancellationToken cancellationToken)
	{
		var posicoes = await obterPosicoes.HandleAsync(cancellationToken);

		if (grupo == GrupoDoMenu.Setores)
		{
			var criterio = new SetorSearchCriteria(
				ApenasAtivos: true,
				Page: new PageRequest(1, NavigationViewComponent.LimiteSetoresNoMenu));
			var encontrados = await buscarSetores.HandleAsync(criterio, cancellationToken);
			var padrao = encontrados.IsSuccess ? encontrados.Value.Items : [];

			return [.. OrdemDoMenu.Ordenar(padrao, setor => setor.Id.ToString("D"), posicoes.Setores)
				.Select(setor => new LinhaDaOrdem(setor.Id.ToString("D"), setor.Nome, setor.Icone))];
		}

		var extras = catalogo.Criar(HttpContext.RequestServices).Select(ItemExtraDoMenu.De);
		var principal = IntranetNavigation.PrincipalNaOrdemPadrao(incluirDiretorio: true, extras);

		return [.. OrdemDoMenu.Ordenar(principal, entrada => entrada.Chave, posicoes.Principal)
			.Select(entrada => new LinhaDaOrdem(entrada.Chave, entrada.Rotulo, entrada.Icone))];
	}
}
