using System.Buffers.Binary;

namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>
/// Lê largura e altura do cabeçalho de PNG, JPEG e WebP, sem decodificar. Existe porque a
/// <c>Secco.SDK.Imaging</c> 0.1.0 só sabe recortar (sem recorte vale o quadrado central) e o Mural
/// não recorta: o recorte do quadro inteiro precisa das medidas. São as medidas gravadas no arquivo,
/// antes da orientação do EXIF.
/// </summary>
internal static class DimensoesDaImagem
{
	/// <summary>Medidas do arquivo, ou <c>null</c> se o cabeçalho não for reconhecido. Devolve o fluxo ao início.</summary>
	/// <param name="fluxo">Fluxo com busca.</param>
	internal static (int Largura, int Altura)? Ler(Stream fluxo)
	{
		fluxo.Position = 0;

		try
		{
			var assinatura = new byte[12];
			var lidos = fluxo.ReadAtLeast(assinatura, assinatura.Length, throwOnEndOfStream: false);

			if (lidos < assinatura.Length)
			{
				return null;
			}

			if (assinatura[0] == 0x89 && assinatura[1] == 0x50 && assinatura[2] == 0x4E && assinatura[3] == 0x47)
			{
				return LerPng(fluxo);
			}

			if (assinatura[0] == 0xFF && assinatura[1] == 0xD8)
			{
				return LerJpeg(fluxo);
			}

			if (assinatura[0] == 'R' && assinatura[1] == 'I' && assinatura[2] == 'F' && assinatura[3] == 'F'
				&& assinatura[8] == 'W' && assinatura[9] == 'E' && assinatura[10] == 'B' && assinatura[11] == 'P')
			{
				return LerWebp(fluxo);
			}

			return null;
		}
		finally
		{
			fluxo.Position = 0;
		}
	}

	private static (int, int)? LerPng(Stream fluxo)
	{
		// Assinatura (8) + tamanho do IHDR (4) + "IHDR" (4) + largura (4) + altura (4).
		fluxo.Position = 16;
		Span<byte> medidas = stackalloc byte[8];

		if (fluxo.ReadAtLeast(medidas, 8, throwOnEndOfStream: false) < 8)
		{
			return null;
		}

		return Validas(BinaryPrimitives.ReadInt32BigEndian(medidas), BinaryPrimitives.ReadInt32BigEndian(medidas[4..]));
	}

	private static (int, int)? LerWebp(Stream fluxo)
	{
		fluxo.Position = 12;
		var cabecalho = new byte[18];

		if (fluxo.ReadAtLeast(cabecalho, cabecalho.Length, throwOnEndOfStream: false) < cabecalho.Length)
		{
			return null;
		}

		var tipo = System.Text.Encoding.ASCII.GetString(cabecalho, 0, 4);

		// Dados do bloco começam em 8 (tipo + tamanho).
		return tipo switch
		{
			"VP8X" => Validas(
				1 + (cabecalho[12] | (cabecalho[13] << 8) | (cabecalho[14] << 16)),
				1 + (cabecalho[15] | (cabecalho[16] << 8) | (cabecalho[17] << 16))),
			"VP8L" => LerVp8L(cabecalho),
			"VP8 " => Validas(
				BitConverter.ToUInt16(cabecalho, 14) & 0x3FFF,
				BitConverter.ToUInt16(cabecalho, 16) & 0x3FFF),
			_ => null,
		};
	}

	private static (int, int)? LerVp8L(byte[] cabecalho)
	{
		// Após a assinatura 0x2F (byte 8 + 4 = 13), 14 bits de largura-1 e 14 de altura-1.
		var bits = BitConverter.ToUInt32(cabecalho, 9);

		return Validas((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
	}

	private static (int, int)? LerJpeg(Stream fluxo)
	{
		fluxo.Position = 2;
		var tamanho = new byte[2];
		var quadro = new byte[5];

		while (true)
		{
			var b = fluxo.ReadByte();

			if (b < 0)
			{
				return null;
			}

			if (b != 0xFF)
			{
				continue;
			}

			int marcador;

			do
			{
				marcador = fluxo.ReadByte();
			}
			while (marcador == 0xFF);

			if (marcador < 0)
			{
				return null;
			}

			// Marcadores sem corpo: 0x01, RSTn e a abertura (SOI).
			if (marcador is 0x00 or 0x01 or 0xD8 or (>= 0xD0 and <= 0xD7))
			{
				continue;
			}

			if (fluxo.ReadAtLeast(tamanho, 2, throwOnEndOfStream: false) < 2)
			{
				return null;
			}

			var corpo = BinaryPrimitives.ReadUInt16BigEndian(tamanho) - 2;
			var ehQuadro = marcador is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

			if (!ehQuadro)
			{
				if (corpo < 0)
				{
					return null;
				}

				fluxo.Seek(corpo, SeekOrigin.Current);

				continue;
			}

			// Precisão (1), altura (2), largura (2).
			if (corpo < 5 || fluxo.ReadAtLeast(quadro, 5, throwOnEndOfStream: false) < 5)
			{
				return null;
			}

			return Validas(
				BinaryPrimitives.ReadUInt16BigEndian(quadro.AsSpan(3)),
				BinaryPrimitives.ReadUInt16BigEndian(quadro.AsSpan(1)));
		}
	}

	private static (int, int)? Validas(int largura, int altura) =>
		largura > 0 && altura > 0 ? (largura, altura) : null;
}
