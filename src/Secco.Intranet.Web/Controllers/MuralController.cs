using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Web.Conteudo;
using Secco.Intranet.Web.Models.Mural;
using Secco.Intranet.Web.Navigation;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Mural de avisos: a página inicial e o único item fixo do menu. É só leitura — publicar
/// acontece na página do setor, porque a publicação pertence ao setor.
/// </summary>
/// <remarks>
/// O handler é resolvido dentro da ação, e não pelo construtor, pelo mesmo motivo do
/// <c>NavigationViewComponent</c>: injetá-lo construiria o <c>DbContext</c> do tenant, e esta
/// é a página inicial. Numa requisição sem tenant resolvido isso trocaria um mural vazio —
/// que é a resposta correta quando não há banco a consultar — por um erro na cara do usuário.
/// </remarks>
/// <param name="serviceProvider">Escopo da requisição, para resolver o handler sob demanda.</param>
/// <param name="tenantContext">Tenant da requisição atual.</param>
/// <param name="renderizador">Conversão do Markdown guardado em HTML.</param>
/// <param name="configuration">Configuração do host, para saber se a autenticação está ativa.</param>
/// <param name="environment">Ambiente de hospedagem — o modo aberto só vale em Development (ADR-0020).</param>
/// <param name="permissoesDeSetor">Em quais setores o usuário tem leitura/escrita (ADR-0021).</param>
/// <param name="logger">Log.</param>
public sealed class MuralController(
	IServiceProvider serviceProvider,
	ITenantContext tenantContext,
	IRenderizadorMarkdown renderizador,
	IConfiguration configuration,
	IWebHostEnvironment environment,
	IPermissoesDeSetor permissoesDeSetor,
	ILogger<MuralController> logger) : Controller
{
	private const int LimiteSetoresConsultados = 200;

	/// <summary>
	/// Setores em que o usuário tem leitura, para filtrar o que ele vê — vazio só no modo aberto
	/// de DEV local (Development sem autenticação configurada), quando o handler ignora o filtro
	/// por completo (<c>ExigirVisibilidade: false</c>). Fora do Development a lista vem sempre da
	/// permissão real.
	/// </summary>
	private async Task<IReadOnlyList<string>> SlugsComLeituraAsync(CancellationToken cancellationToken)
	{
		if (AcessoAdministrativo.ModoAbertoDeDev(environment, configuration))
		{
			return [];
		}

		var setores = await serviceProvider
			.GetRequiredService<SearchSetoresHandler>()
			.HandleAsync(new SetorSearchCriteria(ApenasAtivos: true, Page: new PageRequest(1, LimiteSetoresConsultados)), cancellationToken)
			.ConfigureAwait(false);

		if (setores.IsFailure)
		{
			return [];
		}

		var slugs = setores.Value.Items.Select(s => s.Slug).ToList();

		return [.. await permissoesDeSetor.SlugsComPermissaoAsync(User, "read", slugs, cancellationToken).ConfigureAwait(false)];
	}
	/// <summary>Lista as publicações no ar e visíveis para o leitor.</summary>
	/// <param name="tipo">Filtro por natureza (opcional).</param>
	/// <param name="page">Página desejada.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Index(
		TipoPublicacao? tipo,
		int page = 1,
		CancellationToken cancellationToken = default)
	{
		if (!tenantContext.IsResolved)
		{
			return View(new MuralViewModel(
				PagedResult.Empty<PublicacaoViewModel>(new PageRequest(page)), tipo));
		}

		var resultado = await serviceProvider
			.GetRequiredService<ListarMuralHandler>()
			.HandleAsync(
				new MuralQuery(
					tipo,
					await SlugsComLeituraAsync(cancellationToken).ConfigureAwait(false),
					ExigirVisibilidade: !AcessoAdministrativo.ModoAbertoDeDev(environment, configuration),
					page),
				cancellationToken)
			.ConfigureAwait(false);

		// A consulta não tem caminho de falha de negócio: o handler sempre devolve sucesso.
		var pagina = resultado.Value;

		var itens = pagina.Items
			.Select(publicacao => new PublicacaoViewModel(
				publicacao.Id,
				publicacao.Titulo,
				renderizador.Renderizar(publicacao.Corpo),
				publicacao.Tipo,
				publicacao.Prioridade,
				publicacao.PublicadoEm,
				publicacao.SetorNome,
				publicacao.SetorSlug))
			.ToList();

		return View(new MuralViewModel(
			PagedResult.Create(itens, new PageRequest(pagina.Page, pagina.Size), pagina.TotalCount),
			tipo));
	}

	/// <summary>Página de uma publicação — o destino do aviso e o endereço de compartilhamento.</summary>
	/// <param name="id">Identificador da publicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("/publicacoes/{id:guid}")]
	public async Task<IActionResult> Detalhe(Guid id, CancellationToken cancellationToken = default)
	{
		if (!tenantContext.IsResolved)
		{
			return NotFound();
		}

		var resultado = await serviceProvider
			.GetRequiredService<ObterPublicacaoHandler>()
			.HandleAsync(
				new ObterPublicacaoQuery(
					id,
					await SlugsComLeituraAsync(cancellationToken).ConfigureAwait(false),
					ExigirVisibilidade: !AcessoAdministrativo.ModoAbertoDeDev(environment, configuration)),
				cancellationToken)
			.ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		var publicacao = resultado.Value;

		// Preguiçoso por desenho: a pergunta só é feita por quem pode agir sobre a resposta.
		var administraOSetor = (await permissoesDeSetor
			.SlugsComPermissaoAsync(User, "write", [publicacao.SetorSlug], cancellationToken)
			.ConfigureAwait(false))
			.Contains(publicacao.SetorSlug);

		var entrega = administraOSetor
			? await serviceProvider
				.GetRequiredService<IConsultaDeEntregas>()
				.DaPublicacaoAsync(publicacao.Id, cancellationToken)
				.ConfigureAwait(false)
			: null;

		return View(new PublicacaoDetalheViewModel(
			new PublicacaoViewModel(
				publicacao.Id,
				publicacao.Titulo,
				renderizador.Renderizar(publicacao.Corpo),
				publicacao.Tipo,
				publicacao.Prioridade,
				publicacao.PublicadoEm,
				publicacao.SetorNome,
				publicacao.SetorSlug),
			entrega));
	}

	/// <summary>
	/// Entrega a imagem da publicação decifrada. <c>no-cache</c> + <c>ETag</c>: o navegador sempre
	/// revalida e leva 304 enquanto a imagem não muda; <c>private</c> impede cache compartilhado de
	/// um conteúdo autenticado. Fora do alcance, sem imagem e arquivo ausente respondem 404.
	/// </summary>
	/// <param name="id">Identificador da publicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("/publicacoes/{id:guid}/imagem")]
	public async Task<IActionResult> Imagem(Guid id, CancellationToken cancellationToken = default)
	{
		if (!tenantContext.IsResolved)
		{
			return NotFound();
		}

		var leitura = await SlugsComLeituraAsync(cancellationToken).ConfigureAwait(false);

		// A escrita só é perguntada para o setor dono, que o handler descobre; aqui ela vale para os
		// setores que o leitor já enxerga, e o handler a confere contra o slug da publicação.
		var comEscrita = AcessoAdministrativo.ModoAbertoDeDev(environment, configuration)
			? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			: await permissoesDeSetor
				.SlugsComPermissaoAsync(User, "write", leitura, cancellationToken)
				.ConfigureAwait(false);

		var resultado = await serviceProvider
			.GetRequiredService<ObterImagemDaPublicacaoHandler>()
			.HandleAsync(
				new ObterImagemDaPublicacaoQuery(
					id,
					leitura,
					comEscrita,
					ExigirVisibilidade: !AcessoAdministrativo.ModoAbertoDeDev(environment, configuration)),
				cancellationToken)
			.ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			return resultado.Error.Type == ErrorType.NotFound ? NotFound() : StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		var imagem = resultado.Value;

		Response.Headers.ETag = imagem.ETag;
		Response.Headers.CacheControl = "private, no-cache";
		Response.Headers.XContentTypeOptions = "nosniff";

		if (Request.Headers.IfNoneMatch.Any(valor => ETagConfere(valor, imagem.ETag)))
		{
			return StatusCode(StatusCodes.Status304NotModified);
		}

		// Decifra antes de escrever o primeiro byte: arquivo ausente ou corrompido vira 404, não 500 com
		// cabeçalhos pela metade. A imagem tem no máximo 2048 px em WebP, então cabe em memória.
		using var conteudo = new MemoryStream();

		try
		{
			await serviceProvider
				.GetRequiredService<IArquivoStore>()
				.EscreverEmAsync(imagem.CaminhoRelativo, imagem.ChaveEmbrulhada, conteudo, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception falha) when (falha is not OperationCanceledException)
		{
			// Só o tipo da exceção: a mensagem de falhas de I/O costuma trazer o caminho absoluto.
			logger.LogWarning("Não foi possível ler a imagem da publicação {PublicacaoId} do armazenamento ({TipoDaFalha}).", id, falha.GetType().Name);

			Response.Headers.Remove("ETag");

			return NotFound();
		}

		Response.ContentType = imagem.ContentType;
		conteudo.Position = 0;
		await conteudo.CopyToAsync(Response.Body, cancellationToken).ConfigureAwait(false);

		return new EmptyResult();
	}

	// If-None-Match pode trazer uma lista de validadores, fracos (W/) ou "*".
	private static bool ETagConfere(string? cabecalho, string etag) =>
		cabecalho is not null
		&& cabecalho.Split(',').Select(parte => parte.Trim()).Any(parte => parte == "*" || (parte.StartsWith("W/", StringComparison.Ordinal) ? parte[2..] : parte) == etag);
}
