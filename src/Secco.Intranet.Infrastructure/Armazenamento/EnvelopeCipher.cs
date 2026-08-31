using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;

namespace Secco.Intranet.Infrastructure.Armazenamento;

/// <summary>
/// Cifragem em envelope dos documentos. Cada arquivo recebe uma chave própria (DEK) gerada
/// ao acaso; o conteúdo é cifrado com ela, e a DEK é embrulhada pela chave mestra e guardada
/// junto dos metadados.
/// </summary>
/// <remarks>
/// <para>
/// O embrulho usa AES-256-GCM no mesmo formato versionado e autodescritivo já adotado pela
/// plataforma: <c>secco-enc:v1:&lt;base64(nonce ‖ cifrado ‖ tag)&gt;</c>. GCM é AEAD — o tag
/// autentica o dado, então adulteração falha em vez de devolver lixo.
/// </para>
/// <para>
/// O conteúdo é cifrado em blocos de 1 MiB, e não em um bloco único, porque GCM só autentica
/// no fim da mensagem: um bloco único obrigaria a carregar o arquivo inteiro em memória antes
/// de poder confiar em qualquer byte. Cada bloco leva nonce e tag próprios, com o índice e a
/// marca de último bloco como dado associado — o que impede reordenar, repetir ou truncar.
/// </para>
/// </remarks>
internal sealed class EnvelopeCipher
{
	/// <summary>Prefixo que marca um valor embrulhado.</summary>
	internal const string Prefixo = "secco-enc:";

	/// <summary>Prefixo completo da versão 1 do formato de embrulho.</summary>
	internal const string PrefixoVersaoUm = $"{Prefixo}v1:";

	/// <summary>Tamanho do bloco de conteúdo em claro, em bytes.</summary>
	internal const int TamanhoDoBloco = 1024 * 1024;

	private const int TamanhoNonce = 12;  // 96 bits — recomendado para AES-GCM
	private const int TamanhoTag = 16;    // 128 bits — máximo do GCM
	private const byte VersaoDoArquivo = 1;

	private static readonly byte[] AssinaturaDoArquivo = "SCCOFILE"u8.ToArray();

	private readonly byte[] _chaveAtiva;
	private readonly IReadOnlyList<byte[]> _chavesAposentadas;

	/// <summary>Compõe o cifrador a partir da configuração.</summary>
	/// <param name="options">Chave mestra ativa e aposentadas.</param>
	/// <param name="environment">Ambiente de hospedagem.</param>
	/// <exception cref="ChaveMestraException">Se a chave for inválida, ou ausente em Production.</exception>
	public EnvelopeCipher(ChaveMestraOptions options, IHostEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(environment);

		var ativa = !string.IsNullOrWhiteSpace(options.ChaveAtiva)
			? options.ChaveAtiva
			: environment.IsProduction()
				// Inalcançável na prática: o validador derruba o startup antes. Defesa em profundidade.
				? throw new ChaveMestraException("Chave mestra de documentos ausente em Production.")
				: ChaveMestraOptions.ChaveDesenvolvimento;

		_chaveAtiva = DecodificarChave(ativa);
		_chavesAposentadas = [.. options.ChavesAposentadas.Select(DecodificarChave)];
	}

	/// <summary>Sorteia a chave de um arquivo novo.</summary>
	public static byte[] GerarChaveDeArquivo() =>
		RandomNumberGenerator.GetBytes(ChaveMestraOptions.TamanhoEmBytes);

	/// <summary>Embrulha a chave de um arquivo com a chave mestra ativa.</summary>
	/// <param name="chaveDoArquivo">Chave de 32 bytes do arquivo.</param>
	public string Embrulhar(byte[] chaveDoArquivo)
	{
		ArgumentNullException.ThrowIfNull(chaveDoArquivo);

		var nonce = RandomNumberGenerator.GetBytes(TamanhoNonce);
		var cifrado = new byte[chaveDoArquivo.Length];
		var tag = new byte[TamanhoTag];

		using (var aes = new AesGcm(_chaveAtiva, TamanhoTag))
		{
			aes.Encrypt(nonce, chaveDoArquivo, cifrado, tag);
		}

		var blob = new byte[TamanhoNonce + cifrado.Length + TamanhoTag];
		nonce.CopyTo(blob, 0);
		cifrado.CopyTo(blob, TamanhoNonce);
		tag.CopyTo(blob, TamanhoNonce + cifrado.Length);

		return PrefixoVersaoUm + Convert.ToBase64String(blob);
	}

