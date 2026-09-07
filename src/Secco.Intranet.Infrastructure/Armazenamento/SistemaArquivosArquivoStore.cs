using System.Globalization;
using Secco.Intranet.Application.Documentos;
using Secco.SDK.AspNetCore.Tenancy;

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
/// <param name="cipher">Cifragem em envelope.</param>
/// <param name="tenantContext">Tenant da requisição atual.</param>
internal sealed class SistemaArquivosArquivoStore(
	ArquivoStoreOptions options,
	DocumentoOptions documentoOptions,
	EnvelopeCipher cipher,
	ITenantContext tenantContext) : IArquivoStore
{
	public async Task<ArquivoGravado> GravarAsync(Stream conteudo, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(conteudo);

		var tenantId = TenantIdOuFalhar();
		var identificador = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

		// Duas casas hexadecimais de subpasta: evita um diretório único com dezenas de
		// milhares de entradas, que degrada listagem e backup em qualquer sistema de arquivos.
		var caminhoRelativo = string.Join('/', tenantId, identificador[..2], identificador);
		var caminhoCompleto = CaminhoCompleto(caminhoRelativo);

		Directory.CreateDirectory(Path.GetDirectoryName(caminhoCompleto)!);

		var chaveDoArquivo = EnvelopeCipher.GerarChaveDeArquivo();

		try
		{
			long tamanho;

			await using (var destino = new FileStream(
				caminhoCompleto, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81_920, useAsync: true))
			{
				tamanho = await EnvelopeCipher
					.CifrarAsync(conteudo, destino, chaveDoArquivo, documentoOptions.TamanhoMaximoBytes, cancellationToken)
					.ConfigureAwait(false);
			}

			return new ArquivoGravado(caminhoRelativo, cipher.Embrulhar(chaveDoArquivo), tamanho);
		}
		catch
		{
			// Gravação interrompida deixaria um arquivo pela metade que nada referencia.
			Apagar(caminhoCompleto);

			throw;
		}
		finally
		{
			Array.Clear(chaveDoArquivo);
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
		var chaveDoArquivo = cipher.Desembrulhar(chaveEmbrulhada);

		try
		{
			await using var origem = new FileStream(
				caminhoCompleto, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81_920, useAsync: true);

			await EnvelopeCipher
				.DecifrarAsync(origem, destino, chaveDoArquivo, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			Array.Clear(chaveDoArquivo);
		}
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
