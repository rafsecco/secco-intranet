using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Secco.Intranet.Infrastructure.Armazenamento;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Cifragem em envelope dos documentos. Além do caminho feliz, cobre adulteração,
/// reordenação e truncamento — as três formas de mexer num arquivo já gravado — e a guarda
/// de chave ausente em Production.
/// </summary>
public class EnvelopeCipherTests
{
	private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = environmentName;

		public string ApplicationName { get; set; } = "Secco.Intranet.Tests";

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private static string ChaveBase64() =>
		Convert.ToBase64String(RandomNumberGenerator.GetBytes(ChaveMestraOptions.TamanhoEmBytes));

	private static EnvelopeCipher Criar(ChaveMestraOptions options, string ambiente = "Development") =>
		new(options, new FakeHostEnvironment(ambiente));

	private static async Task<byte[]> CifrarAsync(byte[] conteudo, byte[] chaveDoArquivo)
	{
		using var origem = new MemoryStream(conteudo);
		using var destino = new MemoryStream();

		await EnvelopeCipher.CifrarAsync(origem, destino, chaveDoArquivo, limiteBytes: long.MaxValue);

		return destino.ToArray();
	}

	private static async Task<byte[]> DecifrarAsync(byte[] cifrado, byte[] chaveDoArquivo)
	{
		using var origem = new MemoryStream(cifrado);
		using var destino = new MemoryStream();

		await EnvelopeCipher.DecifrarAsync(origem, destino, chaveDoArquivo);

		return destino.ToArray();
	}

	/// <summary>Procura <paramref name="procurado"/> como trecho contíguo de <paramref name="onde"/>.</summary>
	private static bool ContemSequencia(byte[] onde, byte[] procurado)
	{
		if (procurado.Length == 0)
		{
			return true;
		}

		for (var inicio = 0; inicio + procurado.Length <= onde.Length; inicio++)
		{
			if (onde.AsSpan(inicio, procurado.Length).SequenceEqual(procurado))
			{
				return true;
			}
		}

		return false;
	}

	[Fact]
	public void Embrulhar_SemChaveConfiguradaEmProduction_Falha()
	{
		var criar = () => Criar(new ChaveMestraOptions(), ambiente: "Production");

		criar.Should().Throw<ChaveMestraException>("uma chave ausente em produção precisa impedir a aplicação de subir");
	}

	[Fact]
	public void Embrulhar_SemChaveConfiguradaForaDeProduction_UsaChaveDeDesenvolvimento()
	{
		var cipher = Criar(new ChaveMestraOptions());

		var embrulhada = cipher.Embrulhar(EnvelopeCipher.GerarChaveDeArquivo());

		embrulhada.Should().StartWith("secco-enc:v1:");
	}

	[Fact]
	public void Desembrulhar_ComAChaveAtiva_DevolveAChaveOriginal()
	{
		var cipher = Criar(new ChaveMestraOptions { ChaveAtiva = ChaveBase64() });
		var chaveDoArquivo = EnvelopeCipher.GerarChaveDeArquivo();

		var recuperada = cipher.Desembrulhar(cipher.Embrulhar(chaveDoArquivo));

		recuperada.Should().Equal(chaveDoArquivo);
	}

	[Fact]
	public void Desembrulhar_ComChaveAposentada_AindaFunciona()
	{
		var chaveAntiga = ChaveBase64();
		var cipherAntigo = Criar(new ChaveMestraOptions { ChaveAtiva = chaveAntiga });
		var chaveDoArquivo = EnvelopeCipher.GerarChaveDeArquivo();
		var embrulhadaAntes = cipherAntigo.Embrulhar(chaveDoArquivo);

		var cipherNovo = Criar(new ChaveMestraOptions
		{
			ChaveAtiva = ChaveBase64(),
			ChavesAposentadas = [chaveAntiga],
		});

		cipherNovo.Desembrulhar(embrulhadaAntes).Should().Equal(
			chaveDoArquivo,
			"rotacionar a chave mestra não pode tornar ilegíveis os documentos já gravados");
	}

