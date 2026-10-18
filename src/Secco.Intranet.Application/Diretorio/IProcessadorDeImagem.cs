using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Recorte escolhido pela pessoa, em pixels da imagem já orientada.</summary>
/// <param name="X">Borda esquerda.</param>
/// <param name="Y">Borda superior.</param>
/// <param name="Largura">Largura do recorte.</param>
/// <param name="Altura">Altura do recorte.</param>
public sealed record RecorteDaFoto(int X, int Y, int Largura, int Altura);

/// <summary>Foto já normalizada, pronta para guardar.</summary>
/// <param name="Conteudo">Bytes da imagem.</param>
/// <param name="ContentType">Tipo do conteúdo gerado.</param>
public sealed record FotoProcessada(byte[] Conteudo, string ContentType);

/// <summary>Porta de processamento da foto enviada pela pessoa.</summary>
public interface IProcessadorDeImagem
{
	/// <summary>
	/// Valida, recorta e reduz a foto. Sem recorte vale o quadrado central. Falha do arquivo volta
	/// como <see cref="Result"/> com um erro de <c>IntranetErrors.Diretorio</c>.
	/// </summary>
	/// <param name="upload">Bytes enviados.</param>
	/// <param name="recorte">Recorte escolhido, ou nulo.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	Task<Result<FotoProcessada>> ProcessarFotoAsync(
		Stream upload,
		RecorteDaFoto? recorte,
		CancellationToken cancellationToken = default);
}
