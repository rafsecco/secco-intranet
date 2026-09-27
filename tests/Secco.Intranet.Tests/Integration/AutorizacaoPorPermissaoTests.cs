using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.Authorization;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Prova a composição da autorização por permissão (ADR-0021): a policy dinâmica do
/// <c>Secco.SDK.AspNetCore</c> está ativa, e o dublê de <see cref="IPermissionResolver"/> da
/// fábrica de testes substitui o resolvedor padrão. Não depende de tenant nem de HTTP — os dois
/// pontos verificados aqui não precisam de nenhum dos dois (a resolução de permissão em si recebe
/// o tenant por parâmetro; quem precisa de <c>ITenantContext</c> é só o handler que decide 403,
/// coberto pela <see cref="ExigePermissaoAttributeTests"/>, com requisição HTTP de verdade).
/// </summary>
public class AutorizacaoPorPermissaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>
{
	[Fact]
	public async Task PolicyDinamica_ReconhecePermissaoNoFormatoCanonico()
	{
		using var scope = factory.Services.CreateScope();
		var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

		var policy = await provider.GetPolicyAsync("diretorio:read");

		policy.Should().NotBeNull();
		policy!.Requirements.Should().NotBeEmpty();
	}

	[Fact]
	public async Task PolicyDinamica_NomeForaDoFormato_NaoViraPolicy()
	{
		using var scope = factory.Services.CreateScope();
		var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

		var policy = await provider.GetPolicyAsync("isso nao e uma permissao");

		policy.Should().BeNull();
	}

	[Fact]
	public async Task ResolvedorDeTeste_SubstituiOPadraoQuandoConfigurado()
	{
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao("diretorio-user", "diretorio:read");

		using var scope = factory.Services.CreateScope();
		var resolvedor = scope.ServiceProvider.GetRequiredService<IPermissionResolver>();

		var permissoes = await resolvedor.ResolveAsync(factory.TenantAlfa, "diretorio-user");

		permissoes.Should().Contain("diretorio:read");

		factory.ResolvedorDePermissoes = null;
	}

	[Fact]
	public async Task SemResolvedorConfigurado_RoleSemPermissaoNenhuma()
	{
		using var scope = factory.Services.CreateScope();
		var resolvedor = scope.ServiceProvider.GetRequiredService<IPermissionResolver>();

		var permissoes = await resolvedor.ResolveAsync(factory.TenantAlfa, "role-qualquer");

		permissoes.Should().BeEmpty();
	}
}
