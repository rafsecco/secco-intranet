using Microsoft.Extensions.Hosting;
using Secco.SDK.EntityFrameworkCore.Cryptography;

namespace Secco.Intranet.Infrastructure.Armazenamento;

/// <summary>
/// Fábrica do cifrador de arquivos do SDK, aplicando a política da chave mestra do produto.
/// </summary>
internal static class CifradorDeArquivos
{
	/// <summary>Compõe o cifrador a partir da configuração da chave mestra.</summary>
	/// <param name="options">Chave mestra ativa e aposentadas.</param>
	/// <param name="environment">Ambiente de hospedagem.</param>
	/// <exception cref="ChaveMestraException">Se a chave for inválida, ou ausente fora de Development.</exception>
	public static ISeccoFileCipher Criar(ChaveMestraOptions options, IHostEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(environment);

		// A chave embutida está no repositório: cifrar com ela fora do host local é não cifrar.
		var ativa = !string.IsNullOrWhiteSpace(options.ChaveAtiva)
			? options.ChaveAtiva
			: environment.IsDevelopment()
				? ChaveMestraOptions.ChaveDesenvolvimento
				: throw new ChaveMestraException("Chave mestra de documentos ausente fora de Development.");

		try
		{
			return new AesGcmFileCipher(AesGcmSecretCipher.FromBase64Keys(ativa, options.ChavesAposentadas));
		}
		catch (SeccoSecretCipherException excecao)
		{
			throw new ChaveMestraException(
				"Chave mestra de documentos inválida: base64 de 32 bytes esperado (AES-256).", excecao);
		}
	}
}
