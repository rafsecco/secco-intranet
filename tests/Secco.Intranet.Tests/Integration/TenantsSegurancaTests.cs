using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.Intranet.Infrastructure.Tenants;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Os pontos de segurança da spec pelo host HTTP real: tenant fora do cadastro é 404 em toda rota,
/// a adoção recusa Guid forjado de tenant protegido, e o script de provisionamento só existe na
/// resposta do POST — sem cookie de TempData, com <c>no-store</c>.
/// </summary>
public class TenantsSegurancaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Admin = Guid.NewGuid();
	private readonly Guid _cadastrado = Guid.NewGuid();
	private readonly Guid _naoCadastrado = Guid.NewGuid();
	private GestaoDeTenantsFalsa _gestao = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa()
			.ComUsuario(Admin, "admin@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin")
			.ComDoisFatores(Admin);
		_gestao = new GestaoDeTenantsFalsa()
			.ComTenant(_cadastrado, $"cad-{_cadastrado:N}"[..20])
			.ComTenant(_naoCadastrado, $"livre-{_naoCadastrado:N}"[..20])
			.ComTenant(TenantsProtegidosDoCatalogo.TenantDeInstalacao, "instalacao")
			.ComTenant(factory.TenantBeta, "outra-intranet");
		factory.GestaoDeTenants = _gestao;

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		await escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>()
			.TentarAdicionarAsync(new TenantAdministrado(_cadastrado, "Sistema de compras", "Ana", OrigemDoTenant.Criado, "admin"));
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Cliente()
	{
		var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, Admin.ToString());

		return client;
	}

	[Fact]
	public async Task TenantForaDoCadastro_404_EmTodaRota_SemTocarAPlataforma()
	{
		var client = Cliente();
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_cadastrado}");
		_gestao.Chamadas.Clear();

		(await client.GetAsync($"/tenants/{_naoCadastrado}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

		foreach (var rota in new[] { "recursos/logstream", "ativar", "desativar" })
		{
			var resposta = await client.PostAsync($"/tenants/{_naoCadastrado}/{rota}",
				AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("confirmacao", "x")));

			resposta.StatusCode.Should().Be(HttpStatusCode.NotFound, $"POST {rota}");
		}

		_gestao.Chamadas.Should().NotContain(c => c.Contains(_naoCadastrado.ToString(), StringComparison.Ordinal));
	}

	[Fact]
	public async Task TenantIdQueNaoEGuid_404() =>
		(await Cliente().GetAsync("/tenants/nao-e-guid")).StatusCode.Should().Be(HttpStatusCode.NotFound);

	[Fact]
	public async Task AdotarComGuidForjado_DeTenantProtegido_Recusa()
	{
		var client = Cliente();

		foreach (var protegido in new[] { TenantsProtegidosDoCatalogo.TenantDeInstalacao, factory.TenantAlfa, factory.TenantBeta })
		{
			var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_cadastrado}");
			var resposta = await client.PostAsync("/tenants/adotar", AuxiliaresDeHttp.Form(
				("__RequestVerificationToken", token), ("TenantId", protegido.ToString()), ("Sistema", "S"), ("Responsavel", "R")));

			resposta.StatusCode.Should().Be(HttpStatusCode.OK, "volta ao formulário com o erro");
			AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync())
				.Should().Contain("não pode ser administrado por aqui");
		}

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var cadastro = await escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>().ListarAsync();
		cadastro.Should().NotContain(t =>
			t.TenantId == TenantsProtegidosDoCatalogo.TenantDeInstalacao || t.TenantId == factory.TenantAlfa || t.TenantId == factory.TenantBeta);
	}

	[Fact]
	public async Task Adotaveis_NaoListaProtegidos()
	{
		var html = await Cliente().GetStringAsync("/tenants/adotar");

		html.Should().Contain(_naoCadastrado.ToString())
			.And.NotContain(TenantsProtegidosDoCatalogo.TenantDeInstalacao.ToString())
			.And.NotContain(factory.TenantBeta.ToString());
	}

	[Fact]
	public async Task ScriptDeProvisionamento_SoNaResposta_NoStore_SemCookie()
	{
		_gestao.ModoScript = true;
		var client = Cliente();
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_cadastrado}");

		var resposta = await client.PostAsync($"/tenants/{_cadastrado}/recursos/logstream",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK, "o script é a própria resposta, não um redirect");
		resposta.Headers.CacheControl!.NoStore.Should().BeTrue();
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("SENHA-SECRETA-DE-TESTE");
		resposta.Headers.TryGetValues("Set-Cookie", out var cookies);
		// O framework pode devolver o cookie de TempData só para limpá-lo (valor vazio, já expirado);
		// o que não pode existir é um cookie de TempData com conteúdo, nem a senha em qualquer cookie.
		(cookies ?? []).Should().NotContain(c => c.Contains("TempData", StringComparison.OrdinalIgnoreCase)
				&& !c.Contains("TempDataProvider=;", StringComparison.OrdinalIgnoreCase),
			"a senha não pode ir para um cookie");
		(cookies ?? []).Should().NotContain(c => c.Contains("SENHA-SECRETA-DE-TESTE", StringComparison.Ordinal));

		var detalhe = await client.GetStringAsync($"/tenants/{_cadastrado}");
		detalhe.Should().NotContain("SENHA-SECRETA-DE-TESTE", "recarregar não mostra o script de novo");
	}

	[Fact]
	public async Task RecursoForaDaLista_NaoChamaAPlataforma()
	{
		var client = Cliente();
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_cadastrado}");
		_gestao.Chamadas.Clear();

		var resposta = await client.PostAsync($"/tenants/{_cadastrado}/recursos/intranet",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token)));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		_gestao.Chamadas.Should().NotContain(c => c.StartsWith("provisionar", StringComparison.Ordinal));
	}
}
