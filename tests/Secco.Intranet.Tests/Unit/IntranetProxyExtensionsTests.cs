using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Secco.Intranet.Web.Hosting;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Teste unitário da leitura de configuração de confiança em reverse proxy
/// (<see cref="IntranetProxyExtensions.Habilitado"/>). A composição de DI em si
/// (<see cref="IntranetProxyExtensions.AddIntranetProxyForwarding"/>) não tem teste automatizado
/// próprio: verificar o efeito exigiria referenciar <c>Microsoft.AspNetCore.HttpOverrides</c> no
/// projeto de testes, e esse tipo não resolve nem em tempo de compilação nem de execução aqui —
/// nada no projeto de testes carrega aquele assembly hoje (confirmado com
/// <c>dotnet build -v:diag</c> e, por reflexão em runtime, com <c>TypeLoadException</c>). A lógica
/// em si (duas linhas: <c>ForwardedHeaders</c> e limpar as duas coleções) foi conferida à mão —
/// ver o roteiro de deploy para o passo manual de confirmar o cabeçalho aceito atrás de um proxy
/// de verdade.
/// </summary>
public class IntranetProxyExtensionsTests
{
	private static IConfiguration BuildConfiguration(bool? habilitado) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection(habilitado is null
				? []
				: new Dictionary<string, string?> { ["Secco:ReverseProxy:Habilitado"] = habilitado.Value.ToString() })
			.Build();

	[Fact]
	public void Habilitado_LePelaChaveDeConfiguracao()
	{
		IntranetProxyExtensions.Habilitado(BuildConfiguration(true)).Should().BeTrue();
		IntranetProxyExtensions.Habilitado(BuildConfiguration(false)).Should().BeFalse();
		IntranetProxyExtensions.Habilitado(BuildConfiguration(null)).Should().BeFalse("sem a chave, o padrão é não confiar em nada");
	}
}
