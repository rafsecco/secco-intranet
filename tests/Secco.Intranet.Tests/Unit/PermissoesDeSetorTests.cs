using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Navigation;
using Secco.SDK.AspNetCore.Authorization;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class PermissoesDeSetorTests
{
	private sealed class TenantContextFalso : ITenantContext
	{
		public Guid? TenantId { get; set; } = Guid.NewGuid();

		public bool IsResolved => TenantId is not null;
	}

	private static (IPermissoesDeSetor Servico, ClaimsPrincipal Usuario) Montar(IPermissionResolver resolvedor, params string[] roles)
	{
		var servicos = new ServiceCollection()
			.AddLogging()
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
			.AddSingleton<ITenantContext>(new TenantContextFalso())
			.AddSingleton(resolvedor)
			.AddAuthorizationCore()
			.AddSeccoAuthorization()
			.BuildServiceProvider();

		var authorizationService = servicos.GetRequiredService<IAuthorizationService>();
		var usuario = new ClaimsPrincipal(new ClaimsIdentity(
			roles.Select(role => new Claim(SeccoClaims.Role, role)), authenticationType: "Teste"));

		return (new PermissoesDeSetor(authorizationService), usuario);
	}

	[Fact]
	public async Task SoDevolveOsSlugsComAPermissaoEspecifica()
	{
		var resolvedor = new PermissionResolverDeTeste().ComPermissao("financeiro-user", IntranetPermissoes.Setor.Read("financeiro"));
		var (servico, usuario) = Montar(resolvedor, "financeiro-user");

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "read", ["financeiro", "ti"]);

		slugs.Should().BeEquivalentTo(["financeiro"]);
	}

	[Fact]
	public async Task PermissaoGlobal_DevolveTodosOsSlugs_InclusiveUmNaoListadoAntes()
	{
		var resolvedor = new PermissionResolverDeTeste().ComPermissao("todos", IntranetPermissoes.Setor.ReadGlobal);
		var (servico, usuario) = Montar(resolvedor, "todos");

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "read", ["financeiro", "ti", "criado-depois"]);

		slugs.Should().BeEquivalentTo(["financeiro", "ti", "criado-depois"]);
	}

	[Fact]
	public async Task IntranetAdmin_DevolveTodosOsSlugs_SemConsultarNada()
	{
		var (servico, usuario) = Montar(new PermissionResolverQueLanca(), "intranet-admin");

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "read", ["financeiro", "ti"]);

		slugs.Should().BeEquivalentTo(["financeiro", "ti"]);
	}

	[Fact]
	public async Task SemNenhumaPermissao_DevolveVazio()
	{
		var (servico, usuario) = Montar(new PermissionResolverDeTeste(), "financeiro-user");

		var slugs = await servico.SlugsComPermissaoAsync(usuario, "read", ["financeiro"]);

		slugs.Should().BeEmpty();
	}

	[Fact]
	public async Task DistingueLeituraDeEscrita()
	{
		var resolvedor = new PermissionResolverDeTeste().ComPermissao("financeiro-user", IntranetPermissoes.Setor.Read("financeiro"));
		var (servico, usuario) = Montar(resolvedor, "financeiro-user");

		var slugsDeLeitura = await servico.SlugsComPermissaoAsync(usuario, "read", ["financeiro"]);
		var slugsDeEscrita = await servico.SlugsComPermissaoAsync(usuario, "write", ["financeiro"]);

		slugsDeLeitura.Should().BeEquivalentTo(["financeiro"]);
		slugsDeEscrita.Should().BeEmpty();
	}
}
