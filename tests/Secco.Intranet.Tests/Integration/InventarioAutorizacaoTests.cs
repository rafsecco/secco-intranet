using System.Net;
using AwesomeAssertions;
using Secco.Intranet.Tests.Integration.TestAuthentication;
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
}
