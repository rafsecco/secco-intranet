using System.Globalization;
using Secco.Intranet.Application.Documentos;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Cryptography;

namespace Secco.Intranet.Infrastructure.Armazenamento;

/// <summary>
/// Armazenamento em sistema de arquivos. Guarda o conteúdo cifrado sob um nome opaco, num
/// caminho derivado do tenant da requisição.
/// </summary>
/// <remarks>
/// O caminho é montado <b>aqui</b>, nunca pelo chamador: é o único ponto do produto que
/// compõe endereço de arquivo, e é por isso que um erro de composição não consegue atravessar
/// a fronteira entre tenants (ADR-0005). O slug do setor não entra no caminho de propósito —
/// setor pode ser renomeado, arquivo já gravado não.
/// </remarks>
/// <param name="options">Raiz do armazenamento.</param>
/// <param name="documentoOptions">Limites de upload.</param>
/// <param name="cipher">Cifrador de arquivos do SDK (envelope: chave por arquivo embrulhada pela chave mestra).</param>
/// <param name="tenantContext">Tenant da requisição atual.</param>
internal sealed class SistemaArquivosArquivoStore(
	ArquivoStoreOptions options,
	DocumentoOptions documentoOptions,
	ISeccoFileCipher cipher,
	ITenantContext tenantContext) : IArquivoStore
{
	public Task<ArquivoGravado> GravarAsync(Stream conteudo, CancellationToken cancellationToken = default) =>
		GravarEmAsync(conteudo, pastaLogica: null, cancellationToken);

	public Task<ArquivoGravado> GravarAsync(
		Stream conteudo,
		string pastaLogica,
		CancellationToken cancellationToken = default)
	{
		// A pasta nunca vem do usuário: só entra o que a porta declara, e isso também fecha a
		// porta a "../" na composição do caminho.
		if (!PastasDeArquivo.Contem(pastaLogica))
		{
			throw new ArgumentException($"Pasta lógica desconhecida: '{pastaLogica}'.", nameof(pastaLogica));
		}

		return GravarEmAsync(conteudo, pastaLogica, cancellationToken);
	}

	private async Task<ArquivoGravado> GravarEmAsync(
		Stream conteudo,
		string? pastaLogica,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(conteudo);

		var tenantId = TenantIdOuFalhar();
		var identificador = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

		// Duas casas hexadecimais de subpasta: evita um diretório único com dezenas de
		// milhares de entradas, que degrada listagem e backup em qualquer sistema de arquivos.
		var caminhoRelativo = pastaLogica is null
			? string.Join('/', tenantId, identificador[..2], identificador)
			: string.Join('/', tenantId, pastaLogica, identificador[..2], identificador);
		var caminhoCompleto = CaminhoCompleto(caminhoRelativo);

		Directory.CreateDirectory(Path.GetDirectoryName(caminhoCompleto)!);

		try
		{
			SeccoEncryptedFile cifrado;

			await using (var destino = new FileStream(
				caminhoCompleto, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81_920, useAsync: true))
			{
				cifrado = await cipher
					.EncryptAsync(conteudo, destino, documentoOptions.TamanhoMaximoBytes, cancellationToken)
					.ConfigureAwait(false);
			}

			return new ArquivoGravado(caminhoRelativo, cifrado.WrappedKey, cifrado.PlaintextLength);
		}
		catch
		{
			// Gravação interrompida deixaria um arquivo pela metade que nada referencia.
			Apagar(caminhoCompleto);

			throw;
		}
	}

	public async Task EscreverEmAsync(
		string caminhoRelativo,
		string chaveEmbrulhada,
		Stream destino,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destino);

		var caminhoCompleto = CaminhoCompleto(caminhoRelativo);
		await using var origem = new FileStream(
			caminhoCompleto, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81_920, useAsync: true);

		await cipher
			.DecryptAsync(origem, destino, chaveEmbrulhada, cancellationToken)
			.ConfigureAwait(false);
	}

	public Task RemoverAsync(string caminhoRelativo, CancellationToken cancellationToken = default)
	{
		Apagar(CaminhoCompleto(caminhoRelativo));

		return Task.CompletedTask;
	}

	private static void Apagar(string caminhoCompleto)
	{
		if (File.Exists(caminhoCompleto))
		{
			File.Delete(caminhoCompleto);
		}
	}

	private Guid TenantIdOuFalhar() =>
		tenantContext.TenantId
		?? throw new InvalidOperationException(
			"Não há tenant resolvido para a requisição: sem ele não é possível endereçar o arquivo (ADR-0005).");

	/// <summary>
	/// Resolve o caminho absoluto conferindo que ele permanece sob a raiz. O valor vem do
	/// banco, mas tratá-lo como confiável seria apostar que nenhuma escrita indevida jamais
	/// alcançará aquela coluna.
	/// </summary>
	private string CaminhoCompleto(string caminhoRelativo)
	{
		if (string.IsNullOrWhiteSpace(caminhoRelativo))
		{
			throw new InvalidOperationException("Caminho de arquivo vazio.");
		}

		var raiz = Path.GetFullPath(options.Raiz);
		var completo = Path.GetFullPath(Path.Combine(raiz, caminhoRelativo));

		if (!completo.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Caminho de arquivo fora da raiz do armazenamento.");
		}

		return completo;
	}
}