	[Fact]
	public void Desembrulhar_ComChaveDesconhecida_Falha()
	{
		var embrulhada = Criar(new ChaveMestraOptions { ChaveAtiva = ChaveBase64() })
			.Embrulhar(EnvelopeCipher.GerarChaveDeArquivo());

		var outro = Criar(new ChaveMestraOptions { ChaveAtiva = ChaveBase64() });

		var desembrulhar = () => outro.Desembrulhar(embrulhada);

		desembrulhar.Should().Throw<ChaveMestraException>();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(64)]
	[InlineData(EnvelopeCipher.TamanhoDoBloco - 1)]
	[InlineData(EnvelopeCipher.TamanhoDoBloco)]
	[InlineData(EnvelopeCipher.TamanhoDoBloco + 1)]
	[InlineData((EnvelopeCipher.TamanhoDoBloco * 2) + 7)]
	public async Task CifrarEDecifrar_EmQualquerTamanho_DevolveOConteudoOriginal(int tamanho)
	{
		var conteudo = RandomNumberGenerator.GetBytes(tamanho);
		var chaveDoArquivo = EnvelopeCipher.GerarChaveDeArquivo();

		var cifrado = await CifrarAsync(conteudo, chaveDoArquivo);

		ContemSequencia(cifrado, conteudo).Should().Be(
			tamanho == 0,
			"o arquivo em repouso não pode conter o conteúdo em claro (a sequência vazia é trivialmente contida)");
		(await DecifrarAsync(cifrado, chaveDoArquivo)).Should().Equal(conteudo);
	}

	[Fact]
	public async Task Cifrar_AcimaDoLimite_Falha()
	{
		using var origem = new MemoryStream(RandomNumberGenerator.GetBytes(4_096));
		using var destino = new MemoryStream();

		var cifrar = async () => await EnvelopeCipher.CifrarAsync(
			origem, destino, EnvelopeCipher.GerarChaveDeArquivo(), limiteBytes: 1_024);

		await cifrar.Should().ThrowAsync<ChaveMestraException>();
	}

	[Fact]
	public async Task Decifrar_ComByteAdulterado_Falha()
	{
		var chaveDoArquivo = EnvelopeCipher.GerarChaveDeArquivo();
		var cifrado = await CifrarAsync(Encoding.UTF8.GetBytes("conteúdo confidencial"), chaveDoArquivo);

		cifrado[^1] ^= 0xFF;

		var decifrar = async () => await DecifrarAsync(cifrado, chaveDoArquivo);

		await decifrar.Should().ThrowAsync<ChaveMestraException>("GCM autentica: adulterar precisa falhar, não devolver lixo");
	}

	[Fact]
	public async Task Decifrar_ComArquivoTruncado_Falha()
	{
		var chaveDoArquivo = EnvelopeCipher.GerarChaveDeArquivo();
		var conteudo = RandomNumberGenerator.GetBytes((EnvelopeCipher.TamanhoDoBloco * 2) + 100);
		var cifrado = await CifrarAsync(conteudo, chaveDoArquivo);

		// Corta o último bloco: sem a marca de "último" no dado associado, isso passaria batido.
		var truncado = cifrado[..(EnvelopeCipher.TamanhoDoBloco + 64)];

		var decifrar = async () => await DecifrarAsync(truncado, chaveDoArquivo);

		await decifrar.Should().ThrowAsync<ChaveMestraException>();
	}

	[Fact]
	public async Task Decifrar_ComBlocosReordenados_Falha()
	{
		var chaveDoArquivo = EnvelopeCipher.GerarChaveDeArquivo();
		var conteudo = RandomNumberGenerator.GetBytes((EnvelopeCipher.TamanhoDoBloco * 2) + 7);
		var cifrado = await CifrarAsync(conteudo, chaveDoArquivo);

		// Cabeçalho: assinatura (8) + versão (1) + tamanho do bloco (4).
		// Bloco cheio: marca de último (1) + nonce (12) + comprimento (4) + cifrado + tag (16).
		const int cabecalho = 8 + 1 + sizeof(int);
		var blocoCheio = 1 + 12 + sizeof(int) + EnvelopeCipher.TamanhoDoBloco + 16;

		var primeiro = cifrado[cabecalho..(cabecalho + blocoCheio)];
		var segundo = cifrado[(cabecalho + blocoCheio)..(cabecalho + (2 * blocoCheio))];

		var trocado = cifrado.ToArray();
		segundo.CopyTo(trocado, cabecalho);
		primeiro.CopyTo(trocado, cabecalho + blocoCheio);

		var decifrar = async () => await DecifrarAsync(trocado, chaveDoArquivo);

		await decifrar.Should().ThrowAsync<ChaveMestraException>(
			"o índice do bloco entra no dado associado, então trocar dois blocos de lugar quebra o tag");
	}

	[Fact]
	public async Task Decifrar_ComCabecalhoDeOutroFormato_Falha()
	{
		var decifrar = async () => await DecifrarAsync(
			Encoding.UTF8.GetBytes("NAOEUMARQUIVOCIFRADO"), EnvelopeCipher.GerarChaveDeArquivo());

		await decifrar.Should().ThrowAsync<ChaveMestraException>();
	}
}
