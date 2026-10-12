using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.Intranet.Infrastructure.Tenants;
using Secco.SecureGate.Client;
using Secco.SecureGate.Client.Administration;
using Xunit;

namespace Secco.Intranet.Tests.Smoke;

/// <summary>
/// Acesso de tenant administrado contra um SecureGate <b>de verdade</b>. Prova, além do contrato, que
/// a plataforma recusa atribuir perfil a um usuário de outro tenant — a Intranet confia nisso.
/// Deixa um tenant desativado por execução.
/// </summary>
public class SecureGateGestaoDeAcessoDoTenantFumacaTests
{
	private static ServiceProvider Provider()
	{
		var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Secco:SecureGate:BaseUrl"] = Environment.GetEnvironmentVariable(FumacaFactAttribute.VariavelDaUrl),
			["Secco:SecureGate:ClientId"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_ID") ?? "secco-dev-console",
			["Secco:SecureGate:ClientSecret"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_SECRET")
				?? "secco-dev-console-secret-32-chars-min!",
		}).Build();

		return new ServiceCollection()
			.AddSingleton<IConfiguration>(configuracao)
			.AddLogging()
			.AddSecureGateAdminClient()
			.BuildServiceProvider();
	}

	[FumacaFact]
	public async Task PerfisPessoasEIsolamento_ContraOContratoReal()
	{
		using var provider = Provider();
		var client = provider.GetRequiredService<ISecureGateClient>();
		var tenants = new SecureGateGestaoDeTenants(client, NullLogger<SecureGateGestaoDeTenants>.Instance);
		var acesso = new SecureGateGestaoDeAcessoDoTenant(client, NullLogger<SecureGateGestaoDeAcessoDoTenant>.Instance);

		var sufixo = Guid.NewGuid().ToString("N")[..10];
		var alvo = (await tenants.CriarTenantAsync($"Fumaça acesso {sufixo}", $"fa-{sufixo}")).Value.Id;
		var outro = (await tenants.CriarTenantAsync($"Fumaça outro {sufixo}", $"fo-{sufixo}")).Value.Id;

		try
		{
			(await acesso.CriarPerfilAsync(alvo, "compras.aprovador")).IsSuccess.Should().BeTrue();
			(await acesso.DefinirPermissoesDoPerfilAsync(alvo, "compras.aprovador", ["pedido:aprovar"])).IsSuccess.Should().BeTrue();
			(await acesso.ObterPerfilAsync(alvo, "compras.aprovador")).Value.Permissoes.Should().Equal("pedido:aprovar");

			var porSenha = await acesso.CadastrarUsuarioAsync(alvo, new NovoUsuarioDoTenant($"senha-{sufixo}@fumaca.invalid", "Senha", ["compras.aprovador"], true));
			porSenha.IsSuccess.Should().BeTrue(porSenha.IsFailure ? porSenha.Error.Description : null);
			(await acesso.ObterUsuarioAsync(alvo, porSenha.Value)).Value.Should().Match<UsuarioDoTenantDetalheDto>(u => !u.TemSenha && u.EntraPorSenha);

			var repetido = await acesso.CadastrarUsuarioAsync(alvo, new NovoUsuarioDoTenant($"senha-{sufixo}@fumaca.invalid", null, [], true));
			repetido.Error.Code.Should().Be("Intranet.Acesso.EmailJaCadastrado");

			var deOutroTenant = await acesso.CadastrarUsuarioAsync(outro, new NovoUsuarioDoTenant($"outro-{sufixo}@fumaca.invalid", null, [], true));
			(await acesso.AtribuirPerfilAsync(alvo, deOutroTenant.Value, "compras.aprovador"))
				.Error.Code.Should().Be("Intranet.Acesso.UsuarioNaoEncontrado", "a plataforma recusa usuário de outro tenant");

			(await acesso.RetirarPerfilAsync(alvo, porSenha.Value, "compras.aprovador")).IsSuccess.Should().BeTrue();
			(await acesso.DesativarUsuarioAsync(alvo, porSenha.Value)).IsSuccess.Should().BeTrue();
		}
		finally
		{
			await tenants.DesativarAsync(alvo);
			await tenants.DesativarAsync(outro);
		}
	}
}
