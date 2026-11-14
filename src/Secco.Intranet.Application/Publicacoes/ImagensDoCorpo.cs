using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>
/// Imagens no meio do texto: lê os destinos do Markdown, aceita ou recusa cada um, grava as
/// novas e reescreve os marcadores <c>nova-imagem:N</c> para o caminho definitivo. Compartilhado
/// por publicar e editar para as duas operações não divergirem.
/// </summary>
internal static partial class ImagensDoCorpo
{
	/// <summary>Máximo de imagens relativas (novas e existentes) no corpo.</summary>
	internal const int Limite = 10;

	private const string EsquemaDoMarcador = "nova-imagem:";

	// Mesmo pipeline do RenderizadorMarkdown, mais a posição exata do destino.
	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
		.DisableHtml()
		.UsePipeTables()
		.UseEmphasisExtras(EmphasisExtraOptions.Inserted)
		.UsePreciseSourceLocation()
		.Build();

	/// <summary>Rota da imagem no texto de uma publicação.</summary>
	/// <param name="publicacaoId">Publicação dona.</param>
	/// <param name="imagemId">Imagem.</param>
	internal static string CaminhoDaImagemNoTexto(Guid publicacaoId, Guid imagemId) =>
		$"/publicacoes/{publicacaoId}/imagens/{imagemId}";

	/// <summary>
	/// Classifica os destinos de imagem do corpo, sem efeito colateral. Falha na primeira imagem
	/// recusada, ou quando passa do limite.
	/// </summary>
	/// <param name="corpo">Corpo em Markdown.</param>
	/// <param name="publicacaoId">Publicação dona do corpo.</param>
	/// <param name="idsExistentes">Imagens já gravadas da publicação.</param>
	/// <param name="arquivos">Quantidade de arquivos enviados junto.</param>
	internal static Result<AnaliseDoCorpo> Analisar(
		string? corpo,
		Guid publicacaoId,
		IReadOnlySet<Guid> idsExistentes,
		int arquivos)
	{
		var marcadores = new List<(int Inicio, int Tamanho, int Indice)>();
		var novas = new SortedSet<int>();
		var existentes = new HashSet<Guid>();

		if (!string.IsNullOrEmpty(corpo))
		{
			foreach (var link in Markdown.Parse(corpo, Pipeline).Descendants<LinkInline>().Where(l => l.IsImage))
			{
				var destino = link.Url ?? string.Empty;

				if (destino.StartsWith(EsquemaDoMarcador, StringComparison.Ordinal))
				{
					if (!int.TryParse(destino.AsSpan(EsquemaDoMarcador.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var indice))
					{
						return IntranetErrors.Publicacoes.ImagemNoTextoInvalida;
					}

					if (indice >= arquivos)
					{
						return IntranetErrors.Publicacoes.ImagemNoTextoSemArquivo;
					}

					var span = link.UrlSpan;

					if (span.IsEmpty || span.End >= corpo.Length || corpo.Substring(span.Start, span.Length) != destino)
					{
						// Sem posição exata (por exemplo, definição de referência) não dá para reescrever.
						return IntranetErrors.Publicacoes.ImagemNoTextoInvalida;
					}

					marcadores.Add((span.Start, span.Length, indice));
					novas.Add(indice);

					continue;
				}

				if (RotaDeImagemRegex().Match(destino) is { Success: true } rota)
				{
					if (!Guid.TryParse(rota.Groups[1].Value, out var dona)
						|| !Guid.TryParse(rota.Groups[2].Value, out var imagemId)
						|| dona != publicacaoId
						|| !idsExistentes.Contains(imagemId))
					{
						return IntranetErrors.Publicacoes.ImagemNoTextoInvalida;
					}

					existentes.Add(imagemId);

					continue;
				}

				if (EhHttp(destino))
				{
					continue;
				}

				return IntranetErrors.Publicacoes.ImagemNoTextoInvalida;
			}
		}

		if (novas.Count + existentes.Count > Limite)
		{
			return IntranetErrors.Publicacoes.ImagensNoTextoDemais;
		}

		// O caminho definitivo é mais longo que o marcador: o corpo reescrito ainda precisa caber na coluna.
		var tamanhoDoCaminho = CaminhoDaImagemNoTexto(publicacaoId, Guid.Empty).Length;

		return (corpo?.Length ?? 0) + marcadores.Sum(m => tamanhoDoCaminho - m.Tamanho) > ValidacaoDePublicacao.LimiteDoCorpo
			? IntranetErrors.Publicacoes.CorpoTooLong
			: new AnaliseDoCorpo(corpo, marcadores, novas.ToList(), existentes);
	}

	/// <summary>
	/// Grava as imagens novas referenciadas, uma de cada vez, e devolve o corpo reescrito. Se uma
	/// falhar, apaga as já gravadas por esta chamada.
	/// </summary>
	/// <param name="analise">Resultado de <see cref="Analisar"/>.</param>
	/// <param name="publicacaoId">Publicação dona.</param>
	/// <param name="enviadas">Arquivos enviados; o índice é o N do marcador.</param>
	/// <param name="processador">Porta de processamento.</param>
	/// <param name="arquivos">Armazenamento cifrado.</param>
	/// <param name="logger">Log.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	internal static async Task<Result<CorpoPreparado>> GravarEReescreverAsync(
		AnaliseDoCorpo analise,
		Guid publicacaoId,
		IReadOnlyList<ImagemEnviada>? enviadas,
		IProcessadorDeImagem processador,
		IArquivoStore arquivos,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		var gravadas = new List<ImagemNoTexto>();
		var porIndice = new Dictionary<int, ImagemNoTexto>();

		try
		{
			foreach (var indice in analise.IndicesNovos)
			{
				var gravacao = await ImagemDaPublicacao
					.GravarAsync(enviadas![indice], processador, arquivos, cancellationToken)
					.ConfigureAwait(false);

				if (gravacao.IsFailure)
				{
					await ApagarAsync(gravadas, arquivos, logger).ConfigureAwait(false);

					return Result.Failure<CorpoPreparado>(gravacao.Error);
				}

				var imagem = new ImagemNoTexto(publicacaoId, gravacao.Value.CaminhoRelativo, gravacao.Value.ChaveEmbrulhada);

				gravadas.Add(imagem);
				porIndice[indice] = imagem;
			}
		}
		catch
		{
			await ApagarAsync(gravadas, arquivos, logger).ConfigureAwait(false);

			throw;
		}

		var corpo = analise.Corpo;

		if (analise.Marcadores.Count > 0)
		{
			var texto = new StringBuilder(corpo);

			// Do fim para o começo, para as posições anteriores continuarem valendo.
			foreach (var (inicio, tamanho, indice) in analise.Marcadores.OrderByDescending(m => m.Inicio))
			{
				texto.Remove(inicio, tamanho).Insert(inicio, CaminhoDaImagemNoTexto(publicacaoId, porIndice[indice].Id));
			}

			corpo = texto.ToString();
		}

		return new CorpoPreparado(corpo, gravadas);
	}

