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
/// Acesso do tenant administrado pelo host real: o trio da ADR-0008 e o 2FA em toda rota, 404 para
/// tenant fora do cadastro ou com SecureGate desligado, e o tenant da rota vencendo o do corpo.
/// </summary>
public class TenantsAcessoAutorizacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
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

	private static IEnumerable<string> RotasGet(Guid tenant) =>
	[
		$"/tenants/{tenant}/acesso",
		$"/tenants/{tenant}/acesso?aba=usuarios",
		$"/tenants/{tenant}/acesso?aba=federacao",
		$"/tenants/{tenant}/acesso/perfis/compras.aprovador",
		$"/tenants/{tenant}/acesso/usuarios/novo",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}",
	];

	private static IEnumerable<string> RotasPost(Guid tenant) =>
	[
		$"/tenants/{tenant}/acesso/perfis",
		$"/tenants/{tenant}/acesso/perfis/compras.aprovador/excluir",
		$"/tenants/{tenant}/acesso/perfis/compras.aprovador/permissoes",
		$"/tenants/{tenant}/acesso/perfis/compras.aprovador/membros",
		$"/tenants/{tenant}/acesso/usuarios/novo",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/perfis",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/perfis/retirar",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/desativar",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/reativar",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/sessoes/encerrar",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/convite",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/vinculo/suspender",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/vinculo/reativar",
		$"/tenants/{tenant}/acesso/usuarios/{Pessoa}/vinculo/remover",
		$"/tenants/{tenant}/acesso/federacao",
	];

	public static TheoryData<string[]> UsuariosSemAcesso => new()
	{
		Array.Empty<string>(),
		new[] { "financeiro-admin" },
		new[] { "financeiro-admin", "rh-admin", "ti-admin" },
		new[] { "inventario-admin" },
	};

	[Theory]
	[MemberData(nameof(UsuariosSemAcesso))]
	public async Task SemIntranetAdmin_403_EmTodaRota(string[] roles)
	{
		var client = Cliente(Admin, roles);

		foreach (var rota in RotasGet(_ligado))
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota}");
		}

		foreach (var rota in RotasPost(_ligado))
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"POST {rota}");
		}
	}

	[Fact]
	public async Task IntranetAdminSemSegundoFator_403_EmTodaRota()
	{
		var client = Cliente(SemFator, "intranet-admin");

		foreach (var rota in RotasGet(_ligado))
		{
			(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {rota}");
		}

		foreach (var rota in RotasPost(_ligado))
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"POST {rota}");
		}
	}

	[Fact]
	public async Task PostSemTokenAntifalsificacao_400_EmTodaRota_SemTocarAPorta()
	{
		var client = Cliente(Admin, "intranet-admin");
		_acesso.Chamadas.Clear();

		foreach (var rota in RotasPost(_ligado))
		{
			(await client.PostAsync(rota, new FormUrlEncodedContent([]))).StatusCode.Should().Be(HttpStatusCode.BadRequest, $"POST {rota}");
		}

		_acesso.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task ForaDoCadastroOuSecureGateDesligado_404_EmTodaRota_SemTocarAPorta()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/acesso");
		_acesso.Chamadas.Clear();

		foreach (var tenant in new[] { _foraDoCadastro, _desligado })
		{
			foreach (var rota in RotasGet(tenant))
			{
				(await client.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.NotFound, $"GET {rota}");
			}

			foreach (var rota in RotasPost(tenant))
			{
				(await client.PostAsync(rota, AuxiliaresDeHttp.Form(("__RequestVerificationToken", token))))
					.StatusCode.Should().Be(HttpStatusCode.NotFound, $"POST {rota}");
			}
		}

		_acesso.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task TenantNoCorpo_EIgnorado_ValeODaRota()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/acesso");

		var resposta = await client.PostAsync($"/tenants/{_ligado}/acesso/perfis",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("nome", "compras-leitor"), ("tenantId", _foraDoCadastro.ToString())));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		_acesso.Chamadas.Should().Contain($"criarPerfil:{_ligado}:compras-leitor")
			.And.NotContain(c => c.Contains(_foraDoCadastro.ToString(), StringComparison.Ordinal));
	}

	[Fact]
	public async Task TenantNaQueryString_EIgnorado_ValeODaRota()
	{
		var client = Cliente(Admin, "intranet-admin");
		var token = await AuxiliaresDeHttp.TokenAsync(client, $"/tenants/{_ligado}/acesso");

		await client.PostAsync($"/tenants/{_ligado}/acesso/perfis?tenantId={_foraDoCadastro}",
			AuxiliaresDeHttp.Form(("__RequestVerificationToken", token), ("nome", "compras-leitor")));

		_acesso.Chamadas.Should().Contain(c => c.StartsWith($"criarPerfil:{_ligado}:", StringComparison.Ordinal))
			.And.NotContain(c => c.Contains(_foraDoCadastro.ToString(), StringComparison.Ordinal));
	}

	[Fact]
	public async Task PerfilComPontoNoNome_AbreNaRota()
	{
		var resposta = await Cliente(Admin, "intranet-admin").GetAsync($"/tenants/{_ligado}/acesso/perfis/compras.aprovador");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
	}
}
