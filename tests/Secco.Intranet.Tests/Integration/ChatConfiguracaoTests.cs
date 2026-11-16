using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Secco.Intranet.Web.Diretorio;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A configuração do chat é validada na subida do host, não no primeiro uso. Sobe um host mínimo só
/// com <c>AddIntranetChat</c> (sem banco nem Docker): o que se prova é a validação na subida, e ela
/// não depende do resto da aplicação.
/// </summary>
public class ChatConfiguracaoTests
{
	private static IHost Host(params (string Chave, string Valor)[] valores)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(valores.ToDictionary(valor => valor.Chave, valor => (string?)valor.Valor))
			.Build();

		return new HostBuilder()
			.ConfigureServices(services => services.AddIntranetChat(configuration))
			.Build();
	}

	private static IEnumerable<string> Mensagens(Exception excecao)
	{
		for (var atual = excecao; atual is not null; atual = atual.InnerException)
		{
			yield return atual.Message;
		}
	}

	[Theory]
	[InlineData("Slack")]
	[InlineData("xyz")]
	public async Task Subida_ProvedorDesconhecido_Falha(string valor)
	{
		using var host = Host(("Intranet:Chat:Provedor", valor));

		var subir = () => host.StartAsync();

		var excecao = (await subir.Should().ThrowAsync<InvalidOperationException>()).Which;
		Mensagens(excecao).Should().Contain(mensagem => mensagem.Contains("Intranet:Chat:Provedor", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(" teams ")]
	[InlineData("TEAMS")]
	public async Task Subida_ProvedorComEspacosECaixa_ValeTeams(string valor)
	{
		using var host = Host(("Intranet:Chat:Provedor", valor));

		await host.StartAsync();

		host.Services.GetRequiredService<ILinkDeChat>().NomeDoProvedor.Should().Be("Teams");
		await host.StopAsync();
	}

	[Fact]
	public async Task Subida_TenantIdQueNaoEGuid_Falha()
	{
		using var host = Host(("Intranet:Chat:Provedor", "Teams"), ("Intranet:Chat:TenantId", "nao-e-guid"));

		var subir = () => host.StartAsync();

		var excecao = (await subir.Should().ThrowAsync<InvalidOperationException>()).Which;
		Mensagens(excecao).Should().Contain(mensagem => mensagem.Contains("Intranet:Chat:TenantId", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Subida_SemSecao_ChatDesligado()
	{
		using var host = Host();

		await host.StartAsync();

		host.Services.GetRequiredService<ILinkDeChat>().NomeDoProvedor.Should().BeNull();
		await host.StopAsync();
	}
}
