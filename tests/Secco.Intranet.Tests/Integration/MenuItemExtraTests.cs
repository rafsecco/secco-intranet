using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Web.Extensibility;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Itens extras de "projetos externos" no menu principal. O assembly de testes é application part do
/// host (<see cref="IntranetWebFactory"/>), então os itens públicos abaixo e os do catálogo ficam todos
/// visíveis: as asserções olham hrefs e rótulos específicos, nunca contagens.
/// </summary>
public class MenuItemExtraTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente(params string[] roles) => Configurar(factory.CreateClient(), roles);

	private HttpClient Configurar(HttpClient client, string[] roles)
	{
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	[Fact]
	public async Task ProjetoReferenciado_SemARoleDoGate_ItemNaoAparece()
	{
		var html = await CriarCliente().GetStringAsync("/");

		html.Should().NotContain("Extra de teste");
	}

	[Fact]
	public async Task ProjetoReferenciado_ComARoleDoGate_ItemApareceNoGrupoPrincipal()
	{
		var vertical = await CriarCliente("extra-de-teste-leitor").GetStringAsync("/");
		vertical.Should().Contain("href=\"/extra-de-teste\"").And.Contain("Extra de teste");

		var horizontal = Configurar(
			factory.WithWebHostBuilder(b => b.UseSetting("Intranet:Theme:Nome", "Horizontal")).CreateClient(),
			["extra-de-teste-leitor"]);
		var html = await horizontal.GetStringAsync("/");

		html.Should().Contain("sc-topnav").And.Contain("href=\"/extra-de-teste\"");
	}

	[Fact]
	public async Task GateQueLancaExcecao_ItemSome_EPaginaRenderiza()
	{
		var resposta = await CriarCliente("extra-explode").GetAsync("/");

		resposta.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
		(await resposta.Content.ReadAsStringAsync()).Should().NotContain("extra-que-explode");
	}

	[Fact]
	public async Task ComPosicaoSalva_OMenuSegueAOrdemDoTenant()
	{
		await LimparPosicoesAsync();

		try
		{
			using (var escopo = factory.Services.CreateScope())
			{
				escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
				await escopo.ServiceProvider.GetRequiredService<IPosicaoMenuRepository>()
					.SubstituirGrupoAsync(GrupoDoMenu.Principal, ["extra-de-teste", "mural"], CancellationToken.None);
			}

			var html = await CriarCliente("extra-de-teste-leitor").GetStringAsync("/");

			var extra = html.IndexOf("href=\"/extra-de-teste\"", StringComparison.Ordinal);
			var mural = html.IndexOf("href=\"/\"", html.IndexOf("sc-nav__item", StringComparison.Ordinal), StringComparison.Ordinal);

			extra.Should().BePositive();
			mural.Should().BePositive();
			extra.Should().BeLessThan(mural);
		}
		finally
		{
			await LimparPosicoesAsync();
		}
	}

	private async Task LimparPosicoesAsync()
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var repositorio = escopo.ServiceProvider.GetRequiredService<IPosicaoMenuRepository>();

		await repositorio.RemoverGrupoAsync(GrupoDoMenu.Principal, CancellationToken.None);
		await repositorio.RemoverGrupoAsync(GrupoDoMenu.Setores, CancellationToken.None);
	}

	/// <summary>Item de exemplo, visível só com a role <c>extra-de-teste-leitor</c>.</summary>
	public sealed class ItemDeMenuDeTeste : IItemDeMenuPrincipal
	{
		public string Chave => "extra-de-teste";

		public string Rotulo => "Extra de teste";

		public string? Icone => "bi-star";

		public string Url => "/extra-de-teste";

		public int Ordem => 0;

		public Task<bool> VisivelParaAsync(ClaimsPrincipal usuario, CancellationToken cancellationToken) =>
			Task.FromResult(usuario.HasClaim(SeccoClaims.Role, "extra-de-teste-leitor"));
	}

	/// <summary>Item cujo gate lança quando o usuário tem a role <c>extra-explode</c>.</summary>
	public sealed class ItemDeMenuQueExplode : IItemDeMenuPrincipal
	{
		public string Chave => "extra-que-explode";

		public string Rotulo => "Extra que explode";

		public string? Icone => null;

		public string Url => "/extra-que-explode";

		public int Ordem => 0;

		public Task<bool> VisivelParaAsync(ClaimsPrincipal usuario, CancellationToken cancellationToken) =>
			usuario.HasClaim(SeccoClaims.Role, "extra-explode")
				? throw new InvalidOperationException("gate quebrado")
				: Task.FromResult(false);
	}
}
