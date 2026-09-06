using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Documentos;
using Secco.Intranet.Web.Models.Publicacoes;
using Secco.Intranet.Web.Navigation;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Página de um setor, com os recursos dele em abas. Separada de <c>SetoresController</c>,
/// que é a administração do cadastro: aqui é onde quem pertence ao setor trabalha.
/// </summary>
/// <param name="getSetorHandler">Leitura do setor pelo slug.</param>
/// <param name="listarHandler">Listagem de documentos do setor.</param>
/// <param name="publicarDocumentoHandler">Publicação de documento.</param>
/// <param name="arquivarHandler">Arquivamento de documento.</param>
/// <param name="publicarPublicacaoHandler">Publicação de aviso.</param>
/// <param name="editarPublicacaoHandler">Edição de aviso.</param>
/// <param name="arquivarPublicacaoHandler">Arquivamento de aviso.</param>
/// <param name="listarPublicacoesDoSetorHandler">Listagem de avisos do setor.</param>
/// <param name="documentoOptions">Limites de upload.</param>
/// <param name="configuration">Configuração do host, para saber se a autenticação está ativa.</param>
[Route("setor/{slug}")]
public sealed class SetorController(
	GetSetorBySlugHandler getSetorHandler,
	ListarDocumentosHandler listarHandler,
	PublicarDocumentoHandler publicarDocumentoHandler,
	ArquivarDocumentoHandler arquivarHandler,
	PublicarPublicacaoHandler publicarPublicacaoHandler,
	EditarPublicacaoHandler editarPublicacaoHandler,
	ArquivarPublicacaoHandler arquivarPublicacaoHandler,
	ListarPublicacoesDoSetorHandler listarPublicacoesDoSetorHandler,
	DocumentoOptions documentoOptions,
	IConfiguration configuration) : Controller
{
	/// <summary>Aba de documentos do setor.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("")]
	[HttpGet("documentos")]
	public async Task<IActionResult> Documentos(string slug, CancellationToken cancellationToken = default)
	{
		var model = await MontarAsync(slug, new DocumentoFormViewModel(), cancellationToken).ConfigureAwait(false);

		return model is null ? NotFound() : View(model);
	}

	/// <summary>Publica um documento no setor.</summary>
	/// <param name="slug">Slug do setor.</param>
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

		if (!PodePublicar(slug))
		{
			// Mesma resposta de setor inexistente: quem não administra o setor não deve
			// conseguir distinguir "não posso" de "não existe".
			return NotFound();
		}

		if (arquivo is null || arquivo.Length == 0)
		{
			ModelState.AddModelError(string.Empty, "Escolha um arquivo para publicar.");
		}

		if (!ModelState.IsValid)
		{
			var invalido = await MontarAsync(slug, form, cancellationToken).ConfigureAwait(false);

			return invalido is null ? NotFound() : View(nameof(Documentos), invalido);
		}

		await using var conteudo = arquivo!.OpenReadStream();

		var resultado = await publicarDocumentoHandler.HandleAsync(
			new PublicarDocumentoCommand(
				slug,
				form.Titulo,
				form.Descricao,
				arquivo.FileName,
				arquivo.Length,
				conteudo,
				form.Visibilidade,
				User.Identity?.Name ?? "desconhecido"),
			cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			var comErro = await MontarAsync(slug, form, cancellationToken).ConfigureAwait(false);

			return comErro is null ? NotFound() : View(nameof(Documentos), comErro);
		}

		TempData["Mensagem"] = $"Documento \"{resultado.Value.Titulo}\" publicado.";

		return RedirectToAction(nameof(Documentos), new { slug });
	}

	/// <summary>Retira um documento de circulação, preservando o registro e o arquivo.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("documentos/{id:guid}/arquivar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Arquivar(string slug, Guid id, CancellationToken cancellationToken = default)
	{
		var resultado = await arquivarHandler.HandleAsync(
			new ArquivarDocumentoCommand(
				id,
				SetorAcesso.SlugsAdministrados(User),
				ExigirVinculo: IntranetAuthenticationExtensions.IsConfigured(configuration)),
			cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		TempData["Mensagem"] = "Documento arquivado.";

		return RedirectToAction(nameof(Documentos), new { slug });
	}

	/// <summary>Aba de avisos do setor.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("avisos")]
	public async Task<IActionResult> Avisos(string slug, CancellationToken cancellationToken = default)
	{
		var model = await MontarAvisosAsync(slug, new PublicacaoFormViewModel(), cancellationToken)
			.ConfigureAwait(false);

		return model is null ? NotFound() : View(model);
	}

	/// <summary>Publica ou atualiza um aviso do setor.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("avisos")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SalvarAviso(
		string slug,
		PublicacaoFormViewModel form,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		if (!PodePublicar(slug))
		{
			return NotFound();
		}

		if (!ModelState.IsValid)
		{
			var invalido = await MontarAvisosAsync(slug, form, cancellationToken).ConfigureAwait(false);

			return invalido is null ? NotFound() : View(nameof(Avisos), invalido);
		}

		var exigirVinculo = IntranetAuthenticationExtensions.IsConfigured(configuration);
		var publicadoEm = new DateTimeOffset(form.PublicadoEm, DateTimeOffset.Now.Offset);
		DateTimeOffset? expiraEm = form.ExpiraEm is null
			? null
			: new DateTimeOffset(form.ExpiraEm.Value, DateTimeOffset.Now.Offset);

		var resultado = form.Id == Guid.Empty
			? await publicarPublicacaoHandler.HandleAsync(
				new PublicarPublicacaoCommand(slug, form.Titulo, form.Corpo, form.Tipo, form.Visibilidade,
					form.Prioridade, publicadoEm, expiraEm, User.Identity?.Name ?? "desconhecido"),
				cancellationToken).ConfigureAwait(false)
			: await editarPublicacaoHandler.HandleAsync(
				new EditarPublicacaoCommand(form.Id, SetorAcesso.SlugsAdministrados(User), exigirVinculo,
					form.Titulo, form.Corpo, form.Tipo, form.Visibilidade, form.Prioridade,
					publicadoEm, expiraEm),
				cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			var comErro = await MontarAvisosAsync(slug, form, cancellationToken).ConfigureAwait(false);

			return comErro is null ? NotFound() : View(nameof(Avisos), comErro);
		}

		TempData["Mensagem"] = $"Publicação \"{resultado.Value.Titulo}\" salva.";

		return RedirectToAction(nameof(Avisos), new { slug });
	}

	/// <summary>Tira um aviso de circulação.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador da publicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("avisos/{id:guid}/arquivar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ArquivarAviso(
		string slug,
		Guid id,
		CancellationToken cancellationToken = default)
	{
		var resultado = await arquivarPublicacaoHandler.HandleAsync(
			new ArquivarPublicacaoCommand(
				id,
				SetorAcesso.SlugsAdministrados(User),
				ExigirVinculo: IntranetAuthenticationExtensions.IsConfigured(configuration)),
			cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		TempData["Mensagem"] = "Publicação arquivada.";

		return RedirectToAction(nameof(Avisos), new { slug });
	}

	private async Task<SetorAvisosViewModel?> MontarAvisosAsync(
		string slug,
		PublicacaoFormViewModel form,
		CancellationToken cancellationToken)
	{
		var setor = await getSetorHandler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

		if (setor.IsFailure || !setor.Value.Ativo)
		{
			return null;
		}

		var publicacoes = await listarPublicacoesDoSetorHandler
			.HandleAsync(slug, cancellationToken)
			.ConfigureAwait(false);

		return new SetorAvisosViewModel(
			setor.Value,
			publicacoes.IsSuccess ? publicacoes.Value : [],
			PodePublicar(slug),
			form,
			DateTimeOffset.UtcNow);
	}

	private async Task<SetorDocumentosViewModel?> MontarAsync(
		string slug,
		DocumentoFormViewModel form,
		CancellationToken cancellationToken)
	{
		var setor = await getSetorHandler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

		if (setor.IsFailure || !setor.Value.Ativo)
		{
			return null;
		}

		var documentos = await listarHandler
			.HandleAsync(new ListarDocumentosQuery(slug), cancellationToken)
			.ConfigureAwait(false);

		return new SetorDocumentosViewModel(
			setor.Value,
			documentos.IsSuccess ? documentos.Value : [],
			PodePublicar(slug),
			form,
			documentoOptions.TamanhoMaximoBytes);
	}

	/// <summary>
	/// Publicar exige a Role <c>{slug}-admin</c> (ADR-0001). Sem autenticação configurada — o
	/// modo aberto de DEV — não há roles a consultar, e a checagem é dispensada.
	/// </summary>
	private bool PodePublicar(string slug) =>
		!IntranetAuthenticationExtensions.IsConfigured(configuration)
		|| SetorAcesso.AdministraSetor(User, slug);
}
