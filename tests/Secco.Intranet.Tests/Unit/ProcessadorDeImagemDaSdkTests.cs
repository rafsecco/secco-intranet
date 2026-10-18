using System.IO.Compression;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Infrastructure.Diretorio;
using Secco.SDK.Imaging;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Adaptador da foto contra a SDK real, com imagens geradas em memória.</summary>
public class ProcessadorDeImagemDaSdkTests
{
	private static ProcessadorDeImagemDaSdk CriarProcessador()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
		services.AddLogging();
		services.AddSeccoImaging("Intranet:Imaging");
		return new ProcessadorDeImagemDaSdk(services.BuildServiceProvider().GetRequiredService<ISeccoImageProcessor>());
	}

	[Fact]
	public async Task Processar_PngSemRecorte_DevolveWebp256OuMenor()
	{
		var resultado = await CriarProcessador().ProcessarFotoAsync(new MemoryStream(Png(800, 600)), null);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.ContentType.Should().Be("image/webp");
		var (largura, altura) = DimensoesWebp(resultado.Value.Conteudo);
		largura.Should().BeLessThanOrEqualTo(256);
		altura.Should().BeLessThanOrEqualTo(256);
	}

	[Fact]
	public async Task Processar_ComRecorteValido_DevolveQuadradoDoRecorte()
	{
		var resultado = await CriarProcessador()
			.ProcessarFotoAsync(new MemoryStream(Png(800, 600)), new RecorteDaFoto(100, 50, 400, 400));

		resultado.IsSuccess.Should().BeTrue();
		var (largura, altura) = DimensoesWebp(resultado.Value.Conteudo);
		largura.Should().Be(altura);
		largura.Should().BeLessThanOrEqualTo(256);
	}

	[Fact]
	public async Task Processar_RecorteForaDaImagem_FotoRecorteInvalido()
	{
		var resultado = await CriarProcessador()
			.ProcessarFotoAsync(new MemoryStream(Png(800, 600)), new RecorteDaFoto(700, 500, 400, 400));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Diretorio.FotoRecorteInvalido);
	}

	[Theory]
	[InlineData(-1, 0, 100, 100)]
	[InlineData(0, -1, 100, 100)]
	[InlineData(0, 0, 0, 100)]
	[InlineData(0, 0, 100, -5)]
	public async Task Processar_RecorteNegativo_FotoRecorteInvalido_SemExcecao(int x, int y, int largura, int altura)
	{
		var resultado = await CriarProcessador()
			.ProcessarFotoAsync(new MemoryStream(Png(800, 600)), new RecorteDaFoto(x, y, largura, altura));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Diretorio.FotoRecorteInvalido);
	}

	[Fact]
	public async Task Processar_TextoQualquer_FotoInvalida()
	{
		var texto = System.Text.Encoding.UTF8.GetBytes("isto nao e uma imagem, so um texto qualquer");

		var resultado = await CriarProcessador().ProcessarFotoAsync(new MemoryStream(texto), null);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Diretorio.FotoInvalida);
	}

	[Fact]
	public async Task Processar_CabecalhoHeic_FotoFormatoNaoSuportado()
	{
		byte[] heic = [0, 0, 0, 24, .. "ftypheic"u8.ToArray(), 0, 0, 0, 0, .. "mif1heic"u8.ToArray()];

		var resultado = await CriarProcessador().ProcessarFotoAsync(new MemoryStream(heic), null);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Diretorio.FotoFormatoNaoSuportado);
	}

	/// <summary>Largura e altura de um WebP lido do cabeçalho (VP8, VP8L ou VP8X).</summary>
	private static (int Largura, int Altura) DimensoesWebp(byte[] webp)
	{
		System.Text.Encoding.ASCII.GetString(webp, 0, 4).Should().Be("RIFF");
		System.Text.Encoding.ASCII.GetString(webp, 8, 4).Should().Be("WEBP");
		var tipo = System.Text.Encoding.ASCII.GetString(webp, 12, 4);
		switch (tipo)
		{
			case "VP8X":
				return (1 + (webp[24] | webp[25] << 8 | webp[26] << 16), 1 + (webp[27] | webp[28] << 8 | webp[29] << 16));
			case "VP8L":
				var bits = BitConverter.ToUInt32(webp, 21);
				return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
			default: // "VP8 "
				return (BitConverter.ToUInt16(webp, 26) & 0x3FFF, BitConverter.ToUInt16(webp, 28) & 0x3FFF);
		}
	}

	/// <summary>PNG RGB 8 bits com degradê: assinatura, IHDR, IDAT (zlib) e IEND, cada bloco com CRC.</summary>
	private static byte[] Png(int largura, int altura)
	{
		using var saida = new MemoryStream();
		saida.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

		var ihdr = new byte[13];
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), largura);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), altura);
		ihdr[8] = 8; // profundidade
		ihdr[9] = 2; // RGB
		Bloco(saida, "IHDR", ihdr);

		using var bruto = new MemoryStream();
		using (var zlib = new ZLibStream(bruto, CompressionLevel.Fastest, leaveOpen: true))
		{
			var linha = new byte[1 + largura * 3];
			for (var y = 0; y < altura; y++)
			{
				for (var x = 0; x < largura; x++)
				{
					linha[1 + x * 3] = (byte)(x * 255 / largura);
					linha[2 + x * 3] = (byte)(y * 255 / altura);
					linha[3 + x * 3] = 128;
				}
				zlib.Write(linha);
			}
		}
		Bloco(saida, "IDAT", bruto.ToArray());
		Bloco(saida, "IEND", []);
		return saida.ToArray();
	}

	private static void Bloco(Stream saida, string tipo, byte[] dados)
	{
		var cabecalho = new byte[4];
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(cabecalho, dados.Length);
		saida.Write(cabecalho);
		var corpo = System.Text.Encoding.ASCII.GetBytes(tipo).Concat(dados).ToArray();
		saida.Write(corpo);
		var crc = new byte[4];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(corpo));
		saida.Write(crc);
	}

	private static uint Crc32(byte[] dados)
	{
		var crc = 0xFFFFFFFFu;
		foreach (var b in dados)
		{
			crc ^= b;
			for (var k = 0; k < 8; k++)
			{
				crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
			}
		}
		return ~crc;
	}
}
