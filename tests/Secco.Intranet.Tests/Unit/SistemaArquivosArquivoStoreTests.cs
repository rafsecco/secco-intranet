using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Infrastructure.Armazenamento;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Cryptography;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Armazenamento em sistema de arquivos: composição do caminho (inclusive a pasta lógica dos
/// arquivos que não são documento) e ida e volta cifrada, sobre um diretório temporário.
/// </summary>
public sealed class SistemaArquivosArquivoStoreTests : IDisposable
{
	private sealed class TenantContextFalso(Guid tenantId) : ITenantContext
	{
		public Guid? TenantId { get; } = tenantId;

		public bool IsResolved => true;
	}

	private sealed class AmbienteFalso : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = "Development";

		public string ApplicationName { get; set; } = "Secco.Intranet.Tests";

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private readonly string _raiz = Path.Combine(Path.GetTempPath(), "secco-store-" + Guid.NewGuid().ToString("N"));
	private readonly Guid _tenantId = Guid.NewGuid();

	public void Dispose()
	{
		if (Directory.Exists(_raiz))
		{
			Directory.Delete(_raiz, recursive: true);
		}
	}

	private SistemaArquivosArquivoStore CriarStore(
		long? limite = null,
		ChaveMestraOptions? chave = null,
		string ambiente = "Development") =>
		new(
			new ArquivoStoreOptions { Raiz = _raiz },
			new DocumentoOptions { TamanhoMaximoBytes = limite ?? new DocumentoOptions().TamanhoMaximoBytes },
			CifradorDeArquivos.Criar(chave ?? new ChaveMestraOptions(), new AmbienteFalso { EnvironmentName = ambiente }),
			new TenantContextFalso(_tenantId));

	private static string ChaveBase64(byte valor) =>
		Convert.ToBase64String(Enumerable.Repeat(valor, ChaveMestraOptions.TamanhoEmBytes).ToArray());

	private string CaminhoNoDisco(ArquivoGravado gravado) => Path.Combine(_raiz, gravado.CaminhoRelativo);

	[Fact]
	public async Task Gravar_ComPastaLogica_CaminhoTemTenantPastaEIdentificador()
	{
		var store = CriarStore();
		using var conteudo = new MemoryStream("foto"u8.ToArray());

		var gravado = await store.GravarAsync(conteudo, PastasDeArquivo.FotosDoDiretorio);

		var partes = gravado.CaminhoRelativo.Split('/');
		partes.Should().HaveCount(5);
		partes[0].Should().Be(_tenantId.ToString());
		(partes[1] + "/" + partes[2]).Should().Be(PastasDeArquivo.FotosDoDiretorio);
		partes[3].Should().Be(partes[4][..2]);
		File.Exists(Path.Combine(_raiz, gravado.CaminhoRelativo)).Should().BeTrue();
	}

	[Fact]
	public async Task Gravar_PastaForaDaLista_Lanca()
	{
		var store = CriarStore();
		using var conteudo = new MemoryStream("x"u8.ToArray());

		var gravar = () => store.GravarAsync(conteudo, "../fora");

		await gravar.Should().ThrowAsync<ArgumentException>();
	}

	[Fact]
	public async Task Gravar_ComPasta_LeDeVoltaOMesmoConteudo()
	{
		var store = CriarStore();
		var original = Encoding.UTF8.GetBytes("conteudo da foto");
		using var entrada = new MemoryStream(original);

		var gravado = await store.GravarAsync(entrada, PastasDeArquivo.FotosDoDiretorio);

		using var saida = new MemoryStream();
		await store.EscreverEmAsync(gravado.CaminhoRelativo, gravado.ChaveEmbrulhada, saida);
		saida.ToArray().Should().Equal(original);
	}

	[Fact]
	public async Task Remover_DaPastaLogica_ApagaOArquivo()
	{
		var store = CriarStore();
		using var entrada = new MemoryStream("x"u8.ToArray());
		var gravado = await store.GravarAsync(entrada, PastasDeArquivo.FotosDoDiretorio);

		await store.RemoverAsync(gravado.CaminhoRelativo);

		File.Exists(Path.Combine(_raiz, gravado.CaminhoRelativo)).Should().BeFalse();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(65535)]
	[InlineData(65536)]
	[InlineData(65537)]
	[InlineData((3 * 65536) + 7)]
	public async Task GravarELer_EmQualquerTamanho_DevolveOConteudo(int tamanho)
	{
		var store = CriarStore();
		var original = new byte[tamanho];
		Random.Shared.NextBytes(original);
		using var entrada = new MemoryStream(original);

		var gravado = await store.GravarAsync(entrada);

		gravado.Tamanho.Should().Be(tamanho);
		gravado.ChaveEmbrulhada.Should().StartWith("secco-enc:v1:");
		using var saida = new MemoryStream();
		await store.EscreverEmAsync(gravado.CaminhoRelativo, gravado.ChaveEmbrulhada, saida);
		saida.ToArray().Should().Equal(original);
	}

	[Fact]
	public async Task Gravar_ArquivoNoDisco_ComecaComOCabecalhoDoSdk()
	{
		var store = CriarStore();
		using var entrada = new MemoryStream("conteudo"u8.ToArray());

		var gravado = await store.GravarAsync(entrada);

		var bytes = await File.ReadAllBytesAsync(CaminhoNoDisco(gravado));
		bytes[..9].Should().Equal([.. "SECCOENC"u8, (byte)0x01]);
	}

	[Fact]
	public async Task Ler_ArquivoAdulterado_Falha()
	{
		var store = CriarStore();
		using var entrada = new MemoryStream(new byte[1000]);
		var gravado = await store.GravarAsync(entrada);
		var caminho = CaminhoNoDisco(gravado);
		var bytes = await File.ReadAllBytesAsync(caminho);
		// Cabeçalho tem 9 bytes: o byte 100 está dentro do texto cifrado do primeiro bloco.
		bytes[100] ^= 0xFF;
		await File.WriteAllBytesAsync(caminho, bytes);

		using var saida = new MemoryStream();
		var ler = () => store.EscreverEmAsync(gravado.CaminhoRelativo, gravado.ChaveEmbrulhada, saida);

		await ler.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Ler_BlocoPosteriorAdulterado_Falha()
	{
		var store = CriarStore();
		using var entrada = new MemoryStream(new byte[(3 * 65536) + 7]);
		var gravado = await store.GravarAsync(entrada);
		var caminho = CaminhoNoDisco(gravado);
		var bytes = await File.ReadAllBytesAsync(caminho);
		bytes[^50] ^= 0xFF;
		await File.WriteAllBytesAsync(caminho, bytes);

		using var saida = new MemoryStream();
		var ler = () => store.EscreverEmAsync(gravado.CaminhoRelativo, gravado.ChaveEmbrulhada, saida);

		await ler.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Ler_ArquivoSemOUltimoBloco_Falha()
	{
		var store = CriarStore();
		using var entrada = new MemoryStream(new byte[(3 * 65536) + 7]);
		var gravado = await store.GravarAsync(entrada);
		var caminho = CaminhoNoDisco(gravado);
		var bytes = await File.ReadAllBytesAsync(caminho);
		// Corta no fim do terceiro bloco cheio: cabeçalho de 9 bytes + 3 blocos de 65536 + 16 (tag).
		await File.WriteAllBytesAsync(caminho, bytes[..(9 + (3 * (65536 + 16)))]);

		using var saida = new MemoryStream();
		var ler = () => store.EscreverEmAsync(gravado.CaminhoRelativo, gravado.ChaveEmbrulhada, saida);

		await ler.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Ler_ArquivoNoFormatoAntigo_Falha()
	{
		var store = CriarStore();
		using var entrada = new MemoryStream("conteudo"u8.ToArray());
		var gravado = await store.GravarAsync(entrada);
		await File.WriteAllBytesAsync(CaminhoNoDisco(gravado), [.. "SCCOFILE"u8, .. new byte[1024]]);

		using var saida = new MemoryStream();
		var ler = () => store.EscreverEmAsync(gravado.CaminhoRelativo, gravado.ChaveEmbrulhada, saida);

		await ler.Should().ThrowAsync<SeccoSecretCipherException>();
		saida.Length.Should().Be(0);
	}

	[Fact]
	public async Task Ler_ComChaveDeOutraChaveMestra_Falha()
	{
		var gravador = CriarStore(chave: new ChaveMestraOptions { ChaveAtiva = ChaveBase64(1) }, ambiente: "Production");
		using var entrada = new MemoryStream("conteudo"u8.ToArray());
		var gravado = await gravador.GravarAsync(entrada);
		var leitor = CriarStore(chave: new ChaveMestraOptions { ChaveAtiva = ChaveBase64(2) }, ambiente: "Production");

		using var saida = new MemoryStream();
		var ler = () => leitor.EscreverEmAsync(gravado.CaminhoRelativo, gravado.ChaveEmbrulhada, saida);

		await ler.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Gravar_NoLimiteExato_Aceita()
	{
		var store = CriarStore(limite: 1000);
		using var entrada = new MemoryStream(new byte[1000]);

		var gravado = await store.GravarAsync(entrada);

		gravado.Tamanho.Should().Be(1000);
		using var saida = new MemoryStream();
		await store.EscreverEmAsync(gravado.CaminhoRelativo, gravado.ChaveEmbrulhada, saida);
		saida.ToArray().Should().Equal(new byte[1000]);
	}

	[Fact]
	public async Task Gravar_UmByteAcimaDoLimite_FalhaSemDeixarArquivo()
	{
		var store = CriarStore(limite: 1000);
		using var entrada = new MemoryStream(new byte[1001]);

		var gravar = () => store.GravarAsync(entrada);

		await gravar.Should().ThrowAsync<SeccoFileTooLargeException>();
		Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories).Should().BeEmpty();
	}

	[Fact]
	public async Task Gravar_Cancelado_FalhaSemDeixarArquivo()
	{
		var store = CriarStore();
		using var entrada = new MemoryStream(new byte[200_000]);
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var gravar = () => store.GravarAsync(entrada, cts.Token);

		await gravar.Should().ThrowAsync<OperationCanceledException>();
		Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories).Should().BeEmpty();
	}
}
