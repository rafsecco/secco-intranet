using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Bibliotecas;
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
/// <param name="listarBibliotecas">Bibliotecas do tenant.</param>
/// <param name="listarLinks">Links do menu principal.</param>
/// <param name="criarLink">Cria um link do menu principal.</param>
/// <param name="editarLink">Edita um link do menu principal.</param>
/// <param name="excluirLink">Exclui um link do menu principal.</param>
[SomenteIntranetAdmin]
public sealed class MenuController(
	ObterPosicoesDoMenuHandler obterPosicoes,
	MoverPosicaoNoMenuHandler moverPosicao,
	RestaurarOrdemDoMenuHandler restaurarOrdem,
	SearchSetoresHandler buscarSetores,
	CatalogoDeItensDoMenuPrincipal catalogo,
	ListarBibliotecasHandler listarBibliotecas,
	ListarLinksDoMenuPrincipalHandler listarLinks,
	CriarLinkDoMenuPrincipalHandler criarLink,
	EditarLinkDoMenuPrincipalHandler editarLink,
	ExcluirLinkDoMenuPrincipalHandler excluirLink) : Controller
{
	private const string ViewDoFormulario = "LinkForm";

	/// <summary>As duas listas na ordem efetiva.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
	{
		var links = await LinksAsync(cancellationToken);
		var principal = await LinhasAsync(GrupoDoMenu.Principal, links, cancellationToken);
		var setores = await LinhasAsync(GrupoDoMenu.Setores, links, cancellationToken);

		return View(new MenuOrdemViewModel(principal, setores, links));
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
		if (!Enum.IsDefined(grupo))
		{
			return BadRequest();
		}

		var linhas = await LinhasAsync(grupo, await LinksAsync(cancellationToken), cancellationToken);
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
		if (!Enum.IsDefined(grupo))
		{
			return BadRequest();
		}

		var resultado = await restaurarOrdem.HandleAsync(grupo, cancellationToken);

		return Voltar(resultado, "Ordem padrão restaurada.");
	}

	/// <summary>Formulário de link novo; a caixa "nova aba" vem marcada.</summary>
	[HttpGet("/menu/links/novo")]
	public IActionResult NovoLink() => View(ViewDoFormulario, new LinkDoMenuFormViewModel { NovaAba = true });

	/// <summary>Cria um link do menu principal.</summary>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("/menu/links/novo")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> NovoLink(LinkDoMenuFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var criado = await criarLink.HandleAsync(
			new CriarLinkDoMenuPrincipalCommand(form.Rotulo, form.Url, form.Icone, form.NovaAba), cancellationToken);

		return criado.IsFailure ? Recusar(form, criado.Error) : Concluir("Link criado.");
	}

	/// <summary>Formulário de edição de um link.</summary>
	/// <param name="id">Link a editar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("/menu/links/{id:guid}/editar")]
	public async Task<IActionResult> EditarLink(Guid id, CancellationToken cancellationToken = default)
	{
		var link = (await LinksAsync(cancellationToken)).FirstOrDefault(l => l.Id == id);

		return link is null ? NotFound() : View(ViewDoFormulario, LinkDoMenuFormViewModel.De(link));
	}

	/// <summary>Grava a edição de um link.</summary>
	/// <param name="id">Link a editar.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("/menu/links/{id:guid}/editar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EditarLink(Guid id, LinkDoMenuFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var editado = await editarLink.HandleAsync(
			new EditarLinkDoMenuPrincipalCommand(id, form.Rotulo, form.Url, form.Icone, form.NovaAba), cancellationToken);

		if (editado.IsFailure)
		{
			form.Id = id;

			return Recusar(form, editado.Error);
		}

		return Concluir("Link salvo.");
	}

	/// <summary>Exclui um link do menu principal.</summary>
	/// <param name="id">Link a excluir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("/menu/links/{id:guid}/excluir")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ExcluirLink(Guid id, CancellationToken cancellationToken = default)
	{
		var excluido = await excluirLink.HandleAsync(id, cancellationToken);

		if (excluido.IsFailure && excluido.Error == IntranetErrors.LinksDoMenu.NotFound)
		{
			return NotFound();
		}

		return excluido.IsFailure ? Voltar(excluido, null) : Concluir("Link excluído.");
	}

	private RedirectToActionResult Concluir(string mensagem)
	{
		TempData[FeedbackViewComponent.ChaveDaMensagem] = mensagem;

		return RedirectToAction(nameof(Index));
	}

	/// <summary>Reapresenta o formulário com a mensagem no campo do erro (<see cref="CampoDoErro"/>); link inexistente vira 404.</summary>
	private IActionResult Recusar(LinkDoMenuFormViewModel form, Error erro)
	{
		if (erro == IntranetErrors.LinksDoMenu.NotFound)
		{
			return NotFound();
		}

		var campo = CampoDoErro(erro);

		ModelState.AddModelError(campo, erro.Description);

		return View(ViewDoFormulario, form);
	}

	/// <summary>
	/// Campo do formulário que leva a mensagem, pelo <c>Error.Code</c>. <c>CampoMuitoLongo</c> divide um
	/// código só entre rótulo e URL, então a descrição ("O rótulo ..." / "A URL ...") desempata.
	/// </summary>
	private static string CampoDoErro(Error erro)
	{
		if (erro.Code == IntranetErrors.Menu.CampoMuitoLongo(string.Empty, 0).Code)
		{
			return erro.Description.StartsWith("O rótulo", StringComparison.Ordinal) ? nameof(LinkDoMenuFormViewModel.Rotulo) : nameof(LinkDoMenuFormViewModel.Url);
		}

		if (erro.Code == IntranetErrors.LinksDoMenu.RotuloRequired.Code)
		{
			return nameof(LinkDoMenuFormViewModel.Rotulo);
		}

		if (erro.Code == IntranetErrors.LinksDoMenu.UrlRequired.Code || erro.Code == IntranetErrors.Menu.RotaInvalida.Code)
		{
			return nameof(LinkDoMenuFormViewModel.Url);
		}

		return erro.Code == IntranetErrors.Menu.IconeInvalido.Code ? nameof(LinkDoMenuFormViewModel.Icone) : string.Empty;
	}

	private async Task<IReadOnlyList<LinkDoMenuPrincipalDto>> LinksAsync(CancellationToken cancellationToken)
	{
		var links = await listarLinks.HandleAsync(cancellationToken);

		return links.IsSuccess ? links.Value : [];
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
	private async Task<IReadOnlyList<LinhaDaOrdem>> LinhasAsync(
		GrupoDoMenu grupo, IReadOnlyList<LinkDoMenuPrincipalDto> links, CancellationToken cancellationToken)
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
		var bibliotecas = await listarBibliotecas.HandleAsync(new ListarBibliotecasQuery(SomenteAtivas: true), cancellationToken);
		var principal = IntranetNavigation.PrincipalNaOrdemPadrao(
			incluirDiretorio: true, extras, bibliotecas.IsSuccess ? bibliotecas.Value : null, links);

		return [.. OrdemDoMenu.Ordenar(principal, entrada => entrada.Chave, posicoes.Principal)
			.Select(entrada => new LinhaDaOrdem(entrada.Chave, entrada.Rotulo, entrada.Icone))];
	}
}
