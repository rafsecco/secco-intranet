using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Web.Demonstracao;
using Secco.Intranet.Web.Models.Mural;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Mural de avisos: a página inicial da Intranet e o único item de menu que fala com a
/// empresa toda, em vez de pertencer a um setor.
/// </summary>
/// <remarks>
/// A rota é permanente — o recurso real chega na Fase 1 do roadmap. Enquanto isso, o
/// conteúdo vem da demonstração quando ela está ligada; desligada, a página mostra o estado
/// vazio, que é o comportamento correto de um mural sem publicações.
/// </remarks>
/// <param name="demoOptions">Estado das páginas de demonstração.</param>
public sealed class MuralController(DemoOptions demoOptions) : Controller
{
	/// <summary>Lista as publicações do mural.</summary>
	/// <param name="tipo">Filtro por natureza da publicação (opcional).</param>
	[HttpGet]
	public IActionResult Index(TipoPublicacao? tipo)
	{
		var publicacoes = demoOptions.Habilitado
			? MuralDemonstracao.Publicacoes(DateTimeOffset.Now)
			: [];

		if (tipo is not null)
		{
			publicacoes = publicacoes.Where(publicacao => publicacao.Tipo == tipo).ToList();
		}

		return View(new MuralViewModel(publicacoes, tipo, demoOptions.Habilitado));
	}
}
