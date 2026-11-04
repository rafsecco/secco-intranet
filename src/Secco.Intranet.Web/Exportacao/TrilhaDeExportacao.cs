using System.Text.Json;
using System.Text.RegularExpressions;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Web.Extensibility;

namespace Secco.Intranet.Web.Exportacao;

/// <summary>
/// Implementação de <see cref="ITrilhaDeExportacao"/> que as RCLs recebem do host (ADR-0013): valida
/// o que a RCL informa, monta o registro e o entrega à trilha obrigatória. O metadata leva formato,
/// linhas e filtros, nunca o conteúdo; cada valor de filtro é cortado, porque pode trazer dado
/// pessoal (ADR-0058 da plataforma).
/// </summary>
/// <param name="trilha">Trilha fail-closed.</param>
public sealed partial class TrilhaDeExportacao(ITrilhaObrigatoria trilha) : ITrilhaDeExportacao
{
	/// <summary>Maior valor de filtro guardado na trilha, o mesmo da exportação do LogStream.</summary>
	public const int TamanhoMaximoDoFiltro = 200;

	/// <inheritdoc />
	public async Task<bool> RegistrarAsync(ExportacaoParaAuditar exportacao, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(exportacao);

		if (exportacao.Recurso is null || !RecursoValido().IsMatch(exportacao.Recurso))
		{
			throw new ArgumentException(
				"O recurso deve ter de 1 a 64 caracteres, só [a-z0-9-].", nameof(exportacao));
		}

		var formato = exportacao.Formato?.ToLowerInvariant();

		if (formato is not ("xlsx" or "pdf"))
		{
			throw new ArgumentException("O formato deve ser 'xlsx' ou 'pdf'.", nameof(exportacao));
		}

		if (exportacao.Linhas < 0)
		{
			throw new ArgumentException("O número de linhas não pode ser negativo.", nameof(exportacao));
		}

		if (exportacao.Filtros is null)
		{
			throw new ArgumentException("Informe os filtros (vazio quando não há).", nameof(exportacao));
		}

		var filtros = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var filtro in exportacao.Filtros)
		{
			filtros[filtro.Nome] = Cortar(filtro.Valor ?? string.Empty);
		}

		var metadata = JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["formato"] = formato,
			["linhas"] = exportacao.Linhas,
			["filtros"] = filtros,
		});

		var registrado = await trilha.RegistrarAsync(
			new RegistroDeAuditoria(VerbosDeAuditoria.ExportacaoGerar, RecursosDeAuditoria.Exportacao, exportacao.Recurso, metadata),
			cancellationToken).ConfigureAwait(false);

		return registrado.IsSuccess;
	}

	private static string Cortar(string valor)
	{
		if (valor.Length <= TamanhoMaximoDoFiltro)
		{
			return valor;
		}

		// Não parte um par substituto ao meio: o JSON sairia com um caractere inválido.
		var tamanho = char.IsHighSurrogate(valor[TamanhoMaximoDoFiltro - 1]) ? TamanhoMaximoDoFiltro - 1 : TamanhoMaximoDoFiltro;

		return valor[..tamanho];
	}

	[GeneratedRegex(@"\A[a-z0-9-]{1,64}\z")]
	private static partial Regex RecursoValido();
}
