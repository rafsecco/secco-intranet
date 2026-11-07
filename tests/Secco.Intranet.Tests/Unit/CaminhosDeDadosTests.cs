using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Secco.Intranet.Web.Hosting;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Os caminhos de dados de DEV são relativos, e o <c>dotnet run</c>, o F5 e o container precisam
/// chegar à mesma pasta: a referência é o content root, nunca o diretório de trabalho do processo.
/// </summary>
public class CaminhosDeDadosTests
{
	private const string ChaveDaRaizDeDocumentos = "Intranet:Documentos:Armazenamento:Raiz";

	private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "app", "src", "Web");

	public static TheoryData<string> Chaves => new()
	{
		ChaveDaRaizDeDocumentos,
		IntranetDadosExtensions.ChavePastaDasChaves,
	};

	private static ConfigurationManager ConfiguracaoCom(string chave, string? valor)
	{
		var configuracao = new ConfigurationManager();
		configuracao.AddInMemoryCollection(new Dictionary<string, string?> { [chave] = valor });
		return configuracao;
	}

	[Theory]
	[MemberData(nameof(Chaves))]
	public void CaminhoRelativo_ViraAbsolutoAPartirDoContentRoot(string chave)
	{
		var configuracao = ConfiguracaoCom(chave, "../../.dados/documentos");

		configuracao.ResolverCaminhosDeDados(ContentRoot);

		configuracao[chave].Should().Be(Path.GetFullPath(Path.Combine(ContentRoot, "../../.dados/documentos")));
	}

	[Theory]
	[MemberData(nameof(Chaves))]
	public void CaminhoAbsoluto_FicaIntacto(string chave)
	{
		var absoluto = Path.Combine(Path.GetTempPath(), "dados", "pasta");
		var configuracao = ConfiguracaoCom(chave, absoluto);

		configuracao.ResolverCaminhosDeDados(ContentRoot);

		configuracao[chave].Should().Be(absoluto);
	}

	[Theory]
	[MemberData(nameof(Chaves))]
	public void ChaveAusente_FicaAusente(string chave)
	{
		var configuracao = new ConfigurationManager();

		configuracao.ResolverCaminhosDeDados(ContentRoot);

		configuracao[chave].Should().BeNull();
	}

	[Theory]
	[MemberData(nameof(Chaves))]
	public void ChaveVazia_FicaVazia(string chave)
	{
		var configuracao = ConfiguracaoCom(chave, "");

		configuracao.ResolverCaminhosDeDados(ContentRoot);

		configuracao[chave].Should().BeEmpty();
	}
}
