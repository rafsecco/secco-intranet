using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Telas do acesso do tenant administrado: o que cada uma mostra e o link na página do tenant.
/// </summary>
public class TenantsAcessoTelasTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Admin = Guid.NewGuid();
	private static readonly Guid SemFator = Guid.NewGuid();
	private static readonly Guid Pessoa = Guid.NewGuid();
	private readonly Guid _ligado = Guid.NewGuid();
	private readonly Guid _desligado = Guid.NewGuid();
	private readonly Guid _foraDoCadastro = Guid.NewGuid();
	private GestaoDeAcessoDoTenantFalsa _acesso = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		factory.GestaoDeAcesso = new GestaoDeAcessoFalsa()
			.ComUsuario(Admin, "admin@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin")
			.ComDoisFatores(Admin)
			.ComUsuario(SemFator, "sem@exemplo.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		factory.GestaoDeTenants = new GestaoDeTenantsFalsa()
			.ComTenant(_ligado, $"lig-{_ligado:N}"[..20])
			.ComTenant(_desligado, $"des-{_desligado:N}"[..20])
			.ComTenant(_foraDoCadastro, $"for-{_foraDoCadastro:N}"[..20]);
		_acesso = new GestaoDeAcessoDoTenantFalsa()
			.ComPerfil("compras.aprovador", false, "pedido:aprovar")
			.ComUsuario(Pessoa, "ana@compras.com", SituacaoDoUsuario.Ativo);
		factory.GestaoDeAcessoDoTenant = _acesso;

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var cadastro = escopo.ServiceProvider.GetRequiredService<ITenantsAdministrados>();
		var comSecureGate = new TenantAdministrado(_ligado, "Compras", "Ana", OrigemDoTenant.Criado, "admin");
		comSecureGate.HabilitarSecureGate();
		await cadastro.TentarAdicionarAsync(comSecureGate);
		await cadastro.TentarAdicionarAsync(new TenantAdministrado(_desligado, "RH", "Bia", OrigemDoTenant.Criado, "admin"));
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Cliente(Guid? usuario, params string[] roles)
	{
		var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		if (usuario is not null)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, usuario.Value.ToString());
		}

		return client;
	}

	[Fact]
	public async Task Index_MostraOSistemaEmQualSeEstaMexendo()
	{
		var html = AuxiliaresDeHttp.Decodificar(await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso"));

		html.Should().Contain("Compras").And.Contain("compras.aprovador").And.Contain("Cadastrar pessoa");
	}

	[Fact]
	public async Task Perfil_PermissoesEmTextoUmaPorLinha()
	{
		var html = AuxiliaresDeHttp.Decodificar(
			await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso/perfis/compras.aprovador"));

		html.Should().Contain("<textarea").And.Contain("pedido:aprovar");
	}

	[Fact]
	public async Task Novo_SemFederacao_NaoOfereceSoPeloEntra()
	{
		var html = await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso/usuarios/novo");

		AuxiliaresDeHttp.Decodificar(html).Should().NotContain("Só pelo Entra");
	}

	[Fact]
	public async Task Novo_ComFederacao_OfereceSoPeloEntra()
	{
		_acesso.Federacao = new(Guid.NewGuid(), true, null);

		var html = AuxiliaresDeHttp.Decodificar(await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso/usuarios/novo"));

		html.Should().Contain("Só pelo Entra");
	}

	[Fact]
	public async Task Usuario_SemSenhaEPorSenha_OfereceReenviarConvite()
	{
		var html = AuxiliaresDeHttp.Decodificar(
			await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso/usuarios/{Pessoa}"));

		html.Should().Contain("Reenviar convite");
	}

	[Fact]
	public async Task Federacao_ComDiretorio_PedeConfirmacaoDeTroca()
	{
		_acesso.Federacao = new(Guid.NewGuid(), true, null);

		var html = AuxiliaresDeHttp.Decodificar(
			await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}/acesso?aba=federacao"));

		html.Should().Contain("name=\"confirmarTroca\"");
	}

	[Fact]
	public async Task DetalheDoTenant_ComSecureGateLigado_LinkaOAcesso()
	{
		var html = await Cliente(Admin, "intranet-admin").GetStringAsync($"/tenants/{_ligado}");

		html.Should().Contain($"href=\"/tenants/{_ligado}/acesso\"");
	}

	[Fact]
	public async Task Cadastrar_PeloFormulario_RedirecionaParaAPessoa()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/acesso/usuarios/novo");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/acesso/usuarios/novo",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("Email", "dani@compras.com"), ("EntraPorSenha", "true"),
				("Perfis", "compras.aprovador")));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.ToString().Should().StartWith($"/tenants/{_ligado}/acesso/usuarios/");
		_acesso.Cadastrados.Should().ContainSingle().Which.Perfis.Should().Equal("compras.aprovador");
	}
}
