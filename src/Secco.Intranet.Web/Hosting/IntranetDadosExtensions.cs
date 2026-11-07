using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Secco.Intranet.Web.Hosting;

/// <summary>
/// Onde a aplicação guarda o que precisa sobreviver a recriar o container ou a limpar o
/// <c>bin/</c>: a resolução dos caminhos de dados e a pasta das chaves do DataProtection.
/// </summary>
public static class IntranetDadosExtensions
{
	/// <summary>Chave de configuração da pasta das chaves do DataProtection.</summary>
	public const string ChavePastaDasChaves = "Intranet:ProtecaoDeDados:PastaDasChaves";

	private const string ChaveRaizDeDocumentos = "Intranet:Documentos:Armazenamento:Raiz";

	/// <summary>
	/// Troca os caminhos de dados relativos pelo absoluto a partir do content root, para o
	/// <c>dotnet run</c> e o F5 chegarem à mesma pasta de qualquer diretório de trabalho. Caminho
	/// absoluto, chave vazia e chave ausente ficam como estão.
	/// </summary>
	/// <param name="configuration">Configuração do host.</param>
	/// <param name="contentRootPath">Content root da aplicação.</param>
	public static void ResolverCaminhosDeDados(this ConfigurationManager configuration, string contentRootPath)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var resolvidos = new Dictionary<string, string?>();

		foreach (var chave in new[] { ChaveRaizDeDocumentos, ChavePastaDasChaves })
		{
			var valor = configuration[chave];

			if (!string.IsNullOrWhiteSpace(valor) && !Path.IsPathRooted(valor))
			{
				resolvidos[chave] = Path.GetFullPath(Path.Combine(contentRootPath, valor));
			}
		}

		if (resolvidos.Count > 0)
		{
			configuration.AddInMemoryCollection(resolvidos);
		}
	}

	/// <summary>
	/// Registra o DataProtection com nome de aplicação fixo e, quando
	/// <see cref="ChavePastaDasChaves"/> está preenchida, com as chaves nessa pasta. Vazia, vale o
	/// padrão do framework.
	/// </summary>
	/// <remarks>
	/// As chaves ficam em arquivo, e não no banco como manda a ADR-0035 da plataforma, porque a
	/// Intranet só tem bancos por tenant, e o conjunto de chaves é da instalação (ADR-0014 da
	/// Intranet). O nome fixo é necessário porque o padrão deriva do content root, que difere entre
	/// o <c>dotnet run</c> e o container. Sem cifra em repouso, a pasta deve ser tratada como
	/// segredo, e o aviso "No XML encryptor configured" no log é esperado.
	/// </remarks>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	/// <param name="configuration">Configuração do host.</param>
	public static IServiceCollection AddIntranetProtecaoDeDados(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		var dataProtection = services.AddDataProtection().SetApplicationName("intranet");
		var pasta = configuration[ChavePastaDasChaves];

		if (!string.IsNullOrWhiteSpace(pasta))
		{
			dataProtection.PersistKeysToFileSystem(new DirectoryInfo(pasta));
		}

		return services;
	}
}