	/// <summary>Desembrulha a chave de um arquivo, tentando a chave ativa e depois as aposentadas.</summary>
	/// <param name="embrulhada">Valor guardado em <c>ds_chave_embrulhada</c>.</param>
	/// <exception cref="ChaveMestraException">Se o formato for desconhecido ou nenhuma chave servir.</exception>
	public byte[] Desembrulhar(string embrulhada)
	{
		ArgumentNullException.ThrowIfNull(embrulhada);

		if (!embrulhada.StartsWith(PrefixoVersaoUm, StringComparison.Ordinal))
		{
			throw new ChaveMestraException("Formato de chave de documento desconhecido.");
		}

		byte[] blob;

		try
		{
			blob = Convert.FromBase64String(embrulhada[PrefixoVersaoUm.Length..]);
		}
		catch (FormatException excecao)
		{
			throw new ChaveMestraException("Chave de documento em base64 inválido.", excecao);
		}

		if (blob.Length != TamanhoNonce + ChaveMestraOptions.TamanhoEmBytes + TamanhoTag)
		{
			throw new ChaveMestraException("Chave de documento com tamanho inesperado.");
		}

		foreach (var chaveMestra in new[] { _chaveAtiva }.Concat(_chavesAposentadas))
		{
			if (TentarDesembrulhar(chaveMestra, blob, out var chaveDoArquivo))
			{
				return chaveDoArquivo;
			}
		}

		throw new ChaveMestraException("Não foi possível desembrulhar a chave: dado adulterado ou chave desconhecida.");
	}

	/// <summary>Cifra <paramref name="origem"/> em <paramref name="destino"/> e devolve o tamanho em claro.</summary>
	/// <param name="origem">Conteúdo original.</param>
	/// <param name="destino">Arquivo de destino.</param>
	/// <param name="chaveDoArquivo">Chave sorteada para este arquivo.</param>
	/// <param name="limiteBytes">Tamanho máximo aceito; ultrapassá-lo aborta a gravação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <exception cref="ChaveMestraException">Se o conteúdo ultrapassar <paramref name="limiteBytes"/>.</exception>
	public static async Task<long> CifrarAsync(
		Stream origem,
		Stream destino,
		byte[] chaveDoArquivo,
		long limiteBytes,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(origem);
		ArgumentNullException.ThrowIfNull(destino);
		ArgumentNullException.ThrowIfNull(chaveDoArquivo);

		await destino.WriteAsync(AssinaturaDoArquivo, cancellationToken).ConfigureAwait(false);
		await destino.WriteAsync(new[] { VersaoDoArquivo }, cancellationToken).ConfigureAwait(false);
		await destino.WriteAsync(BitConverter.GetBytes(TamanhoDoBloco), cancellationToken).ConfigureAwait(false);

		var buffer = new byte[TamanhoDoBloco];
		var cifrado = new byte[TamanhoDoBloco];
		var nonce = new byte[TamanhoNonce];
		var tag = new byte[TamanhoTag];
		var total = 0L;
		var indice = 0UL;

		using var aes = new AesGcm(chaveDoArquivo, TamanhoTag);

		while (true)
		{
			var lidos = await origem
				.ReadAtLeastAsync(buffer, TamanhoDoBloco, throwOnEndOfStream: false, cancellationToken)
				.ConfigureAwait(false);

			var ultimo = lidos < TamanhoDoBloco;
			total += lidos;

			if (total > limiteBytes)
			{
				throw new ChaveMestraException("Conteúdo acima do tamanho máximo aceito.");
			}

			RandomNumberGenerator.Fill(nonce);
			aes.Encrypt(nonce, buffer.AsSpan(0, lidos), cifrado.AsSpan(0, lidos), tag, DadoAssociado(indice, ultimo));

			await destino.WriteAsync(new[] { ultimo ? (byte)1 : (byte)0 }, cancellationToken).ConfigureAwait(false);
			await destino.WriteAsync(nonce, cancellationToken).ConfigureAwait(false);
			await destino.WriteAsync(BitConverter.GetBytes(lidos), cancellationToken).ConfigureAwait(false);
			await destino.WriteAsync(cifrado.AsMemory(0, lidos), cancellationToken).ConfigureAwait(false);
			await destino.WriteAsync(tag, cancellationToken).ConfigureAwait(false);

			indice++;

			if (ultimo)
			{
				return total;
			}
		}
	}

