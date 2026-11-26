using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Secco.Intranet.Infrastructure.Armazenamento;
using Secco.SDK.EntityFrameworkCore.Cryptography;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public sealed class CifradorDeArquivosTests
{
	private sealed class AmbienteFalso(string nome) : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = nome;

		public string ApplicationName { get; set; } = "Secco.Intranet.Tests";

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private static string NovaChave(int bytes = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

	private static async Task<(string ChaveEmbrulhada, byte[] Cifrado)> CifrarAsync(ISeccoFileCipher cifrador, byte[] claro)
	{
		using var origem = new MemoryStream(claro);
		using var destino = new MemoryStream();
		var resultado = await cifrador.EncryptAsync(origem, destino);

		return (resultado.WrappedKey, destino.ToArray());
	}

	private static async Task<byte[]> DecifrarAsync(ISeccoFileCipher cifrador, byte[] cifrado, string chaveEmbrulhada)
	{
		using var origem = new MemoryStream(cifrado);
		using var destino = new MemoryStream();
		await cifrador.DecryptAsync(origem, destino, chaveEmbrulhada);

		return destino.ToArray();
	}

	[Theory]
	[InlineData("Production")]
	[InlineData("Staging")]
	[InlineData("Testing")]
	public void Criar_SemChaveForaDeDevelopment_Falha(string ambiente)
	{
		var acao = () => CifradorDeArquivos.Criar(new ChaveMestraOptions { ChaveAtiva = null }, new AmbienteFalso(ambiente));

		acao.Should().Throw<ChaveMestraException>();
	}

	[Fact]
	public async Task Criar_ComChaveEmBrancoEmDevelopment_UsaAChaveDeDesenvolvimento()
	{
		var cifrador = CifradorDeArquivos.Criar(new ChaveMestraOptions { ChaveAtiva = "  " }, new AmbienteFalso("Development"));
		var claro = "conteudo"u8.ToArray();

		var (chave, cifrado) = await CifrarAsync(cifrador, claro);

		(await DecifrarAsync(cifrador, cifrado, chave)).Should().Equal(claro);

		var manual = new AesGcmFileCipher(AesGcmSecretCipher.FromBase64Keys(ChaveMestraOptions.ChaveDesenvolvimento));
		(await DecifrarAsync(manual, cifrado, chave)).Should().Equal(claro);
	}

	[Fact]
	public void Criar_ComChaveAtivaEmBase64Invalido_Falha()
	{
		var acao = () => CifradorDeArquivos.Criar(new ChaveMestraOptions { ChaveAtiva = "não é base64" }, new AmbienteFalso("Production"));

		acao.Should().Throw<ChaveMestraException>()
			.WithMessage("*Chave mestra*")
			.WithInnerException<SeccoSecretCipherException>();
	}

	[Fact]
	public void Criar_ComChaveAtivaDeTamanhoErrado_Falha()
	{
		var acao = () => CifradorDeArquivos.Criar(new ChaveMestraOptions { ChaveAtiva = NovaChave(16) }, new AmbienteFalso("Production"));

		acao.Should().Throw<ChaveMestraException>()
			.WithMessage("*Chave mestra*")
			.WithInnerException<SeccoSecretCipherException>();
	}

	[Fact]
	public void Criar_ComChaveAposentadaMalformada_Falha()
	{
		var options = new ChaveMestraOptions { ChaveAtiva = NovaChave(), ChavesAposentadas = ["xx"] };

		var acao = () => CifradorDeArquivos.Criar(options, new AmbienteFalso("Production"));

		acao.Should().Throw<ChaveMestraException>()
			.WithMessage("*Chave mestra*")
			.WithInnerException<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Decifrar_ArquivoDaChaveAposentada_Funciona()
	{
		var k1 = NovaChave();
		var k2 = NovaChave();
		var ambiente = new AmbienteFalso("Production");
		var a = CifradorDeArquivos.Criar(new ChaveMestraOptions { ChaveAtiva = k1 }, ambiente);
		var b = CifradorDeArquivos.Criar(new ChaveMestraOptions { ChaveAtiva = k2, ChavesAposentadas = [k1] }, ambiente);
		var claro = RandomNumberGenerator.GetBytes(4096);

		var (chave, cifrado) = await CifrarAsync(a, claro);

		(await DecifrarAsync(b, cifrado, chave)).Should().Equal(claro);
		b.IsKeyWrappedWithActiveKey(chave).Should().BeFalse();
	}

	[Fact]
	public async Task Decifrar_ComChaveMestraDesconhecida_Falha()
	{
		var ambiente = new AmbienteFalso("Production");
		var a = CifradorDeArquivos.Criar(new ChaveMestraOptions { ChaveAtiva = NovaChave() }, ambiente);
		var b = CifradorDeArquivos.Criar(new ChaveMestraOptions { ChaveAtiva = NovaChave() }, ambiente);

		var (chave, cifrado) = await CifrarAsync(a, "segredo"u8.ToArray());

		var acao = () => DecifrarAsync(b, cifrado, chave);

		await acao.Should().ThrowAsync<SeccoSecretCipherException>();
	}
}
