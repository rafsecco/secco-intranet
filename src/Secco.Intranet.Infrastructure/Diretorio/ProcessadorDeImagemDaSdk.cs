using Secco.Intranet.Application;
using Secco.Intranet.Application.Diretorio;
using Secco.SDK.Imaging;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>Processa a foto do colaborador com o <c>Secco.SDK.Imaging</c>.</summary>
/// <param name="imagens">Processador da SDK.</param>
internal sealed class ProcessadorDeImagemDaSdk(ISeccoImageProcessor imagens) : IProcessadorDeImagem
{
	private const string NomeDaVariante = "foto";
	private const int LadoMaximo = 256;
	private const string NomeDaVarianteDePublicacao = "publicacao";
	private const int LadoMaximoDePublicacao = 2048;

	/// <inheritdoc />
	public async Task<Result<FotoProcessada>> ProcessarFotoAsync(
		Stream upload,
		RecorteDaFoto? recorte,
		CancellationToken cancellationToken = default)
	{
		// A SDK lança ArgumentException para pedido mal montado; coordenada de formulário
		// não pode virar 500.
		if (recorte is { X: < 0 } or { Y: < 0 } or { Largura: <= 0 } or { Altura: <= 0 }
			|| (recorte is not null
				&& ((long)recorte.X + recorte.Largura > int.MaxValue || (long)recorte.Y + recorte.Altura > int.MaxValue)))
		{
			return Result.Failure<FotoProcessada>(IntranetErrors.Diretorio.FotoRecorteInvalido);
		}

		var pedido = new ImageProcessingRequest(
			Variants: [new ImageVariant(NomeDaVariante, LadoMaximo)],
			Crop: recorte is null ? null : new CropRectangle(recorte.X, recorte.Y, recorte.Largura, recorte.Altura),
			Format: ImageOutputFormat.WebP);

		var resultado = await imagens.ProcessAsync(upload, pedido, cancellationToken);
		if (resultado.IsFailure)
		{
			return Result.Failure<FotoProcessada>(Traduzir(resultado.Error));
		}

		var saida = resultado.Value.Variants[NomeDaVariante];
		return Result.Success(new FotoProcessada(saida.Content, saida.ContentType));
	}

	/// <inheritdoc />
	public async Task<Result<FotoProcessada>> ProcessarImagemDePublicacaoAsync(
		Stream upload,
		CancellationToken cancellationToken = default)
	{
		// A SDK 0.1.0 não tem "sem recorte": o padrão é o quadrado central. O quadro inteiro é pedido
		// como recorte explícito, com as medidas do cabeçalho.
		var fluxo = upload;

		if (!fluxo.CanSeek)
		{
			var copia = new MemoryStream();
			await upload.CopyToAsync(copia, cancellationToken);
			fluxo = copia;
		}

		try
		{
			return await ProcessarQuadroInteiroAsync(fluxo, cancellationToken);
		}
		finally
		{
			if (!ReferenceEquals(fluxo, upload))
			{
				await fluxo.DisposeAsync();
			}
		}
	}

	private async Task<Result<FotoProcessada>> ProcessarQuadroInteiroAsync(Stream fluxo, CancellationToken cancellationToken)
	{
		var medidas = DimensoesDaImagem.Ler(fluxo);
		var variantes = new[] { new ImageVariant(NomeDaVarianteDePublicacao, LadoMaximoDePublicacao) };

		if (medidas is null)
		{
			// Cabeçalho irreconhecível: a SDK dá o veredito certo (formato, tamanho, ilegível). Se ainda
			// assim ela aceitar, o quadrado central não é o que a pessoa enviou, então recusa.
			var veredito = await imagens.ProcessAsync(
				fluxo, new ImageProcessingRequest(Variants: variantes, Format: ImageOutputFormat.WebP), cancellationToken);

			return veredito.IsFailure
				? Result.Failure<FotoProcessada>(TraduzirPublicacao(veredito.Error))
				: Result.Failure<FotoProcessada>(IntranetErrors.Publicacoes.ImagemInvalida);
		}

		var (largura, altura) = medidas.Value;
		var resultado = await ProcessarComRecorteAsync(fluxo, variantes, new CropRectangle(0, 0, largura, altura), cancellationToken);

		// JPEG com orientação de EXIF de 90 graus: o quadro orientado tem as medidas trocadas.
		if (resultado.IsFailure
			&& resultado.Error.Code == SeccoImagingErrors.InvalidCrop.Code
			&& largura != altura)
		{
			resultado = await ProcessarComRecorteAsync(fluxo, variantes, new CropRectangle(0, 0, altura, largura), cancellationToken);
		}

		if (resultado.IsFailure)
		{
			return Result.Failure<FotoProcessada>(TraduzirPublicacao(resultado.Error));
		}

		var saida = resultado.Value.Variants[NomeDaVarianteDePublicacao];
		return Result.Success(new FotoProcessada(saida.Content, saida.ContentType));
	}

	private Task<Result<ProcessedImage>> ProcessarComRecorteAsync(
		Stream fluxo,
		ImageVariant[] variantes,
		CropRectangle recorte,
		CancellationToken cancellationToken)
	{
		fluxo.Position = 0;

		return imagens.ProcessAsync(
			fluxo,
			new ImageProcessingRequest(Variants: variantes, Crop: recorte, Format: ImageOutputFormat.WebP),
			cancellationToken);
	}

	private static Error TraduzirPublicacao(Error erro) => erro.Code switch
	{
		var c when c == SeccoImagingErrors.InputTooLarge.Code => IntranetErrors.Publicacoes.ImagemGrandeDemais,
		var c when c == SeccoImagingErrors.UnsupportedFormat.Code => IntranetErrors.Publicacoes.ImagemFormatoNaoSuportado,
		_ => IntranetErrors.Publicacoes.ImagemInvalida,
	};

	private static Error Traduzir(Error erro) => erro.Code switch
	{
		var c when c == SeccoImagingErrors.InputTooLarge.Code => IntranetErrors.Diretorio.FotoGrandeDemais,
		var c when c == SeccoImagingErrors.UnsupportedFormat.Code => IntranetErrors.Diretorio.FotoFormatoNaoSuportado,
		var c when c == SeccoImagingErrors.InvalidCrop.Code => IntranetErrors.Diretorio.FotoRecorteInvalido,
		_ => IntranetErrors.Diretorio.FotoInvalida,
	};
}
