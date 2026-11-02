using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Web.Models.Documentos;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.ViewComponents;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Página de uma biblioteca de documentos e as ações de documento dela, em <c>/bibliotecas/{slug}</c>
/// (ADR-0002 regra 1: só orquestra handlers). Os nomes das actions e o valor de rota <c>slug</c> são
/// os do <c>SetorController</c>, para as views compartilhadas resolverem no controller corrente.
/// Biblioteca inexistente, inativa ou sem permissão responde o mesmo 404 (ADR-0005). As rotas fixas
/// de <c>BibliotecasController</c> (cadastro) vencem por serem literais.
/// </summary>
/// <param name="acesso">Checagens de leitura e escrita da biblioteca.</param>
/// <param name="listarHandler">Listagem de documentos da biblioteca.</param>
/// <param name="publicarHandler">Publicação de documento.</param>
/// <param name="arquivarHandler">Arquivamento de documento.</param>
/// <param name="obterParaEdicaoHandler">Leitura dos dados de um documento para o formulário de edição.</param>
/// <param name="editarHandler">Edição de documento e troca de arquivo.</param>
/// <param name="criarGrupoHandler">Criação de grupo de documentos.</param>
/// <param name="moverHandler">Movimento de documento na lista.</param>
/// <param name="documentoOptions">Limites de upload.</param>
[Route("bibliotecas/{slug}")]
public sealed class BibliotecaController(
	AcessoABiblioteca acesso,
	ListarDocumentosHandler listarHandler,
	PublicarDocumentoHandler publicarHandler,
	ArquivarDocumentoHandler arquivarHandler,
	ObterDocumentoParaEdicaoHandler obterParaEdicaoHandler,
	EditarDocumentoHandler editarHandler,
	CriarGrupoDeDocumentosHandler criarGrupoHandler,
	MoverDocumentoHandler moverHandler,
	DocumentoOptions documentoOptions) : Controller
{
	/// <summary>Lista os documentos da biblioteca.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("")]
	public async Task<IActionResult> Index(string slug, CancellationToken cancellationToken = default)
	{
		var biblioteca = await acesso.LeituraAsync(User, slug, cancellationToken).ConfigureAwait(false);

		if (biblioteca is null)
		{
			return NotFound();
		}

		return View(
			"Documentos",
			await MontarDocumentosAsync(biblioteca, new DocumentoFormViewModel(), cancellationToken).ConfigureAwait(false));
	}

	/// <summary>Publica um documento na biblioteca.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="arquivo">Arquivo enviado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("documentos")]
	[ValidateAntiForgeryToken]
	[RequestSizeLimit(64L * 1024 * 1024)]
	public async Task<IActionResult> Publicar(
		string slug,
		DocumentoFormViewModel form,
		IFormFile? arquivo,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var biblioteca = await acesso.EscritaAsync(User, slug, cancellationToken).ConfigureAwait(false);

		if (biblioteca is null)
		{
			return NotFound();
		}

		if (arquivo is null || arquivo.Length == 0)
		{
			ModelState.AddModelError(string.Empty, "Escolha um arquivo para publicar.");
		}

		if (!ModelState.IsValid)
		{
			return View("Documentos", await MontarDocumentosAsync(biblioteca, form, cancellationToken).ConfigureAwait(false));
		}

		await using var conteudo = arquivo!.OpenReadStream();

		// A visibilidade não existe para biblioteca: o handler a ignora.
		var resultado = await publicarHandler.HandleAsync(
			new PublicarDocumentoCommand(
				Dono(biblioteca),
				form.Titulo,
				form.Descricao,
				arquivo.FileName,
				arquivo.Length,
				conteudo,
				form.Visibilidade,
				User.Identity?.Name ?? "desconhecido",
				form.ParentId),
			cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View("Documentos", await MontarDocumentosAsync(biblioteca, form, cancellationToken).ConfigureAwait(false));
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Documento \"{resultado.Value.Titulo}\" publicado.";

		return RedirectToAction(nameof(Index), new { slug });
	}

	/// <summary>Tela de organizar: ordem, hierarquia, grupos e arquivamento dos documentos da biblioteca.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("documentos/organizar")]
	public async Task<IActionResult> Organizar(string slug, CancellationToken cancellationToken = default)
	{
		var biblioteca = await acesso.EscritaAsync(User, slug, cancellationToken).ConfigureAwait(false);

		if (biblioteca is null)
		{
			return NotFound();
		}

		return View(
			"OrganizarDocumentos",
			await MontarOrganizarAsync(biblioteca, new GrupoFormViewModel(), cancellationToken).ConfigureAwait(false));
	}

	/// <summary>Formulário de edição de um documento ou grupo da biblioteca.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("documentos/{id:guid}/editar")]
	public async Task<IActionResult> EditarDocumento(string slug, Guid id, CancellationToken cancellationToken = default)
	{
		var biblioteca = await acesso.EscritaAsync(User, slug, cancellationToken).ConfigureAwait(false);

		if (biblioteca is null)
		{
			return NotFound();
		}

		var documento = await obterParaEdicaoHandler.HandleAsync(
			new ObterDocumentoParaEdicaoQuery(
				id, Dono(biblioteca), Administrados(biblioteca), ExigirVinculo: !acesso.ModoAbertoDeDev),
			cancellationToken).ConfigureAwait(false);

		if (documento.IsFailure)
		{
			return NotFound();
		}

		var dto = documento.Value;

		return View(
			"EditarDocumento",
			MontarEdicao(
				biblioteca,
				dto.Id,
				dto.EhGrupo,
				dto.NomeArquivo,
				new EditarDocumentoFormViewModel { Titulo = dto.Titulo, Descricao = dto.Descricao }));
	}

	/// <summary>Salva a edição de um documento ou grupo, trocando o arquivo quando um novo é enviado.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="arquivo">Arquivo novo; vazio ou ausente mantém o atual.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("documentos/{id:guid}/editar")]
	[ValidateAntiForgeryToken]
	[RequestSizeLimit(64L * 1024 * 1024)]
	public async Task<IActionResult> EditarDocumento(
		string slug,
		Guid id,
		EditarDocumentoFormViewModel form,
		IFormFile? arquivo,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var biblioteca = await acesso.EscritaAsync(User, slug, cancellationToken).ConfigureAwait(false);

		if (biblioteca is null)
		{
			return NotFound();
		}

		var administrados = Administrados(biblioteca);
		var exigirVinculo = !acesso.ModoAbertoDeDev;

		// O formulário precisa do nome do arquivo atual e de saber se é grupo, que não vêm do POST.
		var atual = await obterParaEdicaoHandler.HandleAsync(
			new ObterDocumentoParaEdicaoQuery(id, Dono(biblioteca), administrados, exigirVinculo),
			cancellationToken).ConfigureAwait(false);

		if (atual.IsFailure)
		{
			return NotFound();
		}

		IActionResult ComErro() => View(
			"EditarDocumento",
			MontarEdicao(biblioteca, id, atual.Value.EhGrupo, atual.Value.NomeArquivo, form));

		if (!ModelState.IsValid)
		{
			return ComErro();
		}

		var enviado = arquivo is { Length: > 0 };
		await using var conteudo = enviado ? arquivo!.OpenReadStream() : null;

		var resultado = await editarHandler.HandleAsync(
			new EditarDocumentoCommand(
				id,
				Dono(biblioteca),
				form.Titulo,
				form.Descricao,
				form.Visibilidade,
				enviado ? arquivo!.FileName : null,
				enviado ? arquivo!.Length : 0,
				conteudo,
				administrados,
				exigirVinculo),
			cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			if (resultado.Error == IntranetErrors.Documentos.NotFound)
			{
				return NotFound();
			}

			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return ComErro();
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = "Documento atualizado.";

		return RedirectToAction(nameof(Organizar), new { slug });
	}

	/// <summary>Muda um documento de lugar: sobe, desce, recua ou volta para a raiz.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="acao">Movimento pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("documentos/{id:guid}/mover")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> MoverDocumento(
		string slug, Guid id, MovimentoDeDocumento acao, CancellationToken cancellationToken = default)
	{
		var biblioteca = await acesso.EscritaAsync(User, slug, cancellationToken).ConfigureAwait(false);

		if (biblioteca is null)
		{
			return NotFound();
		}

		var resultado = await moverHandler
			.HandleAsync(new MoverDocumentoCommand(Dono(biblioteca), id, acao), cancellationToken)
			.ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			if (resultado.Error == IntranetErrors.Documentos.NotFound)
			{
				return NotFound();
			}

			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}

		return RedirectToAction(nameof(Organizar), new { slug });
	}

	/// <summary>Cria um grupo no topo da lista de documentos da biblioteca.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("documentos/grupos")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> CriarGrupo(
		string slug, GrupoFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var biblioteca = await acesso.EscritaAsync(User, slug, cancellationToken).ConfigureAwait(false);

		if (biblioteca is null)
		{
			return NotFound();
		}

		if (ModelState.IsValid)
		{
			var resultado = await criarGrupoHandler
				.HandleAsync(
					new CriarGrupoDeDocumentosCommand(Dono(biblioteca), form.Titulo, form.Descricao, User.Identity?.Name ?? "desconhecido"),
					cancellationToken)
				.ConfigureAwait(false);

			if (resultado.IsSuccess)
			{
				TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Grupo \"{resultado.Value.Titulo}\" criado.";

				return RedirectToAction(nameof(Organizar), new { slug });
			}

			ModelState.AddModelError(string.Empty, resultado.Error.Description);
		}

		return View(
			"OrganizarDocumentos",
			await MontarOrganizarAsync(biblioteca, form, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>Retira um documento de circulação, preservando o registro e o arquivo.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("documentos/{id:guid}/arquivar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Arquivar(string slug, Guid id, CancellationToken cancellationToken = default)
	{
		var biblioteca = await acesso.EscritaAsync(User, slug, cancellationToken).ConfigureAwait(false);

		if (biblioteca is null)
		{
			return NotFound();
		}

		// O conjunto só tem esta biblioteca: documento de outro dono não é encontrado.
		var resultado = await arquivarHandler.HandleAsync(
			new ArquivarDocumentoCommand(id, Administrados(biblioteca), ExigirVinculo: !acesso.ModoAbertoDeDev),
			cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			if (resultado.Error != IntranetErrors.Documentos.ItemComFilhos)
			{
				return NotFound();
			}

			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}
		else
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = "Documento arquivado.";
		}

		return RedirectToAction(nameof(Organizar), new { slug });
	}

	private static ReferenciaDeDono Dono(BibliotecaDto biblioteca) => ReferenciaDeDono.DaBiblioteca(biblioteca.Slug);

	private static IReadOnlySet<ReferenciaDeDono> Administrados(BibliotecaDto biblioteca) =>
		new HashSet<ReferenciaDeDono> { Dono(biblioteca) };

	private static DonoDaPaginaDeDocumentos DonoDaPagina(BibliotecaDto biblioteca) =>
		new(biblioteca.Nome, biblioteca.Slug, biblioteca.Icone, MostraVisibilidade: false);

	private async Task<IReadOnlyList<NoDeDocumentoDto>> ListarAsync(BibliotecaDto biblioteca, CancellationToken cancellationToken)
	{
		var documentos = await listarHandler
			.HandleAsync(new ListarDocumentosQuery(Dono(biblioteca)), cancellationToken)
			.ConfigureAwait(false);

		return documentos.IsSuccess ? documentos.Value : [];
	}

	private async Task<DocumentosViewModel> MontarDocumentosAsync(
		BibliotecaDto biblioteca, DocumentoFormViewModel form, CancellationToken cancellationToken) =>
		DocumentosViewModel.Montar(
			DonoDaPagina(biblioteca),
			await ListarAsync(biblioteca, cancellationToken).ConfigureAwait(false),
			id => Url.Action("Download", "Documentos", new { id })!,
			await acesso.EscritaAsync(User, biblioteca.Slug, cancellationToken).ConfigureAwait(false) is not null,
			form,
			documentoOptions.TamanhoMaximoBytes,
			biblioteca.Nome,
			biblioteca.Nome);

	private async Task<OrganizarDocumentosViewModel> MontarOrganizarAsync(
		BibliotecaDto biblioteca, GrupoFormViewModel form, CancellationToken cancellationToken) =>
		new(
			DonoDaPagina(biblioteca),
			biblioteca.Nome,
			biblioteca.Nome,
			Url.Action(nameof(Index), new { slug = biblioteca.Slug })!,
			OrganizarDocumentosViewModel.MontarLinhas(await ListarAsync(biblioteca, cancellationToken).ConfigureAwait(false)),
			form);

	private EditarDocumentoViewModel MontarEdicao(
		BibliotecaDto biblioteca, Guid id, bool ehGrupo, string? nomeArquivo, EditarDocumentoFormViewModel form) =>
		new(DonoDaPagina(biblioteca), id, ehGrupo, nomeArquivo, form, Url.Action(nameof(Organizar), new { slug = biblioteca.Slug })!);
}
