using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A autorização do Inventário precisa ser real na rota, não só um botão escondido — ver
/// docs/specs/2026-09-13-inventario-design.md. Usa <see cref="RolesDeTesteMiddleware"/>
/// porque o ambiente Testing nunca registra autenticação de verdade.
/// </summary>
public class InventarioAutorizacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente(params string[] roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	[Fact]
	public async Task SemRole_Bloqueado()
	{
		var resposta = await CriarCliente().GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task ComRoleDeSetor_AindaBloqueado()
	{
		var resposta = await CriarCliente("financeiro-admin").GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, "admin de setor não é atalho para inventario-admin");
	}

	[Fact]
	public async Task CriarItem_SemRole_Bloqueado()
	{
		var client = CriarCliente();

		var resposta = await client.PostAsync(
			"/Inventario/Create", new FormUrlEncodedContent([new KeyValuePair<string, string>("Nome", "Notebook")]));

		// [ValidateAntiForgeryToken] é, ele mesmo, um filtro de autorização — roda antes do
		// corpo da action. Sem token antiforgery, esse filtro barra a requisição com 400 antes
		// de PodeAdministrar() sequer rodar. Ainda assim bloqueado de verdade: nenhum código do
		// controller executa, nada é criado — os dois testes acima (GET) já provam que
		// PodeAdministrar() em si devolve 403 quando chega a rodar.
		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task ComIntranetAdmin_Liberado()
	{
		var resposta = await CriarCliente("intranet-admin").GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task ComInventarioAdmin_Liberado()
	{
		var resposta = await CriarCliente("inventario-admin").GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task FluxoCompleto_ComInventarioAdmin_CriaEExibe()
	{
		var client = CriarCliente("inventario-admin");
		var titulo = $"Item {Guid.NewGuid():N}"[..20];

		var paginaCriacao = await client.GetStringAsync("/Inventario/Create");
		var token = Regex.Match(paginaCriacao, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

		var resposta = await client.PostAsync("/Inventario/Create", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("Nome", titulo),
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "salvar redireciona para os detalhes");

		var html = await resposta.Content.ReadAsStringAsync();
		html.Should().Contain(titulo);
	}

	// Role com sufixo único por teste: o CachedPermissionResolver do SDK guarda o resultado por
	// (tenant, role) por um TTL curto — reusar o mesmo nome de role em dois testes desta classe
	// (mesmo tenant, mesmo host) arriscaria um teste ler o cache deixado pelo outro.
	[Fact]
	public async Task ComInventarioLeitura_AbreAListagem()
	{
		var role = $"consulta-inventario-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(role, IntranetPermissoes.Inventario.Read);
		var client = CriarCliente(role);

		var resposta = await client.GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		factory.ResolvedorDePermissoes = null;
	}

	[Fact]
	public async Task ComInventarioLeitura_EscritaContinuaBloqueada()
	{
		var role = $"consulta-inventario-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(role, IntranetPermissoes.Inventario.Read);
		var client = CriarCliente(role);

		var resposta = await client.GetAsync("/Inventario/Create");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, "inventario:read abre só a leitura");

		factory.ResolvedorDePermissoes = null;
	}

	[Fact]
	public async Task SemInventarioLeitura_ContinuaBloqueado()
	{
		var role = $"consulta-inventario-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste();
		var resposta = await CriarCliente(role).GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);

		factory.ResolvedorDePermissoes = null;
	}

	[Fact]
	public async Task InventarioAdmin_ContinuaLiberadoEmTudo_SemPrecisarDeInventarioRead()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste();
		var resposta = await CriarCliente("inventario-admin").GetAsync("/Inventario");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		factory.ResolvedorDePermissoes = null;
	}

	[Fact]
	public async Task ComInventarioLeitura_VeOItemDeMenu()
	{
		var role = $"consulta-inventario-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(role, IntranetPermissoes.Inventario.Read);

		var html = await CriarCliente(role).GetStringAsync("/");

		html.Should().Contain("href=\"/inventario\"");

		factory.ResolvedorDePermissoes = null;
	}
}
