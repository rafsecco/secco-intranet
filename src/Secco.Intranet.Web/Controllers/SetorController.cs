using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Web.Models;
using Secco.Intranet.Web.Models.Documentos;
using Secco.Intranet.Web.Models.Publicacoes;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Constants;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Página de um item da árvore de menu de um setor, em <c>/{setor}/{item}/…</c> na raiz da URL —
/// as rotas fixas do produto vencem por precedência, e <c>SlugsReservados</c> impede setor com o
/// nome delas. O menu principal é quem navega a árvore; aqui só se resolve o nó pelo tipo.
/// Separada de <c>SetoresController</c>, que é a administração do cadastro.
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
/// <param name="environment">Ambiente de hospedagem — o modo aberto só vale em Development (ADR-0020).</param>
/// <param name="searchSetores">Busca de setores, para saber o universo de slugs a checar.</param>
/// <param name="permissoesDeSetor">Em quais setores o usuário tem leitura/escrita (ADR-0021).</param>
/// <param name="resolverCaminho">Resolução de caminho na árvore de itens de menu do setor.</param>
[Route("{slug:" + SlugDeSetorRouteConstraint.Nome + "}")]
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
	IConfiguration configuration,
	IWebHostEnvironment environment,
	SearchSetoresHandler searchSetores,
	IPermissoesDeSetor permissoesDeSetor,
	ResolverCaminhoDeMenuHandler resolverCaminho) : Controller
{
	private const int LimiteSetoresConsultados = 200;

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
				await MontarAvisosAsync(setor.Value, resolvido.Value, new PublicacaoFormViewModel(), cancellationToken).ConfigureAwait(false)),
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
				User.Identity?.Name ?? "desconhecido"),
			cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			ModelState.AddModelError(string.Empty, resultado.Error.Description);

			return View("Documentos", await MontarDocumentosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = $"Documento \"{resultado.Value.Titulo}\" publicado.";

		return VoltarPara(slug, resolucao);
	}

	/// <summary>Retira um documento de circulação, preservando o registro e o arquivo.</summary>
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
			return NotFound();
		}

		TempData[FeedbackViewComponent.ChaveDaMensagem] = "Documento arquivado.";

		return VoltarPara(slug, alvo.Value.Resolucao);
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
			return View("Avisos", await MontarAvisosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
		}

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
					IdDoUsuarioAtual()),
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
					publicadoEm, expiraEm),
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

		return View("Avisos", await MontarAvisosAsync(setor, resolucao, form, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>
	/// Setor e posição na árvore do item de um tipo embutido. Nulo quando o setor não existe
	/// ou está inativo, ou quando o recurso está desligado (item ausente, ou ele/um ancestral
	/// desativado) — quem chama responde 404 nos três casos.
	/// </summary>
	private async Task<(SetorDto Setor, ResultadoDaResolucao Resolucao)?> ResolverTipoAsync(
		string slug, TipoDeItemMenu tipo, CancellationToken cancellationToken)
	{
		var setor = await getSetorHandler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

		if (setor.IsFailure || !setor.Value.Ativo)
		{
			return null;
		}

		var caminho = await resolverCaminho.CaminhoDoTipoAsync(setor.Value.Id, tipo, cancellationToken).ConfigureAwait(false);

		if (caminho is null)
		{
			return null;
		}

		var resolvido = await resolverCaminho.HandleAsync(setor.Value.Id, caminho, cancellationToken).ConfigureAwait(false);

		return resolvido.IsFailure ? null : (setor.Value, resolvido.Value);
	}

	private RedirectToActionResult VoltarPara(string slug, ResultadoDaResolucao resolucao) =>
		RedirectToAction(nameof(Resolver), new { slug, caminho = string.Join('/', resolucao.CaminhoCompleto) });

	/// <summary>"Financeiro › Relatórios": o setor e os ancestrais do nó, para o subtítulo da página.</summary>
	private static string Trilho(SetorDto setor, ResultadoDaResolucao resolucao) =>
		string.Join(" › ", [setor.Nome, .. resolucao.Ancestrais.Select(ancestral => ancestral.Nome)]);

	private async Task<SetorDocumentosViewModel> MontarDocumentosAsync(
		SetorDto setor, ResultadoDaResolucao resolucao, DocumentoFormViewModel form, CancellationToken cancellationToken)
	{
		var documentos = await listarHandler
			.HandleAsync(new ListarDocumentosQuery(setor.Slug), cancellationToken)
			.ConfigureAwait(false);

		return new SetorDocumentosViewModel(
			setor,
			documentos.IsSuccess ? documentos.Value : [],
			await PodePublicarAsync(setor.Slug, cancellationToken).ConfigureAwait(false),
			form,
			documentoOptions.TamanhoMaximoBytes,
			resolucao.No.Nome,
			Trilho(setor, resolucao));
	}

	private async Task<SetorAvisosViewModel> MontarAvisosAsync(
		SetorDto setor, ResultadoDaResolucao resolucao, PublicacaoFormViewModel form, CancellationToken cancellationToken)
	{
		var publicacoes = await listarPublicacoesDoSetorHandler
			.HandleAsync(setor.Slug, cancellationToken)
			.ConfigureAwait(false);

		return new SetorAvisosViewModel(
			setor,
			publicacoes.IsSuccess ? publicacoes.Value : [],
			await PodePublicarAsync(setor.Slug, cancellationToken).ConfigureAwait(false),
			form,
			DateTimeOffset.UtcNow,
			resolucao.No.Nome,
			Trilho(setor, resolucao));
	}

	/// <summary>
	/// Ler exige a permissão de leitura do setor (ADR-0021). Mesmo bypass de
	/// <see cref="PodePublicarAsync"/>: só em Development sem autenticação configurada (modo
	/// aberto de DEV local) não há permissão a resolver — consistente com o menu, que nesse modo
	/// mostra todo setor. Em Testing (que também não configura SecureGate) e em qualquer outro
	/// ambiente, o caminho de permissão de verdade roda sempre, para nunca ficar aberto por
	/// engano fora do Development (ADR-0020).
	/// </summary>
	private async Task<bool> PodeLerAsync(string slug, CancellationToken cancellationToken)
	{
		if (AcessoAdministrativo.ModoAbertoDeDev(environment, configuration))
		{
			return true;
		}

		var slugsComLeitura = await permissoesDeSetor
			.SlugsComPermissaoAsync(User, "read", [slug], cancellationToken)
			.ConfigureAwait(false);

		return slugsComLeitura.Contains(slug);
	}

	/// <summary>
	/// Publicar exige a permissão de escrita do setor (ADR-0021). Só em Development sem
	/// autenticação configurada — o modo aberto de DEV local — não há permissão a resolver, e a
	/// checagem é dispensada; fora do Development, mesmo sem SecureGate configurado, a permissão
	/// de verdade é sempre consultada (ADR-0020).
	/// </summary>
	private async Task<bool> PodePublicarAsync(string slug, CancellationToken cancellationToken)
	{
		if (AcessoAdministrativo.ModoAbertoDeDev(environment, configuration))
		{
			return true;
		}

		var slugsComEscrita = await permissoesDeSetor
			.SlugsComPermissaoAsync(User, "write", [slug], cancellationToken)
			.ConfigureAwait(false);

		return slugsComEscrita.Contains(slug);
	}

	/// <summary>
	/// Setores em que o usuário tem escrita — vazio só no modo aberto de DEV local (Development
	/// sem autenticação configurada), quando o handler ignora o filtro por completo
	/// (<c>ExigirVinculo: false</c>). Fora do Development a lista vem sempre da permissão real.
	/// </summary>
	private async Task<IReadOnlySet<string>> SlugsComEscritaAsync(CancellationToken cancellationToken)
	{
		if (AcessoAdministrativo.ModoAbertoDeDev(environment, configuration))
		{
			return new HashSet<string>();
		}

		var setores = await searchSetores
			.HandleAsync(new SetorSearchCriteria(ApenasAtivos: true, Page: new PageRequest(1, LimiteSetoresConsultados)), cancellationToken)
			.ConfigureAwait(false);

		if (setores.IsFailure)
		{
			return new HashSet<string>();
		}

		var slugs = setores.Value.Items.Select(s => s.Slug).ToList();

		return await permissoesDeSetor.SlugsComPermissaoAsync(User, "write", slugs, cancellationToken).ConfigureAwait(false);
	}
}
