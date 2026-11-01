using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Application.Vencimentos;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Web.Models;
using Secco.Intranet.Web.Models.Documentos;
using Secco.Intranet.Web.Models.Publicacoes;
using Secco.Intranet.Web.Models.Vencimentos;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Constants;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Página de um item da árvore de menu de um setor, em <c>/{setor}/{item}/…</c> na raiz da URL —
/// as rotas fixas do produto vencem por precedência, e <c>ISlugsReservados</c> impede setor com o
/// nome delas. O menu principal é quem navega a árvore; aqui só se resolve o nó pelo tipo.
/// Separada de <c>SetoresController</c>, que é a administração do cadastro.
/// </summary>
/// <param name="getSetorHandler">Leitura do setor pelo slug.</param>
/// <param name="listarHandler">Listagem de documentos do setor.</param>
/// <param name="publicarDocumentoHandler">Publicação de documento.</param>
/// <param name="arquivarHandler">Arquivamento de documento.</param>
/// <param name="obterParaEdicaoHandler">Leitura dos dados de um documento para o formulário de edição.</param>
/// <param name="editarHandler">Edição de documento e troca de arquivo.</param>
/// <param name="criarGrupoHandler">Criação de grupo de documentos.</param>
/// <param name="moverHandler">Movimento de documento na lista do setor.</param>
/// <param name="publicarPublicacaoHandler">Publicação de aviso.</param>
/// <param name="editarPublicacaoHandler">Edição de aviso.</param>
/// <param name="arquivarPublicacaoHandler">Arquivamento de aviso.</param>
/// <param name="listarPublicacoesDoSetorHandler">Listagem de avisos do setor.</param>
/// <param name="documentoOptions">Limites de upload.</param>
/// <param name="configuration">Configuração do host, para saber se a autenticação está ativa.</param>
/// <param name="environment">Ambiente de hospedagem — o modo aberto só vale em Development (ADR-0020).</param>
/// <param name="listarVencimentosHandler">Listagem de vencimentos do setor.</param>
/// <param name="responsaveisHandler">Nomes de quem pode ser responsável, para a coluna da lista.</param>
/// <param name="acesso">Checagens de leitura, escrita e recurso ligado, compartilhadas com os demais controllers de recurso do setor.</param>
/// <param name="resolverCaminho">Resolução de caminho na árvore de itens de menu do setor.</param>
[Route("{slug:" + SlugDeSetorRouteConstraint.Nome + "}")]
public sealed class SetorController(
	GetSetorBySlugHandler getSetorHandler,
	ListarDocumentosHandler listarHandler,
	PublicarDocumentoHandler publicarDocumentoHandler,
	ArquivarDocumentoHandler arquivarHandler,
	ObterDocumentoParaEdicaoHandler obterParaEdicaoHandler,
	EditarDocumentoHandler editarHandler,
	CriarGrupoDeDocumentosHandler criarGrupoHandler,
	MoverDocumentoHandler moverHandler,
	PublicarPublicacaoHandler publicarPublicacaoHandler,
	EditarPublicacaoHandler editarPublicacaoHandler,
	ArquivarPublicacaoHandler arquivarPublicacaoHandler,
	ListarPublicacoesDoSetorHandler listarPublicacoesDoSetorHandler,
	DocumentoOptions documentoOptions,
	IConfiguration configuration,
	IWebHostEnvironment environment,
	ListarVencimentosDoSetorHandler listarVencimentosHandler,
	ListarResponsaveisPossiveisHandler responsaveisHandler,
	AcessoAoRecursoDoSetor acesso,
	ResolverCaminhoDeMenuHandler resolverCaminho) : Controller
{
	/// <summary>
	/// Qualquer nó da árvore do setor: renderiza pelo tipo; nó que só agrupa responde 404.
	/// </summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="caminho">Slugs da árvore separados por barra; vazio é a raiz.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{**caminho}")]
	public async Task<IActionResult> Resolver(string slug, string? caminho, CancellationToken cancellationToken = default)
	{
		var setor = await getSetorHandler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

		if (setor.IsFailure || !setor.Value.Ativo || !await PodeLerAsync(slug, cancellationToken).ConfigureAwait(false))
		{
			// Sem permissão responde igual a inexistente — não revela que o setor existe.
			return NotFound();
		}

		IReadOnlyList<string> segmentos = string.IsNullOrWhiteSpace(caminho)
			? []
			: caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);

		var resolvido = await resolverCaminho.HandleAsync(setor.Value.Id, segmentos, cancellationToken).ConfigureAwait(false);

		if (resolvido.IsFailure)
		{
			return NotFound();
		}

		var no = resolvido.Value.No;

		return no.Tipo switch
		{
			TipoDeItemMenu.Documentos => View(
				"Documentos",
				await MontarDocumentosAsync(setor.Value, resolvido.Value, new DocumentoFormViewModel(), cancellationToken).ConfigureAwait(false)),
			TipoDeItemMenu.Avisos => View(
				"Avisos",
				await MontarAvisosAsync(setor.Value, resolvido.Value, new PublicacaoFormViewModel(), cancellationToken, EditarSolicitado()).ConfigureAwait(false)),
			TipoDeItemMenu.Vencimentos => View(
				"Vencimentos",
				await MontarVencimentosAsync(setor.Value, resolvido.Value, cancellationToken).ConfigureAwait(false)),
			// Rota validada na criação do item: caminho local ou http(s) absoluto.
			TipoDeItemMenu.Personalizado when !string.IsNullOrWhiteSpace(no.Rota) => Redirect(no.Rota),
			// A raiz e o Personalizado sem rota só agrupam: o menu não oferece link para eles.
			_ => NotFound(),
		};
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

		if (!await PodePublicarAsync(slug, cancellationToken).ConfigureAwait(false))
		{
			// Mesma resposta de setor inexistente: quem não administra o setor não deve
			// conseguir distinguir "não posso" de "não existe".
			return NotFound();
		}

		// Recurso desligado também é 404 — antes de validar o formulário, para campos
		// faltando não mascararem a recusa.
		var alvo = await ResolverTipoAsync(slug, TipoDeItemMenu.Documentos, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var (setor, resolucao) = alvo.Value;

		if (arquivo is null || arquivo.Length == 0)
		{
			ModelState.AddModelError(string.Empty, "Escolha um arquivo para publicar.");
		}

		if (!ModelState.IsValid)
		{
			return View("Documentos", await MontarDocumentosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
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
				User.Identity?.Name ?? "desconhecido",
				form.ParentId),
			cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View("Documentos", await MontarDocumentosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Documento \"{resultado.Value.Titulo}\" publicado.";

		return VoltarPara(slug, resolucao);
	}

	/// <summary>Tela de organizar: ordem, hierarquia, grupos e arquivamento dos documentos do setor.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("documentos/organizar")]
	public async Task<IActionResult> Organizar(string slug, CancellationToken cancellationToken = default)
	{
		var alvo = await ResolverEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		return View(
			"OrganizarDocumentos",
			await MontarOrganizarAsync(alvo.Value.Setor, alvo.Value.Resolucao, new GrupoFormViewModel(), cancellationToken)
				.ConfigureAwait(false));
	}

	/// <summary>Formulário de edição de um documento ou grupo do setor.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("documentos/{id:guid}/editar")]
	public async Task<IActionResult> EditarDocumento(string slug, Guid id, CancellationToken cancellationToken = default)
	{
		var alvo = await ResolverEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var documento = await obterParaEdicaoHandler.HandleAsync(
			new ObterDocumentoParaEdicaoQuery(
				id,
				slug,
				await SlugsComEscritaAsync(cancellationToken).ConfigureAwait(false),
				ExigirVinculo: !AcessoAdministrativo.ModoAbertoDeDev(environment, configuration)),
			cancellationToken).ConfigureAwait(false);

		if (documento.IsFailure)
		{
			return NotFound();
		}

		var dto = documento.Value;

		return View(
			"EditarDocumento",
			MontarEdicao(
				alvo.Value.Setor,
				alvo.Value.Resolucao,
				dto.Id,
				dto.EhGrupo,
				dto.NomeArquivo,
				new EditarDocumentoFormViewModel
				{
					Titulo = dto.Titulo,
					Descricao = dto.Descricao,
					Visibilidade = dto.Visibilidade,
				}));
	}

	/// <summary>Salva a edição de um documento ou grupo, trocando o arquivo quando um novo é enviado.</summary>
	/// <param name="slug">Slug do setor.</param>
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

		if (!await PodePublicarAsync(slug, cancellationToken).ConfigureAwait(false))
		{
			return NotFound();
		}

		var alvo = await ResolverTipoAsync(slug, TipoDeItemMenu.Documentos, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var administrados = await SlugsComEscritaAsync(cancellationToken).ConfigureAwait(false);
		var exigirVinculo = !AcessoAdministrativo.ModoAbertoDeDev(environment, configuration);

		// O formulário precisa do nome do arquivo atual e de saber se é grupo, que não vêm do POST.
		var atual = await obterParaEdicaoHandler.HandleAsync(
			new ObterDocumentoParaEdicaoQuery(id, slug, administrados, exigirVinculo),
			cancellationToken).ConfigureAwait(false);

		if (atual.IsFailure)
		{
			return NotFound();
		}

		var (setor, resolucao) = alvo.Value;

		IActionResult ComErro() => View(
			"EditarDocumento",
			MontarEdicao(setor, resolucao, id, atual.Value.EhGrupo, atual.Value.NomeArquivo, form));

		if (!ModelState.IsValid)
		{
			return ComErro();
		}

		var enviado = arquivo is { Length: > 0 };
		await using var conteudo = enviado ? arquivo!.OpenReadStream() : null;

		var resultado = await editarHandler.HandleAsync(
			new EditarDocumentoCommand(
				id,
				slug,
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
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="acao">Movimento pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("documentos/{id:guid}/mover")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> MoverDocumento(
		string slug, Guid id, MovimentoDeDocumento acao, CancellationToken cancellationToken = default)
	{
		var alvo = await ResolverEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var resultado = await moverHandler
			.HandleAsync(new MoverDocumentoCommand(slug, id, acao), cancellationToken)
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

	/// <summary>Cria um grupo no topo da lista de documentos do setor.</summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="form">Dados do formulário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("documentos/grupos")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> CriarGrupo(
		string slug, GrupoFormViewModel form, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(form);

		var alvo = await ResolverEscritaAsync(slug, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var (setor, resolucao) = alvo.Value;

		if (ModelState.IsValid)
		{
			var resultado = await criarGrupoHandler
				.HandleAsync(
					new CriarGrupoDeDocumentosCommand(slug, form.Titulo, form.Descricao, User.Identity?.Name ?? "desconhecido"),
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
			await MontarOrganizarAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>
	/// Retira um documento de circulação, preservando o registro e o arquivo. Sempre volta para a
	/// tela de organizar, que é o único lugar com o botão.
	/// </summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost("documentos/{id:guid}/arquivar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Arquivar(string slug, Guid id, CancellationToken cancellationToken = default)
	{
		var alvo = await ResolverTipoAsync(slug, TipoDeItemMenu.Documentos, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var resultado = await arquivarHandler.HandleAsync(
			new ArquivarDocumentoCommand(
				id,
				await SlugsComEscritaAsync(cancellationToken).ConfigureAwait(false),
				ExigirVinculo: !AcessoAdministrativo.ModoAbertoDeDev(environment, configuration)),
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

		if (!await PodePublicarAsync(slug, cancellationToken).ConfigureAwait(false))
		{
			return NotFound();
		}

		var alvo = await ResolverTipoAsync(slug, TipoDeItemMenu.Avisos, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var (setor, resolucao) = alvo.Value;

		if (!ModelState.IsValid)
		{
			PedirAImagemDeNovo(form);

			return View("Avisos", await MontarAvisosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
		}

		// O navegador manda um arquivo vazio quando nada foi escolhido: isso é "sem imagem".
		await using var arquivo = form.Imagem is { Length: > 0 } ? form.Imagem.OpenReadStream() : null;
		var imagem = arquivo is null ? null : new ImagemEnviada(arquivo);

		var exigirVinculo = !AcessoAdministrativo.ModoAbertoDeDev(environment, configuration);
		var publicadoEm = new DateTimeOffset(form.PublicadoEm, DateTimeOffset.Now.Offset);
		DateTimeOffset? expiraEm = form.ExpiraEm is null
			? null
			: new DateTimeOffset(form.ExpiraEm.Value, DateTimeOffset.Now.Offset);

		// Publicar e editar deixaram de devolver o mesmo tipo: publicar traz o relatório do
		// aviso junto, e editar nunca notifica.
		string titulo;
		RelatorioDeNotificacao? relatorio = null;

		if (form.Id == Guid.Empty)
		{
			var publicado = await publicarPublicacaoHandler.HandleAsync(
				new PublicarPublicacaoCommand(slug, form.Titulo, form.Corpo, form.Tipo, form.Visibilidade,
					form.Prioridade, publicadoEm, expiraEm, User.Identity?.Name ?? "desconhecido",
					IdDoUsuarioAtual(), imagem, form.ImagemDescricao),
				cancellationToken).ConfigureAwait(false);

			if (publicado.IsFailure)
			{
				return await ComErroAsync(setor, resolucao, form, publicado.Error.Description, cancellationToken)
					.ConfigureAwait(false);
			}

			titulo = publicado.Value.Publicacao.Titulo;
			relatorio = publicado.Value.Notificacao;
		}
		else
		{
			var editado = await editarPublicacaoHandler.HandleAsync(
				new EditarPublicacaoCommand(
					form.Id, await SlugsComEscritaAsync(cancellationToken).ConfigureAwait(false), exigirVinculo,
					form.Titulo, form.Corpo, form.Tipo, form.Visibilidade, form.Prioridade,
					publicadoEm, expiraEm, imagem, form.ImagemDescricao, form.RemoverImagem),
				cancellationToken).ConfigureAwait(false);

			if (editado.IsFailure)
			{
				return await ComErroAsync(setor, resolucao, form, editado.Error.Description, cancellationToken)
					.ConfigureAwait(false);
			}

			titulo = editado.Value.Titulo;
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = MensagemDeSalvamento.Montar(titulo, relatorio);

		return VoltarPara(slug, resolucao);
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
		var alvo = await ResolverTipoAsync(slug, TipoDeItemMenu.Avisos, cancellationToken).ConfigureAwait(false);

		if (alvo is null)
		{
			return NotFound();
		}

		var resultado = await arquivarPublicacaoHandler.HandleAsync(
			new ArquivarPublicacaoCommand(
				id,
				await SlugsComEscritaAsync(cancellationToken).ConfigureAwait(false),
				ExigirVinculo: !AcessoAdministrativo.ModoAbertoDeDev(environment, configuration)),
			cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = "Publicação arquivada.";

		return VoltarPara(slug, alvo.Value.Resolucao);
	}

	private async Task<SetorVencimentosViewModel> MontarVencimentosAsync(
		SetorDto setor, ResultadoDaResolucao resolucao, CancellationToken cancellationToken)
	{
		var incluiEncerrados = string.Equals(Request.Query["encerrados"], "true", StringComparison.OrdinalIgnoreCase);

		var itens = await listarVencimentosHandler
			.HandleAsync(setor.Id, incluiEncerrados, cancellationToken)
			.ConfigureAwait(false);

		var responsaveis = await responsaveisHandler.HandleAsync(setor.Slug, cancellationToken).ConfigureAwait(false);

		return new SetorVencimentosViewModel(
			setor,
			itens.IsSuccess ? itens.Value : [],
			responsaveis.ToDictionary(responsavel => responsavel.Id, responsavel => responsavel.Nome),
			await PodePublicarAsync(setor.Slug, cancellationToken).ConfigureAwait(false),
			incluiEncerrados,
			resolucao.No.Nome,
			Trilho(setor, resolucao));
	}

	/// <summary>
	/// Identificador do usuário atual, quando a autenticação está ativa. Sem ele — o modo
	/// aberto de DEV — ninguém é excluído do próprio aviso, o que é inofensivo.
	/// </summary>
	private Guid? IdDoUsuarioAtual() =>
		Guid.TryParse(User.FindFirst(SeccoClaims.Subject)?.Value, out var id) ? id : null;

	private async Task<IActionResult> ComErroAsync(
		SetorDto setor,
		ResultadoDaResolucao resolucao,
		PublicacaoFormViewModel form,
		string mensagem,
		CancellationToken cancellationToken)
	{
		ModelState.AddModelError(string.Empty, mensagem);
		PedirAImagemDeNovo(form);

		return View("Avisos", await MontarAvisosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
	}

	private Task<(SetorDto Setor, ResultadoDaResolucao Resolucao)?> ResolverTipoAsync(
		string slug, TipoDeItemMenu tipo, CancellationToken cancellationToken) =>
		acesso.ResolverTipoAsync(slug, tipo, cancellationToken);

	private RedirectToActionResult VoltarPara(string slug, ResultadoDaResolucao resolucao) =>
		RedirectToAction(nameof(Resolver), new { slug, caminho = string.Join('/', resolucao.CaminhoCompleto) });

	/// <summary>"Financeiro › Relatórios": o setor e os ancestrais do nó, para o subtítulo da página.</summary>
	private static string Trilho(SetorDto setor, ResultadoDaResolucao resolucao) =>
		AcessoAoRecursoDoSetor.Trilho(setor, resolucao);

	private async Task<SetorDocumentosViewModel> MontarDocumentosAsync(
		SetorDto setor, ResultadoDaResolucao resolucao, DocumentoFormViewModel form, CancellationToken cancellationToken)
	{
		var documentos = await listarHandler
			.HandleAsync(new ListarDocumentosQuery(setor.Slug), cancellationToken)
			.ConfigureAwait(false);

		IReadOnlyList<NoDeDocumentoDto> nos = documentos.IsSuccess ? documentos.Value : [];

		return new SetorDocumentosViewModel(
			setor,
			ListaDeDocumentosFactory.Montar(nos, id => Url.Action("Download", "Documentos", new { id })!),
			await PodePublicarAsync(setor.Slug, cancellationToken).ConfigureAwait(false),
			form,
			documentoOptions.TamanhoMaximoBytes,
			resolucao.No.Nome,
			Trilho(setor, resolucao),
			[.. nos.Select(no => new OpcaoAbaixoDe(no.Documento.Id, no.Documento.Titulo))]);
	}

	/// <summary>Setor e item de Documentos, só para quem escreve; quem não pode recebe o mesmo que setor inexistente.</summary>
	private async Task<(SetorDto Setor, ResultadoDaResolucao Resolucao)?> ResolverEscritaAsync(
		string slug, CancellationToken cancellationToken) =>
		await PodePublicarAsync(slug, cancellationToken).ConfigureAwait(false)
			? await ResolverTipoAsync(slug, TipoDeItemMenu.Documentos, cancellationToken).ConfigureAwait(false)
			: null;

	private EditarDocumentoViewModel MontarEdicao(
		SetorDto setor, ResultadoDaResolucao resolucao, Guid id, bool ehGrupo, string? nomeArquivo,
		EditarDocumentoFormViewModel form) =>
		new(setor, id, ehGrupo, nomeArquivo, form, Url.Action(nameof(Organizar), new { slug = setor.Slug })!);

	private async Task<OrganizarDocumentosViewModel> MontarOrganizarAsync(
		SetorDto setor, ResultadoDaResolucao resolucao, GrupoFormViewModel form, CancellationToken cancellationToken)
	{
		var documentos = await listarHandler
			.HandleAsync(new ListarDocumentosQuery(setor.Slug), cancellationToken)
			.ConfigureAwait(false);

		IReadOnlyList<NoDeDocumentoDto> nos = documentos.IsSuccess ? documentos.Value : [];

		return new OrganizarDocumentosViewModel(
			setor,
			resolucao.No.Nome,
			Trilho(setor, resolucao),
			Url.Action(nameof(Resolver), new { slug = setor.Slug, caminho = string.Join('/', resolucao.CaminhoCompleto) })!,
			OrganizarDocumentosViewModel.MontarLinhas(nos),
			form);
	}

	/// <summary>
	/// O navegador não reenvia o arquivo quando o formulário volta com erro: se havia um escolhido,
	/// avisa para escolher de novo, além do erro que já está no <c>ModelState</c>.
	/// </summary>
	private void PedirAImagemDeNovo(PublicacaoFormViewModel form)
	{
		if (form.Imagem is { Length: > 0 })
		{
			ModelState.AddModelError(string.Empty, "Escolha a imagem de novo.");
		}
	}

	/// <summary>Identificador pedido em <c>?editar=</c>, quando válido.</summary>
	private Guid? EditarSolicitado() =>
		Guid.TryParse(Request.Query["editar"], out var id) && id != Guid.Empty ? id : null;

	private async Task<SetorAvisosViewModel> MontarAvisosAsync(
		SetorDto setor, ResultadoDaResolucao resolucao, PublicacaoFormViewModel form, CancellationToken cancellationToken,
		Guid? editar = null)
	{
		var publicacoes = await listarPublicacoesDoSetorHandler
			.HandleAsync(setor.Slug, cancellationToken)
			.ConfigureAwait(false);

		var lista = publicacoes.IsSuccess ? publicacoes.Value : [];
		var podePublicar = await PodePublicarAsync(setor.Slug, cancellationToken).ConfigureAwait(false);

		// Só a lista do próprio setor é consultada: id de outro setor ou inexistente cai no form vazio.
		if (editar is not null && podePublicar)
		{
			var alvo = lista.FirstOrDefault(p => p.Id == editar && p.Ativo);

			if (alvo is not null)
			{
				form = new PublicacaoFormViewModel
				{
					Id = alvo.Id,
					Titulo = alvo.Titulo,
					Corpo = alvo.Corpo,
					Tipo = alvo.Tipo,
					Visibilidade = alvo.Visibilidade,
					Prioridade = alvo.Prioridade,
					PublicadoEm = alvo.PublicadoEm.ToLocalTime().DateTime,
					ExpiraEm = alvo.ExpiraEm?.ToLocalTime().DateTime,
					ImagemDescricao = alvo.ImagemDescricao,
				};
			}
		}

		// Recalculado do que está guardado, nunca do que veio no formulário.
		form.TemImagem = form.Id != Guid.Empty && lista.Any(p => p.Id == form.Id && p.TemImagem);

		return new SetorAvisosViewModel(
			setor,
			lista,
			podePublicar,
			form,
			DateTimeOffset.UtcNow,
			resolucao.No.Nome,
			Trilho(setor, resolucao),
			Url.Action(nameof(Resolver), new { slug = setor.Slug, caminho = string.Join('/', resolucao.CaminhoCompleto) })!);
	}

	private Task<bool> PodeLerAsync(string slug, CancellationToken cancellationToken) =>
		acesso.PodeLerAsync(User, slug, cancellationToken);

	private Task<bool> PodePublicarAsync(string slug, CancellationToken cancellationToken) =>
		acesso.PodeEscreverAsync(User, slug, cancellationToken);

	private Task<IReadOnlySet<string>> SlugsComEscritaAsync(CancellationToken cancellationToken) =>
		acesso.SlugsComEscritaAsync(User, cancellationToken);
}
