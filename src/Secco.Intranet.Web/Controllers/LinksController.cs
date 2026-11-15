using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Links;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Web.Models.Links;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// "Gerenciar links" de um setor, em <c>/{setor}/links</c>: a única tela da funcionalidade, só
/// para quem escreve no setor (<c>setor-{slug}:write</c>). Quem lê vê os links no menu. Sem escrita,
/// ou com o nó Links desligado, a resposta é 404, sem distinguir um caso do outro.
/// </summary>
/// <param name="acesso">Checagens de escrita e de recurso ligado.</param>
/// <param name="listarHandler">Leitura dos grupos e links.</param>
/// <param name="criarGrupoHandler">Criação de grupo.</param>
/// <param name="criarHandler">Criação de link.</param>
/// <param name="editarHandler">Edição de link ou grupo.</param>
/// <param name="moverHandler">Troca de posição.</param>
/// <param name="excluirHandler">Exclusão.</param>
[Route("{slug:" + SlugDeSetorRouteConstraint.Nome + "}/links")]
public sealed class LinksController(
	AcessoAoRecursoDoSetor acesso,
	ListarLinksDoSetorHandler listarHandler,
	CriarGrupoDeLinksHandler criarGrupoHandler,
	CriarLinkHandler criarHandler,
	EditarLinkHandler editarHandler,
	MoverLinkHandler moverHandler,
	ExcluirLinkHandler excluirHandler) : Controller
{
	private const string ViewDeGestao = "~/Views/Setor/Links.cshtml";
	private const string ViewDeFormulario = "~/Views/Setor/LinkForm.cshtml";

	/// <summary>Tela de gestão.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("")]
	public async Task<IActionResult> Index(string slug, CancellationToken cancellationToken = default)
	{
		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var links = await listarHandler.HandleAsync(alvo.Value.Setor.Id, cancellationToken).ConfigureAwait(false);

		return links.IsFailure
			? NotFound()
			: View(ViewDeGestao, new GerenciarLinksViewModel(alvo.Value.Setor, links.Value, Trilho(alvo.Value)));
	}

	/// <summary>Formulário de link novo; a caixa "nova página" vem marcada.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("novo")]
	public async Task<IActionResult> Novo(string slug, CancellationToken cancellationToken = default)
	{
		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		return alvo is null
			? NotFound()
			: await FormularioAsync(alvo.Value, new LinkFormViewModel { NovaAba = true }, null, ehGrupo: false, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Cria um link.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("novo")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Novo(string slug, LinkFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var criado = await criarHandler.HandleAsync(
			new CriarLinkCommand(alvo.Value.Setor.Id, form.Nome, form.Rota, form.NovaAba, form.GrupoId),
			cancellationToken).ConfigureAwait(false);

		if (criado.IsFailure)
		{
			return criado.Error == IntranetErrors.Links.NotFound
				? NotFound()
				: await RecusarAsync(alvo.Value, form, null, ehGrupo: false, criado.Error.Description, cancellationToken).ConfigureAwait(false);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Link \"{form.Nome!.Trim()}\" criado.";

		return RedirectToAction(nameof(Index), new { slug });
	}

	/// <summary>Cria um grupo, pelo formulário da tela de gestão.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="nome">Nome do grupo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("grupos")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Grupos(string slug, string? nome, CancellationToken cancellationToken = default)
	{
		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var criado = await criarGrupoHandler.HandleAsync(alvo.Value.Setor.Id, nome, cancellationToken).ConfigureAwait(false);

		if (criado.IsFailure && criado.Error == IntranetErrors.Links.NotFound)
		{
			return NotFound();
		}

		if (criado.IsFailure)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = criado.Error.Description;
		}
		else
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Grupo \"{nome!.Trim()}\" criado.";
		}

		return RedirectToAction(nameof(Index), new { slug });
	}

	/// <summary>Formulário de edição de um link ou grupo.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Item a editar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{id:guid}/editar")]
	public async Task<IActionResult> Editar(string slug, Guid id, CancellationToken cancellationToken = default)
	{
		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var links = await listarHandler.HandleAsync(alvo.Value.Setor.Id, cancellationToken).ConfigureAwait(false);

		if (links.IsFailure)
		{
			return NotFound();
		}

		var grupo = links.Value.Grupos.FirstOrDefault(g => g.Id == id);

		if (grupo is not null)
		{
			return await FormularioAsync(alvo.Value, new LinkFormViewModel { Nome = grupo.Nome }, id, ehGrupo: true, cancellationToken).ConfigureAwait(false);
		}

		var link = Links(links.Value).FirstOrDefault(l => l.Id == id);

		return link is null
			? NotFound()
			: await FormularioAsync(alvo.Value, LinkFormViewModel.De(link), id, ehGrupo: false, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Grava a edição.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Item a editar.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/editar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Editar(string slug, Guid id, LinkFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var links = await listarHandler.HandleAsync(alvo.Value.Setor.Id, cancellationToken).ConfigureAwait(false);

		if (links.IsFailure)
		{
			return NotFound();
		}

		var ehGrupo = links.Value.Grupos.Any(g => g.Id == id);

		if (!ehGrupo && Links(links.Value).All(l => l.Id != id))
		{
			return NotFound();
		}

		var editado = await editarHandler.HandleAsync(
			new EditarLinkCommand(alvo.Value.Setor.Id, id, form.Nome, form.Rota, form.NovaAba, form.GrupoId),
			cancellationToken).ConfigureAwait(false);

		if (editado.IsFailure)
		{
			return editado.Error == IntranetErrors.Links.NotFound
				? NotFound()
				: await RecusarAsync(alvo.Value, form, id, ehGrupo, editado.Error.Description, cancellationToken).ConfigureAwait(false);
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"\"{form.Nome!.Trim()}\" atualizado.";

		return RedirectToAction(nameof(Index), new { slug });
	}

	/// <summary>Sobe ou desce um item entre os irmãos.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Item a mover.</param>
	/// <param name="paraCima">Se sobe (<c>true</c>) ou desce (<c>false</c>).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/mover")]
	[ValidateAntiForgeryToken]
	public Task<IActionResult> Mover(string slug, Guid id, bool paraCima, CancellationToken cancellationToken = default) =>
		AcaoAsync(
			slug,
			setorId => moverHandler.HandleAsync(setorId, id, paraCima, cancellationToken),
			sucesso: null,
			cancellationToken);

	/// <summary>Exclui um link, ou um grupo sem links.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Item a excluir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("{id:guid}/excluir")]
	[ValidateAntiForgeryToken]
	public Task<IActionResult> Excluir(string slug, Guid id, CancellationToken cancellationToken = default) =>
		AcaoAsync(
			slug,
			setorId => excluirHandler.HandleAsync(setorId, id, cancellationToken),
			sucesso: "Item excluído.",
			cancellationToken);

	private static IEnumerable<LinkDto> Links(LinksDoSetorDto links) =>
		links.Entradas.Where(e => e.Link is not null).Select(e => e.Link!)
			.Concat(links.Grupos.SelectMany(g => g.Links));

	private static string Trilho((SetorDto Setor, ResultadoDaResolucao Resolucao) alvo) =>
		AcessoAoRecursoDoSetor.Trilho(alvo.Setor, alvo.Resolucao);

	/// <summary>Passo comum de mover e excluir: confere a escrita, executa e volta à tela de gestão.</summary>
	private async Task<IActionResult> AcaoAsync(
		string slug,
		Func<Guid, Task<Result>> executar,
		string? sucesso,
		CancellationToken cancellationToken)
	{
		var alvo = await AlvoDeEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var resultado = await executar(alvo.Value.Setor.Id).ConfigureAwait(false);

		if (resultado.IsFailure && resultado.Error == IntranetErrors.Links.NotFound)
		{
			return NotFound();
		}

		if (resultado.IsFailure)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}
		else if (sucesso is not null)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = sucesso;
		}

		return RedirectToAction(nameof(Index), new { slug });
	}

	/// <summary>Escrita: permissão do setor e nó Links ligado; nulo vira 404 em quem chama.</summary>
	private async Task<(SetorDto Setor, ResultadoDaResolucao Resolucao)?> AlvoDeEscritaAsync(
		string slug, CancellationToken cancellationToken)
	{
		if (!await acesso.PodeEscreverAsync(User, slug, cancellationToken).ConfigureAwait(false))
		{
			return null;
		}

		return await acesso.ResolverTipoAsync(slug, TipoDeItemMenu.Links, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Reapresenta o formulário com o que foi digitado e o motivo da recusa.</summary>
	private async Task<IActionResult> RecusarAsync(
		(SetorDto Setor, ResultadoDaResolucao Resolucao) alvo,
		LinkFormViewModel form,
		Guid? id,
		bool ehGrupo,
		string mensagem,
		CancellationToken cancellationToken)
	{
		ModelState.AddModelError(string.Empty, mensagem);

		return await FormularioAsync(alvo, form, id, ehGrupo, cancellationToken).ConfigureAwait(false);
	}

	private async Task<IActionResult> FormularioAsync(
		(SetorDto Setor, ResultadoDaResolucao Resolucao) alvo,
		LinkFormViewModel form,
		Guid? id,
		bool ehGrupo,
		CancellationToken cancellationToken)
	{
		var links = await listarHandler.HandleAsync(alvo.Setor.Id, cancellationToken).ConfigureAwait(false);

		return links.IsFailure
			? NotFound()
			: View(ViewDeFormulario, new LinkFormPaginaViewModel(alvo.Setor, form, links.Value.Grupos, id, ehGrupo, Trilho(alvo)));
	}
}
