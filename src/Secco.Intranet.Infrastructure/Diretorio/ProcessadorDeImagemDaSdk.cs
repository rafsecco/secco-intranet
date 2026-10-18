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

	/// <inheritdoc />
	public async Task<Result<FotoProcessada>> ProcessarFotoAsync(
		Stream upload,
		RecorteDaFoto? recorte,
		CancellationToken cancellationToken = default)
	{
		// A SDK lança ArgumentException para pedido mal montado; coordenada de formulário
		// não pode virar 500.
		if (recorte is { X: < 0 } or { Y: < 0 } or { Largura: <= 0 } or { Altura: <= 0 })
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

	private static Error Traduzir(Error erro) => erro.Code switch
	{
		var c when c == SeccoImagingErrors.InputTooLarge.Code => IntranetErrors.Diretorio.FotoGrandeDemais,
		var c when c == SeccoImagingErrors.UnsupportedFormat.Code => IntranetErrors.Diretorio.FotoFormatoNaoSuportado,
		var c when c == SeccoImagingErrors.InvalidCrop.Code => IntranetErrors.Diretorio.FotoRecorteInvalido,
		_ => IntranetErrors.Diretorio.FotoInvalida,
	};
}
