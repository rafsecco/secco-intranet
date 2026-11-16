using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Menu do usuário no cabeçalho. Sem login configurado (host local e de testes) não há nome,
/// "Meu perfil", "Entrar" nem "Sair" a mostrar: o menu não pode abrir vazio.
/// </summary>
public class MenuDoUsuarioTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public static TheoryData<string> Temas => new() { "Vertical", "Horizontal" };

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Theory]
	[MemberData(nameof(Temas))]
	public async Task SemLoginConfigurado_MenuDizQuemEEPorQueNaoHaOpcoes(string tema)
	{
		var cliente = factory.WithWebHostBuilder(b => b.UseSetting("Intranet:Theme:Nome", tema)).CreateClient();
		cliente.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		var html = WebUtility.HtmlDecode(await cliente.GetStringAsync("/"));

		var menu = Regex.Match(
			html, "aria-label=\"Menu do usuário\".*?<ul class=\"dropdown-menu[^\"]*\">(.*?)</ul>", RegexOptions.Singleline);
		menu.Success.Should().BeTrue("o cabeçalho tem o menu do usuário");

		var itens = menu.Groups[1].Value;
		itens.Should().Contain("Visitante");
		itens.Should().Contain("Sem login configurado nesta instalação.");
		itens.Should().NotContain("Sair").And.NotContain("Entrar");
	}
}
