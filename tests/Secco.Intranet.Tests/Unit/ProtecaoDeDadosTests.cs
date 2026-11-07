using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Secco.Intranet.Web.Hosting;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// As chaves do DataProtection assinam cookie de sessão, antiforgery e TempData. Fora de uma pasta
/// persistente, recriar o container invalida tudo isso; presas ao content root ou à máquina, o
/// <c>dotnet run</c> e o container deixam de se entender, e copiar a pasta não leva as chaves.
/// </summary>
public sealed class ProtecaoDeDadosTests : IDisposable
{
	private readonly string _pasta = Path.Combine(Path.GetTempPath(), "intranet-testes-chaves", Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(_pasta))
		{
			Directory.Delete(_pasta, recursive: true);
		}
	}

	private sealed class AmbienteFalso(string contentRootPath) : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = "Testing";

		public string ApplicationName { get; set; } = "testes";

		public string ContentRootPath { get; set; } = contentRootPath;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private static ServiceProvider Montar(string contentRoot, string? pastaDasChaves)
	{
		var configuracao = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[IntranetDadosExtensions.ChavePastaDasChaves] = pastaDasChaves,
			})
			.Build();

		var servicos = new ServiceCollection();
		servicos.AddSingleton<IHostEnvironment>(new AmbienteFalso(contentRoot));
		servicos.AddIntranetProtecaoDeDados(configuracao);
		return servicos.BuildServiceProvider();
	}

	[Fact]
	public void ComPasta_GravaAChaveNela()
	{
		using var provedor = Montar("/app", _pasta);

		provedor.GetRequiredService<IDataProtectionProvider>().CreateProtector("t").Protect("x");

		Directory.GetFiles(_pasta, "key-*.xml").Should().NotBeEmpty();
	}

	[Fact]
	public void AChaveNaoFicaPresaAMaquina()
	{
		using var provedor = Montar("/app", _pasta);

		provedor.GetRequiredService<IDataProtectionProvider>().CreateProtector("t").Protect("x");

		var xml = File.ReadAllText(Directory.GetFiles(_pasta, "key-*.xml").Single());
		xml.Should().NotContain("encryptedSecret");
	}

	[Fact]
	public void ContentRootsDiferentes_DeciframOQueOOutroCifrou()
	{
		using var doDotnetRun = Montar("/repositorio/src/Secco.Intranet.Web", _pasta);
		using var doContainer = Montar("/app", _pasta);

		var cifrado = doDotnetRun.GetRequiredService<IDataProtectionProvider>().CreateProtector("t").Protect("x");

		doContainer.GetRequiredService<IDataProtectionProvider>().CreateProtector("t").Unprotect(cifrado).Should().Be("x");
	}

	[Fact]
	public void SemPasta_ProtegeEDecifra()
	{
		using var provedor = Montar("/app", null);
		var protetor = provedor.GetRequiredService<IDataProtectionProvider>().CreateProtector("t");

		protetor.Unprotect(protetor.Protect("x")).Should().Be("x");
	}
}
