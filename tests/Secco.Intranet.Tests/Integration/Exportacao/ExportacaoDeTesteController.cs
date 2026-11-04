using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Secco.Intranet.Web.Extensibility;
using Secco.SDK.Documents;

namespace Secco.Intranet.Tests.Integration.Exportacao;

/// <summary>
/// Faz o papel de um controller de RCL externa que exporta (ADR-0013). O assembly de testes é
/// application part do host (<see cref="IntranetWebFactory"/>), então ele é descoberto como o de um
/// projeto referenciado, e só injeta o que o host registrou. Segue a ordem do guia: gera, registra
/// na trilha e só então envia.
/// </summary>
/// <param name="exportador">Exportador do <c>Secco.SDK.Documents</c>.</param>
/// <param name="trilha">Trilha de exportação do host.</param>
/// <param name="antiforgery">Gerador do token, para o GET devolver um.</param>
public sealed class ExportacaoDeTesteController(
	ISeccoDocumentExporter exportador,
	ITrilhaDeExportacao trilha,
	IAntiforgery antiforgery) : Controller
{
	/// <summary>Recurso registrado na trilha.</summary>
	public const string Recurso = "relatorio-de-teste";

	/// <summary>Caminho do GET (token) e do POST (exportação).</summary>
	public const string Caminho = "/exportacao-de-teste";

	/// <summary>No lugar da tela com o formulário: devolve o token antiforgery e grava o cookie.</summary>
	[HttpGet(Caminho)]
	public IActionResult Token() => Content(antiforgery.GetAndStoreTokens(HttpContext).RequestToken!);

	/// <summary>Exporta três linhas fixas.</summary>
	/// <param name="formato"><c>xlsx</c> ou <c>pdf</c>.</param>
	/// <param name="filtro">Filtro da "tela", só para ir à trilha.</param>
	[HttpPost(Caminho)]
	[ValidateAntiForgeryToken]
	[EnableRateLimiting(PoliticaDeExportacao.Nome)]
	public async Task<IResult> Exportar([FromForm] string formato, [FromForm] string? filtro)
	{
		var formatoDoArquivo = formato == "pdf" ? ExportFormat.Pdf : ExportFormat.Xlsx;
		var filtros = new List<ExportFilter> { new("Filtro", filtro ?? string.Empty) };

		var gerado = await exportador.ExportAsync(
			new ExportRequest<string>(
				formatoDoArquivo,
				"Relatório de teste",
				Recurso,
				new ExportColumns<string>().Text("Nome", linha => linha),
				Linhas())
			{
				Filters = filtros,
			},
			HttpContext.RequestAborted);

		if (gerado.IsFailure)
		{
			return Results.Redirect(Caminho);
		}

		var documento = gerado.Value;

		try
		{
			var registrado = await trilha.RegistrarAsync(
				new ExportacaoParaAuditar(
					Recurso,
					formato,
					documento.RowCount,
					filtros.Select(f => new FiltroDeExportacao(f.Name, f.Value)).ToList()),
				HttpContext.RequestAborted);

			if (!registrado)
			{
				await documento.DisposeAsync();

				return Results.Redirect(Caminho);
			}
		}
		catch
		{
			await documento.DisposeAsync();
			throw;
		}

		return documento.ToHttpResult();
	}

	private static async IAsyncEnumerable<string> Linhas()
	{
		foreach (var nome in new[] { "Ana", "Bruno", "Carla" })
		{
			await Task.Yield();
			yield return nome;
		}
	}
}