	/// <summary>Apaga os arquivos das imagens recém-gravadas (melhor esforço).</summary>
	/// <param name="imagens">Imagens a descartar.</param>
	/// <param name="arquivos">Armazenamento cifrado.</param>
	/// <param name="logger">Log.</param>
	internal static async Task ApagarAsync(IEnumerable<ImagemNoTexto> imagens, IArquivoStore arquivos, ILogger logger)
	{
		foreach (var imagem in imagens)
		{
			await ImagemDaPublicacao
				.ApagarAsync(imagem.CaminhoRelativo, arquivos, logger, CancellationToken.None)
				.ConfigureAwait(false);
		}
	}

	private static bool EhHttp(string destino) =>
		Uri.TryCreate(destino, UriKind.Absolute, out var uri)
		&& (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
		&& destino.StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase);

	[GeneratedRegex(@"^/publicacoes/([0-9a-fA-F-]{36})/imagens/([0-9a-fA-F-]{36})$", RegexOptions.CultureInvariant)]
	private static partial Regex RotaDeImagemRegex();

	/// <summary>Destinos de imagem classificados.</summary>
	/// <param name="Corpo">Corpo original.</param>
	/// <param name="Marcadores">Posição, tamanho e índice de cada <c>nova-imagem:N</c>.</param>
	/// <param name="IndicesNovos">Índices distintos referenciados, em ordem.</param>
	/// <param name="Existentes">Imagens existentes referenciadas.</param>
	internal sealed record AnaliseDoCorpo(
		string? Corpo,
		IReadOnlyList<(int Inicio, int Tamanho, int Indice)> Marcadores,
		IReadOnlyList<int> IndicesNovos,
		IReadOnlySet<Guid> Existentes);

	/// <summary>Corpo reescrito e as imagens novas gravadas.</summary>
	/// <param name="Corpo">Corpo com os marcadores trocados.</param>
	/// <param name="Novas">Imagens gravadas, ainda não persistidas.</param>
	internal sealed record CorpoPreparado(string? Corpo, IReadOnlyList<ImagemNoTexto> Novas);
}
