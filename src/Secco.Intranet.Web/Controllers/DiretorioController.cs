using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Web.Demonstracao;
using Secco.Intranet.Web.Models.Diretorio;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Diretório organizacional e perfil, em versão de demonstração. Diferente do Mural, aqui
/// não existe recurso real por trás: sem <c>Intranet:Demo:Habilitado</c> as rotas não
/// respondem, porque exibir pessoas inventadas numa intranet em uso seria pior do que não
/// ter a página.
/// </summary>
/// <param name="demoOptions">Estado das páginas de demonstração.</param>
[Route("diretorio")]
public sealed class DiretorioController(DemoOptions demoOptions) : Controller
{
	/// <summary>Grade de pessoas.</summary>
	/// <param name="busca">Trecho do nome, cargo ou setor a filtrar.</param>
	[HttpGet("")]
	public IActionResult Index(string? busca)
	{
		if (!demoOptions.Habilitado)
		{
			return NotFound();
		}

		var pessoas = DiretorioDemonstracao.Pessoas();

		if (!string.IsNullOrWhiteSpace(busca))
		{
			pessoas = pessoas
				.Where(pessoa =>
					pessoa.Nome.Contains(busca, StringComparison.OrdinalIgnoreCase)
					|| pessoa.Cargo.Contains(busca, StringComparison.OrdinalIgnoreCase)
					|| pessoa.SetorNome.Contains(busca, StringComparison.OrdinalIgnoreCase))
				.ToList();
		}

		return View(new DiretorioViewModel(pessoas, busca));
	}

	/// <summary>Perfil de quem está usando a Intranet.</summary>
	[HttpGet("perfil")]
	public IActionResult Perfil()
	{
		if (!demoOptions.Habilitado)
		{
			return NotFound();
		}

		var nome = User.Identity?.IsAuthenticated == true
			? User.Identity.Name ?? "Usuário"
			: "Visitante";

		return View(new PessoaViewModel(
			nome,
			"Analista de Infraestrutura",
			"Infraestrutura",
			"infraestrutura",
			"visitante@exemplo.local",
			"2210"));
	}
}
