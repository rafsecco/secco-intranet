using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Web.Authentication;
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
public sealed class MuralController(
	IServiceProvider serviceProvider,
	ITenantContext tenantContext,
	IRenderizadorMarkdown renderizador,
	IConfiguration configuration) : Controller
{
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
					[.. SetorAcesso.SlugsDoUsuario(User)],
					ExigirVisibilidade: IntranetAuthenticationExtensions.IsConfigured(configuration),
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
}