	/// <summary>Decifra <paramref name="origem"/> escrevendo o conteúdo original em <paramref name="destino"/>.</summary>
	/// <param name="origem">Arquivo cifrado.</param>
	/// <param name="destino">Fluxo de destino, normalmente o corpo da resposta.</param>
	/// <param name="chaveDoArquivo">Chave desembrulhada do arquivo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <exception cref="ChaveMestraException">Se o cabeçalho for inválido ou algum bloco não autenticar.</exception>
	public static async Task DecifrarAsync(
		Stream origem,
		Stream destino,
		byte[] chaveDoArquivo,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(origem);
		ArgumentNullException.ThrowIfNull(destino);
		ArgumentNullException.ThrowIfNull(chaveDoArquivo);

		var cabecalho = new byte[AssinaturaDoArquivo.Length + 1 + sizeof(int)];
		await LerExatamenteAsync(origem, cabecalho, cancellationToken).ConfigureAwait(false);

		if (!cabecalho.AsSpan(0, AssinaturaDoArquivo.Length).SequenceEqual(AssinaturaDoArquivo)
			|| cabecalho[AssinaturaDoArquivo.Length] != VersaoDoArquivo)
		{
			throw new ChaveMestraException("Arquivo cifrado com cabeçalho inválido ou versão desconhecida.");
		}

		var tamanhoDoBloco = BitConverter.ToInt32(cabecalho, AssinaturaDoArquivo.Length + 1);

		if (tamanhoDoBloco <= 0 || tamanhoDoBloco > TamanhoDoBloco)
		{
			throw new ChaveMestraException("Arquivo cifrado com tamanho de bloco inválido.");
		}

		var cifrado = new byte[tamanhoDoBloco];
		var claro = new byte[tamanhoDoBloco];
		var prefixo = new byte[1 + TamanhoNonce + sizeof(int)];
		var tag = new byte[TamanhoTag];
		var indice = 0UL;

		using var aes = new AesGcm(chaveDoArquivo, TamanhoTag);

		while (true)
		{
			await LerExatamenteAsync(origem, prefixo, cancellationToken).ConfigureAwait(false);

			var ultimo = prefixo[0] == 1;
			var comprimento = BitConverter.ToInt32(prefixo, 1 + TamanhoNonce);

			if (comprimento < 0 || comprimento > tamanhoDoBloco)
			{
				throw new ChaveMestraException("Arquivo cifrado com bloco de comprimento inválido.");
			}

			await LerExatamenteAsync(origem, cifrado.AsMemory(0, comprimento), cancellationToken).ConfigureAwait(false);
			await LerExatamenteAsync(origem, tag, cancellationToken).ConfigureAwait(false);

			try
			{
				aes.Decrypt(
					prefixo.AsSpan(1, TamanhoNonce),
					cifrado.AsSpan(0, comprimento),
					tag,
					claro.AsSpan(0, comprimento),
					DadoAssociado(indice, ultimo));
			}
			catch (CryptographicException excecao)
			{
				throw new ChaveMestraException("Arquivo adulterado: um bloco não autenticou.", excecao);
			}

			await destino.WriteAsync(claro.AsMemory(0, comprimento), cancellationToken).ConfigureAwait(false);

			indice++;

			if (ultimo)
			{
				return;
			}
		}
	}

	/// <summary>
	/// Dado associado de cada bloco: índice e marca de último. Amarra o bloco à sua posição, o
	/// que faz reordenar, repetir ou cortar o fim do arquivo falhar na verificação do tag.
	/// </summary>
	private static byte[] DadoAssociado(ulong indice, bool ultimo)
	{
		var dado = new byte[sizeof(ulong) + 1];
		BitConverter.TryWriteBytes(dado, indice);
		dado[sizeof(ulong)] = ultimo ? (byte)1 : (byte)0;

		return dado;
	}

	private static async Task LerExatamenteAsync(Stream origem, Memory<byte> destino, CancellationToken cancellationToken)
	{
		try
		{
			await origem.ReadExactlyAsync(destino, cancellationToken).ConfigureAwait(false);
		}
		catch (EndOfStreamException excecao)
		{
			// Um bloco não marcado como último seguido de fim de arquivo é exatamente o que
			// acontece quando o final do arquivo foi cortado.
			throw new ChaveMestraException("Arquivo cifrado truncado.", excecao);
		}
	}

	private static bool TentarDesembrulhar(byte[] chaveMestra, byte[] blob, out byte[] chaveDoArquivo)
	{
		var nonce = blob.AsSpan(0, TamanhoNonce);
		var tag = blob.AsSpan(blob.Length - TamanhoTag, TamanhoTag);
		var cifrado = blob.AsSpan(TamanhoNonce, blob.Length - TamanhoNonce - TamanhoTag);

		chaveDoArquivo = new byte[cifrado.Length];

		try
		{
			using var aes = new AesGcm(chaveMestra, TamanhoTag);
			aes.Decrypt(nonce, cifrado, tag, chaveDoArquivo);

			return true;
		}
		catch (CryptographicException)
		{
			// Tag não confere para esta chave: dado adulterado OU chave errada. Quem chama
			// tenta a próxima chave aposentada antes de desistir.
			chaveDoArquivo = [];

			return false;
		}
	}

	private static byte[] DecodificarChave(string base64)
	{
		byte[] chave;

		try
		{
			chave = Convert.FromBase64String(base64);
		}
		catch (FormatException excecao)
		{
			throw new ChaveMestraException("Chave mestra de documentos em base64 inválido.", excecao);
		}

		return chave.Length == ChaveMestraOptions.TamanhoEmBytes
			? chave
			: throw new ChaveMestraException(
				$"Chave mestra de documentos deve ter {ChaveMestraOptions.TamanhoEmBytes} bytes (AES-256).");
	}
}
