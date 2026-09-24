using System.Net;
using AwesomeAssertions;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Estado vazio do mural e resolução do tema. As duas coisas se checam pelo HTML real que sai
/// do host, e não pela configuração que entrou nele.
/// </summary>
public class DemonstracaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task Mural_SemPublicacoes_RespondeComEstadoVazio()
	{
		// O mural nao olha mais a flag de demonstracao: ele le publicacoes reais. O que
		// continua valendo e que um mural sem nada a mostrar responde 200 com estado vazio.
		var resposta = await factory.CreateClient().GetAsync("/");
		var html = await resposta.Content.ReadAsStringAsync();

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "o mural é a página inicial e responde sempre");
		html.Should().Contain("sc-empty", "sem publicações, o mural mostra o estado vazio em vez de uma lista vazia");
	}

	[Fact]
	public async Task Home_SempreRenderizaComOLayoutDoTema()
	{
		var html = await factory.CreateClient().GetStringAsync("/");

		html.Should().Contain("data-theme=\"vertical\"", "o layout precisa vir da RCL do tema, não do core");
		html.Should().Contain("_content/Secco.Intranet.Themes.Vertical/css/theme.css");
		html.Should().Contain("sc-sidebar", "o menu é renderizado pelo view component com markup do tema");
	}
}
