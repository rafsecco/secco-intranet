using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O botão de chat nas três telas do Diretório (cartão do Organograma, página da pessoa e lista),
/// com o chat ligado e desligado. A fábrica padrão fica com o chat desligado; para ligar, cada teste
/// abre uma fábrica derivada com <c>Intranet:Chat:Provedor=Teams</c>.
/// </summary>
public class ChatNoDiretorioTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private const string BaseDoTeams = "https://teams.microsoft.com/l/chat/0/0?users=";

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(UsuariosParaDiretorioFalso usuarios, bool comTeams, string tema = "Vertical", Guid? quemVe = null)
	{
		factory.UsuariosDoDiretorio = usuarios;
		var client = factory
			.WithWebHostBuilder(builder =>
			{
				builder.UseSetting("Intranet:Theme:Nome", tema);

				if (comTeams)
				{
					builder.UseSetting("Intranet:Chat:Provedor", "Teams");
				}
			})
			.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "diretorio-user");

		if (quemVe is not null)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, quemVe.ToString());
		}

		return client;
	}

	private async Task DefinirGestorAsync(UsuariosParaDiretorioFalso usuarios, Guid pessoa, Guid gestor)
	{
		factory.UsuariosDoDiretorio = usuarios;
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		(await escopo.ServiceProvider.GetRequiredService<EditarDadosFuncionaisHandler>()
			.HandleAsync(new EditarDadosFuncionaisCommand(pessoa, null, null, gestor))).IsSuccess.Should().BeTrue();
	}

	private static int Contar(string html, string trecho) =>
		html.Split(trecho).Length - 1;

	[Theory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Organograma_ComTeams_CartaoTemOLinkDeChat(string tema)
	{
		var chefe = Guid.NewGuid();
		var membro = Guid.NewGuid();
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var usuarios = new UsuariosParaDiretorioFalso().Com(chefe, $"chefe{sufixo}@exemplo.test").Com(membro, $"membro{sufixo}@exemplo.test");
		await DefinirGestorAsync(usuarios, membro, chefe);

		var html = await CriarCliente(usuarios, comTeams: true, tema).GetStringAsync("/diretorio/organograma");

		html.Should().Contain(tema == "Horizontal" ? "sc-topnav" : "sc-sidebar", "garante que o tema trocou de fato");
		html.Should().MatchRegex(
			$"<a class=\"sc-org__chat\" href=\"{System.Text.RegularExpressions.Regex.Escape(BaseDoTeams)}chefe{sufixo}%40exemplo\\.test\" target=\"_blank\" rel=\"noopener noreferrer\"");
		html.Should().Contain($"{BaseDoTeams}membro{sufixo}%40exemplo.test");
		Decodificar(html).Should().Contain("Conversar com chefe" + sufixo + "@exemplo.test no Teams");
	}

	[Fact]
	public async Task Pessoa_ComTeams_TemOBotao()
	{
		var ana = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(ana, "ana@exemplo.test", "Ana Exemplo");

		var html = await CriarCliente(usuarios, comTeams: true, quemVe: Guid.NewGuid()).GetStringAsync($"/diretorio/{ana}");

		html.Should().Contain($"href=\"{BaseDoTeams}ana%40exemplo.test\"");
		html.Should().Contain("target=\"_blank\"").And.Contain("rel=\"noopener noreferrer\"");
		Decodificar(html).Should().Contain("Conversar com Ana Exemplo no Teams");
	}

	[Fact]
	public async Task Lista_ComTeams_CadaLinhaTemOBotao()
	{
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var usuarios = new UsuariosParaDiretorioFalso()
			.Com(Guid.NewGuid(), $"ana{sufixo}@exemplo.test")
			.Com(Guid.NewGuid(), $"bruno{sufixo}@exemplo.test");

		var html = await CriarCliente(usuarios, comTeams: true, quemVe: Guid.NewGuid()).GetStringAsync($"/diretorio?busca={sufixo}");

		Contar(html, $"href=\"{BaseDoTeams}").Should().Be(2);
		html.Should().Contain($"{BaseDoTeams}ana{sufixo}%40exemplo.test").And.Contain($"{BaseDoTeams}bruno{sufixo}%40exemplo.test");
	}

	[Fact]
	public async Task Pessoa_APropria_NaoTemOBotao()
	{
		var eu = Guid.NewGuid();
		var usuarios = new UsuariosParaDiretorioFalso().Com(eu, "eu@exemplo.test", "Eu Mesmo");

		var html = await CriarCliente(usuarios, comTeams: true, quemVe: eu).GetStringAsync($"/diretorio/{eu}");

		html.Should().NotContain("teams.microsoft.com");
		html.Should().NotContain("Conversar com");
	}

	[Theory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Organograma_SemConfiguracao_NaoTemLinkDeChat(string tema)
	{
		var chefe = Guid.NewGuid();
		var membro = Guid.NewGuid();
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var usuarios = new UsuariosParaDiretorioFalso().Com(chefe, $"chefe{sufixo}@exemplo.test").Com(membro, $"membro{sufixo}@exemplo.test");
		await DefinirGestorAsync(usuarios, membro, chefe);

		var html = await CriarCliente(usuarios, comTeams: false, tema).GetStringAsync("/diretorio/organograma");

		html.Should().Contain($"chefe{sufixo}@exemplo.test", "o organograma abriu com a equipe");
		html.Should().NotContain("sc-org__chat").And.NotContain("teams.microsoft.com");
	}

	[Fact]
	public async Task Lista_SemConfiguracao_NaoTemBotao()
	{
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var usuarios = new UsuariosParaDiretorioFalso().Com(Guid.NewGuid(), $"ana{sufixo}@exemplo.test");

		var html = await CriarCliente(usuarios, comTeams: false, quemVe: Guid.NewGuid()).GetStringAsync($"/diretorio?busca={sufixo}");

		html.Should().Contain($"ana{sufixo}@exemplo.test", "a pessoa está na lista");
		html.Should().NotContain("teams.microsoft.com").And.NotContain("Conversar com");
	}
}
