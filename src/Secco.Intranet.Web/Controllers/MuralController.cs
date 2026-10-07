using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Web.Conteudo;
using Secco.Intranet.Web.Models.Mural;
using Secco.Intranet.Web.Navigation;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Pagination;

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
public sealed class MuralController(
	IServiceProvider serviceProvider,
	ITenantContext tenantContext,
	IRenderizadorMarkdown renderizador,
	IConfiguration configuration,
	IWebHostEnvironment environment,
	IPermissoesDeSetor permissoesDeSetor) : Controller
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
}
