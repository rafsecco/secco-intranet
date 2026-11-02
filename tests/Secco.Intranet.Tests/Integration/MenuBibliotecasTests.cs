using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Navigation;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// As bibliotecas como itens do menu principal: aparecem só para quem as lê, somem quando
/// inativas e entram na tela de ordenação do <c>intranet-admin</c>, com a posição valendo no menu.
/// </summary>
public class MenuBibliotecasTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.ResolvedorDePermissoes = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(params string[] roles)
	{
		var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private async Task<BibliotecaDto> CriarBibliotecaAsync(string nome, bool ativa = true)
	{
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa().ComPerfilImplicito();

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var slug = $"mb-{Guid.NewGuid():N}"[..14];
		var criada = await escopo.ServiceProvider.GetRequiredService<CriarBibliotecaHandler>()
			.HandleAsync(new CriarBibliotecaCommand(nome, slug, null, TodosLeem: false));

		criada.IsSuccess.Should().BeTrue();

		if (!ativa)
		{
			(await escopo.ServiceProvider.GetRequiredService<EditarBibliotecaHandler>()
				.HandleAsync(new EditarBibliotecaCommand(criada.Value.Id, nome, null, Ativo: false))).IsSuccess.Should().BeTrue();
		}

		return criada.Value;
	}

	private async Task LimparPosicoesAsync()
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		await escopo.ServiceProvider.GetRequiredService<IPosicaoMenuRepository>()
			.RemoverGrupoAsync(GrupoDoMenu.Principal, CancellationToken.None);
	}

	private static List<string> ChavesDoPrincipal(string html) =>
		Regex.Matches(html, "data-grupo=\"Principal\"[^>]*data-chave=\"([^\"]+)\"")
			.Select(m => m.Groups[1].Value)
			.ToList();

	[Fact]
	public async Task Item_aparece_so_para_quem_le()
	{
		var biblioteca = await CriarBibliotecaAsync("Manuais do menu");
		var role = $"leitor-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao(role, IntranetPermissoes.Biblioteca.Read(biblioteca.Slug));
		var link = $"href=\"/bibliotecas/{biblioteca.Slug}\"";

		(await CriarCliente(role).GetStringAsync("/")).Should().Contain(link);
		(await CriarCliente("intranet-admin").GetStringAsync("/")).Should().Contain(link);
		(await CriarCliente($"sem-{Guid.NewGuid():N}").GetStringAsync("/")).Should().NotContain(link);
	}

	[Fact]
	public async Task Biblioteca_inativa_nao_aparece()
	{
		var biblioteca = await CriarBibliotecaAsync("Arquivo morto", ativa: false);

		var html = await CriarCliente("intranet-admin").GetStringAsync("/");

		html.Should().NotContain($"href=\"/bibliotecas/{biblioteca.Slug}\"");
	}

	[Fact]
	public async Task Admin_ordena_a_biblioteca_em_menu_e_a_posicao_vale_no_menu()
	{
		await LimparPosicoesAsync();

		try
		{
			var biblioteca = await CriarBibliotecaAsync("Ordenavel");
			var chave = CatalogoDeItensDoMenuPrincipal.ChaveDeBiblioteca(biblioteca.Slug);
			var admin = CriarCliente("intranet-admin");
			var html = await admin.GetStringAsync("/menu");

			ChavesDoPrincipal(html).Should().Contain(chave);

			for (var i = 0; i < 40 && ChavesDoPrincipal(html)[0] != chave; i++)
			{
				var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
				var resposta = await admin.PostAsync("/menu/mover", new FormUrlEncodedContent(
				[
					new KeyValuePair<string, string>("grupo", "Principal"),
					new KeyValuePair<string, string>("chave", chave),
					new KeyValuePair<string, string>("paraCima", "true"),
					new KeyValuePair<string, string>("__RequestVerificationToken", token),
				]));

				resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
				html = await admin.GetStringAsync("/menu");
			}

			ChavesDoPrincipal(html)[0].Should().Be(chave);

			var menu = await admin.GetStringAsync("/");
			var item = menu.IndexOf($"href=\"/bibliotecas/{biblioteca.Slug}\"", StringComparison.Ordinal);
			var mural = menu.IndexOf("href=\"/\"", menu.IndexOf("sc-nav__item", StringComparison.Ordinal), StringComparison.Ordinal);

			item.Should().BePositive();
			item.Should().BeLessThan(mural);
		}
		finally
		{
			await LimparPosicoesAsync();
		}
	}
}
