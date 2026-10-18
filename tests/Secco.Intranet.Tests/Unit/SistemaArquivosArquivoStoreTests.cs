using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Infrastructure.Armazenamento;
using Secco.SDK.AspNetCore.Tenancy;
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

	private SistemaArquivosArquivoStore CriarStore() =>
		new(
			new ArquivoStoreOptions { Raiz = _raiz },
			new DocumentoOptions(),
			new EnvelopeCipher(new ChaveMestraOptions(), new AmbienteFalso()),
			new TenantContextFalso(_tenantId));

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
}
